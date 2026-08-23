namespace ReelForge.Core;

internal static class GenerationInvariantValidator
{
    public static void ValidateDraft(
        VideoProject project,
        ProjectValidationContext context,
        List<string> errors)
    {
        var draft = project.CurrentGenerationDraft;
        if (draft is null) return;

        if (draft.ParentGenerationId.HasValue != draft.RelationshipType.HasValue)
            errors.Add("The current generation draft must pair parent ID and relationship type.");
        if (draft.ParentGenerationId is { } parentId && !context.Generations.ContainsKey(parentId))
            errors.Add("The current generation draft references a missing parent generation.");

        ValidationHelpers.AddDuplicateErrors(
            draft.References.Select(reference => reference.ReferenceId),
            "generation draft reference",
            errors);
        foreach (var reference in draft.References)
        {
            if (reference.ReferenceId == Guid.Empty)
                errors.Add("The current generation draft contains an empty reference ID.");
            if (reference.ObjectKind == GenerationReferenceObjectKind.Asset &&
                !context.Assets.ContainsKey(reference.LogicalObjectId))
                errors.Add($"The current generation draft references missing asset '{reference.LogicalObjectId}'.");
            if (reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor)
            {
                if (!context.Anchors.TryGetValue(reference.LogicalObjectId, out var anchor))
                    errors.Add(
                        $"The current generation draft references missing anchor '{reference.LogicalObjectId}'.");
                else if (reference.AnchorRevisionId is { } revisionId &&
                         (!context.AnchorRevisions.TryGetValue(revisionId, out var revision) ||
                          revision.AnchorId != anchor.Id))
                    errors.Add(
                        $"The current generation draft references missing anchor revision '{revisionId}'.");
            }
        }
    }

    public static void ValidateHistory(
        VideoProject project,
        ProjectValidationContext context,
        List<string> errors)
    {
        foreach (var generation in project.Generations)
        {
            if (generation.ParentGenerationId.HasValue != generation.RelationshipType.HasValue)
                errors.Add($"Generation '{generation.Id}' must pair parent ID and relationship type.");
            if (generation.ParentGenerationId is { } parentId &&
                (!context.Generations.ContainsKey(parentId) || parentId == generation.Id))
                errors.Add($"Generation '{generation.Id}' has an invalid parent.");

            var duplicateReferenceIds = generation.RequestSnapshot.References
                .GroupBy(reference => reference.ReferenceId)
                .Where(group => group.Key == Guid.Empty || group.Count() > 1);
            foreach (var duplicate in duplicateReferenceIds)
                errors.Add(
                    $"Generation '{generation.Id}' has invalid or duplicate reference ID '{duplicate.Key}'.");

            foreach (var reference in generation.RequestSnapshot.References)
            {
                if (reference.ObjectKind == GenerationReferenceObjectKind.Asset)
                {
                    ValidateAssetReference(reference, generation.Id, context, errors);
                }
                else
                {
                    ValidateAnchorReference(reference, generation.Id, context, errors);
                }
            }

            foreach (var outputId in generation.OutputAssetIds)
            {
                if (!context.Assets.TryGetValue(outputId, out var output) ||
                    output.StorageKind != AssetStorageKind.Physical)
                    errors.Add($"Generation '{generation.Id}' output '{outputId}' must be a physical asset.");
                else if (output.Provenance?.GenerationId != generation.Id)
                    errors.Add($"Generation '{generation.Id}' output '{outputId}' lacks reverse provenance.");
            }
        }

        foreach (var output in project.Assets.Where(asset => asset.Provenance?.GenerationId is not null))
        {
            var generationId = output.Provenance!.GenerationId!.Value;
            if (!context.Generations.TryGetValue(generationId, out var generation) ||
                !generation.OutputAssetIds.Contains(output.Id))
                errors.Add(
                    $"Asset '{output.Id}' has generation provenance without a matching generation output link.");
        }

        var states = new Dictionary<Guid, int>();
        foreach (var generation in project.Generations)
            Visit(generation.Id, context.Generations, states, errors);
    }

