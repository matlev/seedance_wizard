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
        Guid? selectedProjectId,
        bool isFfprobeAvailable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (!IsSelectedProjectCurrent(selectedProjectId))
            return PhysicalAssetSelectionPreparationResult.Stale;

        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            return PhysicalAssetSelectionPreparationResult.Ready;

        var absolutePath = _workspace.GetAbsoluteAssetPath(asset);
        if (!File.Exists(absolutePath))
        {
            asset.Physical.Availability = PhysicalAssetAvailability.Missing;
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return IsSelectedProjectCurrent(selectedProjectId)
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
            if (!IsSelectedProjectCurrent(selectedProjectId))
                return PhysicalAssetSelectionPreparationResult.Stale;

            asset.Encoding = encoding;
            asset.DurationSeconds = encoding.DurationSeconds;
            asset.Width = encoding.Video?.Width;
            asset.Height = encoding.Video?.Height;
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            if (!IsSelectedProjectCurrent(selectedProjectId))
                return PhysicalAssetSelectionPreparationResult.Stale;
        }

        return PhysicalAssetSelectionPreparationResult.Ready;
    }

    private bool IsSelectedProjectCurrent(Guid? selectedProjectId) =>
        selectedProjectId is not null && _workspace.Project?.Id == selectedProjectId;
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
