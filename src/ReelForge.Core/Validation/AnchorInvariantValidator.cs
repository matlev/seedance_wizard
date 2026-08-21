namespace ReelForge.Core;

internal static class AnchorInvariantValidator
{
    public static void Validate(
        VideoProject project,
        ProjectValidationContext context,
        List<string> errors)
    {
        foreach (var anchor in project.Anchors)
        {
            if (anchor.CurrentRevisionId is null ||
                !context.AnchorRevisions.TryGetValue(anchor.CurrentRevisionId.Value, out var current) ||
                current.AnchorId != anchor.Id)
                errors.Add($"Anchor '{anchor.Id}' must reference its current committed revision.");
            else if (project.AnchorRevisions.Any(revision =>
                         revision.AnchorId == anchor.Id && revision.RevisionNumber > current.RevisionNumber))
                errors.Add($"Anchor '{anchor.Id}' current revision pointer does not reference its latest revision.");
        }

        foreach (var revision in project.AnchorRevisions)
        {
            if (!context.Anchors.ContainsKey(revision.AnchorId))
                errors.Add($"Anchor revision '{revision.Id}' belongs to missing anchor '{revision.AnchorId}'.");
            if (!context.Assets.TryGetValue(revision.SourceAssetId, out var source) ||
                source.MediaType != MediaType.Video)
            {
                errors.Add($"Anchor revision '{revision.Id}' must reference a video asset.");
            }
            else if (source.StorageKind == AssetStorageKind.Physical)
            {
                if (revision.SourceRecipeRevisionId is not null)
                    errors.Add($"Physical anchor revision '{revision.Id}' cannot pin a recipe revision.");
            }
            else if (revision.SourceRecipeRevisionId is not { } sourceRevisionId ||
                     !project.RecipeRevisions.Any(candidate =>
                         candidate.Id == sourceRevisionId && candidate.VirtualAssetId == source.Id))
            {
                errors.Add($"Virtual anchor revision '{revision.Id}' must pin a source recipe revision.");
            }
            if (revision.RevisionNumber < 1)
                errors.Add($"Anchor revision '{revision.Id}' has an invalid revision number.");
            if (!ValidationHelpers.IsSha256(revision.SourceContentHash))
                errors.Add($"Anchor revision '{revision.Id}' has an invalid source SHA-256 value.");
            if (revision.RevisionNumber == 1 && revision.PreviousRevisionId is not null)
                errors.Add($"Anchor revision '{revision.Id}' first revision cannot have a predecessor.");
            if (revision.RevisionNumber > 1 &&
                (revision.PreviousRevisionId is not { } previousId ||
                 !context.AnchorRevisions.TryGetValue(previousId, out var previous) ||
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
}
