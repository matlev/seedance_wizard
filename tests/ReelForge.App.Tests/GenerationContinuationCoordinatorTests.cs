using ReelForge.App.Views.Generation;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class GenerationContinuationCoordinatorTests
{
    [Fact]
    public void DeletedPhysicalVideoIsNotEligibleForContinuation()
    {
        var deleted = new ProjectAsset
        {
            IsDeleted = true,
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage { Availability = PhysicalAssetAvailability.Missing }
        };

        Assert.False(GenerationContinuationCoordinator.CanContinueFrom(deleted));
    }

    [Fact]
    public void AvailablePhysicalVideoRemainsEligibleForContinuation()
    {
        var source = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage { Availability = PhysicalAssetAvailability.Available }
        };

        Assert.True(GenerationContinuationCoordinator.CanContinueFrom(source));
    }
}