    private static void ValidateAssetReference(
        GenerationReferenceSnapshot reference,
        Guid generationId,
        ProjectValidationContext context,
        List<string> errors)
    {
        if (reference.Anchor is not null)
            errors.Add(
                $"Generation '{generationId}' asset reference '{reference.ReferenceId}' cannot include anchor state.");
        if (!context.Assets.TryGetValue(reference.LogicalObjectId, out var asset))
        {
            errors.Add($"Generation '{generationId}' references missing asset '{reference.LogicalObjectId}'.");
        }
        else if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            if (reference.RecipeRevisionId is null ||
                !context.RecipeRevisions.TryGetValue(reference.RecipeRevisionId.Value, out var revision) ||
                revision.VirtualAssetId != asset.Id)
                errors.Add(
                    $"Generation '{generationId}' must pin an exact revision for virtual asset '{asset.Id}'.");
        }
        else if (reference.RecipeRevisionId is not null)
        {
            errors.Add(
                $"Generation '{generationId}' cannot assign a recipe revision to physical asset '{asset.Id}'.");
        }
    }

    private static void ValidateAnchorReference(
        GenerationReferenceSnapshot reference,
        Guid generationId,
        ProjectValidationContext context,
        List<string> errors)
    {
        if (!context.Anchors.TryGetValue(reference.LogicalObjectId, out _))
            errors.Add($"Generation '{generationId}' references missing anchor '{reference.LogicalObjectId}'.");
        if (reference.Anchor is null)
        {
            errors.Add(
                $"Generation '{generationId}' must pin exact state for anchor '{reference.LogicalObjectId}'.");
        }
        else if (!context.AnchorRevisions.TryGetValue(reference.Anchor.AnchorRevisionId, out var anchorRevision) ||
                 anchorRevision.AnchorId != reference.LogicalObjectId)
        {
            errors.Add(
                $"Generation '{generationId}' references missing anchor revision '{reference.Anchor.AnchorRevisionId}'.");
        }
        else
        {
            ValidateAnchorSnapshot(reference, anchorRevision, generationId, errors);
        }
    }

    private static void ValidateAnchorSnapshot(
        GenerationReferenceSnapshot reference,
        FrameAnchorRevision revision,
        Guid generationId,
        List<string> errors)
    {
        var snapshot = reference.Anchor!;
        if (snapshot.SourceAssetId != revision.SourceAssetId ||
            snapshot.SourceRecipeRevisionId != revision.SourceRecipeRevisionId ||
            !string.Equals(snapshot.SourceContentHash, revision.SourceContentHash, StringComparison.OrdinalIgnoreCase) ||
            snapshot.VideoStreamIndex != revision.VideoStreamIndex ||
            snapshot.PresentationTimestamp != revision.PresentationTimestamp ||
            snapshot.TimeBaseNumerator != revision.TimeBaseNumerator ||
            snapshot.TimeBaseDenominator != revision.TimeBaseDenominator ||
            snapshot.FrameNumber != revision.FrameNumber)
            errors.Add(
                $"Generation '{generationId}' anchor reference '{reference.ReferenceId}' does not match its pinned revision.");
        if (!string.Equals(reference.ContentHash, snapshot.SourceContentHash, StringComparison.OrdinalIgnoreCase))
            errors.Add(
                $"Generation '{generationId}' anchor reference '{reference.ReferenceId}' has inconsistent source identity.");
    }

    private static void Visit(
        Guid id,
        Dictionary<Guid, GenerationRecord> generations,
        Dictionary<Guid, int> states,
        List<string> errors)
    {
        if (states.TryGetValue(id, out var state))
        {
            if (state == 1) errors.Add($"Generation lineage cycle includes generation '{id}'.");
            return;
        }

        states[id] = 1;
        if (generations.TryGetValue(id, out var generation) &&
            generation.ParentGenerationId is { } parentId)
            Visit(parentId, generations, states, errors);
        states[id] = 2;
    }
}
