using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Project-local file staging owned by Infrastructure. A staged copy is not project truth
/// until the application service has verified it and committed the project metadata.
/// </summary>
public interface IPhysicalAssetRelinkStager
{
    Task<StagedPhysicalAssetRelink> StageAsync(
        ProjectLocation location,
        ProjectAsset asset,
        string candidatePath,
        CancellationToken cancellationToken = default);

    Task DiscardAsync(
        StagedPhysicalAssetRelink staged,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Identifies the exact project-local artifact created by a relink stage. The signature lets
/// Infrastructure decline cleanup if another process has replaced the destination meanwhile.
/// </summary>
public sealed record StagedPhysicalAssetRelink(
    string DestinationPath,
    string Sha256,
    long LengthBytes,
    DateTimeOffset LastWriteTimeUtc);

public enum PhysicalAssetRelinkStatus
{
    Verified,
    Missing,
    Inaccessible,
    Mismatched,
    Failed,
    Cancelled,
    Stale
}

public sealed record PhysicalAssetRelinkResult(
    PhysicalAssetRelinkStatus Status,
    ProjectAssetDependencyReport DependencyReport,
    string? Detail = null);

public enum MissingPhysicalAssetProbeStatus
{
    NotApplicable,
    Verified,
    Missing,
    Inaccessible,
    Cancelled
}

/// <summary>
/// A read-only, verified candidate identity with live missing physical assets that can be
/// repaired from it. This is intentionally distinct from deleted-source restoration: missing
/// assets retain their existing logical identity and are repaired through RelinkMissing.
/// </summary>
public sealed record MissingPhysicalAssetRelinkProbe(
    MissingPhysicalAssetProbeStatus Status,
    ContentIdentity? CandidateIdentity,
    IReadOnlyList<MissingPhysicalAssetRelinkMatch> Matches,
    string? Detail = null);

public sealed record MissingPhysicalAssetRelinkMatch(
    Guid AssetId,
    string DisplayName,
    MediaType MediaType,
    ProjectAssetDependencyReport DependencyReport);

/// <summary>
/// Separates ordinary repair of a live missing asset from the explicit resurrection of a
/// deliberately deleted logical asset. Callers must never infer restore mode from a hash.
/// </summary>
public enum PhysicalAssetRelinkMode
{
    RelinkMissing,
    RestoreDeleted
}

/// <summary>
/// Relinks an existing physical asset only when the chosen bytes match its recorded verified
/// SHA-256 identity. It never adopts candidate bytes as a replacement identity.
/// </summary>
public sealed class PhysicalAssetRelinkService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IContentHashService _contentHashService;
    private readonly IPhysicalAssetRelinkStager _stager;
    private readonly ProjectAssetDependencyAnalyzer _dependencyAnalyzer;

    public PhysicalAssetRelinkService(
        ProjectWorkspace workspace,
        IContentHashService contentHashService,
        IPhysicalAssetRelinkStager stager,
        ProjectAssetDependencyAnalyzer dependencyAnalyzer)
    {
        _workspace = workspace;
        _contentHashService = contentHashService;
        _stager = stager;
        _dependencyAnalyzer = dependencyAnalyzer;
    }

    /// <summary>
    /// Hashes a dropped/imported candidate only when this project has an eligible live missing
    /// source of the same media type. It never mutates project state or chooses a match.
    /// </summary>
    public async Task<MissingPhysicalAssetRelinkProbe> ProbeMissingAsync(
        string candidatePath,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        if (!project.Assets.Any(asset => IsEligibleMissingIdentity(asset, mediaType)))
            return new MissingPhysicalAssetRelinkProbe(MissingPhysicalAssetProbeStatus.NotApplicable, null, []);

        ContentIdentity identity;
        try
        {
            identity = await _contentHashService.ComputeAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new MissingPhysicalAssetRelinkProbe(MissingPhysicalAssetProbeStatus.Missing, null, [], exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MissingPhysicalAssetRelinkProbe(MissingPhysicalAssetProbeStatus.Cancelled, null, []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new MissingPhysicalAssetRelinkProbe(MissingPhysicalAssetProbeStatus.Inaccessible, null, [], exception.Message);
        }

        return new MissingPhysicalAssetRelinkProbe(
            MissingPhysicalAssetProbeStatus.Verified,
            identity,
            FindMissingMatches(identity, mediaType));
    }

    public IReadOnlyList<MissingPhysicalAssetRelinkMatch> FindMissingMatches(
        ContentIdentity identity,
        MediaType mediaType)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        if (identity.Status != ContentHashStatus.Verified || !IsSha256(identity.Sha256)) return [];

        return project.Assets
            .Where(asset => IsEligibleMissingIdentity(asset, mediaType) &&
                            HashesEqual(asset.Physical!.ContentIdentity.Sha256, identity.Sha256))
            .Select(asset => new MissingPhysicalAssetRelinkMatch(
                asset.Id, asset.EffectiveDisplayName, asset.MediaType, _dependencyAnalyzer.Analyze(project, asset.Id)))
            .ToArray();
    }

    public async Task<PhysicalAssetRelinkResult> RelinkAsync(
        Guid assetId,
        string candidatePath,
        CancellationToken cancellationToken = default)
        => await RelinkAsync(assetId, candidatePath, PhysicalAssetRelinkMode.RelinkMissing, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Verifies and stages a candidate into project storage. RestoreDeleted is intentionally
    /// explicit: it is the only mode allowed to clear a deletion tombstone.
    /// </summary>
    public async Task<PhysicalAssetRelinkResult> RelinkAsync(
        Guid assetId,
        string candidatePath,
        PhysicalAssetRelinkMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("Create or open a project first.");
        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == assetId)
            ?? throw new InvalidOperationException($"Asset '{assetId}' does not exist in this project.");
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException("Only physical assets can be relinked.");
        if (mode == PhysicalAssetRelinkMode.RelinkMissing && asset.IsDeleted)
            throw new InvalidOperationException("Deleted media must be explicitly restored, not relinked.");
        if (mode == PhysicalAssetRelinkMode.RestoreDeleted && !asset.IsDeleted)
            throw new InvalidOperationException("Only deleted media can be restored.");

        var identity = asset.Physical.ContentIdentity;
        if (identity.Status != ContentHashStatus.Verified || !IsSha256(identity.Sha256))
        {
            throw new InvalidOperationException(
                "Relinking requires an existing verified SHA-256 identity for the physical asset.");
        }

        var dependencies = _dependencyAnalyzer.Analyze(project, asset.Id);
        try
        {
            await _workspace.EnsurePhysicalAssetRelinkCanStartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Cancelled, dependencies);
        }

        ContentVerificationResult candidateVerification;
        try
        {
            candidateVerification = await _contentHashService
                .VerifyAsync(candidatePath, identity, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Missing, dependencies, exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Cancelled, dependencies);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Inaccessible, dependencies, exception.Message);
        }

        if (!candidateVerification.MatchesExpected)
            return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Mismatched, dependencies);

