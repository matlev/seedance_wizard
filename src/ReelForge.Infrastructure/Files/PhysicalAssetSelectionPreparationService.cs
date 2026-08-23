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

    public PhysicalAssetSelectionPreparationService(
        ProjectWorkspace workspace,
        IMediaInspectionService mediaInspector)
    {
        _workspace = workspace;
        _mediaInspector = mediaInspector;
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

        var absolutePath = ProjectPathPolicy.ResolveContainedPath(selectedLocation, asset.Physical.RelativePath);
        if (!File.Exists(absolutePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSelectedProjectCurrent(selectedProject, selectedLocation))
                return PhysicalAssetSelectionPreparationResult.Stale;

            asset.Physical.Availability = PhysicalAssetAvailability.Missing;
            var saved = await _workspace
                .SaveIfCurrentAsync(selectedProject, selectedLocation, cancellationToken)
                .ConfigureAwait(false);
            return saved
                ? PhysicalAssetSelectionPreparationResult.Missing
                : PhysicalAssetSelectionPreparationResult.Stale;
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

    private bool IsSelectedProjectCurrent(VideoProject selectedProject, ProjectLocation selectedLocation) =>
        ReferenceEquals(_workspace.Project, selectedProject) &&
        ReferenceEquals(_workspace.Location, selectedLocation);
}

public enum PhysicalAssetSelectionPreparationKind
{
    Ready,
    Missing,
    Stale
}

public sealed record PhysicalAssetSelectionPreparationResult(PhysicalAssetSelectionPreparationKind Kind)
{
    public static readonly PhysicalAssetSelectionPreparationResult Ready = new(PhysicalAssetSelectionPreparationKind.Ready);
    public static readonly PhysicalAssetSelectionPreparationResult Missing = new(PhysicalAssetSelectionPreparationKind.Missing);
    public static readonly PhysicalAssetSelectionPreparationResult Stale = new(PhysicalAssetSelectionPreparationKind.Stale);
}
