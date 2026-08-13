namespace ReelForge.Core;

public sealed class ProjectValidationException : Exception
{
    public ProjectValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors)) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

public static class ProjectInvariantValidator
{
    public static IReadOnlyList<string> Validate(VideoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var errors = new List<string>();

        AddDuplicateErrors(project.Assets.Select(asset => asset.Id), "asset", errors);
        AddDuplicateErrors(project.Anchors.Select(anchor => anchor.Id), "anchor", errors);
        AddDuplicateErrors(project.AnchorRevisions.Select(revision => revision.Id), "anchor revision", errors);
        AddDuplicateErrors(project.RecipeRevisions.Select(revision => revision.Id), "recipe revision", errors);
        AddDuplicateErrors(project.RecipeDrafts.Select(draft => draft.Id), "recipe draft", errors);
        AddDuplicateErrors(project.Generations.Select(generation => generation.Id), "generation", errors);

        var assets = project.Assets.GroupBy(asset => asset.Id).ToDictionary(group => group.Key, group => group.First());
        var anchors = project.Anchors.GroupBy(anchor => anchor.Id).ToDictionary(group => group.Key, group => group.First());
        var anchorRevisions = project.AnchorRevisions.GroupBy(revision => revision.Id).ToDictionary(group => group.Key, group => group.First());
        var revisions = project.RecipeRevisions.GroupBy(revision => revision.Id).ToDictionary(group => group.Key, group => group.First());
        var generations = project.Generations.GroupBy(generation => generation.Id).ToDictionary(group => group.Key, group => group.First());

        foreach (var asset in project.Assets)
        {
            if (asset.StorageKind == AssetStorageKind.Physical)
            {
                if (asset.Physical is null || asset.Virtual is not null)
                    errors.Add($"Physical asset '{asset.Id}' must have only physical storage metadata.");
                else
                {
                    if (string.IsNullOrWhiteSpace(asset.Physical.RelativePath))
                        errors.Add($"Physical asset '{asset.Id}' requires a relative path.");
                    if (!asset.Physical.ContentIdentity.Algorithm.Equals(ContentIdentity.Sha256Algorithm, StringComparison.Ordinal))
                        errors.Add($"Physical asset '{asset.Id}' must use SHA-256 content identity.");
                    if (asset.Physical.ContentIdentity.Status == ContentHashStatus.Verified &&
                        !IsSha256(asset.Physical.ContentIdentity.Sha256))
                        errors.Add($"Physical asset '{asset.Id}' has an invalid verified SHA-256 value.");
                }
            }
            else if (asset.Virtual is null || asset.Physical is not null)
            {
                errors.Add($"Virtual asset '{asset.Id}' must have only virtual storage metadata.");
            }
        }

        foreach (var anchor in project.Anchors)
        {
            if (anchor.CurrentRevisionId is null ||
                !anchorRevisions.TryGetValue(anchor.CurrentRevisionId.Value, out var current) ||
                current.AnchorId != anchor.Id)
                errors.Add($"Anchor '{anchor.Id}' must reference its current committed revision.");
            else if (project.AnchorRevisions.Any(revision =>
                         revision.AnchorId == anchor.Id && revision.RevisionNumber > current.RevisionNumber))
                errors.Add($"Anchor '{anchor.Id}' current revision pointer does not reference its latest revision.");
        }

        ValidateAnchorRevisions(project, assets, anchors, anchorRevisions, errors);

        ValidateRecipeRevisions(project, assets, anchors, anchorRevisions, revisions, errors);
        ValidateGenerationDraft(project, assets, anchors, anchorRevisions, generations, errors);
        ValidateGenerations(project, assets, anchors, anchorRevisions, revisions, generations, errors);
        ValidateTimeline(project, assets, revisions, errors);
        return errors;
    }

