using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Determines whether an audio operation should be offered before media is
/// materialized and inspected. Saved Clip metadata from older projects may omit
/// stream details, so their realized media remains the authority.
/// </summary>
public static class MediaAudioCapabilityPolicy
{
    public static bool CanAttemptAudioOperation(ProjectAsset? asset)
    {
        if (asset?.MediaType != MediaType.Video)
            return false;

        if (asset.StorageKind == AssetStorageKind.Physical)
            return asset.Encoding?.Audio is not null || asset.Encoding is null;

        return asset.Virtual?.Kind == VirtualAssetKind.SavedClip;
    }
}
