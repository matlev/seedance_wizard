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
            if (asset.IsDeleted && asset.StorageKind != AssetStorageKind.Physical)
                errors.Add($"Deleted asset '{asset.Id}' must retain physical storage metadata.");

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
                }
            }
            else if (asset.Virtual is null || asset.Physical is not null)
            {
                errors.Add($"Virtual asset '{asset.Id}' must have only virtual storage metadata.");
            }
        }
    }
}
