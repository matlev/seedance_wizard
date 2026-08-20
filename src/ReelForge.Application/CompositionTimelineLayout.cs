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

    public double GetTimeAtX(double x)
    {
        if (Segments.Count == 0 || ProjectedDurationSeconds <= 0 || ContentWidth <= 0) return 0;
        var clamped = Math.Clamp(x, 0, ContentWidth);
        var segment = Segments.FirstOrDefault(candidate => clamped < candidate.Left + candidate.Width) ??
                      Segments[^1];
        var progress = segment.Width <= 0
            ? 0
            : Math.Clamp((clamped - segment.Left) / segment.Width, 0, 1);
        return segment.StartSeconds + segment.DurationSeconds * progress;
    }

    public double GetAutoScrollOffset(
        double playbackSeconds,
        double currentOffset,
        double viewportWidth,
        double leadingInset = 8)
    {
        if (!double.IsFinite(currentOffset) || currentOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(currentOffset));
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(leadingInset) || leadingInset < 0)
            throw new ArgumentOutOfRangeException(nameof(leadingInset));

        var maximumOffset = Math.Max(0, ContentWidth - viewportWidth);
        var boundedOffset = Math.Clamp(currentOffset, 0, maximumOffset);
        if (maximumOffset <= 0) return 0;

        var playheadX = GetPlayheadX(playbackSeconds);
        if (playheadX >= boundedOffset && playheadX <= boundedOffset + viewportWidth)
            return boundedOffset;
        return Math.Clamp(playheadX - leadingInset, 0, maximumOffset);
    }

    public int GetVideoInsertionIndex(double x)
    {
        for (var index = 0; index < Segments.Count; index++)
        {
            var segment = Segments[index];
            if (x < segment.Left + segment.Width / 2) return index;
        }

        return Segments.Count;
    }

    public double GetVideoInsertionX(int insertionIndex)
    {
        if (Segments.Count == 0) return 0;
        var boundedIndex = Math.Clamp(insertionIndex, 0, Segments.Count);
        return boundedIndex == Segments.Count
            ? ContentWidth
            : Segments[boundedIndex].Left;
    }
}

public sealed record CompositionTimelineReorderPreview(
    int InsertionIndex,
    IReadOnlyList<Guid> OrderedSegmentIds);

public sealed record CompositionTimelineAudioInput(
    Guid AudioClipId,
    double StartSeconds,
    double DurationSeconds);

public sealed record CompositionTimelineAudioLaneLayout(
    int LaneCount,
    IReadOnlyDictionary<Guid, int> LaneByAudioClipId);

public static class CompositionTimelineLayout
{
    public static CompositionTimelineAudioLaneLayout CalculateAudioLanes(
        IReadOnlyList<CompositionTimelineAudioInput> audioClips)
    {
        ArgumentNullException.ThrowIfNull(audioClips);
        if (audioClips.Count == 0)
            return new CompositionTimelineAudioLaneLayout(0, new Dictionary<Guid, int>());

        foreach (var clip in audioClips)
        {
            if (!double.IsFinite(clip.StartSeconds) || clip.StartSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(audioClips), "Audio start times must be finite and non-negative.");
            if (!double.IsFinite(clip.DurationSeconds) || clip.DurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(audioClips), "Audio durations must be finite and positive.");
        }

        var duplicate = audioClips.GroupBy(clip => clip.AudioClipId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Audio clip {duplicate.Key} appears more than once.", nameof(audioClips));

        var originalOrder = audioClips
            .Select((clip, index) => new { clip.AudioClipId, Index = index })
            .ToDictionary(item => item.AudioClipId, item => item.Index);
        var laneEnds = new List<double>();
        var laneByClipId = new Dictionary<Guid, int>(audioClips.Count);
        foreach (var clip in audioClips
                     .OrderBy(clip => clip.StartSeconds)
                     .ThenBy(clip => originalOrder[clip.AudioClipId]))
        {
            var lane = laneEnds.FindIndex(end => end <= clip.StartSeconds + 0.000_001);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(0);
            }

            laneEnds[lane] = clip.StartSeconds + clip.DurationSeconds;
            laneByClipId.Add(clip.AudioClipId, lane);
        }

        return new CompositionTimelineAudioLaneLayout(laneEnds.Count, laneByClipId);
    }