    private static void ValidateGenerationDraft(
        VideoProject project,
        Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, FrameAnchor> anchors,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        Dictionary<Guid, GenerationRecord> generations,
        List<string> errors)
    {
        var draft = project.CurrentGenerationDraft;
        if (draft is null) return;

        if (draft.ParentGenerationId.HasValue != draft.RelationshipType.HasValue)
            errors.Add("The current generation draft must pair parent ID and relationship type.");
        if (draft.ParentGenerationId is { } parentId && !generations.ContainsKey(parentId))
            errors.Add("The current generation draft references a missing parent generation.");

        AddDuplicateErrors(draft.References.Select(reference => reference.ReferenceId), "generation draft reference", errors);
        foreach (var reference in draft.References)
        {
            if (reference.ReferenceId == Guid.Empty)
                errors.Add("The current generation draft contains an empty reference ID.");
            if (reference.ObjectKind == GenerationReferenceObjectKind.Asset && !assets.ContainsKey(reference.LogicalObjectId))
                errors.Add($"The current generation draft references missing asset '{reference.LogicalObjectId}'.");
            if (reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor)
            {
                if (!anchors.TryGetValue(reference.LogicalObjectId, out var anchor))
                    errors.Add($"The current generation draft references missing anchor '{reference.LogicalObjectId}'.");
                else if (reference.AnchorRevisionId is { } revisionId &&
                         (!anchorRevisions.TryGetValue(revisionId, out var revision) || revision.AnchorId != anchor.Id))
                    errors.Add($"The current generation draft references missing anchor revision '{revisionId}'.");
            }
        }
    }

    private static void ValidateAnchorRevisions(
        VideoProject project,
        Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, FrameAnchor> anchors,
        Dictionary<Guid, FrameAnchorRevision> revisions,
        List<string> errors)
    {
        foreach (var revision in project.AnchorRevisions)
        {
            if (!anchors.ContainsKey(revision.AnchorId))
                errors.Add($"Anchor revision '{revision.Id}' belongs to missing anchor '{revision.AnchorId}'.");
            if (!assets.TryGetValue(revision.SourceAssetId, out var source) ||
                source.StorageKind != AssetStorageKind.Physical || source.MediaType != MediaType.Video)
                errors.Add($"Anchor revision '{revision.Id}' must reference a durable physical video asset.");
            if (revision.RevisionNumber < 1)
                errors.Add($"Anchor revision '{revision.Id}' has an invalid revision number.");
            if (!IsSha256(revision.SourceContentHash))
                errors.Add($"Anchor revision '{revision.Id}' has an invalid source SHA-256 value.");
            if (revision.RevisionNumber == 1 && revision.PreviousRevisionId is not null)
                errors.Add($"Anchor revision '{revision.Id}' first revision cannot have a predecessor.");
            if (revision.RevisionNumber > 1 &&
                (revision.PreviousRevisionId is not { } previousId ||
                 !revisions.TryGetValue(previousId, out var previous) ||
                 previous.AnchorId != revision.AnchorId ||
                 previous.RevisionNumber != revision.RevisionNumber - 1))
                errors.Add($"Anchor revision '{revision.Id}' has an invalid predecessor.");

            if (revision.VideoStreamIndex < 0 ||
                revision.TimeBaseNumerator <= 0 || revision.TimeBaseDenominator <= 0)
                errors.Add($"Anchor revision '{revision.Id}' has invalid presentation timing.");
        }

        foreach (var duplicate in project.AnchorRevisions
                     .GroupBy(revision => (revision.AnchorId, revision.RevisionNumber))
                     .Where(group => group.Count() > 1))
            errors.Add($"Anchor '{duplicate.Key.AnchorId}' has duplicate revision number {duplicate.Key.RevisionNumber}.");
    }

    public static void ThrowIfInvalid(VideoProject project)
    {
        var errors = Validate(project);
        if (errors.Count > 0) throw new ProjectValidationException(errors);
    }

