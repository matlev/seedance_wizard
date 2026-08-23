using ReelForge.Application;

namespace ReelForge.Application.Tests;

public sealed class CompositionAuditionFrameNavigationTests
{
    [Fact]
    public void AdjacentFrameUsesDecodedPtsInsteadOfAssumedFrameDuration()
    {
        var frames = Frames(0.000, 0.041, 0.113, 0.204, 0.377);

        var next = CompositionAuditionFrameNavigation.FindAdjacentFrame(frames, 0.113, 0, 0.5, 1);
        var previous = CompositionAuditionFrameNavigation.FindAdjacentFrame(frames, 0.113, 0, 0.5, -1);

        Assert.Equal(0.204, next!.TimestampSeconds, 6);
        Assert.Equal(0.041, previous!.TimestampSeconds, 6);
    }

    [Fact]
    public void AdjacentFrameOrdersPresentationTimesAcrossDifferentTimeBases()
    {
        var frames = new[]
        {
            new VideoPresentationFrame(0, 19, 1, 24),
            new VideoPresentationFrame(0, 20, 1, 24),
            new VideoPresentationFrame(0, 900, 1, 1_000)
        };

        var next = CompositionAuditionFrameNavigation.FindAdjacentFrame(
            frames, 20d / 24, 0, 1, 1);
        var previous = CompositionAuditionFrameNavigation.FindAdjacentFrame(
            frames, 20d / 24, 0, 1, -1);

        Assert.Equal(900, next!.PresentationTimestamp);
        Assert.Equal(1_000, next.TimeBaseDenominator);
        Assert.Equal(19, previous!.PresentationTimestamp);
        Assert.Equal(24, previous.TimeBaseDenominator);
    }

    [Fact]
    public void AdjacentFrameHonorsHalfOpenSegmentRangesAtASplitCut()
    {
        var frames = Frames(0.875, 0.958, 1.000, 1.041);

        var finalFirstHalf = CompositionAuditionFrameNavigation.FindAdjacentFrame(frames, 0.958, 0, 1, 1);
        var firstSecondHalf = CompositionAuditionFrameNavigation.FindBoundaryFrame(frames, 1, 2, 1);
        var finalSecondHalf = CompositionAuditionFrameNavigation.FindBoundaryFrame(frames, 1, 2, -1);

        Assert.Null(finalFirstHalf);
        Assert.Equal(1.000, firstSecondHalf!.TimestampSeconds, 6);
        Assert.Equal(1.041, finalSecondHalf!.TimestampSeconds, 6);
    }

    [Fact]
    public void BoundaryFrameDoesNotInventFramesOutsideTheSourceRange()
    {
        var frames = Frames(0.000, 0.041, 0.083);

        Assert.Null(CompositionAuditionFrameNavigation.FindBoundaryFrame(frames, 1, 2, 1));
        Assert.Null(CompositionAuditionFrameNavigation.FindAdjacentFrame(frames, 0, 0, 1, -1));
    }

    [Fact]
    public void AdjacentFrameDoesNotMoveBeyondEitherCompositionEnd()
    {
        var frames = Frames(0.000, 0.041, 0.083);

        var beforeStart = CompositionAuditionFrameNavigation.FindAdjacentFrame(frames, 0.000, 0, 0.1, -1);
        var afterEnd = CompositionAuditionFrameNavigation.FindAdjacentFrame(frames, 0.083, 0, 0.1, 1);

        Assert.Null(beforeStart);
        Assert.Null(afterEnd);
    }

    private static VideoPresentationFrame[] Frames(params double[] seconds) =>
        seconds.Select((second, index) => new VideoPresentationFrame(0, (long)Math.Round(second * 1_000_000), 1, 1_000_000, index)).ToArray();
}
