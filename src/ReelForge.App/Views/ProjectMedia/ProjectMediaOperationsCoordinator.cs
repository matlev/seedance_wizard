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

    public ProjectMediaOperationsCoordinator(ProjectWorkspace workspace)
    {
        _workspace = workspace;
        _savedClipService = new SavedClipService(workspace);
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
