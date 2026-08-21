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

        var segments = new List<CompositionAuditionSegment>(recipe.Segments.Count);
        var timelineStart = 0d;
        foreach (var segment in recipe.Segments)
        {
            var source = project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId)
                ?? throw new InvalidDataException(
                    $"Composition segment {segment.Id} references missing asset {segment.Source.AssetId}.");
            var duration = CompositionSegmentTiming.ResolveDuration(project, segment, source);
            if (duration is not > 0 || !double.IsFinite(duration.Value))
                throw new InvalidDataException(
                    $"The duration of '{source.EffectiveDisplayName}' is unknown or invalid.");

            segments.Add(new CompositionAuditionSegment(
                segment.Id,
                segment.Source,
                timelineStart,
                ResolveBoundarySeconds(project, segment.Start, source, isEnd: false),
                duration.Value,
                segment.AudioEnabled));
            timelineStart += duration.Value;
        }

        if (segments.Count == 0)
            throw new InvalidDataException("The Working Composition has no video segments.");

        return new CompositionAuditionPlan(segments);
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

    private static double ResolveBoundarySeconds(
        VideoProject project,
        RecipeBoundary boundary,
        ProjectAsset source,
        bool isEnd)
    {
        var duration = source.DurationSeconds ?? source.Encoding?.DurationSeconds ??
                       source.Virtual?.ExpectedMediaProperties?.DurationSeconds ?? 0;
        return boundary.Kind switch
        {
            RecipeBoundaryKind.SourceStart => 0,
            RecipeBoundaryKind.SourceEnd => duration,
            RecipeBoundaryKind.Timestamp => boundary.TimestampSeconds ?? (isEnd ? duration : 0),
            RecipeBoundaryKind.Anchor when boundary.Anchor is { } reference =>
                project.AnchorRevisions.Single(revision =>
                    revision.Id == reference.AnchorRevisionId && revision.AnchorId == reference.AnchorId)
                    .TimestampSeconds,
            _ => isEnd ? duration : 0
        };
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
