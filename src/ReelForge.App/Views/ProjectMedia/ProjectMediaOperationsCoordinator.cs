using ReelForge.Application;
using ReelForge.App.Views.Editing;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Coordinates narrowly-scoped Project Media mutations whose persistence is owned
/// by application or infrastructure services. UI policy remains with the shell.
/// </summary>
public sealed class ProjectMediaOperationsCoordinator : ICompositionRenderOperations
{
    private readonly ProjectWorkspace _workspace;
    private readonly SavedClipService _savedClipService;
    private readonly RenderedAssetPromotionService _renderedAssetPromotionService;
    private readonly AudioExtractionService _audioExtractionService;
    private readonly ProjectAssetDependencyAnalyzer _dependencyAnalyzer;
    private readonly PhysicalAssetRemovalService _physicalAssetRemovalService;
    private readonly ProjectAssetTransferWorkflow _projectAssetTransferWorkflow;
    private readonly MaterializedProjectMediaTransferService _materializedProjectMediaTransferService;

    public ProjectMediaOperationsCoordinator(
        ProjectWorkspace workspace,
        RenderedAssetPromotionService renderedAssetPromotionService,
        AudioExtractionService audioExtractionService,
        ProjectAssetDependencyAnalyzer dependencyAnalyzer,
        PhysicalAssetRemovalService physicalAssetRemovalService,
        ProjectAssetTransferWorkflow projectAssetTransferWorkflow,
        MaterializedProjectMediaTransferService materializedProjectMediaTransferService)
    {
        _workspace = workspace;
        _savedClipService = new SavedClipService(workspace);
        _renderedAssetPromotionService = renderedAssetPromotionService;
        _audioExtractionService = audioExtractionService;
        _dependencyAnalyzer = dependencyAnalyzer;
        _physicalAssetRemovalService = physicalAssetRemovalService;
        _projectAssetTransferWorkflow = projectAssetTransferWorkflow;
        _materializedProjectMediaTransferService = materializedProjectMediaTransferService;
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

    public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
        IReadOnlyCollection<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        return _workspace.ImportAssetsAsync(sourcePaths, cancellationToken);
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

    public ProjectAssetDependencyReport AnalyzeDependencies(ProjectAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        return _dependencyAnalyzer.Analyze(project, asset.Id);
    }

    public Task DeletePhysicalAssetAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        _physicalAssetRemovalService.RemoveAsync(_workspace, assetId, cancellationToken);

    public Task DeleteSavedClipAsync(Guid assetId, CancellationToken cancellationToken = default) =>
        _savedClipService.DeleteAsync(assetId, cancellationToken);

    public Task<ProjectAssetCopyResult> CopyPhysicalAssetToProjectAsync(
        ProjectAsset asset,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default) =>
        _projectAssetTransferWorkflow.CopyAsync(asset, targetProjectFilePath, cancellationToken);

    public Task<ProjectAssetMoveResult> MovePhysicalAssetToProjectAsync(
        ProjectAsset asset,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default) =>
        _projectAssetTransferWorkflow.MoveAsync(asset, targetProjectFilePath, cancellationToken);

    public Task<ProjectAssetCopyResult> CopySavedFrameToProjectAsync(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        string requestedFileName,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default) =>
        _materializedProjectMediaTransferService.CopySavedFrameAsync(
            anchor, revision, requestedFileName, targetProjectFilePath, cancellationToken);

    public Task<ProjectAssetCopyResult> CopyVirtualVideoToProjectAsync(
        ProjectAsset asset,
        Guid recipeRevisionId,
        string requestedFileName,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default) =>
        _materializedProjectMediaTransferService.CopyVirtualVideoAsync(
            asset, recipeRevisionId, requestedFileName, targetProjectFilePath, cancellationToken);
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
