using ReelForge.App.Views.ProjectMedia;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class ProjectMediaMissingPresentationTests
{
    [Fact]
    public void MissingPhysicalAssetUsesWarningPresentationAndRestrictedMenu()
    {
        var asset = PhysicalAsset(PhysicalAssetAvailability.Missing);
        var item = new ProjectMediaListItem(asset);

        Assert.True(item.IsMissingPhysicalAsset);
        Assert.Equal("⚠", item.Glyph);
        Assert.Contains("Right-click", item.GlyphToolTip);
        Assert.True(ProjectMediaContextMenuPolicy.UsesMissingAssetMenu(asset));
    }

    [Theory]
    [InlineData(PhysicalAssetAvailability.Unknown)]
    [InlineData(PhysicalAssetAvailability.Available)]
    [InlineData(PhysicalAssetAvailability.Inaccessible)]
    [InlineData(PhysicalAssetAvailability.Mismatched)]
    public void OtherPhysicalAssetStatesRetainNormalPresentation(PhysicalAssetAvailability availability)
    {
        var asset = PhysicalAsset(availability);
        var item = new ProjectMediaListItem(asset);

        Assert.False(item.IsMissingPhysicalAsset);
        Assert.Equal("▶", item.Glyph);
        Assert.Null(item.GlyphToolTip);
        Assert.False(ProjectMediaContextMenuPolicy.UsesMissingAssetMenu(asset));
    }

    private static ProjectAsset PhysicalAsset(PhysicalAssetAvailability availability) => new()
    {
        FileName = "source.mp4",
        DisplayName = "source.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage { Availability = availability }
    };
}
