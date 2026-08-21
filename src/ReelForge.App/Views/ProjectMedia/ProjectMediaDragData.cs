using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

internal static class ProjectMediaDragData
{
    public const string Format = "ReelForge.ProjectMediaAssetId";

    public static bool CanAddToComposition(ProjectAsset asset) =>
        asset is { MediaType: MediaType.Audio, StorageKind: AssetStorageKind.Physical } ||
        asset.MediaType == MediaType.Video &&
        (asset.StorageKind == AssetStorageKind.Physical || asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
}
