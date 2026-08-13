using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class ExactFrameContactWindowTests
{
    private static readonly IReadOnlyList<VideoPresentationFrame> Frames = Enumerable.Range(0, 30)
        .Select(index => new VideoPresentationFrame(0, index, 1, 10))
        .ToArray();

    [Fact]
    public void MovingOneFrameReusesEightOfNineVisiblePositions()
    {
        var first = ExactFrameContactWindow.Select(Frames, 1.0);
        var next = ExactFrameContactWindow.Select(Frames, 1.1);

        Assert.Equal(9, first.Count);
        Assert.Equal(9, next.Count);
        Assert.Equal(8, first.Intersect(next).Count());
    }

    [Fact]
    public void WindowClampsAtBeginningAndEndWithoutInventingFrames()
    {
        var beginning = ExactFrameContactWindow.Select(Frames, 0);
        var end = ExactFrameContactWindow.Select(Frames, 99);

        Assert.Equal(Enumerable.Range(0, 9).Select(index => (long)index),
            beginning.Select(frame => frame.PresentationTimestamp));
        Assert.Equal(Enumerable.Range(21, 9).Select(index => (long)index),
            end.Select(frame => frame.PresentationTimestamp));
    }

    [Fact]
    public void SelectingThreeFramesAwayOnlyIntroducesThreeNewPositions()
    {
        var first = ExactFrameContactWindow.Select(Frames, 1.0);
        var shifted = ExactFrameContactWindow.Select(Frames, 1.3);

        Assert.Equal(6, first.Intersect(shifted).Count());
    }
}
