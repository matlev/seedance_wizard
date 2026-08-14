namespace ReelForge.Application;

public sealed record CompositionTimelineSegmentInput(Guid SegmentId, double? DurationSeconds);

public sealed record CompositionTimelineSegmentSpan(
    Guid SegmentId,
    double Left,
    double Width,
    double StartSeconds,
    double DurationSeconds);

public sealed record CompositionTimelineLayoutResult(
    double ContentWidth,
    double ProjectedDurationSeconds,
    double KnownDurationSeconds,
    bool HasUnknownDurations,
    IReadOnlyList<CompositionTimelineSegmentSpan> Segments)
{
    public double GetPlayheadX(double playbackSeconds)
    {
        if (Segments.Count == 0 || ProjectedDurationSeconds <= 0) return 0;
        var clamped = Math.Clamp(playbackSeconds, 0, ProjectedDurationSeconds);
        var segment = Segments.FirstOrDefault(candidate =>
                          clamped < candidate.StartSeconds + candidate.DurationSeconds) ??
                      Segments[^1];
        var progress = segment.DurationSeconds <= 0
            ? 0
            : Math.Clamp((clamped - segment.StartSeconds) / segment.DurationSeconds, 0, 1);
        return segment.Left + segment.Width * progress;
    }
}

public static class CompositionTimelineLayout
{
    public static CompositionTimelineLayoutResult Calculate(
        IReadOnlyList<CompositionTimelineSegmentInput> segments,
        double viewportWidth,
        double minimumSegmentWidth = 88,
        double pixelsPerSecond = 24)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (!double.IsFinite(viewportWidth) || viewportWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(minimumSegmentWidth) || minimumSegmentWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentWidth));
        if (!double.IsFinite(pixelsPerSecond) || pixelsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerSecond));
        if (segments.Count == 0)
            return new CompositionTimelineLayoutResult(
                Math.Max(1, viewportWidth), 0, 0, false, []);

        var knownDurations = segments
            .Select(segment => ValidDuration(segment.DurationSeconds))
            .Where(duration => duration is not null)
            .Select(duration => duration!.Value)
            .ToArray();
        var fallbackDuration = knownDurations.Length == 0 ? 1 : knownDurations.Average();
        var effectiveDurations = segments
            .Select(segment => ValidDuration(segment.DurationSeconds) ?? fallbackDuration)
            .ToArray();
        var projectedDuration = effectiveDurations.Sum();
        var contentWidth = Math.Max(
            Math.Max(1, viewportWidth),
            Math.Max(segments.Count * minimumSegmentWidth, projectedDuration * pixelsPerSecond));
        var minimumWidthTotal = segments.Count * minimumSegmentWidth;
        var distributableWidth = Math.Max(0, contentWidth - minimumWidthTotal);

        var spans = new List<CompositionTimelineSegmentSpan>(segments.Count);
        double left = 0;
        double startSeconds = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            var width = minimumSegmentWidth +
                        distributableWidth * effectiveDurations[index] / projectedDuration;
            if (index == segments.Count - 1) width = contentWidth - left;
            spans.Add(new CompositionTimelineSegmentSpan(
                segments[index].SegmentId,
                left,
                width,
                startSeconds,
                effectiveDurations[index]));
            left += width;
            startSeconds += effectiveDurations[index];
        }

        return new CompositionTimelineLayoutResult(
            contentWidth,
            projectedDuration,
            knownDurations.Sum(),
            knownDurations.Length != segments.Count,
            spans);
    }

    private static double? ValidDuration(double? value) =>
        value is > 0 && double.IsFinite(value.Value) ? value : null;
}
