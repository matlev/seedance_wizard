using ReelForge.Core;

namespace ReelForge.Application;

public enum DeletedPhysicalAssetProbeStatus
{
    NotApplicable,
    Verified,
    Missing,
    Inaccessible,
    Cancelled
}

/// <summary>
/// A verified, read-only candidate identity together with every deleted logical source it could
/// restore. Selection remains a user decision when more than one tombstone matches.
/// </summary>
public sealed record DeletedPhysicalAssetRestoreProbe(
    DeletedPhysicalAssetProbeStatus Status,
    ContentIdentity? CandidateIdentity,
    IReadOnlyList<DeletedPhysicalAssetRestoreMatch> Matches,
    string? Detail = null);

public sealed record DeletedPhysicalAssetRestoreMatch(
    Guid AssetId,
    string DisplayName,
    MediaType MediaType,
    ProjectAssetDependencyReport DependencyReport);

public sealed record DeletedPhysicalAssetRestoreResult(
    PhysicalAssetRelinkResult Relink,
    Guid RestoredAssetId,
    Guid? DonorAssetId = null,
    bool DonorWasFolded = false);

/// <summary>
/// Finds and explicitly restores deliberately deleted physical-media records. Hash equality is
/// evidence only; this service never chooses a tombstone on behalf of the caller.
/// </summary>
public sealed class DeletedPhysicalAssetRestorationService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IContentHashService _contentHashService;
    private readonly PhysicalAssetRelinkService _relinkService;
    private readonly IPhysicalAssetRelinkStager _stager;
    private readonly ProjectAssetDependencyAnalyzer _dependencyAnalyzer;

    public DeletedPhysicalAssetRestorationService(
        ProjectWorkspace workspace,
        IContentHashService contentHashService,
        PhysicalAssetRelinkService relinkService,
        IPhysicalAssetRelinkStager stager,
        ProjectAssetDependencyAnalyzer dependencyAnalyzer)
    {
        _workspace = workspace;
        _contentHashService = contentHashService;
        _relinkService = relinkService;
        _stager = stager;
        _dependencyAnalyzer = dependencyAnalyzer;
    }

    public async Task<DeletedPhysicalAssetRestoreProbe> ProbeAsync(
        string candidatePath,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        if (!project.Assets.Any(asset => IsEligibleDeletedIdentity(asset, mediaType)))
            return new DeletedPhysicalAssetRestoreProbe(DeletedPhysicalAssetProbeStatus.NotApplicable, null, []);

        ContentIdentity identity;
        try
        {
            identity = await _contentHashService.ComputeAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DeletedPhysicalAssetRestoreProbe(DeletedPhysicalAssetProbeStatus.Missing, null, [], exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DeletedPhysicalAssetRestoreProbe(DeletedPhysicalAssetProbeStatus.Cancelled, null, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new DeletedPhysicalAssetRestoreProbe(DeletedPhysicalAssetProbeStatus.Inaccessible, null, [], exception.Message);
        }

        return new DeletedPhysicalAssetRestoreProbe(
            DeletedPhysicalAssetProbeStatus.Verified,
            identity,
            FindDeletedMatches(identity, mediaType));
    }

    public IReadOnlyList<DeletedPhysicalAssetRestoreMatch> FindDeletedMatches(ContentIdentity identity, MediaType mediaType)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        if (identity.Status != ContentHashStatus.Verified || !IsSha256(identity.Sha256))
            return [];

        return project.Assets
            .Where(asset => asset.IsDeleted && asset.StorageKind == AssetStorageKind.Physical &&
                            asset.Physical is not null && asset.MediaType == mediaType &&
                            asset.Physical.ContentIdentity.Status == ContentHashStatus.Verified &&
                            HashesEqual(asset.Physical.ContentIdentity.Sha256, identity.Sha256))
            .Select(asset => new DeletedPhysicalAssetRestoreMatch(
                asset.Id, asset.EffectiveDisplayName, asset.MediaType, _dependencyAnalyzer.Analyze(project, asset.Id)))
            .ToArray();
    }

    public Task<DeletedPhysicalAssetRestoreResult> RestoreExternalAsync(
        Guid deletedAssetId,
        string candidatePath,
        CancellationToken cancellationToken = default) =>
        RestoreExternalCoreAsync(deletedAssetId, candidatePath, null, cancellationToken);

    /// <summary>
    /// Restores a selected tombstone from an active matching asset. An unused donor is folded only
    /// after a separately staged project-local copy has been verified; a referenced donor is
    /// retained and copied through the ordinary verified relink transaction.
    /// </summary>
    public async Task<DeletedPhysicalAssetRestoreResult> RestoreFromActiveDonorAsync(
        Guid deletedAssetId,
        Guid donorAssetId,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("Create or open a project first.");
        var deleted = FindDeletedPhysicalAsset(project, deletedAssetId);
        var donor = project.Assets.SingleOrDefault(asset => asset.Id == donorAssetId)
            ?? throw new InvalidOperationException($"Asset '{donorAssetId}' does not exist in this project.");
        if (donor.IsDeleted || donor.StorageKind != AssetStorageKind.Physical || donor.Physical is null ||
            donor.MediaType != deleted.MediaType || !HasSameVerifiedIdentity(deleted, donor))
            throw new InvalidOperationException("The selected donor is not a matching active physical asset.");

        var dependencies = _dependencyAnalyzer.Analyze(project, deleted.Id);
        try
        {
            await _workspace.EnsurePhysicalAssetRelinkCanStartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RestoreResult(PhysicalAssetRelinkStatus.Cancelled, dependencies, deleted.Id, donor.Id);
        }

        var donorPath = _workspace.GetAbsoluteAssetPath(donor);
        ContentVerificationResult verification;
        try
        {
            verification = await _contentHashService
                .VerifyAsync(donorPath, deleted.Physical!.ContentIdentity, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return RestoreResult(PhysicalAssetRelinkStatus.Missing, dependencies, deleted.Id, donor.Id, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return RestoreResult(PhysicalAssetRelinkStatus.Cancelled, dependencies, deleted.Id, donor.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return RestoreResult(PhysicalAssetRelinkStatus.Inaccessible, dependencies, deleted.Id, donor.Id, exception.Message);
        }
        catch (Exception exception)
        {
            return RestoreResult(PhysicalAssetRelinkStatus.Failed, dependencies, deleted.Id, donor.Id, exception.Message);
        }

        if (!verification.MatchesExpected)
            return RestoreResult(PhysicalAssetRelinkStatus.Mismatched, dependencies, deleted.Id, donor.Id);

        var donorDependencies = _dependencyAnalyzer.Analyze(project, donor.Id);
        if (donorDependencies.IsInUse)
            return await RestoreExternalCoreAsync(deleted.Id, donorPath, donor.Id, cancellationToken).ConfigureAwait(false);

        var deletedSnapshot = DeletedSnapshot.Capture(deleted, project);
        var rollbackHandled = false;
        var donorIndexAtApply = -1;
        StagedPhysicalAssetRelink? staged = null;
        ContentVerificationResult? stagedVerification = null;

        async Task RollbackAsync()
        {
            deletedSnapshot.Restore(deleted, project);
            if (!project.Assets.Contains(donor))
            {
                var insertionIndex = donorIndexAtApply < 0
                    ? project.Assets.Count
                    : Math.Clamp(donorIndexAtApply, 0, project.Assets.Count);
                project.Assets.Insert(insertionIndex, donor);
            }

            if (staged is not null)
            {
                try
                {
                    await _stager.DiscardAsync(staged, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // The staged artifact is deliberately unreferenced if guarded retirement
                    // declines or fails. Project truth has still been restored in memory.
                }
            }

            rollbackHandled = true;
        }

        try
        {
            staged = await _stager.StageAsync(location, deleted, donorPath, cancellationToken).ConfigureAwait(false);
            stagedVerification = await _contentHashService
                .VerifyAsync(staged.DestinationPath, deleted.Physical!.ContentIdentity, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            await RollbackAsync().ConfigureAwait(false);
            return RestoreResult(PhysicalAssetRelinkStatus.Missing, dependencies, deleted.Id, donor.Id, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync().ConfigureAwait(false);
            return RestoreResult(PhysicalAssetRelinkStatus.Cancelled, dependencies, deleted.Id, donor.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await RollbackAsync().ConfigureAwait(false);
            return RestoreResult(PhysicalAssetRelinkStatus.Inaccessible, dependencies, deleted.Id, donor.Id, exception.Message);
        }
        catch (Exception exception)
        {
            await RollbackAsync().ConfigureAwait(false);
            return RestoreResult(PhysicalAssetRelinkStatus.Failed, dependencies, deleted.Id, donor.Id, exception.Message);
        }

        if (!stagedVerification.MatchesExpected)
        {
            await RollbackAsync().ConfigureAwait(false);
            return RestoreResult(
                PhysicalAssetRelinkStatus.Mismatched,
                dependencies,
                deleted.Id,
                donor.Id,
                "The donor changed while its restoration copy was being staged.");
        }

        void Apply()
        {
            // The save transaction serializes the commit, but another project operation can have
            // changed this in-memory graph while verification was running. Never remove by the
            // index captured before that work; revalidate the exact objects at mutation time.
            if (project.Assets.SingleOrDefault(asset => asset.Id == deletedAssetId) is not { } currentDeleted ||
                !ReferenceEquals(currentDeleted, deleted) ||
                project.Assets.SingleOrDefault(asset => asset.Id == donorAssetId) is not { } currentDonor ||
                !ReferenceEquals(currentDonor, donor) ||
                !deleted.IsDeleted || donor.IsDeleted || deleted.StorageKind != AssetStorageKind.Physical ||
                deleted.Physical is null || donor.StorageKind != AssetStorageKind.Physical || donor.Physical is null ||
                donor.MediaType != deleted.MediaType || !HasSameVerifiedIdentity(deleted, donor) ||
                _dependencyAnalyzer.Analyze(project, donor.Id).IsInUse)
            {
                throw new InvalidOperationException("The selected restoration donor changed before it could be folded.");
            }

            donorIndexAtApply = project.Assets.IndexOf(donor);
            if (donorIndexAtApply < 0)
                throw new InvalidOperationException("The selected restoration donor is no longer in this project.");

            deleted.IsDeleted = false;
            deleted.Physical!.RelativePath = ProjectPathPolicy.GetRelativePath(location, staged.DestinationPath);
            deleted.Physical.Availability = PhysicalAssetAvailability.Available;
            deleted.Physical.ContentIdentity.LengthBytes = stagedVerification.Observed.LengthBytes;
            deleted.Physical.ContentIdentity.ObservedLastWriteTimeUtc = stagedVerification.Observed.ObservedLastWriteTimeUtc;
            deleted.FileName = Path.GetFileName(staged.DestinationPath);
            project.Assets.RemoveAt(donorIndexAtApply);
        }

        var save = await _workspace.SavePhysicalAssetRelinkIfCurrentAsync(project, location, Apply, RollbackAsync, cancellationToken)
            .ConfigureAwait(false);
        var status = save.Committed ? PhysicalAssetRelinkStatus.Verified :
            cancellationToken.IsCancellationRequested || save.Failure is OperationCanceledException
                ? PhysicalAssetRelinkStatus.Cancelled
                : save.Failure is null ? PhysicalAssetRelinkStatus.Stale : PhysicalAssetRelinkStatus.Failed;
        if (!save.Committed && !rollbackHandled)
            await RollbackAsync().ConfigureAwait(false);
        if (!save.Committed)
            return RestoreResult(status, dependencies, deleted.Id, donor.Id, save.Failure?.Message);

        // This is best-effort hygiene after persistence succeeds. The signature captured before
        // staging makes a replacement donor ineligible for deletion.
        try
        {
            await _stager.DiscardAsync(new StagedPhysicalAssetRelink(
                donorPath,
                verification.Observed.Sha256!.ToUpperInvariant(),
                verification.Observed.LengthBytes ?? 0,
                verification.Observed.ObservedLastWriteTimeUtc ?? DateTimeOffset.MinValue), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            // A failed or declined retirement cannot roll back an already committed project.
        }

        return RestoreResult(PhysicalAssetRelinkStatus.Verified, dependencies, deleted.Id, donor.Id, donorWasFolded: true);
    }

    private async Task<DeletedPhysicalAssetRestoreResult> RestoreExternalCoreAsync(
        Guid deletedAssetId, string candidatePath, Guid? donorAssetId, CancellationToken cancellationToken)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        _ = FindDeletedPhysicalAsset(project, deletedAssetId);
        var result = await _relinkService
            .RelinkAsync(deletedAssetId, candidatePath, PhysicalAssetRelinkMode.RestoreDeleted, cancellationToken)
            .ConfigureAwait(false);
        return new DeletedPhysicalAssetRestoreResult(result, deletedAssetId, donorAssetId);
    }

    private static DeletedPhysicalAssetRestoreResult RestoreResult(
        PhysicalAssetRelinkStatus status,
        ProjectAssetDependencyReport dependencies,
        Guid deletedAssetId,
        Guid? donorAssetId,
        string? detail = null,
        bool donorWasFolded = false) =>
        new(new PhysicalAssetRelinkResult(status, dependencies, detail), deletedAssetId, donorAssetId, donorWasFolded);

    private static ProjectAsset FindDeletedPhysicalAsset(VideoProject project, Guid id)
    {
        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == id)
            ?? throw new InvalidOperationException($"Asset '{id}' does not exist in this project.");
        if (!asset.IsDeleted || asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException("Only deleted physical media can be restored.");
        if (asset.Physical.ContentIdentity.Status != ContentHashStatus.Verified || !IsSha256(asset.Physical.ContentIdentity.Sha256))
            throw new InvalidOperationException("Restoring deleted media requires its verified SHA-256 identity.");
        return asset;
    }

    private static bool HasSameVerifiedIdentity(ProjectAsset first, ProjectAsset second) =>
        first.Physical!.ContentIdentity.Status == ContentHashStatus.Verified &&
        second.Physical!.ContentIdentity.Status == ContentHashStatus.Verified &&
        HashesEqual(first.Physical.ContentIdentity.Sha256, second.Physical.ContentIdentity.Sha256);

    private static bool IsEligibleDeletedIdentity(ProjectAsset asset, MediaType mediaType) =>
        asset.IsDeleted && asset.StorageKind == AssetStorageKind.Physical && asset.Physical is not null &&
        asset.MediaType == mediaType && asset.Physical.ContentIdentity.Status == ContentHashStatus.Verified &&
        IsSha256(asset.Physical.ContentIdentity.Sha256);

    private static bool HashesEqual(string? first, string? second) =>
        first is not null && second is not null && string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record DeletedSnapshot(bool IsDeleted, string RelativePath, PhysicalAssetDurability Durability, PhysicalAssetAvailability Availability,
        string FileName, long? LengthBytes, DateTimeOffset? LastWriteTimeUtc, DateTimeOffset ModifiedAt)
    {
        public static DeletedSnapshot Capture(ProjectAsset asset, VideoProject project) => new(asset.IsDeleted,
            asset.Physical!.RelativePath, asset.Physical.Durability, asset.Physical.Availability, asset.FileName,
            asset.Physical.ContentIdentity.LengthBytes, asset.Physical.ContentIdentity.ObservedLastWriteTimeUtc, project.ModifiedAt);
        public void Restore(ProjectAsset asset, VideoProject project)
        {
            asset.IsDeleted = IsDeleted;
            asset.Physical!.RelativePath = RelativePath;
            asset.Physical.Durability = Durability;
            asset.Physical.Availability = Availability;
            asset.FileName = FileName;
            asset.Physical.ContentIdentity.LengthBytes = LengthBytes;
            asset.Physical.ContentIdentity.ObservedLastWriteTimeUtc = LastWriteTimeUtc;
            project.ModifiedAt = ModifiedAt;
        }
    }
}
