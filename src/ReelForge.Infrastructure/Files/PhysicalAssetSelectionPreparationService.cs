using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// Resolves a physical asset's file availability and lazily persists its media metadata.
/// Presentation routing remains with the caller.
/// </summary>
public sealed class PhysicalAssetSelectionPreparationService : IPhysicalAssetAvailabilityReconciler
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaInspectionService _mediaInspector;
    private readonly IContentHashService _contentHashService;

    public PhysicalAssetSelectionPreparationService(
        ProjectWorkspace workspace,
        IMediaInspectionService mediaInspector,
        IContentHashService? contentHashService = null)
    {
        _workspace = workspace;
        _mediaInspector = mediaInspector;
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
    }

    public async Task<PhysicalAssetSelectionPreparationResult> PrepareAsync(
        ProjectAsset asset,
        VideoProject selectedProject,
        ProjectLocation selectedLocation,
        bool isFfprobeAvailable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(selectedProject);
        ArgumentNullException.ThrowIfNull(selectedLocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
            return PhysicalAssetSelectionPreparationResult.Stale;

        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            return PhysicalAssetSelectionPreparationResult.Ready;

        string absolutePath;
        try
        {
            absolutePath = ProjectPathPolicy.ResolveContainedPath(selectedLocation, asset.Physical.RelativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return await PersistAvailabilityAsync(
                asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Inaccessible, cancellationToken)
                .ConfigureAwait(false);
        }
        if (asset.Physical.ContentIdentity is { Status: ContentHashStatus.Verified, Sha256: { } })
        {
            ContentVerificationResult verification;
            try
            {
                verification = await _contentHashService
                    .VerifyAsync(absolutePath, asset.Physical.ContentIdentity, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Missing, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DirectoryNotFoundException)
            {
                return await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Missing, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Inaccessible, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
                return PhysicalAssetSelectionPreparationResult.Stale;
            if (!verification.MatchesExpected)
            {
                // Do not replace the expected verified identity with observed mismatching bytes.
                return await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Mismatched, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (asset.Physical.Availability != PhysicalAssetAvailability.Available)
            {
                var availability = await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Available, cancellationToken)
                    .ConfigureAwait(false);
                if (availability.Kind == PhysicalAssetSelectionPreparationKind.Stale)
                    return availability;
            }
        }
        else
        {
            var availability = await ProbeAvailabilityAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            if (availability is not null)
            {
                return await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, availability.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (asset.Physical.Availability != PhysicalAssetAvailability.Available)
            {
                var persisted = await PersistAvailabilityAsync(
                    asset, selectedProject, selectedLocation, PhysicalAssetAvailability.Available, cancellationToken)
                    .ConfigureAwait(false);
                if (persisted.Kind == PhysicalAssetSelectionPreparationKind.Stale)
                    return persisted;
            }
        }

        if (asset.MediaType is MediaType.Video or MediaType.Audio &&
            asset.Encoding is null &&
            isFfprobeAvailable)
        {
            var encoding = await _mediaInspector
                .InspectAsync(absolutePath, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
                return PhysicalAssetSelectionPreparationResult.Stale;

            asset.Encoding = encoding;
            asset.DurationSeconds = encoding.DurationSeconds;
            asset.Width = encoding.Video?.Width;
            asset.Height = encoding.Video?.Height;
            var saved = await _workspace
                .SaveIfCurrentAsync(selectedProject, selectedLocation, cancellationToken)
                .ConfigureAwait(false);
            if (!saved)
                return PhysicalAssetSelectionPreparationResult.Stale;
        }

        return PhysicalAssetSelectionPreparationResult.Ready;
    }

    /// <summary>
    /// Reconciles availability for every active physical asset in the captured project session.
    /// File observations are collected before any project state changes, then persisted through one
    /// recovery-aware workspace mutation so derived-media analysis sees a coherent availability view.
    /// </summary>
    public async Task<PhysicalAssetAvailabilityReconciliationResult> ReconcileActivePhysicalAssetsAsync(
        VideoProject selectedProject,
        ProjectLocation selectedLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedProject);
        ArgumentNullException.ThrowIfNull(selectedLocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
            return PhysicalAssetAvailabilityReconciliationResult.Stale;

        var observations = new List<AvailabilityObservation>();
        foreach (var asset in selectedProject.Assets.Where(asset =>
                     !asset.IsDeleted &&
                     asset.StorageKind == AssetStorageKind.Physical &&
                     asset.Physical is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var availability = await DetermineAvailabilityAsync(asset, selectedLocation, cancellationToken)
                .ConfigureAwait(false);
            observations.Add(new AvailabilityObservation(asset, availability));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
            return PhysicalAssetAvailabilityReconciliationResult.Stale;

        var changes = observations.Where(observation =>
                observation.Asset.Physical!.Availability != observation.Availability)
            .ToArray();
        if (changes.Length == 0)
            return PhysicalAssetAvailabilityReconciliationResult.Unchanged;

        var priorModifiedAt = selectedProject.ModifiedAt;
        var priorAvailabilities = changes.ToDictionary(
            change => change.Asset.Id,
            change => change.Asset.Physical!.Availability);
        var mutation = await _workspace.SaveMutationIfCurrentAsync(
                selectedProject,
                selectedLocation,
                () =>
                {
                    foreach (var change in changes)
                        change.Asset.Physical!.Availability = change.Availability;
                },
                () =>
                {
                    foreach (var change in changes)
                        change.Asset.Physical!.Availability = priorAvailabilities[change.Asset.Id];
                    selectedProject.ModifiedAt = priorModifiedAt;
                    return Task.CompletedTask;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!mutation.Committed)
        {
            return mutation.Failure is null
                ? PhysicalAssetAvailabilityReconciliationResult.Stale
                : new PhysicalAssetAvailabilityReconciliationResult(false, false, mutation.Failure);
        }

        return new PhysicalAssetAvailabilityReconciliationResult(true, false);
    }

    private async Task<PhysicalAssetAvailability> DetermineAvailabilityAsync(
        ProjectAsset asset,
        ProjectLocation location,
        CancellationToken cancellationToken)
    {
        string absolutePath;
        try
        {
            absolutePath = ProjectPathPolicy.ResolveContainedPath(location, asset.Physical!.RelativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return PhysicalAssetAvailability.Inaccessible;
        }

        var probe = await ProbeReadableFileAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        if (probe.Availability is { } unavailable)
            return unavailable;

        var identity = asset.Physical!.ContentIdentity;
        if (!HasVerifiedSha256(identity))
            return PhysicalAssetAvailability.Available;

        if (HasTrustedPersistedSignature(identity, probe.Signature))
            return PhysicalAssetAvailability.Available;

        try
        {
            var verification = await _contentHashService
                .VerifyAsync(absolutePath, identity, cancellationToken)
                .ConfigureAwait(false);
            return verification.MatchesExpected
                ? PhysicalAssetAvailability.Available
                : PhysicalAssetAvailability.Mismatched;
        }
        catch (FileNotFoundException)
        {
            return PhysicalAssetAvailability.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return PhysicalAssetAvailability.Missing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PhysicalAssetAvailability.Inaccessible;
        }
    }

    private static bool HasVerifiedSha256(ContentIdentity identity) =>
        identity.Status == ContentHashStatus.Verified &&
        string.Equals(identity.Algorithm, ContentIdentity.Sha256Algorithm, StringComparison.OrdinalIgnoreCase) &&
        identity.Sha256 is { Length: 64 } sha256 && sha256.All(Uri.IsHexDigit);

    private static bool HasTrustedPersistedSignature(ContentIdentity identity, FileSignature? signature) =>
        signature is not null &&
        identity.LengthBytes == signature.LengthBytes &&
        identity.ObservedLastWriteTimeUtc == signature.LastWriteTimeUtc;

    private static async Task<FileProbe> ProbeReadableFileAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var info = new FileInfo(absolutePath);
            return new FileProbe(null, new FileSignature(
                stream.Length,
                new DateTimeOffset(info.LastWriteTimeUtc)));
        }
        catch (FileNotFoundException)
        {
            return new FileProbe(PhysicalAssetAvailability.Missing, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new FileProbe(PhysicalAssetAvailability.Missing, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new FileProbe(PhysicalAssetAvailability.Inaccessible, null);
        }
    }

    private static async Task<PhysicalAssetAvailability?> ProbeAvailabilityAsync(
        string absolutePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(
                absolutePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            _ = stream.Length;
            return null;
        }
        catch (FileNotFoundException)
        {
            return PhysicalAssetAvailability.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return PhysicalAssetAvailability.Missing;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PhysicalAssetAvailability.Inaccessible;
        }
    }

    private sealed record AvailabilityObservation(ProjectAsset Asset, PhysicalAssetAvailability Availability);
    private sealed record FileSignature(long LengthBytes, DateTimeOffset LastWriteTimeUtc);
    private sealed record FileProbe(PhysicalAssetAvailability? Availability, FileSignature? Signature);

    private bool IsSelectedProjectCurrent(VideoProject selectedProject, ProjectLocation selectedLocation) =>
        ReferenceEquals(_workspace.Project, selectedProject) &&
        ReferenceEquals(_workspace.Location, selectedLocation);

    private async Task<PhysicalAssetSelectionPreparationResult> PersistAvailabilityAsync(
        ProjectAsset asset,
        VideoProject selectedProject,
        ProjectLocation selectedLocation,
        PhysicalAssetAvailability availability,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
            return PhysicalAssetSelectionPreparationResult.Stale;

        asset.Physical!.Availability = availability;
        var saved = await _workspace
            .SaveIfCurrentAsync(selectedProject, selectedLocation, cancellationToken)
            .ConfigureAwait(false);
        if (!saved)
            return PhysicalAssetSelectionPreparationResult.Stale;
        return availability switch
        {
            PhysicalAssetAvailability.Missing => PhysicalAssetSelectionPreparationResult.Missing,
            PhysicalAssetAvailability.Inaccessible => PhysicalAssetSelectionPreparationResult.Inaccessible,
            PhysicalAssetAvailability.Mismatched => PhysicalAssetSelectionPreparationResult.Mismatched,
            _ => PhysicalAssetSelectionPreparationResult.Ready
        };
    }
}

public enum PhysicalAssetSelectionPreparationKind
{
    Ready,
    Missing,
    Inaccessible,
    Mismatched,
    Stale
}

public sealed record PhysicalAssetSelectionPreparationResult(PhysicalAssetSelectionPreparationKind Kind)
{
    public static readonly PhysicalAssetSelectionPreparationResult Ready = new(PhysicalAssetSelectionPreparationKind.Ready);
    public static readonly PhysicalAssetSelectionPreparationResult Missing = new(PhysicalAssetSelectionPreparationKind.Missing);
    public static readonly PhysicalAssetSelectionPreparationResult Inaccessible = new(PhysicalAssetSelectionPreparationKind.Inaccessible);
    public static readonly PhysicalAssetSelectionPreparationResult Mismatched = new(PhysicalAssetSelectionPreparationKind.Mismatched);
    public static readonly PhysicalAssetSelectionPreparationResult Stale = new(PhysicalAssetSelectionPreparationKind.Stale);
}