    private static void ValidateRecipeRevisions(
        VideoProject project,
        Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, FrameAnchor> anchors,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        Dictionary<Guid, RecipeRevision> revisions,
        List<string> errors)
    {
        foreach (var revision in project.RecipeRevisions)
        {
            if (!assets.TryGetValue(revision.VirtualAssetId, out var asset) || asset.StorageKind != AssetStorageKind.Virtual)
                errors.Add($"Recipe revision '{revision.Id}' must belong to an existing virtual asset.");
            if (revision.RevisionNumber < 1) errors.Add($"Recipe revision '{revision.Id}' has an invalid number.");
            if (revision.PreviousRevisionId is { } previousId)
            {
                if (!revisions.TryGetValue(previousId, out var previous) ||
                    previous.VirtualAssetId != revision.VirtualAssetId ||
                    previous.RevisionNumber >= revision.RevisionNumber)
                    errors.Add($"Recipe revision '{revision.Id}' has an invalid predecessor.");
            }

            foreach (var source in GetRecipeSources(revision.Recipe))
                ValidateAssetRevisionReference(source, assets, revisions, $"Recipe revision '{revision.Id}'", errors);

            foreach (var anchorReference in GetRecipeAnchors(revision.Recipe))
                ValidateAnchorRevisionReference(anchorReference, anchors, anchorRevisions, $"Recipe revision '{revision.Id}'", errors);

            ValidateRecipeSemantics(revision, anchorRevisions, errors);
        }

        foreach (var duplicate in project.RecipeRevisions
                     .GroupBy(revision => (revision.VirtualAssetId, revision.RevisionNumber))
                     .Where(group => group.Count() > 1))
            errors.Add($"Virtual asset '{duplicate.Key.VirtualAssetId}' has duplicate recipe revision number {duplicate.Key.RevisionNumber}.");

        foreach (var draft in project.RecipeDrafts)
        {
            if (draft.VirtualAssetId is { } assetId &&
                (!assets.TryGetValue(assetId, out var asset) || asset.StorageKind != AssetStorageKind.Virtual))
                errors.Add($"Recipe draft '{draft.Id}' references an invalid virtual asset.");
            if (draft.BasedOnRevisionId is { } revisionId && !revisions.ContainsKey(revisionId))
                errors.Add($"Recipe draft '{draft.Id}' references missing base revision '{revisionId}'.");
        }

        foreach (var asset in project.Assets.Where(asset => asset.StorageKind == AssetStorageKind.Virtual))
        {
            var currentId = asset.Virtual?.CurrentRecipeRevisionId;
            if (currentId is null || !revisions.TryGetValue(currentId.Value, out var current) || current.VirtualAssetId != asset.Id)
                errors.Add($"Virtual asset '{asset.Id}' must reference its current committed recipe revision.");
        }

        var states = new Dictionary<Guid, int>();
        foreach (var revision in project.RecipeRevisions)
            VisitRecipe(revision.Id, revisions, states, errors);
    }

    private static void ValidateRecipeSemantics(
        RecipeRevision revision,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        List<string> errors)
    {
        switch (revision.Recipe)
        {
            case TrimRecipe trim:
                ValidateBoundary(trim.Start, trim.Source.AssetId, revision.Id, anchorRevisions, errors);
                ValidateBoundary(trim.End, trim.Source.AssetId, revision.Id, anchorRevisions, errors);
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
        }
    }

    private static void ValidateBoundary(
        RecipeBoundary boundary,
        Guid sourceAssetId,
        Guid revisionId,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        List<string> errors)
    {
        if (boundary.Kind == RecipeBoundaryKind.Anchor &&
            boundary.Anchor is { } anchor &&
            anchorRevisions.TryGetValue(anchor.AnchorRevisionId, out var anchorRevision) &&
            anchorRevision.SourceAssetId != sourceAssetId)
            errors.Add($"Recipe revision '{revisionId}' trim anchor must belong to its source asset.");
        if (boundary.Kind == RecipeBoundaryKind.Anchor &&
            (boundary.Anchor is null || boundary.Edge is null))
            errors.Add($"Recipe revision '{revisionId}' anchor boundary requires a pinned revision and frame edge.");
        if (boundary.Kind != RecipeBoundaryKind.Anchor && (boundary.Anchor is not null || boundary.Edge is not null))
            errors.Add($"Recipe revision '{revisionId}' has anchor data on a non-anchor boundary.");
        if (boundary.Kind == RecipeBoundaryKind.Timestamp &&
            (boundary.TimestampSeconds is null || boundary.TimestampSeconds < 0 ||
             double.IsNaN(boundary.TimestampSeconds.Value) || double.IsInfinity(boundary.TimestampSeconds.Value)))
            errors.Add($"Recipe revision '{revisionId}' has an invalid timestamp boundary.");
    }

    private static double? ResolveBoundarySeconds(
        RecipeBoundary boundary,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions) => boundary.Kind switch
    {
        RecipeBoundaryKind.SourceStart => 0,
        RecipeBoundaryKind.Timestamp => boundary.TimestampSeconds,
        RecipeBoundaryKind.Anchor when boundary.Edge == AnchorBoundaryEdge.BeforeFrame && boundary.Anchor is { } anchor &&
            anchorRevisions.TryGetValue(anchor.AnchorRevisionId, out var revision) => revision.TimestampSeconds,
        _ => null
    };

