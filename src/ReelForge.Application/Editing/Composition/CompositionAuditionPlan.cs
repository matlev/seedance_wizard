using System.Numerics;
using System.Globalization;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed record CompositionAuditionSegment(
    Guid SegmentId,
    AssetRevisionReference Source,
    double TimelineStartSeconds,
    double SourceStartSeconds,
    double DurationSeconds,
    bool AudioEnabled)
{
    public double TimelineEndSeconds => TimelineStartSeconds + DurationSeconds;
}

public sealed class CompositionAuditionPlan
{
    private const double BoundaryToleranceSeconds = 0.000_000_1;

    private CompositionAuditionPlan(IReadOnlyList<CompositionAuditionSegment> segments)
    {
        Segments = segments;
        DurationSeconds = segments.Count == 0 ? 0 : segments[^1].TimelineEndSeconds;
    }

    public IReadOnlyList<CompositionAuditionSegment> Segments { get; }
    public double DurationSeconds { get; }

    public static CompositionAuditionPlan Create(VideoProject project, CompositionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(recipe);

        // The existing audition session is a sequential source-player session, not a
        // compositing engine. Keep its projection deliberately narrow until the M6
        // renderer can honor general multitrack composition meaning.
        var contributingTracks = recipe.Composition.ContributingVideoTracks;
        if (contributingTracks.Count != 1)
            throw new InvalidDataException(
                "Composition audition currently supports exactly one visible video track. " +
                "Show one video track or use a future track-aware preview.");

        var items = contributingTracks[0].Items
            .OrderBy(item => item.CompositionStart)
            .ToArray();
        if (items.Length == 0)
            throw new InvalidDataException("The visible video track has no items to audition.");

        var segments = new List<CompositionAuditionSegment>(items.Length);
        var expectedStart = new ExactTime(0, 1);
        foreach (var item in items)
        {
            var source = project.Assets.SingleOrDefault(asset => asset.Id == item.Source.AssetId);
            if (item.CompositionStart != expectedStart)
            {
                var issue = item.CompositionStart < expectedStart
                    ? "is overlapping the preceding item"
                    : "has a gap before it";
                var displayName = source?.EffectiveDisplayName ?? "Missing source";
                throw new InvalidDataException(
                    $"Composition audition currently requires one contiguous video track; “{displayName}” at {FormatTimelinePosition(item.CompositionStart)} {issue}.");
            }

            if (source is null)
                throw new InvalidDataException(
                    $"Composition video item '{item.Id}' references missing asset {item.Source.AssetId}.");
            if (source.MediaType != MediaType.Video)
                throw new InvalidDataException(
                    $"Composition video item '{item.Id}' references non-video asset '{source.EffectiveDisplayName}'.");

            var timing = item.TimingAssessment;
            if (timing.Readiness is not TimingReadiness.Exact and not TimingReadiness.Estimated ||
                !timing.HasUsableSequentialDecodePath)
                throw new InvalidDataException(
                    $"Composition video item '{item.Id}' does not retain usable pinned timing evidence for audition.");
            if (timing.SourcePresentationStart is null)
                throw new InvalidDataException(
                    $"Composition video item '{item.Id}' has no pinned source start for audition.");

            segments.Add(new CompositionAuditionSegment(
                item.Id,
                item.Source,
                item.CompositionStart.ToDoubleSeconds(),
                timing.SourcePresentationStart.ToDoubleSeconds(),
                timing.TimelineDuration.ToDoubleSeconds(),
                AudioEnabled: false));
            expectedStart = Add(item.CompositionStart, timing.TimelineDuration);
        }

        return new CompositionAuditionPlan(segments);
    }