    public static double GetStickyContentOffset(
        double itemLeft,
        double itemWidth,
        double viewportLeft,
        double minimumTrailingWidth)
    {
        if (!double.IsFinite(itemLeft))
            throw new ArgumentOutOfRangeException(nameof(itemLeft));
        if (!double.IsFinite(itemWidth) || itemWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(itemWidth));
        if (!double.IsFinite(viewportLeft) || viewportLeft < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportLeft));
        if (!double.IsFinite(minimumTrailingWidth) || minimumTrailingWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumTrailingWidth));

        return Math.Clamp(
            viewportLeft - itemLeft,
            0,
            Math.Max(0, itemWidth - minimumTrailingWidth));
    }

    public static double GetEdgeAutoScrollDelta(
        double pointerX,
        double viewportWidth,
        double edgeZoneWidth = 48,
        double maximumStep = 48)
    {
        if (!double.IsFinite(pointerX))
            throw new ArgumentOutOfRangeException(nameof(pointerX));
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(edgeZoneWidth) || edgeZoneWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(edgeZoneWidth));
        if (!double.IsFinite(maximumStep) || maximumStep <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumStep));

        var effectiveEdgeWidth = Math.Min(edgeZoneWidth, viewportWidth / 2);
        if (pointerX < effectiveEdgeWidth)
        {
            var penetration = Math.Clamp((effectiveEdgeWidth - pointerX) / effectiveEdgeWidth, 0, 1);
            return -maximumStep * penetration;
        }
        if (pointerX > viewportWidth - effectiveEdgeWidth)
        {
            var penetration = Math.Clamp(
                (pointerX - (viewportWidth - effectiveEdgeWidth)) / effectiveEdgeWidth,
                0,
                1);
            return maximumStep * penetration;
        }
        return 0;
    }

    public static CompositionTimelineLayoutResult Calculate(
        IReadOnlyList<CompositionTimelineSegmentInput> segments,
        double viewportWidth,
        double minimumSegmentWidth = 88,
        double pixelsPerSecond = 24,
        double zoomFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (!double.IsFinite(viewportWidth) || viewportWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(minimumSegmentWidth) || minimumSegmentWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentWidth));
        if (!double.IsFinite(pixelsPerSecond) || pixelsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerSecond));
        if (!double.IsFinite(zoomFactor) || zoomFactor < 1)
            throw new ArgumentOutOfRangeException(nameof(zoomFactor));
        if (segments.Count == 0)
            return new CompositionTimelineLayoutResult(
                Math.Max(1, viewportWidth * zoomFactor), 0, 0, false, []);

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
        var baseContentWidth = Math.Max(
            Math.Max(1, viewportWidth),
            Math.Max(segments.Count * minimumSegmentWidth, projectedDuration * pixelsPerSecond));
        var contentWidth = baseContentWidth * zoomFactor;
        var minimumWidthTotal = segments.Count * minimumSegmentWidth;
        var distributableWidth = Math.Max(0, baseContentWidth - minimumWidthTotal);

        var spans = new List<CompositionTimelineSegmentSpan>(segments.Count);
        double left = 0;
        double startSeconds = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            var width = (minimumSegmentWidth +
                         distributableWidth * effectiveDurations[index] / projectedDuration) * zoomFactor;
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

    public static CompositionTimelineReorderPreview CalculateReorder(
        IReadOnlyList<CompositionTimelineSegmentInput> segments,
        Guid draggedSegmentId,
        double pointerX,
        double viewportWidth,
        double minimumSegmentWidth = 88,
        double pixelsPerSecond = 24,
        double zoomFactor = 1)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (!segments.Any(segment => segment.SegmentId == draggedSegmentId))
            throw new ArgumentException("The dragged segment is not present in the timeline.", nameof(draggedSegmentId));

        var remaining = segments
            .Where(segment => segment.SegmentId != draggedSegmentId)
            .ToArray();
        if (remaining.Length == 0)
            return new CompositionTimelineReorderPreview(0, [draggedSegmentId]);

        var remainingLayout = Calculate(
            remaining,
            viewportWidth,
            minimumSegmentWidth,
            pixelsPerSecond,
            zoomFactor);
        var insertionIndex = remainingLayout.Segments.Count;
        for (var index = 0; index < remainingLayout.Segments.Count; index++)
        {
            var span = remainingLayout.Segments[index];
            if (pointerX >= span.Left + span.Width / 2) continue;
            insertionIndex = index;
            break;
        }

        var orderedIds = remaining.Select(segment => segment.SegmentId).ToList();
        orderedIds.Insert(insertionIndex, draggedSegmentId);
        return new CompositionTimelineReorderPreview(insertionIndex, orderedIds);
    }

    private static double? ValidDuration(double? value) =>
        value is > 0 && double.IsFinite(value.Value) ? value : null;
}