    private static void VisitRecipe(Guid id, Dictionary<Guid, RecipeRevision> revisions, Dictionary<Guid, int> states, List<string> errors)
    {
        if (states.TryGetValue(id, out var state))
        {
            if (state == 1) errors.Add($"Recipe dependency cycle includes revision '{id}'.");
            return;
        }

        states[id] = 1;
        if (revisions.TryGetValue(id, out var revision))
            foreach (var dependency in GetRecipeSources(revision.Recipe).Select(source => source.RecipeRevisionId).OfType<Guid>())
                VisitRecipe(dependency, revisions, states, errors);
        states[id] = 2;
    }

    private static void ValidateGenerations(
        VideoProject project,
        Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, FrameAnchor> anchors,
        Dictionary<Guid, FrameAnchorRevision> anchorRevisions,
        Dictionary<Guid, RecipeRevision> revisions,
        Dictionary<Guid, GenerationRecord> generations,
        List<string> errors)
    {
        foreach (var generation in project.Generations)
        {
            if (generation.ParentGenerationId.HasValue != generation.RelationshipType.HasValue)
                errors.Add($"Generation '{generation.Id}' must pair parent ID and relationship type.");
            if (generation.ParentGenerationId is { } parentId && (!generations.ContainsKey(parentId) || parentId == generation.Id))
                errors.Add($"Generation '{generation.Id}' has an invalid parent.");

            var duplicateReferenceIds = generation.RequestSnapshot.References
                .GroupBy(reference => reference.ReferenceId)
                .Where(group => group.Key == Guid.Empty || group.Count() > 1);
            foreach (var duplicate in duplicateReferenceIds)
                errors.Add($"Generation '{generation.Id}' has invalid or duplicate reference ID '{duplicate.Key}'.");

            foreach (var reference in generation.RequestSnapshot.References)
            {
                if (reference.ObjectKind == GenerationReferenceObjectKind.Asset)
                {
                    if (reference.Anchor is not null)
                        errors.Add($"Generation '{generation.Id}' asset reference '{reference.ReferenceId}' cannot include anchor state.");
                    if (!assets.TryGetValue(reference.LogicalObjectId, out var asset))
                        errors.Add($"Generation '{generation.Id}' references missing asset '{reference.LogicalObjectId}'.");
                    else if (asset.StorageKind == AssetStorageKind.Virtual)
                    {
                        if (reference.RecipeRevisionId is null ||
                            !revisions.TryGetValue(reference.RecipeRevisionId.Value, out var revision) ||
                            revision.VirtualAssetId != asset.Id)
                            errors.Add($"Generation '{generation.Id}' must pin an exact revision for virtual asset '{asset.Id}'.");
                    }
                    else if (reference.RecipeRevisionId is not null)
                        errors.Add($"Generation '{generation.Id}' cannot assign a recipe revision to physical asset '{asset.Id}'.");
                }
                else
                {
                    if (!anchors.TryGetValue(reference.LogicalObjectId, out var anchor))
                        errors.Add($"Generation '{generation.Id}' references missing anchor '{reference.LogicalObjectId}'.");
                    if (reference.Anchor is null)
                    {
                        errors.Add($"Generation '{generation.Id}' must pin exact state for anchor '{reference.LogicalObjectId}'.");
                    }
                    else if (!anchorRevisions.TryGetValue(reference.Anchor.AnchorRevisionId, out var anchorRevision) ||
                             anchorRevision.AnchorId != reference.LogicalObjectId)
                    {
                        errors.Add($"Generation '{generation.Id}' references missing anchor revision '{reference.Anchor.AnchorRevisionId}'.");
                    }
                    else
                    {
                        ValidateAnchorSnapshot(reference, anchorRevision, generation.Id, errors);
                    }
                }
            }

            foreach (var outputId in generation.OutputAssetIds)
            {
                if (!assets.TryGetValue(outputId, out var output) || output.StorageKind != AssetStorageKind.Physical)
                    errors.Add($"Generation '{generation.Id}' output '{outputId}' must be a physical asset.");
                else if (output.Provenance?.GenerationId != generation.Id)
                    errors.Add($"Generation '{generation.Id}' output '{outputId}' lacks reverse provenance.");
            }
        }

        foreach (var output in project.Assets.Where(asset => asset.Provenance?.GenerationId is not null))
        {
            var generationId = output.Provenance!.GenerationId!.Value;
            if (!generations.TryGetValue(generationId, out var generation) || !generation.OutputAssetIds.Contains(output.Id))
                errors.Add($"Asset '{output.Id}' has generation provenance without a matching generation output link.");
        }

        var states = new Dictionary<Guid, int>();
        foreach (var generation in project.Generations)
            VisitGeneration(generation.Id, generations, states, errors);
    }

