using ReelForge.App.Views.Editing;

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
}
