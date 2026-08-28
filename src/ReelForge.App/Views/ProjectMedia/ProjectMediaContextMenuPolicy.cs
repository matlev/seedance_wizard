using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

public static class ProjectMediaContextMenuPolicy
{
    public static bool UsesMissingAssetMenu(ProjectAsset? asset) => asset is
    {
        StorageKind: AssetStorageKind.Physical,
        Physical.Availability: PhysicalAssetAvailability.Missing
    };
}
