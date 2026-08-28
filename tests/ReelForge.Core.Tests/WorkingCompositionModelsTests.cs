using ReelForge.Core;

namespace ReelForge.Core.Tests;

public sealed class WorkingCompositionModelsTests
{
    [Fact]
    public void StateRetainsTrackAndItemOrderIncludingEmptyTracksAndDefensivelyCopiesCollections()
    {
        var visibleEmpty = VideoTrack(isVisible: true);
        var sourceItems = new List<CompositionVideoItem> { VideoItem() };
        var hiddenWithItem = VideoTrack(isVisible: false, items: sourceItems);
        var mutedEmpty = AudioTrack(isMuted: true);
        var source = new List<CompositionVideoTrack> { visibleEmpty, hiddenWithItem };

        var state = new WorkingCompositionState(source, [mutedEmpty]);
        source.Clear();
        sourceItems.Clear();

        Assert.Equal([visibleEmpty.Id, hiddenWithItem.Id], state.VideoTracks.Select(track => track.Id));
        Assert.Single(state.AudioTracks);
        Assert.Empty(state.VideoTracks[0].Items);
        Assert.Single(state.VideoTracks[1].Items);
        Assert.Single(state.ContributingVideoTracks);
        Assert.Equal(visibleEmpty.Id, state.ContributingVideoTracks[0].Id);
        Assert.Empty(state.ContributingAudioTracks);
    }

    [Fact]
    public void StateAllowsReasonableManyTrackCountWithoutDroppingEmptyTracks()
    {
        var tracks = Enumerable.Range(0, 256).Select(_ => VideoTrack()).ToArray();

        var state = new WorkingCompositionState(tracks, []);

        Assert.Equal(256, state.VideoTracks.Count);
        Assert.Equal(256, state.ContributingVideoTracks.Count);
    }

    [Fact]
    public void ContributionUsesVisibilityAndMuteButNeverLock()
    {
        var lockedVisibleVideo = VideoTrack(isLocked: true, isVisible: true);
        var unlockedHiddenVideo = VideoTrack(isLocked: false, isVisible: false);
        var lockedUnmutedAudio = AudioTrack(isLocked: true, isMuted: false);
        var unlockedMutedAudio = AudioTrack(isLocked: false, isMuted: true);

        var state = new WorkingCompositionState(
            [lockedVisibleVideo, unlockedHiddenVideo],
            [lockedUnmutedAudio, unlockedMutedAudio]);

        Assert.Equal([lockedVisibleVideo.Id], state.ContributingVideoTracks.Select(track => track.Id));
        Assert.Equal([lockedUnmutedAudio.Id], state.ContributingAudioTracks.Select(track => track.Id));
    }

    [Fact]
    public void ItemsPreserveExactTimeStreamRangeAndValidLinkedSourcePairWithoutCreatingAsset()
    {
        var source = new AssetRevisionReference { AssetId = Guid.NewGuid(), RecipeRevisionId = Guid.NewGuid() };
        var link = Guid.NewGuid();
        var compositionStart = new ExactTime(1001, 60_000);
        var video = VideoItem(source: source, stream: 2, start: -3, end: 4, compositionStart: compositionStart, linkGroupId: link);
        var audio = AudioItem(source: source, stream: 1, start: 0, end: 48_000, compositionStart: compositionStart, linkGroupId: link);

        var state = new WorkingCompositionState([VideoTrack(items: [video])], [AudioTrack(items: [audio])]);

        Assert.Same(source, state.VideoTracks[0].Items[0].Source);
        Assert.Equal(2, video.SelectedStreamIndex);
        Assert.Equal(-3, video.SourceRange.Start.PresentationTimestamp);
        Assert.Equal(4, video.SourceRange.End.PresentationTimestamp);
        Assert.Equal(1, audio.SelectedStreamIndex);
        Assert.Equal(48_000, audio.SourceRange.End.SampleFrameOffset);
        Assert.Equal(compositionStart, audio.CompositionStart);
        Assert.Equal(link, video.LinkGroupId);
    }

    [Fact]
    public void ConstructorsRejectEmptyOrDuplicateTrackAndItemIds()
    {
        Assert.Throws<ArgumentException>(() => VideoTrack(Guid.Empty));
        Assert.Throws<ArgumentException>(() => VideoItem(Guid.Empty));

        var duplicate = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState([VideoTrack(duplicate)], [AudioTrack(duplicate)]));

