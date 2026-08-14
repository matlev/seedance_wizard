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

    public async Task DeleteAsync(Guid savedClipAssetId, CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var clip = project.Assets.SingleOrDefault(asset => asset.Id == savedClipAssetId)
            ?? throw new InvalidOperationException("The Saved Clip no longer exists.");
        if (clip.Virtual?.Kind != VirtualAssetKind.SavedClip)
            throw new InvalidOperationException("Only a Saved Clip can be removed through this operation.");

        var dependencies = FindExternalDependencies(project, clip.Id);
        if (dependencies.Count > 0)
            throw new InvalidOperationException(
                $"'{clip.EffectiveDisplayName}' cannot be deleted because it is used by {string.Join(", ", dependencies)}.");

        var assets = project.Assets.ToList();
        var revisions = project.RecipeRevisions.ToList();
        var drafts = project.RecipeDrafts.ToList();
        var anchors = project.Anchors.ToList();
        var anchorRevisions = project.AnchorRevisions.ToList();
        var modifiedAt = project.ModifiedAt;
        try
        {
            var ownedRevisions = project.RecipeRevisions
                .Where(revision => revision.VirtualAssetId == clip.Id)
                .ToArray();
            var boundaryAnchorIds = ownedRevisions
                .SelectMany(revision => GetAnchorReferences(revision.Recipe))
                .Select(reference => reference.AnchorId)
                .Distinct()
                .ToArray();
            project.RecipeDrafts.RemoveAll(draft => draft.VirtualAssetId == clip.Id);
            project.RecipeRevisions.RemoveAll(revision => revision.VirtualAssetId == clip.Id);
            project.Assets.Remove(clip);
            foreach (var anchorId in boundaryAnchorIds)
            {
                if (project.Anchors.Any(anchor => anchor.Id == anchorId))
                    project.RemoveOrArchiveAnchor(anchorId);
            }
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            project.Assets = assets;
            project.RecipeRevisions = revisions;
            project.RecipeDrafts = drafts;
            project.Anchors = anchors;
            project.AnchorRevisions = anchorRevisions;
            project.ModifiedAt = modifiedAt;
            throw;
        }
    }

    private static List<string> FindExternalDependencies(VideoProject project, Guid clipId)
    {
        var dependencies = new List<string>();
        if (project.CurrentGenerationDraft?.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.Asset &&
                reference.LogicalObjectId == clipId) == true)
            dependencies.Add("the current generation draft");
        if (project.Generations.Any(generation => generation.RequestSnapshot.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.Asset &&
                reference.LogicalObjectId == clipId)))
            dependencies.Add("submitted generation history");
        if (project.RecipeRevisions.Any(revision =>
                revision.VirtualAssetId != clipId && RecipeReferencesAsset(revision.Recipe, clipId)) ||
            project.RecipeDrafts.Any(draft =>
                draft.VirtualAssetId != clipId && RecipeReferencesAsset(draft.EditableRecipe, clipId)))
            dependencies.Add("another media recipe");
        if (project.Assets.Any(asset => asset.Id != clipId &&
                asset.Provenance?.SourceAssetIds.Contains(clipId) == true))
            dependencies.Add("derived media history");
        return dependencies;
    }

    private static bool RecipeReferencesAsset(AssetRecipe recipe, Guid assetId) => recipe switch
    {
        TrimRecipe trim => trim.Source.AssetId == assetId,
        ExtractFrameRecipe frame => frame.Source.AssetId == assetId,
        CompositionRecipe composition => composition.Segments.Any(segment => segment.Source.AssetId == assetId) ||
                                         composition.AudioClips.Any(clip => clip.Source.AssetId == assetId),
        _ => false
    };

    private static IEnumerable<AnchorRevisionReference> GetAnchorReferences(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => new[] { trim.Start.Anchor, trim.End.Anchor }.OfType<AnchorRevisionReference>(),
        _ => []
    };

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
