namespace ReelForge.Core;

internal static class AssetInvariantValidator
{
    public static void Validate(
        VideoProject project,
        ProjectValidationContext context,
        List<string> errors)
    {
        if (project.WorkingCompositionAssetId is { } workingCompositionId &&
            (!context.Assets.TryGetValue(workingCompositionId, out var workingComposition) ||
             workingComposition.StorageKind != AssetStorageKind.Virtual ||
             workingComposition.Virtual?.Kind != VirtualAssetKind.Composition))
            errors.Add("The Working Composition ID must reference a virtual composition asset in this project.");

        foreach (var asset in project.Assets)
        {
            foreach (var duplicate in asset.TimingAssessments.GroupBy(assessment => assessment.MediaType).Where(group => group.Count() > 1))
                errors.Add($"Physical asset '{asset.Id}' has duplicate current {duplicate.Key} timing assessments.");
            if (asset.StorageKind == AssetStorageKind.Physical)
            {
                if (asset.Physical is null || asset.Virtual is not null)
                    errors.Add($"Physical asset '{asset.Id}' must have only physical storage metadata.");
                else
                {
                    if (string.IsNullOrWhiteSpace(asset.Physical.RelativePath))
                        errors.Add($"Physical asset '{asset.Id}' requires a relative path.");
                    if (!asset.Physical.ContentIdentity.Algorithm.Equals(
                            ContentIdentity.Sha256Algorithm,
                            StringComparison.Ordinal))
                        errors.Add($"Physical asset '{asset.Id}' must use SHA-256 content identity.");
                    if (asset.Physical.ContentIdentity.Status == ContentHashStatus.Verified &&
                        !ValidationHelpers.IsSha256(asset.Physical.ContentIdentity.Sha256))
                        errors.Add($"Physical asset '{asset.Id}' has an invalid verified SHA-256 value.");
                    if (asset.IsDeleted && asset.Physical.Availability != PhysicalAssetAvailability.Missing)
                        errors.Add($"Deleted physical asset '{asset.Id}' must be marked missing.");
                    ValidateTimingAssessments(asset, errors);
                }
            }
            else if (asset.Virtual is null || asset.Physical is not null)
            {
                errors.Add($"Virtual asset '{asset.Id}' must have only virtual storage metadata.");
            }
            else if (asset.TimingAssessments.Count != 0)
            {
                errors.Add($"Virtual asset '{asset.Id}' cannot retain physical-stream timing assessments.");
            }
        }
    }

    private static void ValidateTimingAssessments(ProjectAsset asset, List<string> errors)
    {
        foreach (var assessment in asset.TimingAssessments)
        {
            if (asset.Physical?.ContentIdentity is not { Status: ContentHashStatus.Verified, Sha256: { } hash } ||
                !hash.Equals(assessment.SourceContentHash, StringComparison.OrdinalIgnoreCase))
                errors.Add($"Timing assessment '{assessment.AssessmentId}' must match physical asset '{asset.Id}' verified content identity.");
            if (asset.MediaType == MediaType.Audio && assessment.MediaType != MediaType.Audio ||
                asset.MediaType != MediaType.Video && asset.MediaType != MediaType.Audio)
                errors.Add($"Timing assessment '{assessment.AssessmentId}' is not valid for asset '{asset.Id}' media type.");

            var expectedIndex = assessment.MediaType == MediaType.Video
                ? asset.Encoding?.Video?.StreamIndex
                : asset.Encoding?.Audio?.StreamIndex;
            var hasDescriptor = assessment.MediaType == MediaType.Video
                ? asset.Encoding?.Video is not null
                : asset.Encoding?.Audio is not null;
            if (assessment.CanPlace && (!hasDescriptor || assessment.SelectedStreamIndex != expectedIndex))
                errors.Add($"Timing assessment '{assessment.AssessmentId}' must match asset '{asset.Id}' selected stream descriptor.");
        }
    }
}
