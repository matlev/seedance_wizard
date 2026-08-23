using ReelForge.App.Views.ProjectMedia;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class ProjectMediaRenamePolicyTests
{
    [Fact]
    public void GetKindIdentifiesPhysicalFilesAndSavedClipsOnly()
    {
        var physical = new ProjectAsset
        {
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage()
        };
        var savedClip = new ProjectAsset
        {
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        var composition = new ProjectAsset
        {
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };

        Assert.Equal(ProjectMediaRenameKind.PhysicalFile, ProjectMediaRenamePolicy.GetKind(physical));
        Assert.Equal(ProjectMediaRenameKind.SavedClip, ProjectMediaRenamePolicy.GetKind(savedClip));
        Assert.Equal(ProjectMediaRenameKind.None, ProjectMediaRenamePolicy.GetKind(composition));
        Assert.Equal(ProjectMediaRenameKind.None, ProjectMediaRenamePolicy.GetKind(null));
    }
}
