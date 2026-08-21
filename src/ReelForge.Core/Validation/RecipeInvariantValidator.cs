namespace ReelForge.Core;

internal static class RecipeInvariantValidator
{
    public static void Validate(
        VideoProject project,
        ProjectValidationContext context,
        List<string> errors)
    {
        foreach (var revision in project.RecipeRevisions)
        {
            if (!context.Assets.TryGetValue(revision.VirtualAssetId, out var asset) ||
                asset.StorageKind != AssetStorageKind.Virtual)
                errors.Add($"Recipe revision '{revision.Id}' must belong to an existing virtual asset.");
            if (revision.RevisionNumber < 1)
                errors.Add($"Recipe revision '{revision.Id}' has an invalid number.");
            if (revision.PreviousRevisionId is { } previousId)
            {
                if (!context.RecipeRevisions.TryGetValue(previousId, out var previous) ||
                    previous.VirtualAssetId != revision.VirtualAssetId ||
                    previous.RevisionNumber >= revision.RevisionNumber)
                    errors.Add($"Recipe revision '{revision.Id}' has an invalid predecessor.");
            }

            foreach (var source in GetSources(revision.Recipe))
                ValidateAssetRevisionReference(
                    source,
                    context.Assets,
                    context.RecipeRevisions,
                    $"Recipe revision '{revision.Id}'",
                    errors);

            foreach (var anchorReference in GetAnchors(revision.Recipe))
                ValidateAnchorRevisionReference(
                    anchorReference,
                    context.Anchors,
                    context.AnchorRevisions,
                    $"Recipe revision '{revision.Id}'",
                    errors);

            ValidateSemantics(revision, context.AnchorRevisions, errors);
        }

        foreach (var duplicate in project.RecipeRevisions
                     .GroupBy(revision => (revision.VirtualAssetId, revision.RevisionNumber))
                     .Where(group => group.Count() > 1))
            errors.Add(
                $"Virtual asset '{duplicate.Key.VirtualAssetId}' has duplicate recipe revision number {duplicate.Key.RevisionNumber}.");

        foreach (var draft in project.RecipeDrafts)
        {
            if (draft.VirtualAssetId is { } assetId &&
                (!context.Assets.TryGetValue(assetId, out var asset) ||
                 asset.StorageKind != AssetStorageKind.Virtual))
                errors.Add($"Recipe draft '{draft.Id}' references an invalid virtual asset.");
            if (draft.BasedOnRevisionId is { } revisionId &&
                !context.RecipeRevisions.ContainsKey(revisionId))
                errors.Add($"Recipe draft '{draft.Id}' references missing base revision '{revisionId}'.");
        }

        foreach (var asset in project.Assets.Where(asset => asset.StorageKind == AssetStorageKind.Virtual))
        {
            var currentId = asset.Virtual?.CurrentRecipeRevisionId;
            if (currentId is null ||
                !context.RecipeRevisions.TryGetValue(currentId.Value, out var current) ||
                current.VirtualAssetId != asset.Id)
                errors.Add($"Virtual asset '{asset.Id}' must reference its current committed recipe revision.");
        }

        var states = new Dictionary<Guid, int>();
        foreach (var revision in project.RecipeRevisions)
            Visit(revision.Id, context.RecipeRevisions, states, errors);
    }

    public static void ValidateAssetRevisionReference(
        AssetRevisionReference reference,
        Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, RecipeRevision> revisions,
        string owner,
        List<string> errors)
    {
        if (!assets.TryGetValue(reference.AssetId, out var source))
        {
            errors.Add($"{owner} references missing asset '{reference.AssetId}'.");
            return;
        }

        if (source.StorageKind == AssetStorageKind.Virtual)
        {
            if (reference.RecipeRevisionId is null ||
                !revisions.TryGetValue(reference.RecipeRevisionId.Value, out var revision) ||
                revision.VirtualAssetId != source.Id)
                errors.Add($"{owner} must pin an exact revision for virtual asset '{source.Id}'.");
        }
        else if (reference.RecipeRevisionId is not null)
            errors.Add($"{owner} cannot assign a recipe revision to physical asset '{source.Id}'.");
    }

    private static void ValidateSemantics(
        RecipeRevision revision,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        List<string> errors)
    {
        switch (revision.Recipe)
        {
            case TrimRecipe trim:
                ValidateBoundary(trim.Start, trim.Source, revision.Id, anchorRevisions, errors);
                ValidateBoundary(trim.End, trim.Source, revision.Id, anchorRevisions, errors);
                var startSeconds = ResolveBoundarySeconds(trim.Start, anchorRevisions);
                var endSeconds = ResolveBoundarySeconds(trim.End, anchorRevisions);
                if (startSeconds is { } start && endSeconds is { } end && end <= start)
                    errors.Add($"Recipe revision '{revision.Id}' trim end must follow its start.");
                break;
            case ExtractFrameRecipe frame when
                anchorRevisions.TryGetValue(frame.Anchor.AnchorRevisionId, out var anchorRevision) &&
                anchorRevision.SourceAssetId != frame.Source.AssetId:
                errors.Add($"Recipe revision '{revision.Id}' frame anchor must belong to its source asset.");
                break;
            case CompositionRecipe composition:
                if (composition.Segments.Count == 0)
                    errors.Add($"Recipe revision '{revision.Id}' composition must contain at least one segment.");
                foreach (var segment in composition.Segments)
                {
                    ValidateBoundary(segment.Start, segment.Source, revision.Id, anchorRevisions, errors);
                    ValidateBoundary(segment.End, segment.Source, revision.Id, anchorRevisions, errors);
                    var segmentStart = ResolveBoundarySeconds(segment.Start, anchorRevisions);
                    var segmentEnd = ResolveBoundarySeconds(segment.End, anchorRevisions);
                    if (segmentStart is { } compositionStart &&
                        segmentEnd is { } compositionEnd &&
                        compositionEnd <= compositionStart)
                        errors.Add(
                            $"Recipe revision '{revision.Id}' composition segment '{segment.Id}' end must follow its start.");
                }
                foreach (var audioClip in composition.AudioClips)
                {
                    if (audioClip.TimelineStartTicks < 0)
                        errors.Add(
                            $"Recipe revision '{revision.Id}' audio clip '{audioClip.Id}' has a negative timeline start.");
                    if (!double.IsFinite(audioClip.GainDecibels) ||
                        audioClip.GainDecibels is < -60 or > 12)
                        errors.Add(
                            $"Recipe revision '{revision.Id}' audio clip '{audioClip.Id}' gain must be between -60 dB and +12 dB.");
                    if (!double.IsFinite(audioClip.Pan) || audioClip.Pan is < -1 or > 1)
                        errors.Add(
                            $"Recipe revision '{revision.Id}' audio clip '{audioClip.Id}' pan must be between -1 and +1.");
                    if (audioClip.FadeInMilliseconds < 0 || audioClip.FadeOutMilliseconds < 0)
                        errors.Add(
                            $"Recipe revision '{revision.Id}' audio clip '{audioClip.Id}' fades cannot be negative.");
                }
                break;
        }
    }

