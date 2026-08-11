using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class PhysicalAssetMaterializer : IMediaMaterializer
{
    private readonly IContentHashService _contentHashService;

    public PhysicalAssetMaterializer(IContentHashService? contentHashService = null)
    {
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
    }

    public async Task<MaterializedMediaLease> MaterializeAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);

        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == request.AssetId)
            ?? throw new InvalidOperationException($"Asset '{request.AssetId}' no longer exists.");
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
        {
            throw new NotSupportedException(
                $"Virtual asset '{asset.EffectiveDisplayName}' cannot be used until its committed recipe can be materialized.");
        }

        if (request.RecipeRevisionId is not null)
            throw new InvalidOperationException("Physical assets cannot be materialized with a recipe revision.");

        var root = Path.GetFullPath(location.RootDirectory + Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(location.RootDirectory, asset.Physical.RelativePath));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The physical asset path escapes the project directory.");
        if (!File.Exists(path))
        {
            asset.Physical.Availability = PhysicalAssetAvailability.Missing;
            throw new FileNotFoundException($"The source media for '{asset.EffectiveDisplayName}' is missing.", path);
        }

        ContentIdentity identity;
        if (asset.Physical.ContentIdentity.Status == ContentHashStatus.Verified &&
            !string.IsNullOrWhiteSpace(asset.Physical.ContentIdentity.Sha256))
        {
            var verification = await _contentHashService
                .VerifyAsync(path, asset.Physical.ContentIdentity, cancellationToken)
                .ConfigureAwait(false);
            if (!verification.MatchesExpected)
            {
                asset.Physical.ContentIdentity.Status = ContentHashStatus.Mismatch;
                throw new InvalidDataException(
                    $"'{asset.EffectiveDisplayName}' has changed since ingestion. Re-import or explicitly replace it before generation.");
            }

            identity = verification.Observed;
        }
        else
        {
            identity = await _contentHashService.ComputeAsync(path, cancellationToken).ConfigureAwait(false);
            asset.Physical.ContentIdentity = identity;
        }

        asset.Physical.Availability = PhysicalAssetAvailability.Available;
        return new MaterializedMediaLease(path, identity, asset.Encoding, isDurableSource: true);
    }
}
