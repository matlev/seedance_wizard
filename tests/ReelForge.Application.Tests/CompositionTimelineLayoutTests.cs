using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class CompositionTimelineLayoutTests
{
    [Fact]
    public void ExplicitOccurrenceTimesPreservePersistedTimelineGeometry()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var result = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(first, 2, 3),
                new CompositionTimelineSegmentInput(second, 4, 8)
            ],
            viewportWidth: 600,
            pixelsPerSecond: 20);

        Assert.Equal(12, result.ProjectedDurationSeconds);
        Assert.Equal(3, result.Segments.Single(segment => segment.SegmentId == first).StartSeconds);
        Assert.Equal(8, result.Segments.Single(segment => segment.SegmentId == second).StartSeconds);
        Assert.Equal(result.Segments.Single(segment => segment.SegmentId == second).Left, result.GetPlayheadX(8), precision: 6);
    }

    [Fact]
    public void LayoutKeepsShortSegmentsReadableAndExpandsLongerSegments()
    {
        var result = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 1),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 9)
            ],
            viewportWidth: 500,
            minimumSegmentWidth: 80,
            pixelsPerSecond: 20);

        Assert.Equal(500, result.ContentWidth);
        Assert.Equal(10, result.ProjectedDurationSeconds);
        Assert.True(result.Segments[0].Width >= 80);
        Assert.True(result.Segments[1].Width > result.Segments[0].Width);
        Assert.Equal(500, result.Segments.Sum(segment => segment.Width), precision: 6);
    }

    [Fact]
    public void UnknownDurationsUseAStableAverageWithoutPretendingToBeKnown()
    {
        var result = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 4),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), null),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 8)
            ],
            viewportWidth: 600);

        Assert.True(result.HasUnknownDurations);
        Assert.Equal(12, result.KnownDurationSeconds);
        Assert.Equal(18, result.ProjectedDurationSeconds);
        Assert.Equal(6, result.Segments[1].DurationSeconds);
    }

    [Fact]
    public void PlayheadMapsThroughSegmentSpansAndClampsAtBothEnds()
    {
        var result = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 2),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 8)
            ],
            viewportWidth: 600,
            minimumSegmentWidth: 100,
            pixelsPerSecond: 20);

        Assert.Equal(0, result.GetPlayheadX(-1));
        Assert.Equal(result.Segments[0].Left + result.Segments[0].Width / 2, result.GetPlayheadX(1));
        Assert.Equal(result.Segments[1].Left, result.GetPlayheadX(2));
        Assert.Equal(result.ContentWidth, result.GetPlayheadX(99), precision: 6);
    }

    [Fact]
    public void DropPositionMapsBackToCompositionTime()
    {
        var result = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 2),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 8)
            ],
            viewportWidth: 600,
            minimumSegmentWidth: 100,
            pixelsPerSecond: 20);

        Assert.Equal(0, result.GetTimeAtX(-10));
        Assert.Equal(1, result.GetTimeAtX(result.Segments[0].Width / 2), precision: 6);
        Assert.Equal(2, result.GetTimeAtX(result.Segments[1].Left), precision: 6);
        Assert.Equal(10, result.GetTimeAtX(9999), precision: 6);
    }

    [Fact]
    public void ReorderPreviewMovesLastSegmentToTheFront()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var result = CompositionTimelineLayout.CalculateReorder(
            [
                new CompositionTimelineSegmentInput(first, 2),
                new CompositionTimelineSegmentInput(second, 4),
                new CompositionTimelineSegmentInput(third, 8)
            ],
            third,
            pointerX: -20,
            viewportWidth: 600);

        Assert.Equal(0, result.InsertionIndex);
        Assert.Equal([third, first, second], result.OrderedSegmentIds);
    }

    [Fact]
    public void ReorderPreviewMovesFirstSegmentToTheEnd()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var result = CompositionTimelineLayout.CalculateReorder(
            [
                new CompositionTimelineSegmentInput(first, 2),
                new CompositionTimelineSegmentInput(second, 4),
                new CompositionTimelineSegmentInput(third, 8)
            ],
            first,
            pointerX: 10_000,
            viewportWidth: 600);

        Assert.Equal(2, result.InsertionIndex);
        Assert.Equal([second, third, first], result.OrderedSegmentIds);
    }

    [Fact]
    public void ExternalVideoDropUsesCommittedSegmentMidpointsAndBoundaries()
    {
        var result = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 2),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 8)
            ],
            viewportWidth: 600,
            minimumSegmentWidth: 100,
            pixelsPerSecond: 20);

        Assert.Equal(0, result.GetVideoInsertionIndex(-1));
        Assert.Equal(0, result.GetVideoInsertionIndex(result.Segments[0].Width / 2 - 1));
        Assert.Equal(1, result.GetVideoInsertionIndex(result.Segments[0].Width / 2 + 1));
        Assert.Equal(2, result.GetVideoInsertionIndex(10_000));
        Assert.Equal(0, result.GetVideoInsertionX(0));
        Assert.Equal(result.Segments[1].Left, result.GetVideoInsertionX(1));
        Assert.Equal(result.ContentWidth, result.GetVideoInsertionX(2));
    }

    [Fact]
    public void ZoomExpandsGeometryWithoutChangingTimelineTimeMapping()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var normal = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(first, 2),
                new CompositionTimelineSegmentInput(second, 8)
            ],
            viewportWidth: 600,
            minimumSegmentWidth: 100,
            pixelsPerSecond: 20);
        var zoomed = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(first, 2),
                new CompositionTimelineSegmentInput(second, 8)
            ],
            viewportWidth: 600,
            minimumSegmentWidth: 100,
            pixelsPerSecond: 20,
            zoomFactor: 2.5);

        Assert.Equal(normal.ContentWidth * 2.5, zoomed.ContentWidth, precision: 6);
        Assert.Equal(normal.Segments[0].Width * 2.5, zoomed.Segments[0].Width, precision: 6);
        Assert.Equal(normal.Segments[1].Left * 2.5, zoomed.Segments[1].Left, precision: 6);
        Assert.Equal(normal.ProjectedDurationSeconds, zoomed.ProjectedDurationSeconds);
        Assert.Equal(5, zoomed.GetTimeAtX(zoomed.GetPlayheadX(5)), precision: 6);
    }

    [Fact]
    public void ReorderPreviewUsesZoomedSegmentMidpoints()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var inputs = new[]
        {
            new CompositionTimelineSegmentInput(first, 2),
            new CompositionTimelineSegmentInput(second, 4),
            new CompositionTimelineSegmentInput(third, 8)
        };
        var remaining = CompositionTimelineLayout.Calculate(
            inputs[1..],
            viewportWidth: 600,
            zoomFactor: 3);
        var result = CompositionTimelineLayout.CalculateReorder(
            inputs,
            first,
            pointerX: remaining.Segments[0].Left + remaining.Segments[0].Width + 1,
            viewportWidth: 600,
            zoomFactor: 3);

        Assert.Equal(1, result.InsertionIndex);
        Assert.Equal([second, first, third], result.OrderedSegmentIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    public void ZoomRejectsValuesBelowOneOrNonFinite(double zoomFactor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CompositionTimelineLayout.Calculate(
            [new CompositionTimelineSegmentInput(Guid.NewGuid(), 1)],
            viewportWidth: 600,
            zoomFactor: zoomFactor));
    }

    [Fact]
    public void AutoScrollKeepsAnAlreadyVisiblePlayheadStationary()
    {
        var layout = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 2),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 8)
            ],
            viewportWidth: 600,
            zoomFactor: 3);

        Assert.Equal(500, layout.GetAutoScrollOffset(
            playbackSeconds: 5,
            currentOffset: 500,
            viewportWidth: 600));
    }

    [Fact]
    public void AutoScrollMovesPlayheadToTheLeftAndClampsAtTimelineEnds()
    {
        var layout = CompositionTimelineLayout.Calculate(
            [
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 2),
                new CompositionTimelineSegmentInput(Guid.NewGuid(), 8)
            ],
            viewportWidth: 600,
            zoomFactor: 3);
        var playheadX = layout.GetPlayheadX(5);

        Assert.Equal(
            playheadX - 8,
            layout.GetAutoScrollOffset(5, currentOffset: 0, viewportWidth: 600),
            precision: 6);
        Assert.Equal(
            layout.ContentWidth - 600,
            layout.GetAutoScrollOffset(10, currentOffset: 0, viewportWidth: 600),
            precision: 6);
        Assert.Equal(0, layout.GetAutoScrollOffset(
            playbackSeconds: 0,
            currentOffset: 600,
            viewportWidth: 600));
    }

    [Fact]
    public void DragEdgeAutoScrollIsIdleInTheCenterAndAcceleratesTowardEdges()
    {
        Assert.Equal(0, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 300,
            viewportWidth: 600));
        Assert.Equal(-24, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 24,
            viewportWidth: 600));
        Assert.Equal(24, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 576,
            viewportWidth: 600));
        Assert.Equal(-48, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 0,
            viewportWidth: 600));
        Assert.Equal(48, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 600,
            viewportWidth: 600));
    }

    [Fact]
    public void DragEdgeAutoScrollHandlesNarrowViewportsAndOutsidePointers()
    {
        Assert.Equal(-48, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: -20,
            viewportWidth: 60));
        Assert.Equal(0, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 30,
            viewportWidth: 60));
        Assert.Equal(48, CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            pointerX: 80,
            viewportWidth: 60));
    }

    [Fact]
    public void StickyContentFollowsViewportButRemainsInsideItsTimelineItem()
    {
        Assert.Equal(0, CompositionTimelineLayout.GetStickyContentOffset(
            itemLeft: 200,
            itemWidth: 800,
            viewportLeft: 100,
            minimumTrailingWidth: 64));
        Assert.Equal(300, CompositionTimelineLayout.GetStickyContentOffset(
            itemLeft: 200,
            itemWidth: 800,
            viewportLeft: 500,
            minimumTrailingWidth: 64));
        Assert.Equal(736, CompositionTimelineLayout.GetStickyContentOffset(
            itemLeft: 200,
            itemWidth: 800,
            viewportLeft: 1_500,
            minimumTrailingWidth: 64));
    }

    [Fact]
    public void StickyContentDoesNotOffsetWhenItemIsTooNarrowForItsMinimumBadge()
    {
        Assert.Equal(0, CompositionTimelineLayout.GetStickyContentOffset(
            itemLeft: 200,
            itemWidth: 40,
            viewportLeft: 500,
            minimumTrailingWidth: 64));
    }

    [Fact]
    public void OverlappingAudioUsesSeparateVisualLanesAndLaterClipsReuseFreeSpace()
    {
        var first = Guid.NewGuid();
        var overlapping = Guid.NewGuid();
        var later = Guid.NewGuid();

        var result = CompositionTimelineLayout.CalculateAudioLanes(
        [
            new CompositionTimelineAudioInput(first, StartSeconds: 0, DurationSeconds: 5),
            new CompositionTimelineAudioInput(overlapping, StartSeconds: 2, DurationSeconds: 4),
            new CompositionTimelineAudioInput(later, StartSeconds: 5, DurationSeconds: 2)
        ]);

        Assert.Equal(2, result.LaneCount);
        Assert.Equal(0, result.LaneByAudioClipId[first]);
        Assert.Equal(1, result.LaneByAudioClipId[overlapping]);
        Assert.Equal(0, result.LaneByAudioClipId[later]);
    }

    [Fact]
    public void ClipsWithTheSameStartKeepTheirInputOrderAcrossLanes()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = CompositionTimelineLayout.CalculateAudioLanes(
        [
            new CompositionTimelineAudioInput(first, StartSeconds: 1, DurationSeconds: 2),
            new CompositionTimelineAudioInput(second, StartSeconds: 1, DurationSeconds: 2)
        ]);

        Assert.Equal(0, result.LaneByAudioClipId[first]);
        Assert.Equal(1, result.LaneByAudioClipId[second]);
    }
}
