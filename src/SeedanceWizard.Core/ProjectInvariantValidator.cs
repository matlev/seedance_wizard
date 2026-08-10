namespace SeedanceWizard.Core;

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

        if (project.SchemaVersion != VideoProject.CurrentSchemaVersion)
            errors.Add($"Project schema must be {VideoProject.CurrentSchemaVersion} in the current domain model.");

        AddDuplicateErrors(project.Assets.Select(asset => asset.Id), "asset", errors);
        AddDuplicateErrors(project.Anchors.Select(anchor => anchor.Id), "anchor", errors);
        AddDuplicateErrors(project.RecipeRevisions.Select(revision => revision.Id), "recipe revision", errors);
        AddDuplicateErrors(project.RecipeDrafts.Select(draft => draft.Id), "recipe draft", errors);
        AddDuplicateErrors(project.Generations.Select(generation => generation.Id), "generation", errors);

        var assets = project.Assets.GroupBy(asset => asset.Id).ToDictionary(group => group.Key, group => group.First());
        var anchors = project.Anchors.GroupBy(anchor => anchor.Id).ToDictionary(group => group.Key, group => group.First());
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

        if (project.MainVideoAssetId is { } mainId &&
            (!assets.TryGetValue(mainId, out var main) || main.MediaType != MediaType.Video || main.StorageKind != AssetStorageKind.Physical))
            errors.Add("The main video must reference a durable physical video asset.");

        foreach (var anchor in project.Anchors)
        {
            if (!assets.TryGetValue(anchor.AssetId, out var source) || source.MediaType != MediaType.Video)
                errors.Add($"Anchor '{anchor.Id}' must reference an existing video asset.");
            if (anchor.TimestampSeconds < 0 || double.IsNaN(anchor.TimestampSeconds) || double.IsInfinity(anchor.TimestampSeconds))
                errors.Add($"Anchor '{anchor.Id}' has an invalid timestamp.");
        }

        ValidateRecipeRevisions(project, assets, anchors, revisions, errors);
        ValidateGenerations(project, assets, anchors, revisions, generations, errors);
        ValidateTimeline(project, assets, revisions, errors);
        return errors;
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
        Dictionary<Guid, RecipeRevision> revisions,
        List<string> errors)
    {
        foreach (var revision in project.RecipeRevisions)
        {
            if (!assets.TryGetValue(revision.VirtualAssetId, out var asset) || asset.StorageKind != AssetStorageKind.Virtual)
                errors.Add($"Recipe revision '{revision.Id}' must belong to an existing virtual asset.");
            if (revision.RevisionNumber < 1) errors.Add($"Recipe revision '{revision.Id}' has an invalid number.");
            if (revision.Recipe.RecipeSchemaVersion != 1)
                errors.Add($"Recipe revision '{revision.Id}' uses unsupported recipe schema {revision.Recipe.RecipeSchemaVersion}.");
            if (revision.PreviousRevisionId is { } previousId)
            {
                if (!revisions.TryGetValue(previousId, out var previous) ||
                    previous.VirtualAssetId != revision.VirtualAssetId ||
                    previous.RevisionNumber >= revision.RevisionNumber)
                    errors.Add($"Recipe revision '{revision.Id}' has an invalid predecessor.");
            }

            foreach (var source in GetRecipeSources(revision.Recipe))
                ValidateAssetRevisionReference(source, assets, revisions, $"Recipe revision '{revision.Id}'", errors);

            foreach (var anchorId in GetRecipeAnchors(revision.Recipe))
                if (!anchors.ContainsKey(anchorId)) errors.Add($"Recipe revision '{revision.Id}' references missing anchor '{anchorId}'.");

            ValidateRecipeSemantics(revision, anchors, errors);
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
        Dictionary<Guid, FrameAnchor> anchors,
        List<string> errors)
    {
        switch (revision.Recipe)
        {
            case TrimRecipe trim:
                ValidateBoundary(trim.Start, trim.Source.AssetId, revision.Id, anchors, errors);
                ValidateBoundary(trim.End, trim.Source.AssetId, revision.Id, anchors, errors);
                var startSeconds = ResolveBoundarySeconds(trim.Start, anchors);
                var endSeconds = ResolveBoundarySeconds(trim.End, anchors);
                if (startSeconds is { } start && endSeconds is { } end && end <= start)
                    errors.Add($"Recipe revision '{revision.Id}' trim end must follow its start.");
                break;
            case ExtractFrameRecipe frame when anchors.TryGetValue(frame.AnchorId, out var anchor) && anchor.AssetId != frame.Source.AssetId:
                errors.Add($"Recipe revision '{revision.Id}' frame anchor must belong to its source asset.");
                break;
        }
    }

    private static void ValidateBoundary(
        RecipeBoundary boundary,
        Guid sourceAssetId,
        Guid revisionId,
        Dictionary<Guid, FrameAnchor> anchors,
        List<string> errors)
    {
        if (boundary.Kind == RecipeBoundaryKind.Anchor &&
            boundary.AnchorId is { } anchorId &&
            anchors.TryGetValue(anchorId, out var anchor) &&
            anchor.AssetId != sourceAssetId)
            errors.Add($"Recipe revision '{revisionId}' trim anchor must belong to its source asset.");
        if (boundary.Kind == RecipeBoundaryKind.Timestamp &&
            (boundary.TimestampSeconds is null || boundary.TimestampSeconds < 0 ||
             double.IsNaN(boundary.TimestampSeconds.Value) || double.IsInfinity(boundary.TimestampSeconds.Value)))
            errors.Add($"Recipe revision '{revisionId}' has an invalid timestamp boundary.");
    }

    private static double? ResolveBoundarySeconds(RecipeBoundary boundary, Dictionary<Guid, FrameAnchor> anchors) => boundary.Kind switch
    {
        RecipeBoundaryKind.SourceStart => 0,
        RecipeBoundaryKind.Timestamp => boundary.TimestampSeconds,
        RecipeBoundaryKind.Anchor when boundary.AnchorId is { } id && anchors.TryGetValue(id, out var anchor) => anchor.TimestampSeconds,
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

            foreach (var reference in generation.RequestSnapshot.References)
            {
                if (reference.ObjectKind == GenerationReferenceObjectKind.Asset)
                {
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
                else if (!anchors.ContainsKey(reference.LogicalObjectId))
                    errors.Add($"Generation '{generation.Id}' references missing anchor '{reference.LogicalObjectId}'.");
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

    private static IEnumerable<AssetRevisionReference> GetRecipeSources(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => [trim.Source],
        ExtractFrameRecipe frame => [frame.Source],
        _ => throw new NotSupportedException($"Recipe type '{recipe.GetType().Name}' is not supported.")
    };

    private static IEnumerable<Guid> GetRecipeAnchors(AssetRecipe recipe) => recipe switch
    {
        TrimRecipe trim => new[] { trim.Start.AnchorId, trim.End.AnchorId }.OfType<Guid>(),
        ExtractFrameRecipe frame when frame.AnchorId != Guid.Empty => [frame.AnchorId],
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
