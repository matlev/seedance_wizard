using ReelForge.Core;
using ReelForge.Application.Editing.Composition;

namespace ReelForge.Application;

public sealed class CompositionSegmentSplitService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly IExactVideoFrameService _exactFrameService;
    private readonly CompositionCurrentAccessor _current;
    private readonly CompositionSplitMutation _splitMutation;

    public CompositionSegmentSplitService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IExactVideoFrameService exactFrameService)
    {
        _workspace = workspace;
        _materializer = materializer;
        _exactFrameService = exactFrameService;
        _current = new CompositionCurrentAccessor(workspace);
        var editor = new TransactionalCompositionRevisionEditor(workspace, _current);
        _splitMutation = new CompositionSplitMutation(_current, editor);
    }

    public async Task<CompositionSegmentSplitResult> SplitAsync(
        Guid segmentId,
        TimeSpan offsetWithinSegment,
        AnchorBoundaryEdge boundaryEdge = AnchorBoundaryEdge.BeforeFrame,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(boundaryEdge))
            throw new ArgumentOutOfRangeException(nameof(boundaryEdge));
        if (offsetWithinSegment < TimeSpan.Zero ||
            (boundaryEdge == AnchorBoundaryEdge.BeforeFrame && offsetWithinSegment == TimeSpan.Zero))
            throw new ArgumentOutOfRangeException(nameof(offsetWithinSegment), "Move the playhead inside the selected segment.");
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        var (_, _, recipe) = _current.GetCurrent();
        var segment = recipe.Segments.SingleOrDefault(candidate => candidate.Id == segmentId)
            ?? throw new InvalidOperationException("The selected composition segment no longer exists.");
        var sourceAsset = project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId)
            ?? throw new InvalidOperationException("The selected segment's source no longer exists.");

        await using var source = await _materializer.MaterializeAsync(
                project,
                location,
                new MaterializationRequest(
                    new AssetMaterializationTarget(segment.Source.AssetId, segment.Source.RecipeRevisionId),
                    MaterializationPurpose.FrameExtraction),
                cancellationToken)
            .ConfigureAwait(false);
        var sourceDuration = source.Encoding?.DurationSeconds ??
                             sourceAsset.DurationSeconds ??
                             sourceAsset.Encoding?.DurationSeconds ??
                             sourceAsset.Virtual?.ExpectedMediaProperties?.DurationSeconds
            ?? throw new InvalidDataException("The source duration is required to split this segment.");
        var startSeconds = await ResolveBoundarySecondsAsync(
                project, segment.Source, source, segment.Start, sourceDuration, isEnd: false, cancellationToken)
            .ConfigureAwait(false);
        var endSeconds = await ResolveBoundarySecondsAsync(
                project, segment.Source, source, segment.End, sourceDuration, isEnd: true, cancellationToken)
            .ConfigureAwait(false);
        var targetSeconds = startSeconds + offsetWithinSegment.TotalSeconds;
        if (!double.IsFinite(targetSeconds) ||
            targetSeconds < startSeconds ||
            (boundaryEdge == AnchorBoundaryEdge.BeforeFrame && targetSeconds <= startSeconds) ||
            targetSeconds >= endSeconds)
            throw new InvalidOperationException("Move the playhead inside the selected segment before splitting it.");

        var nearbyFrames = await _exactFrameService.IndexWindowAsync(
                source.Path,
                Math.Max(0, targetSeconds),
                radiusSeconds: 2,
                cancellationToken)
            .ConfigureAwait(false);
        var candidates = nearbyFrames
            .Where(frame => (boundaryEdge == AnchorBoundaryEdge.AfterFrame
                                ? frame.TimestampSeconds >= startSeconds - 0.000_000_1
                                : frame.TimestampSeconds > startSeconds + 0.000_000_1) &&
                            frame.TimestampSeconds < endSeconds - 0.000_000_1)
            .OrderBy(frame => frame.TimestampSeconds)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("No decoded frame exists strictly inside the selected segment.");
        var frame = candidates[ExactFrameContactWindow.FindNearestIndex(candidates, targetSeconds)];
        var boundaryTimestampSeconds = frame.TimestampSeconds;
        if (boundaryEdge == AnchorBoundaryEdge.AfterFrame)
        {
            var nextFrame = nearbyFrames
                .Where(candidate => candidate.VideoStreamIndex == frame.VideoStreamIndex &&
                                    candidate.PresentationTimestamp > frame.PresentationTimestamp)
                .OrderBy(candidate => candidate.PresentationTimestamp)
                .FirstOrDefault();
            if (nextFrame is null)
            {
                var followingFrames = await _exactFrameService.IndexWindowAsync(
                        source.Path,
                        Math.Min(endSeconds, frame.TimestampSeconds + 2),
                        radiusSeconds: 4,
                        cancellationToken)
                    .ConfigureAwait(false);
                nextFrame = followingFrames
                    .Where(candidate => candidate.VideoStreamIndex == frame.VideoStreamIndex &&
                                        candidate.PresentationTimestamp > frame.PresentationTimestamp)
                    .OrderBy(candidate => candidate.PresentationTimestamp)
                    .FirstOrDefault();
            }
            if (nextFrame is null || nextFrame.TimestampSeconds >= endSeconds - 0.000_000_1)
                throw new InvalidOperationException(
                    "The selected frame is the segment's final frame, so it cannot start a non-empty second clip.");
            boundaryTimestampSeconds = nextFrame.TimestampSeconds;
        }
        var sourceHash = source.ContentIdentity.Sha256;
        if (source.ContentIdentity.Status != ContentHashStatus.Verified || string.IsNullOrWhiteSpace(sourceHash))
            throw new InvalidDataException("The selected segment source does not have a verified content identity.");

        var position = new ExactFramePosition(
            segment.Source.AssetId,
            sourceHash,
            frame.VideoStreamIndex,
            frame.PresentationTimestamp,
            frame.TimeBaseNumerator,
            frame.TimeBaseDenominator,
            frame.FrameNumber,
            segment.Source.RecipeRevisionId);
        return await _splitMutation.SplitAsync(
                segmentId,
                position,
                boundaryEdge,
                boundaryTimestampSeconds,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<double> ResolveBoundarySecondsAsync(
        VideoProject project,
        AssetRevisionReference sourceReference,
        MaterializedMediaLease source,
        RecipeBoundary boundary,
        double sourceDuration,
        bool isEnd,
        CancellationToken cancellationToken)
    {
        if (boundary.Kind == RecipeBoundaryKind.SourceStart) return 0;
        if (boundary.Kind == RecipeBoundaryKind.SourceEnd) return sourceDuration;
        if (boundary.Kind == RecipeBoundaryKind.Timestamp && boundary.TimestampSeconds is { } timestamp) return timestamp;
        if (boundary.Kind != RecipeBoundaryKind.Anchor || boundary.Anchor is null || boundary.Edge is null)
            throw new InvalidDataException("The selected segment has an incomplete exact boundary.");
        var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                candidate.Id == boundary.Anchor.AnchorRevisionId && candidate.AnchorId == boundary.Anchor.AnchorId)
            ?? throw new InvalidDataException("The selected segment references a missing exact boundary.");
        if (revision.SourceAssetId != sourceReference.AssetId ||
            revision.SourceRecipeRevisionId != sourceReference.RecipeRevisionId ||
            !string.Equals(revision.SourceContentHash, source.ContentIdentity.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected segment boundary no longer matches its pinned source.");
        if (boundary.Edge == AnchorBoundaryEdge.BeforeFrame) return revision.TimestampSeconds;

        var nearbyFrames = await _exactFrameService.IndexWindowAsync(
                source.Path,
                Math.Max(0, revision.TimestampSeconds),
                radiusSeconds: 2,
                cancellationToken)
            .ConfigureAwait(false);
        var next = nearbyFrames
            .Where(frame => frame.VideoStreamIndex == revision.VideoStreamIndex &&
                            frame.PresentationTimestamp > revision.PresentationTimestamp)
            .OrderBy(frame => frame.PresentationTimestamp)
            .FirstOrDefault();
        if (next is not null) return next.TimestampSeconds;
        if (isEnd) return sourceDuration;
        throw new InvalidDataException("The frame following the selected segment boundary could not be resolved.");
    }
}

public static class CompositionSegmentTiming
{
    public static double? ResolveDuration(
        VideoProject project,
        CompositionSegment segment,
        ProjectAsset? source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(segment);
        var sourceDuration = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ??
                             source?.Virtual?.ExpectedMediaProperties?.DurationSeconds;
        var start = ResolveApproximateBoundary(project, segment.Start, sourceDuration);
        var end = ResolveApproximateBoundary(project, segment.End, sourceDuration);
        return start is { } startSeconds && end is { } endSeconds && endSeconds > startSeconds
            ? endSeconds - startSeconds
            : null;
    }

    private static double? ResolveApproximateBoundary(
        VideoProject project,
        RecipeBoundary boundary,
        double? sourceDuration) => boundary.Kind switch
    {
        RecipeBoundaryKind.SourceStart => 0,
        RecipeBoundaryKind.SourceEnd => sourceDuration,
        RecipeBoundaryKind.Timestamp => boundary.TimestampSeconds,
        RecipeBoundaryKind.Anchor when boundary.Anchor is { } reference =>
            project.AnchorRevisions.SingleOrDefault(revision =>
                revision.Id == reference.AnchorRevisionId && revision.AnchorId == reference.AnchorId)?.TimestampSeconds,
        _ => null
    };
}
