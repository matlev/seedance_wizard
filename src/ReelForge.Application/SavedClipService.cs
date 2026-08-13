using ReelForge.Core;

namespace ReelForge.Application;

public enum ClipBoundaryKind { SourceStart, SourceEnd, ExactFrame }

public sealed record ClipBoundarySelection(
    ClipBoundaryKind Kind,
    ExactFramePosition? ExactPosition = null,
    AnchorBoundaryEdge? Edge = null)
{
    public static ClipBoundarySelection SourceStart { get; } = new(ClipBoundaryKind.SourceStart);
    public static ClipBoundarySelection SourceEnd { get; } = new(ClipBoundaryKind.SourceEnd);

    public static ClipBoundarySelection AtFrame(ExactFramePosition position, AnchorBoundaryEdge edge) =>
        new(ClipBoundaryKind.ExactFrame, position, edge);
}

public sealed class SavedClipService
{
    private readonly ProjectWorkspace _workspace;

    public SavedClipService(ProjectWorkspace workspace) => _workspace = workspace;

    public async Task<ProjectAsset> CreateAsync(
        string displayName,
        Guid sourceAssetId,
        ClipBoundarySelection start,
        ClipBoundarySelection end,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The clip source no longer exists.");
        if (source is not { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Video })
            throw new InvalidOperationException("Saved Clips currently require a durable physical video source.");
        ValidateBoundaryKinds(start, end);
        ValidateOrder(start, end, source.DurationSeconds);

        var assetsCount = project.Assets.Count;
        var anchorsCount = project.Anchors.Count;
        var anchorRevisionsCount = project.AnchorRevisions.Count;
        var recipeRevisionsCount = project.RecipeRevisions.Count;
        try
        {
            var startBoundary = CreateBoundary(project, start);
            var endBoundary = CreateBoundary(project, end);
            var clip = new ProjectAsset
            {
                DisplayName = displayName.Trim(),
                MediaType = MediaType.Video,
                StorageKind = AssetStorageKind.Virtual,
                Origin = AssetOrigin.EditorDerived,
                Physical = null,
                Virtual = new VirtualAssetState
                {
                    Kind = VirtualAssetKind.SavedClip,
                    ExpectedMediaProperties = new MediaEncodingMetadata
                    {
                        ContainerFormat = "mp4",
                        DurationSeconds = CalculateDuration(start, end, source.DurationSeconds)
                    }
                },
                Provenance = new AssetProvenance
                {
                    Operation = "saved-clip",
                    SourceAssetIds = [source.Id]
                }
            };
            project.AddAsset(clip);
            project.CommitRecipe(clip.Id, new TrimRecipe
            {
                Source = new AssetRevisionReference { AssetId = source.Id },
                Start = startBoundary,
                End = endBoundary
            });
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return clip;
        }
        catch
        {
            project.Assets.RemoveRange(assetsCount, project.Assets.Count - assetsCount);
            project.Anchors.RemoveRange(anchorsCount, project.Anchors.Count - anchorsCount);
            project.AnchorRevisions.RemoveRange(anchorRevisionsCount, project.AnchorRevisions.Count - anchorRevisionsCount);
            project.RecipeRevisions.RemoveRange(recipeRevisionsCount, project.RecipeRevisions.Count - recipeRevisionsCount);
            throw;
        }
    }

    private static RecipeBoundary CreateBoundary(VideoProject project, ClipBoundarySelection selection)
    {
        if (selection.Kind == ClipBoundaryKind.SourceStart) return RecipeBoundary.SourceStart;
        if (selection.Kind == ClipBoundaryKind.SourceEnd) return RecipeBoundary.SourceEnd;
        var position = selection.ExactPosition
            ?? throw new InvalidOperationException("An exact clip boundary requires a decoded frame position.");
        var edge = selection.Edge
            ?? throw new InvalidOperationException("An exact clip boundary requires BeforeFrame or AfterFrame intent.");
        var anchor = new FrameAnchor { IsArchived = true };
        project.Anchors.Add(anchor);
        var revision = project.CommitAnchorRevision(anchor.Id, position);
        return new RecipeBoundary
        {
            Kind = RecipeBoundaryKind.Anchor,
            Anchor = new AnchorRevisionReference { AnchorId = anchor.Id, AnchorRevisionId = revision.Id },
            Edge = edge
        };
    }

    private static void ValidateBoundaryKinds(ClipBoundarySelection start, ClipBoundarySelection end)
    {
        if (start.Kind == ClipBoundaryKind.SourceEnd)
            throw new InvalidOperationException("A clip cannot start at the end of its source.");
        if (end.Kind == ClipBoundaryKind.SourceStart)
            throw new InvalidOperationException("A clip cannot end at the start of its source.");
    }

    private static void ValidateOrder(
        ClipBoundarySelection start,
        ClipBoundarySelection end,
        double? sourceDuration)
    {
        var startSeconds = start.Kind == ClipBoundaryKind.SourceStart ? 0 : ToSeconds(start.ExactPosition);
        var endSeconds = end.Kind == ClipBoundaryKind.SourceEnd ? sourceDuration : ToSeconds(end.ExactPosition);
        if (startSeconds is { } startValue && endSeconds is { } endValue && endValue <= startValue)
            throw new InvalidOperationException("The clip end must follow its start.");
    }

    private static double? CalculateDuration(
        ClipBoundarySelection start,
        ClipBoundarySelection end,
        double? sourceDuration)
    {
        var startSeconds = start.Kind == ClipBoundaryKind.SourceStart ? 0 : ToSeconds(start.ExactPosition);
        var endSeconds = end.Kind == ClipBoundaryKind.SourceEnd ? sourceDuration : ToSeconds(end.ExactPosition);
        return startSeconds is { } startValue && endSeconds is { } endValue
            ? Math.Max(0, endValue - startValue)
            : null;
    }

    private static double? ToSeconds(ExactFramePosition? position) => position is null
        ? null
        : position.PresentationTimestamp * (double)position.TimeBaseNumerator / position.TimeBaseDenominator;
}