    private static void VisitGeneration(Guid id, Dictionary<Guid, GenerationRecord> generations, Dictionary<Guid, int> states, List<string> errors)
    {
        if (states.TryGetValue(id, out var state))
        {
            if (state == 1) errors.Add($"Generation lineage cycle includes generation '{id}'.");
            return;
        }

        states[id] = 1;
        if (generations.TryGetValue(id, out var generation) && generation.ParentGenerationId is { } parentId)
            VisitGeneration(parentId, generations, states, errors);
        states[id] = 2;
    }

    private static void ValidateTimeline(
        VideoProject project,
        Dictionary<Guid, ProjectAsset> assets,
        Dictionary<Guid, RecipeRevision> revisions,
        List<string> errors)
    {
        foreach (var clip in project.Timeline.Clips)
        {
            if (!assets.TryGetValue(clip.SourceAssetId, out var asset))
            {
                errors.Add($"Timeline clip '{clip.Id}' references missing asset '{clip.SourceAssetId}'.");
                continue;
            }

            if (asset.StorageKind == AssetStorageKind.Virtual)
            {
                if (clip.SourceRecipeRevisionId is null ||
                    !revisions.TryGetValue(clip.SourceRecipeRevisionId.Value, out var revision) ||
                    revision.VirtualAssetId != asset.Id)
                    errors.Add($"Timeline clip '{clip.Id}' must pin an exact virtual recipe revision.");
            }
            else if (clip.SourceRecipeRevisionId is not null)
                errors.Add($"Timeline clip '{clip.Id}' cannot pin a recipe revision for a physical asset.");
        }
    }

    private static void ValidateAssetRevisionReference(
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

    private static void ValidateAnchorRevisionReference(
        AnchorRevisionReference reference,
        Dictionary<Guid, FrameAnchor> anchors,
        Dictionary<Guid, FrameAnchorRevision> revisions,
        string owner,
        List<string> errors)
    {
        if (!anchors.ContainsKey(reference.AnchorId))
            errors.Add($"{owner} references missing anchor '{reference.AnchorId}'.");
        if (!revisions.TryGetValue(reference.AnchorRevisionId, out var revision) || revision.AnchorId != reference.AnchorId)
            errors.Add($"{owner} references missing anchor revision '{reference.AnchorRevisionId}'.");
    }

    private static void ValidateAnchorSnapshot(
        GenerationReferenceSnapshot reference,
        FrameAnchorRevision revision,
        Guid generationId,
        List<string> errors)
    {
        var snapshot = reference.Anchor!;
        if (snapshot.SourceAssetId != revision.SourceAssetId ||
            !string.Equals(snapshot.SourceContentHash, revision.SourceContentHash, StringComparison.OrdinalIgnoreCase) ||
            snapshot.VideoStreamIndex != revision.VideoStreamIndex ||
            snapshot.PresentationTimestamp != revision.PresentationTimestamp ||
            snapshot.TimeBaseNumerator != revision.TimeBaseNumerator ||
            snapshot.TimeBaseDenominator != revision.TimeBaseDenominator ||
            snapshot.FrameNumber != revision.FrameNumber)
            errors.Add($"Generation '{generationId}' anchor reference '{reference.ReferenceId}' does not match its pinned revision.");
        if (!string.Equals(reference.ContentHash, snapshot.SourceContentHash, StringComparison.OrdinalIgnoreCase))
            errors.Add($"Generation '{generationId}' anchor reference '{reference.ReferenceId}' has inconsistent source identity.");
    }

    private static IEnumerable<AssetRevisionReference> GetRecipeSources(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => [trim.Source],
        ExtractFrameRecipe frame => [frame.Source],
        _ => throw new NotSupportedException($"Recipe type '{recipe.GetType().Name}' is not supported.")
    };

    private static IEnumerable<AnchorRevisionReference> GetRecipeAnchors(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => new[] { trim.Start.Anchor, trim.End.Anchor }.OfType<AnchorRevisionReference>(),
        ExtractFrameRecipe frame when frame.Anchor.AnchorId != Guid.Empty && frame.Anchor.AnchorRevisionId != Guid.Empty => [frame.Anchor],
        _ => []
    };

    private static void AddDuplicateErrors(IEnumerable<Guid> ids, string label, List<string> errors)
    {
        foreach (var duplicate in ids.GroupBy(id => id).Where(group => group.Count() > 1))
            errors.Add($"Duplicate {label} ID '{duplicate.Key}'.");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));
}