        var itemId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState(
            [VideoTrack(items: [VideoItem(itemId)])],
            [AudioTrack(items: [AudioItem(itemId)])]));
    }

    [Fact]
    public void ConstructorsRejectInvalidStreamsPlacementsAndSourceRanges()
    {
        Assert.Throws<ArgumentException>(() => VideoItem(source: new AssetRevisionReference()));
        Assert.Throws<ArgumentException>(() => AudioItem(source: new AssetRevisionReference
        {
            AssetId = Guid.NewGuid(),
            RecipeRevisionId = Guid.Empty
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoItem(stream: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioItem(stream: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoItem(compositionStart: new ExactTime(-1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioItem(compositionStart: new ExactTime(-1, 1)));
        Assert.Throws<ArgumentException>(() => new VideoSourceRange(Vpt(0), Vpt(0)));
        Assert.Throws<ArgumentException>(() => new VideoSourceRange(Vpt(0), new VideoPresentationTime(1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSourceRange(Ast(-1), Ast(1)));
        Assert.Throws<ArgumentException>(() => new AudioSourceRange(Ast(0), new AudioSampleTime(1, 44_100)));
        Assert.Throws<ArgumentException>(() => new AudioSourceRange(Ast(1), Ast(1)));
    }

    [Fact]
    public void CompositionCollectionsRejectNullEntries()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkingCompositionState(null!, []));
        Assert.Throws<ArgumentNullException>(() => new WorkingCompositionState([], null!));
        Assert.Throws<ArgumentNullException>(() => new CompositionVideoTrack(Guid.NewGuid(), false, true, null!));
        Assert.Throws<ArgumentNullException>(() => new CompositionAudioTrack(Guid.NewGuid(), false, false, null!));
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState([null!], []));
        Assert.Throws<ArgumentException>(() => VideoTrack(items: [null!]));
        Assert.Throws<ArgumentException>(() => AudioTrack(items: [null!]));
    }

    [Fact]
    public void LinkGroupsRejectIncompleteSingleKindAndMisalignedItems()
    {
        var link = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState([VideoTrack(items: [VideoItem(linkGroupId: link)])], []));
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState([], [AudioTrack(items: [AudioItem(linkGroupId: link)])]));
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState(
            [VideoTrack(items: [VideoItem(compositionStart: new ExactTime(0, 1), linkGroupId: link)])],
            [AudioTrack(items: [AudioItem(compositionStart: new ExactTime(1, 1), linkGroupId: link)])]));
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState(
            [VideoTrack(items: [VideoItem(linkGroupId: link)])],
            [AudioTrack(items: [AudioItem(source: new AssetRevisionReference { AssetId = Guid.NewGuid() }, linkGroupId: link)])]));
        var source = new AssetRevisionReference { AssetId = Guid.NewGuid() };
        Assert.Throws<ArgumentException>(() => new WorkingCompositionState(
            [VideoTrack(items: [VideoItem(source: source, linkGroupId: link), VideoItem(source: source, linkGroupId: link)])],
            [AudioTrack(items: [AudioItem(source: source, linkGroupId: link)])]));
    }

    private static CompositionVideoTrack VideoTrack(
        Guid? id = null, bool isLocked = false, bool isVisible = true, IEnumerable<CompositionVideoItem>? items = null)
        => new(id ?? Guid.NewGuid(), isLocked, isVisible, items ?? []);

    private static CompositionAudioTrack AudioTrack(
        Guid? id = null, bool isLocked = false, bool isMuted = false, IEnumerable<CompositionAudioItem>? items = null)
        => new(id ?? Guid.NewGuid(), isLocked, isMuted, items ?? []);

    private static CompositionVideoItem VideoItem(
        Guid? id = null, AssetRevisionReference? source = null, int stream = 0, long start = 0, long end = 1,
        ExactTime? compositionStart = null, Guid? linkGroupId = null)
        => new(id ?? Guid.NewGuid(), source ?? new AssetRevisionReference { AssetId = Guid.NewGuid() }, stream,
            new VideoSourceRange(Vpt(start), Vpt(end)), compositionStart ?? new ExactTime(0, 1), linkGroupId);

    private static CompositionAudioItem AudioItem(
        Guid? id = null, AssetRevisionReference? source = null, int stream = 0, long start = 0, long end = 1,
        ExactTime? compositionStart = null, Guid? linkGroupId = null)
        => new(id ?? Guid.NewGuid(), source ?? new AssetRevisionReference { AssetId = Guid.NewGuid() }, stream,
            new AudioSourceRange(Ast(start), Ast(end)), compositionStart ?? new ExactTime(0, 1), linkGroupId);

    private static VideoPresentationTime Vpt(long pts) => new(pts, 1001, 60_000);
    private static AudioSampleTime Ast(long sample) => new(sample, 48_000);
}