    private static void ValidateBoundary(
        RecipeBoundary boundary,
        AssetRevisionReference source,
        Guid revisionId,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        List<string> errors)
    {
        if (boundary.Kind == RecipeBoundaryKind.Anchor &&
            boundary.Anchor is { } anchor &&
            anchorRevisions.TryGetValue(anchor.AnchorRevisionId, out var anchorRevision) &&
            (anchorRevision.SourceAssetId != source.AssetId ||
             anchorRevision.SourceRecipeRevisionId != source.RecipeRevisionId))
            errors.Add($"Recipe revision '{revisionId}' trim anchor must belong to its source asset.");
        if (boundary.Kind == RecipeBoundaryKind.Anchor &&
            (boundary.Anchor is null || boundary.Edge is null))
            errors.Add($"Recipe revision '{revisionId}' anchor boundary requires a pinned revision and frame edge.");
        if (boundary.Kind != RecipeBoundaryKind.Anchor &&
            (boundary.Anchor is not null || boundary.Edge is not null))
            errors.Add($"Recipe revision '{revisionId}' has anchor data on a non-anchor boundary.");
        if (boundary.Kind == RecipeBoundaryKind.Timestamp &&
            (boundary.TimestampSeconds is null || boundary.TimestampSeconds < 0 ||
             double.IsNaN(boundary.TimestampSeconds.Value) ||
             double.IsInfinity(boundary.TimestampSeconds.Value)))
            errors.Add($"Recipe revision '{revisionId}' has an invalid timestamp boundary.");
    }

    private static double? ResolveBoundarySeconds(
        RecipeBoundary boundary,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions) => boundary.Kind switch
    {
        RecipeBoundaryKind.SourceStart => 0,
        RecipeBoundaryKind.Timestamp => boundary.TimestampSeconds,
        RecipeBoundaryKind.Anchor when boundary.Edge == AnchorBoundaryEdge.BeforeFrame &&
            boundary.Anchor is { } anchor &&
            anchorRevisions.TryGetValue(anchor.AnchorRevisionId, out var revision) => revision.TimestampSeconds,
        _ => null
    };

    private static void Visit(
        Guid id,
        Dictionary<Guid, RecipeRevision> revisions,
        Dictionary<Guid, int> states,
        List<string> errors)
    {
        if (states.TryGetValue(id, out var state))
        {
            if (state == 1) errors.Add($"Recipe dependency cycle includes revision '{id}'.");
            return;
        }

        states[id] = 1;
        if (revisions.TryGetValue(id, out var revision))
        {
            foreach (var dependency in GetSources(revision.Recipe)
                         .Select(source => source.RecipeRevisionId)
                         .OfType<Guid>())
                Visit(dependency, revisions, states, errors);
        }
        states[id] = 2;
    }

    private static void ValidateAnchorRevisionReference(
        AnchorRevisionReference reference,
        Dictionary<Guid, FrameAnchor> anchors,
        Dictionary<Guid, FrameAnchorRevision> revisions,
        string owner,
        List<string> errors)
    {
        if (!anchors.ContainsKey(reference.AnchorId))
            errors.Add($"{owner} references missing anchor '{reference.AnchorId}'.");
        if (!revisions.TryGetValue(reference.AnchorRevisionId, out var revision) ||
            revision.AnchorId != reference.AnchorId)
            errors.Add($"{owner} references missing anchor revision '{reference.AnchorRevisionId}'.");
    }

    private static IEnumerable<AssetRevisionReference> GetSources(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => [trim.Source],
        ExtractFrameRecipe frame => [frame.Source],
        CompositionRecipe composition => composition.Segments.Select(segment => segment.Source)
            .Concat(composition.AudioClips.Select(clip => clip.Source)),
        _ => throw new NotSupportedException($"Recipe type '{recipe.GetType().Name}' is not supported.")
    };

    private static IEnumerable<AnchorRevisionReference> GetAnchors(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => new[] { trim.Start.Anchor, trim.End.Anchor }.OfType<AnchorRevisionReference>(),
        ExtractFrameRecipe frame when
            frame.Anchor.AnchorId != Guid.Empty && frame.Anchor.AnchorRevisionId != Guid.Empty => [frame.Anchor],
        CompositionRecipe composition => composition.Segments
            .SelectMany(segment => new[] { segment.Start.Anchor, segment.End.Anchor })
            .OfType<AnchorRevisionReference>(),
        _ => []
    };
}
