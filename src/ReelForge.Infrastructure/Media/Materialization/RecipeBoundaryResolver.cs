using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class RecipeBoundaryResolver
{
    private readonly IExactVideoFrameService _exactFrameService;

    public RecipeBoundaryResolver(IExactVideoFrameService exactFrameService)
    {
        _exactFrameService = exactFrameService;
    }

    public async Task<double> ResolveSecondsAsync(
        VideoProject project,
        AssetRevisionReference sourceReference,
        ProjectAsset sourceAsset,
        MaterializedMediaLease source,
        RecipeBoundary boundary,
        double? sourceDurationSeconds,
        bool isEnd,
        CancellationToken cancellationToken)
    {
        if (boundary.Kind == RecipeBoundaryKind.SourceStart) return 0;
        if (boundary.Kind == RecipeBoundaryKind.SourceEnd)
            return ResolveSourceDuration(sourceAsset, sourceDurationSeconds);
        if (boundary.Kind == RecipeBoundaryKind.Timestamp && boundary.TimestampSeconds is { } timestamp)
            return timestamp;
        if (boundary.Kind != RecipeBoundaryKind.Anchor || boundary.Anchor is null || boundary.Edge is null)
            throw new InvalidDataException("The Saved Clip contains an incomplete boundary.");
        var anchorRevision = project.AnchorRevisions.SingleOrDefault(candidate =>
                candidate.Id == boundary.Anchor.AnchorRevisionId && candidate.AnchorId == boundary.Anchor.AnchorId)
            ?? throw new InvalidOperationException(
                $"Clip boundary revision '{boundary.Anchor.AnchorRevisionId}' no longer exists.");
        if (anchorRevision.SourceAssetId != sourceAsset.Id ||
            anchorRevision.SourceRecipeRevisionId != sourceReference.RecipeRevisionId)
            throw new InvalidDataException("A Saved Clip boundary points at a different source asset.");
        if (!string.Equals(
                anchorRevision.SourceContentHash,
                source.ContentIdentity.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Saved Clip boundary no longer matches its source content.");

        if (boundary.Edge == AnchorBoundaryEdge.BeforeFrame) return anchorRevision.TimestampSeconds;
        var nearbyFrames = await _exactFrameService.IndexWindowAsync(
                source.Path,
                Math.Max(0, anchorRevision.TimestampSeconds),
                radiusSeconds: 2,
                cancellationToken)
            .ConfigureAwait(false);
        var next = nearbyFrames
            .Where(frame => frame.VideoStreamIndex == anchorRevision.VideoStreamIndex &&
                            frame.PresentationTimestamp > anchorRevision.PresentationTimestamp)
            .OrderBy(frame => frame.PresentationTimestamp)
            .FirstOrDefault();
        if (next is not null) return next.TimestampSeconds;
        if (isEnd) return ResolveSourceDuration(sourceAsset, sourceDurationSeconds);
        throw new InvalidDataException("The frame following the Saved Clip start could not be resolved.");
    }

    public static AssetRevisionReference GetAssetRevisionReference(MediaRenderPlanNode node) => new()
    {
        AssetId = node.AssetId,
        RecipeRevisionId = node switch
        {
            TrimRenderPlanNode trim => trim.RecipeRevisionId,
            ExtractFrameRenderPlanNode frame => frame.RecipeRevisionId,
            CompositionRenderPlanNode composition => composition.RecipeRevisionId,
            _ => null
        }
    };

    public static MaterializationRequest GetBoundarySourceRequest(
        VideoProject project,
        AssetRevisionReference source,
        RecipeBoundary start,
        RecipeBoundary end,
        MaterializationRequest request)
    {
        if (source.RecipeRevisionId is null ||
            !ReferencesVirtualExactPosition(project, start, source) &&
            !ReferencesVirtualExactPosition(project, end, source))
            return request;
        return request with
        {
            Purpose = MaterializationPurpose.FrameExtraction,
            Profile = null
        };
    }

    private static double ResolveSourceDuration(ProjectAsset sourceAsset, double? materializedDurationSeconds) =>
        materializedDurationSeconds ?? sourceAsset.DurationSeconds ?? sourceAsset.Encoding?.DurationSeconds ??
        sourceAsset.Virtual?.ExpectedMediaProperties?.DurationSeconds
        ?? throw new InvalidDataException("The source duration is required to resolve the end of this Saved Clip.");

    private static bool ReferencesVirtualExactPosition(
        VideoProject project,
        RecipeBoundary boundary,
        AssetRevisionReference source) =>
        boundary.Kind == RecipeBoundaryKind.Anchor &&
        boundary.Anchor is { } reference &&
        project.AnchorRevisions.Any(revision =>
            revision.Id == reference.AnchorRevisionId &&
            revision.AnchorId == reference.AnchorId &&
            revision.SourceAssetId == source.AssetId &&
            revision.SourceRecipeRevisionId == source.RecipeRevisionId);
}