    private static string FormatTimelinePosition(ExactTime time)
    {
        var milliseconds = time.RescaleToInteger(1000, ExactTimeRounding.NearestTiesToEven);
        var value = TimeSpan.FromMilliseconds(milliseconds);
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    public int FindSegmentIndex(double globalSeconds)
    {
        if (Segments.Count == 0) return -1;
        var position = ClampGlobalPosition(globalSeconds);
        for (var index = 0; index < Segments.Count; index++)
        {
            if (position < Segments[index].TimelineEndSeconds - BoundaryToleranceSeconds)
                return index;
        }
        return Segments.Count - 1;
    }

    public double ClampGlobalPosition(double globalSeconds) =>
        Math.Clamp(double.IsFinite(globalSeconds) ? globalSeconds : 0, 0, DurationSeconds);

    public double GetSourcePosition(int segmentIndex, double globalSeconds)
    {
        var segment = GetSegment(segmentIndex);
        var position = Math.Clamp(
            ClampGlobalPosition(globalSeconds),
            segment.TimelineStartSeconds,
            segment.TimelineEndSeconds);
        return segment.SourceStartSeconds + position - segment.TimelineStartSeconds;
    }

    public double GetGlobalPosition(int segmentIndex, double sourceSeconds)
    {
        var segment = GetSegment(segmentIndex);
        return Math.Clamp(
            segment.TimelineStartSeconds + sourceSeconds - segment.SourceStartSeconds,
            segment.TimelineStartSeconds,
            segment.TimelineEndSeconds);
    }

    public bool TryGetNextSegmentIndex(int segmentIndex, out int nextIndex)
    {
        nextIndex = segmentIndex + 1;
        return segmentIndex >= 0 && nextIndex < Segments.Count;
    }

    private CompositionAuditionSegment GetSegment(int segmentIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= Segments.Count)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        return Segments[segmentIndex];
    }

    private static ExactTime Add(ExactTime left, ExactTime right)
    {
        var numerator = (BigInteger)left.Numerator * right.Denominator +
                        (BigInteger)right.Numerator * left.Denominator;
        var denominator = (BigInteger)left.Denominator * right.Denominator;
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        numerator /= divisor;
        denominator /= divisor;
        if (numerator < long.MinValue || numerator > long.MaxValue || denominator > long.MaxValue)
            throw new InvalidDataException("The sequential audition timeline exceeds the supported exact time domain.");
        return new ExactTime((long)numerator, (long)denominator);
    }
}

public sealed record CompositionAuditionPosition(
    int SegmentIndex,
    double GlobalSeconds,
    double SourceSeconds);

public sealed class CompositionAuditionSession
{
    public CompositionAuditionSession(
        Guid recipeRevisionId,
        CompositionAuditionPlan plan,
        double initialGlobalSeconds = 0)
    {
        if (recipeRevisionId == Guid.Empty)
            throw new ArgumentException("A composition audition must pin a recipe revision.", nameof(recipeRevisionId));
        RecipeRevisionId = recipeRevisionId;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Seek(initialGlobalSeconds);
    }

    public Guid RecipeRevisionId { get; }
    public CompositionAuditionPlan Plan { get; }
    public int ActiveSegmentIndex { get; private set; }
    public double PositionSeconds { get; private set; }
    public CompositionAuditionSegment ActiveSegment => Plan.Segments[ActiveSegmentIndex];

    public CompositionAuditionPosition Seek(double globalSeconds)
    {
        PositionSeconds = Plan.ClampGlobalPosition(globalSeconds);
        ActiveSegmentIndex = Plan.FindSegmentIndex(PositionSeconds);
        return CurrentPosition;
    }

    public CompositionAuditionPosition ActivateSegment(int segmentIndex, double globalSeconds)
    {
        if (segmentIndex < 0 || segmentIndex >= Plan.Segments.Count)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        ActiveSegmentIndex = segmentIndex;
        var segment = ActiveSegment;
        PositionSeconds = Math.Clamp(
            Plan.ClampGlobalPosition(globalSeconds),
            segment.TimelineStartSeconds,
            segment.TimelineEndSeconds);
        return CurrentPosition;
    }

    public CompositionAuditionPosition UpdateFromSourcePosition(double sourceSeconds)
    {
        PositionSeconds = Plan.GetGlobalPosition(ActiveSegmentIndex, sourceSeconds);
        return CurrentPosition;
    }

    public bool TryAdvance(out CompositionAuditionPosition position)
    {
        if (!Plan.TryGetNextSegmentIndex(ActiveSegmentIndex, out var nextIndex))
        {
            position = CurrentPosition;
            return false;
        }
        position = ActivateSegment(nextIndex, Plan.Segments[nextIndex].TimelineStartSeconds);
        return true;
    }

    public CompositionAuditionPosition Complete()
    {
        PositionSeconds = Plan.DurationSeconds;
        ActiveSegmentIndex = Plan.Segments.Count - 1;
        return CurrentPosition;
    }

    public CompositionAuditionPosition CurrentPosition => new(
        ActiveSegmentIndex,
        PositionSeconds,
        Plan.GetSourcePosition(ActiveSegmentIndex, PositionSeconds));
}
