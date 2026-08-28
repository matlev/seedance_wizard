using System.Windows.Media.Imaging;
using ReelForge.App.Views.Generation;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
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

    [Fact]
    public void BuildExcludesDeletedAssetsFromMediaAndNewGenerationReferenceChoices()
    {
        var visible = PhysicalAsset("visible.mp4", MediaType.Video);
        var deleted = PhysicalAsset("deleted.mp4", MediaType.Video);
        deleted.IsDeleted = true;
        var project = new VideoProject { Assets = [visible, deleted] };

        var projection = ProjectMediaProjectionBuilder.Build(
            project,
            _ => "C:\\media\\not-used.mp4",
            _ => throw new InvalidOperationException("Video thumbnails are not decoded."),
            []);

        var media = Assert.Single(projection.MediaItems);
        Assert.Equal(visible.Id, media.Asset?.Id);
        var choice = Assert.Single(projection.ReferenceChoices);
        Assert.Equal(visible.Id, choice.LogicalObjectId);
        Assert.DoesNotContain(projection.ReferenceChoices, candidate => candidate.LogicalObjectId == deleted.Id);
    }

    [Fact]
    public void BuildMarksOnlyMatchingActivePhysicalAssetsAsRestoreCandidates()
    {
        const string sharedHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var active = PhysicalAsset("reimported.mp4", MediaType.Video, sharedHash);
        var deletedMatch = PhysicalAsset("deleted.mp4", MediaType.Video, sharedHash);
        deletedMatch.IsDeleted = true;
        var differentType = PhysicalAsset("deleted.m4a", MediaType.Audio, sharedHash);
        differentType.IsDeleted = true;
        var differentHash = PhysicalAsset("different.mp4", MediaType.Video,
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        var projection = ProjectMediaProjectionBuilder.Build(
            new VideoProject { Assets = [active, deletedMatch, differentType, differentHash] },
            _ => "C:\\media\\not-used.mp4",
            _ => throw new InvalidOperationException("Video thumbnails are not decoded."),
            []);

        Assert.True(projection.MediaItems.Single(item => item.Asset?.Id == active.Id).CanRestoreDeletedSource);
        Assert.False(projection.MediaItems.Single(item => item.Asset?.Id == differentHash.Id).CanRestoreDeletedSource);
    }

    [Fact]
    public void BuildMarksOnlyReportedDerivedProjectMediaAsDegraded()
    {
        var source = PhysicalAsset("source.mp4", MediaType.Video);
        var savedClip = VirtualAsset("Broken clip", VirtualAssetKind.SavedClip);
        var composition = VirtualAsset("Working Composition", VirtualAssetKind.Composition);
        var anchor = new FrameAnchor { DisplayLabel = "Broken frame" };
        var revision = AnchorRevision(anchor.Id, source.Id);
        anchor.CurrentRevisionId = revision.Id;
        var project = new VideoProject
        {
            Assets = [source, savedClip, composition],
            Anchors = [anchor],
            AnchorRevisions = [revision]
        };
        var degradation = new ProjectDegradationReport(
        [
            new ProjectDegradedMediaItem(anchor.Id, ProjectDegradedMediaKind.SavedFrame, anchor.DisplayLabel!),
            new ProjectDegradedMediaItem(savedClip.Id, ProjectDegradedMediaKind.SavedClip, savedClip.EffectiveDisplayName)
        ]);

        var projection = ProjectMediaProjectionBuilder.Build(
            project,
            _ => "C:\\media\\not-used.mp4",
            _ => throw new InvalidOperationException("No images are present."),
            [],
            degradation);

        Assert.True(projection.MediaItems.Single(item => item.Anchor?.Id == anchor.Id).IsDegradedDerivedAsset);
        Assert.True(projection.MediaItems.Single(item => item.Asset?.Id == savedClip.Id).IsDegradedDerivedAsset);
        Assert.False(projection.MediaItems.Single(item => item.Asset?.Id == composition.Id).IsDegradedDerivedAsset);
        Assert.False(projection.MediaItems.Single(item => item.Asset?.Id == source.Id).IsDegradedDerivedAsset);
    }

    [Fact]
    public void UpdateDegradedStateNotifiesWarningBindings()
    {
        var item = new ProjectMediaListItem(VirtualAsset("Saved clip", VirtualAssetKind.SavedClip));
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.UpdateDegradedState(true);

        Assert.True(item.IsDegradedDerivedAsset);
        Assert.Equal("⚠", item.Glyph);
        Assert.Contains(nameof(ProjectMediaListItem.IsDegradedDerivedAsset), changed);
        Assert.Contains(nameof(ProjectMediaListItem.Glyph), changed);
        Assert.Contains(nameof(ProjectMediaListItem.GlyphToolTip), changed);
    }

    [Fact]
    public void BuildRetainsExactlyTheDeletedAssetOccurrencesInTheCurrentDraftForReopen()
    {
        var deleted = PhysicalAsset("deleted.mp4", MediaType.Video);
        deleted.IsDeleted = true;
        var firstReference = new GenerationReferenceDraft
        {
            ReferenceId = Guid.NewGuid(),
            LogicalObjectId = deleted.Id,
            Order = 0,
            Role = GenerationReferenceRole.Character,
            Label = "Opening shot"
        };
        var secondReference = new GenerationReferenceDraft
        {
            ReferenceId = Guid.NewGuid(),
            LogicalObjectId = deleted.Id,
            Order = 1,
            Role = GenerationReferenceRole.Style,
            Notes = "Keep the motion"
        };
        var project = new VideoProject
        {
            Assets = [deleted],
            CurrentGenerationDraft = new GenerationDraft
            {
                References = [firstReference, secondReference]
            }
        };

        var projection = ProjectMediaProjectionBuilder.Build(
            project,
            _ => "C:\\media\\not-used.mp4",
            _ => throw new InvalidOperationException("Video thumbnails are not decoded."),
            []);

        Assert.Empty(projection.MediaItems);
        Assert.Equal([firstReference.ReferenceId, secondReference.ReferenceId],
            projection.ReferenceChoices.Select(choice => choice.ReferenceId));
        Assert.All(projection.ReferenceChoices, choice =>
        {
            Assert.True(choice.IsDeleted);
            Assert.False(choice.CanCreateAdditionalOccurrence);
            Assert.Equal(deleted.Id, choice.LogicalObjectId);
        });

        var reopenedChoices = new System.Collections.ObjectModel.ObservableCollection<GenerationReferenceChoice>(
            projection.ReferenceChoices);
        GenerationReferenceEditor.ApplyDraft(project.CurrentGenerationDraft.References, reopenedChoices);

        Assert.Equal(2, reopenedChoices.Count);
        Assert.All(reopenedChoices, choice => Assert.True(choice.IsSelected));
        Assert.Equal(GenerationReferenceRole.Character, reopenedChoices[0].Role);
        Assert.Equal(GenerationReferenceRole.Style, reopenedChoices[1].Role);
        Assert.Equal("Opening shot", reopenedChoices[0].Label);
        Assert.Equal("Keep the motion", reopenedChoices[1].Notes);

        var autosavedReferences = GenerationReferenceEditor.Capture(
            GenerationMode.ReferenceToVideo,
            reopenedChoices);
        Assert.Equal(
            [firstReference.ReferenceId, secondReference.ReferenceId],
            autosavedReferences.Select(reference => reference.ReferenceId));
    }

    [Fact]
    public void BuildKeepsSavedFramesFromDeletedSourcesVisibleButOnlyRetainsTheirExistingDraftOccurrences()
    {
        var source = PhysicalAsset("deleted-source.mp4", MediaType.Video);
        source.IsDeleted = true;
        var anchor = new FrameAnchor { DisplayLabel = "Opening frame" };
        var revision = AnchorRevision(anchor.Id, source.Id);
        anchor.CurrentRevisionId = revision.Id;
        var firstReference = new GenerationReferenceDraft
        {
            ReferenceId = Guid.NewGuid(),
            ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
            LogicalObjectId = anchor.Id,
            AnchorRevisionId = revision.Id,
            Order = 0
        };
        var secondReference = new GenerationReferenceDraft
        {
            ReferenceId = Guid.NewGuid(),
            ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
            LogicalObjectId = anchor.Id,
            AnchorRevisionId = revision.Id,
            Order = 1
        };
        var project = new VideoProject
        {
            Assets = [source],
            Anchors = [anchor],
            AnchorRevisions = [revision],
            CurrentGenerationDraft = new GenerationDraft { References = [firstReference, secondReference] }
        };

        var projection = ProjectMediaProjectionBuilder.Build(
            project,
            _ => "C:\\media\\not-used.mp4",
            _ => throw new InvalidOperationException("Video thumbnails are not decoded."),
            []);

        Assert.Contains(projection.MediaItems, item => item.Anchor?.Id == anchor.Id);
        Assert.Equal([firstReference.ReferenceId, secondReference.ReferenceId],
            projection.ReferenceChoices.Select(choice => choice.ReferenceId));
        Assert.All(projection.ReferenceChoices, choice =>
        {
            Assert.True(choice.IsDeleted);
            Assert.False(choice.CanCreateAdditionalOccurrence);
            Assert.Equal(anchor.Id, choice.LogicalObjectId);
        });

        var reopenedChoices = new System.Collections.ObjectModel.ObservableCollection<GenerationReferenceChoice>(
            projection.ReferenceChoices);
        GenerationReferenceEditor.ApplyDraft(project.CurrentGenerationDraft.References, reopenedChoices);
        var autosavedReferences = GenerationReferenceEditor.Capture(GenerationMode.ReferenceToVideo, reopenedChoices);

        Assert.Equal([firstReference.ReferenceId, secondReference.ReferenceId],
            autosavedReferences.Select(reference => reference.ReferenceId));
        Assert.Equal(2, reopenedChoices.Count);
    }

    private static ProjectAsset PhysicalAsset(string fileName, MediaType mediaType, string? sha256 = null) => new()
    {
        FileName = fileName,
        DisplayName = fileName,
        MediaType = mediaType,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = fileName,
            ContentIdentity = new ContentIdentity
            {
                Status = sha256 is null ? ContentHashStatus.Pending : ContentHashStatus.Verified,
                Sha256 = sha256
            }
        }
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
