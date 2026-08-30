using ReelForge.App.Views.Editing;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class CompositionTimelineTrackRowTests
{
    [Fact]
    public void TrackRowsRetainTheirKindOrderStateAndEmptyItemCount()
    {
        var video = new CompositionTimelineTrackRow(Guid.NewGuid(), CompositionTimelineTrackKind.Video, 1,
            IsLocked: true, IsVisibleOrMuted: false, ItemCount: 0);
        var audio = new CompositionTimelineTrackRow(Guid.NewGuid(), CompositionTimelineTrackKind.Audio, 0,
            IsLocked: false, IsVisibleOrMuted: true, ItemCount: 3);

        Assert.Equal("Video 2", video.DisplayName);
        Assert.Equal("Hidden", video.StatusText);
        Assert.Equal(0, video.ItemCount);
        Assert.Equal("Audio 1", audio.DisplayName);
        Assert.Equal("Muted", audio.StatusText);
        Assert.Equal(3, audio.ItemCount);
    }

    [Fact]
    public void TrackRowsPreferPersistedNamesOverGeneratedFallbacks()
    {
        var track = new CompositionTimelineTrackRow(Guid.NewGuid(), CompositionTimelineTrackKind.Audio, 2,
            IsLocked: false, IsVisibleOrMuted: false, ItemCount: 0, Name: "Dialogue");

        Assert.Equal("Dialogue", track.DisplayName);
    }

    [Fact]
    public void LockedItemDisablesRemoveButtonAndContextMenuCapability()
    {
        var itemId = Guid.NewGuid();
        var state = CompositionTimelineState.Empty with
        {
            Capabilities = new Dictionary<Guid, CompositionTimelineItemCapabilities>
            {
                [itemId] = new(CanRemove: false)
            }
        };

        Assert.False(CompositionTimelineControl.CanRemove(state, itemId));
    }

    [Fact]
    public void UnlockedItemEnablesRemoveButtonAndContextMenuCapability()
    {
        var itemId = Guid.NewGuid();
        var state = CompositionTimelineState.Empty with
        {
            Capabilities = new Dictionary<Guid, CompositionTimelineItemCapabilities>
            {
                [itemId] = new(CanRemove: true)
            }
        };

        Assert.True(CompositionTimelineControl.CanRemove(state, itemId));
    }

    [Fact]
    public void DropTargetRequiresAnUnlockedMatchingTrackRowAndSupportsEmptyTracks()
    {
        var videoId = Guid.NewGuid();
        var audioId = Guid.NewGuid();
        var state = CompositionTimelineState.Empty with
        {
            Tracks =
            [
                new(videoId, CompositionTimelineTrackKind.Video, 0, IsLocked: false, IsVisibleOrMuted: true, ItemCount: 0),
                new(audioId, CompositionTimelineTrackKind.Audio, 0, IsLocked: true, IsVisibleOrMuted: false, ItemCount: 0)
            ]
        };

        Assert.Equal(videoId, CompositionTimelineControl.ResolveDropTargetTrack(
            state, CompositionTimelineDropKind.Video, timelineY: 30)?.TrackId);
        Assert.Null(CompositionTimelineControl.ResolveDropTargetTrack(
            state, CompositionTimelineDropKind.Audio, timelineY: 30));
        Assert.Null(CompositionTimelineControl.ResolveDropTargetTrack(
            state, CompositionTimelineDropKind.Audio, timelineY: 100));
        Assert.Null(CompositionTimelineControl.ResolveDropTargetTrack(
            state, CompositionTimelineDropKind.Video, timelineY: 10));
    }

    [Fact]
    public void TimelineDropTimeUsesDeterministicMillisecondProjectTime()
    {
        Assert.Equal(new ExactTime(617, 500), CompositionWorkspaceCoordinator.ExactTimelineTime(1.234));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CompositionWorkspaceCoordinator.ExactTimelineTime(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CompositionWorkspaceCoordinator.ExactTimelineTime(-0.001));
    }
}
