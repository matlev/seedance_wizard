using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class MediaAudioCapabilityPolicyTests
{
    [Fact]
    public void SavedClipWithLegacyMissingAudioMetadataCanStillBeMaterializedAndInspected()
    {
        var clip = SavedClip(new MediaEncodingMetadata
        {
            DurationSeconds = 4,
            Video = new VideoStreamMetadata { Codec = "h264" },
            Audio = null
        });

        Assert.True(MediaAudioCapabilityPolicy.CanAttemptAudioOperation(clip));
    }

    [Fact]
    public void PhysicalVideoKnownToHaveNoAudioIsNotOfferedAudioOperations()
    {
        var video = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Encoding = new MediaEncodingMetadata
            {
                Video = new VideoStreamMetadata { Codec = "h264" },
                Audio = null
            }
        };

        Assert.False(MediaAudioCapabilityPolicy.CanAttemptAudioOperation(video));
    }

    [Fact]
    public void UninspectedPhysicalVideoCanStillAttemptAudioOperation()
    {
        var video = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical
        };

        Assert.True(MediaAudioCapabilityPolicy.CanAttemptAudioOperation(video));
    }

    private static ProjectAsset SavedClip(MediaEncodingMetadata properties) => new()
    {
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Virtual,
        Virtual = new VirtualAssetState
        {
            Kind = VirtualAssetKind.SavedClip,
            ExpectedMediaProperties = properties
        }
    };
}
