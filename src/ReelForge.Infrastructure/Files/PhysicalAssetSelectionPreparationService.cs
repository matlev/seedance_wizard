using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// Resolves a physical asset's file availability and lazily persists its media metadata.
/// Presentation routing remains with the caller.
/// </summary>
public sealed class PhysicalAssetSelectionPreparationService
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
