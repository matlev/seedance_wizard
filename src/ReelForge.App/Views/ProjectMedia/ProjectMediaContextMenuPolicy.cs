using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

public static class ProjectMediaContextMenuPolicy
{
    public static bool CanRelink(ProjectAsset? asset) => asset is
    {
        StorageKind: AssetStorageKind.Physical,
        Physical.Availability: PhysicalAssetAvailability.Missing
    };

    public static bool UsesMissingAssetMenu(ProjectAsset? asset) => CanRelink(asset);
}
