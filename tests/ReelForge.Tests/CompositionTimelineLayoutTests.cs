using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class CompositionTimelineLayoutTests
{
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
}
