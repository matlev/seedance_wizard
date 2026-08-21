using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

internal sealed class CompositionSplitMutation
{
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionSplitMutation(CompositionCurrentAccessor current, TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
        _editor = editor;
    }

    public async Task<CompositionSegmentSplitResult> SplitAsync(
        Guid segmentId,
        ExactFramePosition position,
        AnchorBoundaryEdge boundaryEdge,
        double boundaryTimestampSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!Enum.IsDefined(boundaryEdge))
            throw new ArgumentOutOfRangeException(nameof(boundaryEdge));
        if (!double.IsFinite(boundaryTimestampSeconds) || boundaryTimestampSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(boundaryTimestampSeconds));

        var project = _current.Project;
        var (_, _, recipe) = _current.GetCurrent();
        var segment = recipe.Segments.SingleOrDefault(candidate => candidate.Id == segmentId)
            ?? throw new InvalidOperationException("The selected composition segment no longer exists.");
        if (position.SourceAssetId != segment.Source.AssetId ||
            position.SourceRecipeRevisionId != segment.Source.RecipeRevisionId)
            throw new InvalidOperationException("The split frame must belong to the selected segment's pinned source.");

        var assetCount = project.Assets.Count;
        var anchorCount = project.Anchors.Count;
        var anchorRevisionCount = project.AnchorRevisions.Count;
        var recipeRevisionCount = project.RecipeRevisions.Count;
        var projectModifiedAt = project.ModifiedAt;
        var anchor = new FrameAnchor { IsArchived = true };
        var sourceAsset = project.Assets.Single(asset => asset.Id == segment.Source.AssetId);
        var leadingClip = CreateSplitClip(project, sourceAsset, segment.Source, "part 1");
        var trailingClip = CreateSplitClip(project, sourceAsset, segment.Source, "part 2");
        var trailingSegmentId = Guid.NewGuid();

        try
        {
            project.Anchors.Add(anchor);
            var anchorRevision = project.CommitAnchorRevision(anchor.Id, position);
            var boundary = new RecipeBoundary
            {
                Kind = RecipeBoundaryKind.Anchor,
                Anchor = new AnchorRevisionReference
                {
                    AnchorId = anchor.Id,
                    AnchorRevisionId = anchorRevision.Id
                },
                Edge = boundaryEdge
            };
            var sourceDuration = sourceAsset.DurationSeconds ??
                                 sourceAsset.Encoding?.DurationSeconds ??
                                 sourceAsset.Virtual?.ExpectedMediaProperties?.DurationSeconds;
            var originalStart = ResolveBoundary(project, segment.Start, sourceDuration) ?? 0;
            var originalEnd = ResolveBoundary(project, segment.End, sourceDuration) ?? sourceDuration;
            if (boundaryTimestampSeconds <= originalStart ||
                originalEnd is { } end && boundaryTimestampSeconds >= end)
                throw new InvalidOperationException("The selected split boundary must leave media on both sides.");

            leadingClip.Virtual!.ExpectedMediaProperties!.DurationSeconds =
                Math.Max(0, boundaryTimestampSeconds - originalStart);
            trailingClip.Virtual!.ExpectedMediaProperties!.DurationSeconds = originalEnd is { } finalEnd
                ? Math.Max(0, finalEnd - boundaryTimestampSeconds)
                : null;
            project.AddAsset(leadingClip);
            project.AddAsset(trailingClip);

            var leadingRevision = project.CommitRecipe(leadingClip.Id, new TrimRecipe
            {
                Source = segment.Source with { },
                Start = segment.Start with { },
                End = boundary
            });
            var trailingRevision = project.CommitRecipe(trailingClip.Id, new TrimRecipe
            {
                Source = segment.Source with { },
                Start = boundary,
                End = segment.End with { }
            });
            var revision = await _editor.UpdateAsync(candidate =>
            {
                var index = candidate.Segments.FindIndex(item => item.Id == segmentId);
                if (index < 0)
                    throw new InvalidOperationException("The selected composition segment no longer exists.");

                var selected = candidate.Segments[index];
                candidate.Segments[index] = selected with
                {
                    Source = new AssetRevisionReference
                    {
                        AssetId = leadingClip.Id,
                        RecipeRevisionId = leadingRevision.Id
                    },
                    Start = RecipeBoundary.SourceStart,
                    End = RecipeBoundary.SourceEnd
                };
                candidate.Segments.Insert(index + 1, selected with
                {
                    Id = trailingSegmentId,
                    Source = new AssetRevisionReference
                    {
                        AssetId = trailingClip.Id,
                        RecipeRevisionId = trailingRevision.Id
                    },
                    Start = RecipeBoundary.SourceStart,
                    End = RecipeBoundary.SourceEnd
                });
            }, cancellationToken).ConfigureAwait(false);

            return new CompositionSegmentSplitResult(
                revision,
                segmentId,
                trailingSegmentId,
                leadingClip.Id,
                trailingClip.Id,
                anchor.Id,
                anchorRevision.Id,
                anchorRevision.TimestampSeconds);
        }
        catch
        {
            project.RecipeRevisions.RemoveRange(recipeRevisionCount, project.RecipeRevisions.Count - recipeRevisionCount);
            project.AnchorRevisions.RemoveRange(anchorRevisionCount, project.AnchorRevisions.Count - anchorRevisionCount);
            project.Anchors.RemoveRange(anchorCount, project.Anchors.Count - anchorCount);
            project.Assets.RemoveRange(assetCount, project.Assets.Count - assetCount);
            project.ModifiedAt = projectModifiedAt;
            throw;
        }
    }

    private static ProjectAsset CreateSplitClip(
        VideoProject project,
        ProjectAsset sourceAsset,
        AssetRevisionReference sourceReference,
        string suffix)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceAsset.EffectiveDisplayName);
        var requestedName = $"{baseName} — {suffix}";
        var displayName = requestedName;
        for (var copy = 2; project.Assets.Any(asset =>
                 asset.EffectiveDisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)); copy++)
            displayName = $"{requestedName} ({copy})";

        return new ProjectAsset
        {
            DisplayName = displayName,
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Origin = AssetOrigin.EditorDerived,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata
                {
                    ContainerFormat = "mp4"
                }
            },
            Provenance = new AssetProvenance
            {
                Operation = "timeline-split",
                SourceAssetIds = [sourceReference.AssetId],
                SourceRecipeRevisionId = sourceReference.RecipeRevisionId
            }
        };
    }

    private static double? ResolveBoundary(
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
