namespace ReelForge.Application;

/// <summary>
/// Selects decoded presentation frames while respecting a composition segment's
/// half-open source range.  The policy deliberately operates on indexed PTS values
/// rather than an assumed frame duration, so it remains correct for VFR media.
/// </summary>
public static class CompositionAuditionFrameNavigation
{
    private const double BoundaryToleranceSeconds = 0.000_000_1;

    public static VideoPresentationFrame? FindAdjacentFrame(
        IEnumerable<VideoPresentationFrame> indexedFrames,
        double currentSeconds,
        double sourceStartSeconds,
        double sourceEndSeconds,
        int direction)
    {
        Validate(direction, sourceStartSeconds, sourceEndSeconds);
        var frames = InSegment(indexedFrames, sourceStartSeconds, sourceEndSeconds);
        return direction < 0
            ? frames.Where(frame => frame.TimestampSeconds < currentSeconds - BoundaryToleranceSeconds)
                .OrderByDescending(frame => frame.TimestampSeconds)
                .FirstOrDefault()
            : frames.Where(frame => frame.TimestampSeconds > currentSeconds + BoundaryToleranceSeconds)
                .OrderBy(frame => frame.TimestampSeconds)
                .FirstOrDefault();
    }

    public static VideoPresentationFrame? FindBoundaryFrame(
        IEnumerable<VideoPresentationFrame> indexedFrames,
        double sourceStartSeconds,
        double sourceEndSeconds,
        int direction)
    {
        Validate(direction, sourceStartSeconds, sourceEndSeconds);
        var frames = InSegment(indexedFrames, sourceStartSeconds, sourceEndSeconds);
        return direction < 0
            ? frames.OrderByDescending(frame => frame.TimestampSeconds).FirstOrDefault()
            : frames.OrderBy(frame => frame.TimestampSeconds).FirstOrDefault();
    }

    private static IEnumerable<VideoPresentationFrame> InSegment(
        IEnumerable<VideoPresentationFrame> indexedFrames,
        double sourceStartSeconds,
        double sourceEndSeconds)
    {
        ArgumentNullException.ThrowIfNull(indexedFrames);
        return indexedFrames.Where(frame =>
            frame.TimestampSeconds >= sourceStartSeconds &&
            frame.TimestampSeconds < sourceEndSeconds);
    }

    private static void Validate(int direction, double sourceStartSeconds, double sourceEndSeconds)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!double.IsFinite(sourceStartSeconds) || !double.IsFinite(sourceEndSeconds) ||
            sourceEndSeconds <= sourceStartSeconds)
            throw new ArgumentOutOfRangeException(nameof(sourceEndSeconds));
    }
}
