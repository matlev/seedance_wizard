using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Coordinates narrowly-scoped Project Media mutations whose persistence is owned
/// by application or infrastructure services. UI policy remains with the shell.
/// </summary>
public sealed class ProjectMediaOperationsCoordinator
{
    private readonly ProjectWorkspace _workspace;
    private readonly SavedClipService _savedClipService;
    private readonly RenderedAssetPromotionService _renderedAssetPromotionService;
    private readonly AudioExtractionService _audioExtractionService;

    public ProjectMediaOperationsCoordinator(
        ProjectWorkspace workspace,
        RenderedAssetPromotionService renderedAssetPromotionService,
        AudioExtractionService audioExtractionService)
    {
        _workspace = workspace;
        _savedClipService = new SavedClipService(workspace);
        _renderedAssetPromotionService = renderedAssetPromotionService;
        _audioExtractionService = audioExtractionService;
    }

    public async Task RenameAsync(
        ProjectAsset asset,
        string requestedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var kind = ProjectMediaRenamePolicy.GetKind(asset);
        switch (kind)
        {
            case ProjectMediaRenameKind.PhysicalFile:
                await PhysicalAssetFileRenameService
                    .RenameAsync(_workspace, asset, requestedName, cancellationToken)
                    .ConfigureAwait(false);
                return;
            case ProjectMediaRenameKind.SavedClip:
                await _savedClipService
                    .RenameAsync(asset.Id, requestedName, cancellationToken)
                    .ConfigureAwait(false);
                return;
            default:
                throw new InvalidOperationException("This Project Media item cannot be renamed.");
        }
    }

    public Task<string> ExportSavedFrameAsync(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(revision);
        return _renderedAssetPromotionService.ExportFrameAsync(
            anchor.Id,
            revision.Id,
            destinationPath,
            cancellationToken);
    }

    public Task<string> ExportVirtualVideoAsync(
        ProjectAsset asset,
        Guid recipeRevisionId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _renderedAssetPromotionService.ExportAsync(
            asset.Id,
            recipeRevisionId,
            destinationPath,
            cancellationToken);
    }

    public Task<ProjectAsset> ExtractAudioAsync(
        ProjectAsset source,
        Guid? recipeRevisionId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return _audioExtractionService.ExtractAsAssetAsync(
            source.Id,
            recipeRevisionId,
            requestedFileName,
            cancellationToken);
    }
}

public enum ProjectMediaRenameKind
{
    None,
    PhysicalFile,
    SavedClip
}

public static class ProjectMediaRenamePolicy
{
    public static ProjectMediaRenameKind GetKind(ProjectAsset? asset) => asset switch
    {
        { StorageKind: AssetStorageKind.Physical, Physical: not null } => ProjectMediaRenameKind.PhysicalFile,
        { StorageKind: AssetStorageKind.Virtual, Virtual.Kind: VirtualAssetKind.SavedClip } => ProjectMediaRenameKind.SavedClip,
        _ => ProjectMediaRenameKind.None
    };
}
