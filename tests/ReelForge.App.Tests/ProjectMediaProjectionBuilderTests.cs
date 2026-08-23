using System.Windows.Media.Imaging;
using ReelForge.App.Views.Generation;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class ProjectMediaProjectionBuilderTests
{
    [Fact]
    public void BuildGroupsAndSortsMediaExcludesArchivedAnchorsAndPreservesReferenceChoices()
    {
        var video = PhysicalAsset("Zulu video.mp4", MediaType.Video);
        var earlierVideo = PhysicalAsset("Alpha video.mp4", MediaType.Video);
        var imageWithMissingThumbnail = PhysicalAsset("Alpha image.png", MediaType.Image);
        var audio = PhysicalAsset("Bravo audio.wav", MediaType.Audio);
        var clip = VirtualAsset("Charlie saved clip", VirtualAssetKind.SavedClip);
        var composition = VirtualAsset("Working Composition", VirtualAssetKind.Composition);
        var visibleAnchor = new FrameAnchor { DisplayLabel = "Alpha saved frame" };
        var archivedAnchor = new FrameAnchor { DisplayLabel = "Hidden frame", IsArchived = true };
        var visibleRevision = AnchorRevision(visibleAnchor.Id, video.Id);
        var archivedRevision = AnchorRevision(archivedAnchor.Id, video.Id);
        visibleAnchor.CurrentRevisionId = visibleRevision.Id;
        archivedAnchor.CurrentRevisionId = archivedRevision.Id;
        var retainedChoice = new GenerationReferenceChoice(video, order: 42)
        {
            IsSelected = true,
            Role = GenerationReferenceRole.Character,
            Label = "Retained label",
            Notes = "Retained notes"
        };
        var oldest = new GenerationRecord { RequestedAt = new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero) };
        var newest = new GenerationRecord { RequestedAt = new DateTimeOffset(2026, 8, 2, 1, 0, 0, TimeSpan.Zero) };
        var project = new VideoProject
        {
            Assets = [video, earlierVideo, imageWithMissingThumbnail, audio, clip, composition],
            Anchors = [archivedAnchor, visibleAnchor],
            AnchorRevisions = [archivedRevision, visibleRevision],
            Generations = [oldest, newest]
        };

        var projection = ProjectMediaProjectionBuilder.Build(
            project,
            _ => "C:\\media\\missing-thumbnail.png",
            _ => throw new InvalidOperationException("Missing files must not be decoded."),
            [retainedChoice]);

        Assert.Collection(projection.MediaItems,
            item => Assert.Equal("Alpha video.mp4", item.DisplayName),
            item => Assert.Equal("Zulu video.mp4", item.DisplayName),
            item => Assert.Equal("Alpha image.png", item.DisplayName),
            item => Assert.Equal("Bravo audio.wav", item.DisplayName),
            item => Assert.Equal("Alpha saved frame", item.DisplayName),
            item => Assert.Equal("Charlie saved clip", item.DisplayName),
            item => Assert.Equal("Working Composition", item.DisplayName));
        Assert.DoesNotContain(projection.MediaItems, item => item.DisplayName == "Hidden frame");

        var preserved = Assert.Single(projection.ReferenceChoices.Where(choice => choice.LogicalObjectId == video.Id));
        Assert.Same(retainedChoice, preserved);
        Assert.True(preserved.IsSelected);
        Assert.Equal(GenerationReferenceRole.Character, preserved.Role);
        Assert.Equal("Retained label", preserved.Label);
        Assert.Equal("Retained notes", preserved.Notes);
        Assert.Null(projection.MediaItems.Single(item => item.Asset?.Id == imageWithMissingThumbnail.Id).Thumbnail);
        Assert.Equal([newest.Id, oldest.Id], projection.GenerationHistory.Select(generation => generation.Id));
    }

    private static ProjectAsset PhysicalAsset(string fileName, MediaType mediaType) => new()
    {
        FileName = fileName,
        DisplayName = fileName,
        MediaType = mediaType,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage { RelativePath = fileName }
    };

    private static ProjectAsset VirtualAsset(string name, VirtualAssetKind kind) => new()
    {
        FileName = name,
        DisplayName = name,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Virtual,
        Physical = null,
        Virtual = new VirtualAssetState { Kind = kind }
    };

    private static FrameAnchorRevision AnchorRevision(Guid anchorId, Guid sourceAssetId) => new()
    {
        AnchorId = anchorId,
        SourceAssetId = sourceAssetId,
        SourceContentHash = "a".PadLeft(64, 'a'),
        VideoStreamIndex = 0,
        PresentationTimestamp = 1,
        TimeBaseNumerator = 1,
        TimeBaseDenominator = 24
    };
}