        StagedPhysicalAssetRelink? staged = null;
        var snapshot = PhysicalSnapshot.Capture(asset, project);
        var transactionRollbackHandled = false;
        Exception? transactionRollbackFailure = null;
        try
        {
            staged = await _stager.StageAsync(location, asset, candidatePath, cancellationToken).ConfigureAwait(false);
            var stagedVerification = await _contentHashService
                .VerifyAsync(staged.DestinationPath, identity, cancellationToken)
                .ConfigureAwait(false);
            if (!stagedVerification.MatchesExpected)
                return await RollBackAsync(
                    PhysicalAssetRelinkStatus.Failed,
                    "The staged project copy did not match the recorded SHA-256 identity.",
                    dependencies, asset, project, location, snapshot, staged).ConfigureAwait(false);

            void ApplyRelinkMetadata()
            {
                asset.Physical.RelativePath = ProjectPathPolicy.GetRelativePath(location, staged.DestinationPath);
                asset.Physical.Availability = PhysicalAssetAvailability.Available;
                asset.Physical.ContentIdentity.LengthBytes = stagedVerification.Observed.LengthBytes;
                asset.Physical.ContentIdentity.ObservedLastWriteTimeUtc = stagedVerification.Observed.ObservedLastWriteTimeUtc;
                asset.FileName = Path.GetFileName(staged.DestinationPath);
                if (mode == PhysicalAssetRelinkMode.RestoreDeleted)
                    asset.IsDeleted = false;
            }

            async Task RollBackUncommittedAsync()
            {
                snapshot.Restore(asset, project);
                if (staged is not null)
                {
                    try
                    {
                        await _stager.DiscardAsync(staged, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        transactionRollbackFailure ??= exception;
                    }
                }

                transactionRollbackHandled = true;
            }

            var save = await _workspace
                .SavePhysicalAssetRelinkIfCurrentAsync(
                    project, location, ApplyRelinkMetadata, RollBackUncommittedAsync, cancellationToken)
                .ConfigureAwait(false);
            if (!save.Committed)
            {
                if (cancellationToken.IsCancellationRequested || save.Failure is OperationCanceledException)
                    return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Cancelled, dependencies);

                var detail = save.Failure?.Message ?? "The project changed before the relink could be saved.";
                if (transactionRollbackFailure is not null)
                    detail = $"{detail} Rollback cleanup failed: {transactionRollbackFailure.Message}";
                return new PhysicalAssetRelinkResult(
                    save.Failure is null ? PhysicalAssetRelinkStatus.Stale : PhysicalAssetRelinkStatus.Failed,
                    dependencies,
                    detail);
            }

            return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Verified, dependencies);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (transactionRollbackHandled)
                return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Cancelled, dependencies);

            return await RollBackAsync(
                PhysicalAssetRelinkStatus.Cancelled,
                null,
                dependencies, asset, project, location, snapshot, staged).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (transactionRollbackHandled)
                return new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Failed, dependencies, exception.Message);

            return await RollBackAsync(
                PhysicalAssetRelinkStatus.Failed,
                exception.Message,
                dependencies, asset, project, location, snapshot, staged).ConfigureAwait(false);
        }
    }

    private async Task<PhysicalAssetRelinkResult> RollBackAsync(
        PhysicalAssetRelinkStatus status,
        string? detail,
        ProjectAssetDependencyReport dependencies,
        ProjectAsset asset,
        VideoProject project,
        ProjectLocation location,
        PhysicalSnapshot snapshot,
        StagedPhysicalAssetRelink? staged)
    {
        snapshot.Restore(asset, project);
        Exception? rollbackFailure = null;
        if (staged is not null)
        {
            try
            {
                await _stager.DiscardAsync(staged, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                rollbackFailure = exception;
            }
        }

        return rollbackFailure is null
            ? new PhysicalAssetRelinkResult(status, dependencies, detail)
            : new PhysicalAssetRelinkResult(
                PhysicalAssetRelinkStatus.Failed,
                dependencies,
                $"{detail ?? "Relink rollback failed."} Rollback cleanup failed: {rollbackFailure.Message}");
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsEligibleMissingIdentity(ProjectAsset asset, MediaType mediaType) =>
        !asset.IsDeleted && asset.StorageKind == AssetStorageKind.Physical && asset.Physical is not null &&
        asset.MediaType == mediaType && asset.Physical.Availability == PhysicalAssetAvailability.Missing &&
        asset.Physical.ContentIdentity.Status == ContentHashStatus.Verified && IsSha256(asset.Physical.ContentIdentity.Sha256);

    private static bool HashesEqual(string? first, string? second) =>
        first is not null && second is not null && string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private sealed record PhysicalSnapshot(
        bool IsDeleted,
        string RelativePath,
        PhysicalAssetAvailability Availability,
        string FileName,
        long? LengthBytes,
        DateTimeOffset? ObservedLastWriteTimeUtc,
        DateTimeOffset ModifiedAt)
    {
        public static PhysicalSnapshot Capture(ProjectAsset asset, VideoProject project) => new(
            asset.IsDeleted,
            asset.Physical!.RelativePath,
            asset.Physical.Availability,
            asset.FileName,
            asset.Physical.ContentIdentity.LengthBytes,
            asset.Physical.ContentIdentity.ObservedLastWriteTimeUtc,
            project.ModifiedAt);

        public void Restore(ProjectAsset asset, VideoProject project)
        {
            asset.IsDeleted = IsDeleted;
            asset.Physical!.RelativePath = RelativePath;
            asset.Physical.Availability = Availability;
            asset.FileName = FileName;
            asset.Physical.ContentIdentity.LengthBytes = LengthBytes;
            asset.Physical.ContentIdentity.ObservedLastWriteTimeUtc = ObservedLastWriteTimeUtc;
            project.ModifiedAt = ModifiedAt;
        }
    }
}
