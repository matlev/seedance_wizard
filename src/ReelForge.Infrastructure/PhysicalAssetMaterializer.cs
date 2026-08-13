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

        return request.Target switch
        {
            AssetMaterializationTarget assetTarget => await MaterializeAssetAsync(
                project, location, assetTarget, cancellationToken).ConfigureAwait(false),
            AnchorMaterializationTarget anchorTarget => await ResolveAnchorSourceAsync(
                project, location, anchorTarget, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Materialization target '{request.Target.GetType().Name}' is not supported.")
        };
    }

    private async Task<MaterializedMediaLease> MaterializeAssetAsync(
        VideoProject project,
        ProjectLocation location,
        AssetMaterializationTarget target,
        CancellationToken cancellationToken)
    {
        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == target.AssetId)
            ?? throw new InvalidOperationException($"Asset '{target.AssetId}' no longer exists.");
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
        {
            throw new NotSupportedException(
                $"Virtual asset '{asset.EffectiveDisplayName}' cannot be used until its committed recipe can be materialized.");
        }

        if (target.RecipeRevisionId is not null)
            throw new InvalidOperationException("Physical assets cannot be materialized with a recipe revision.");

        return await OpenVerifiedPhysicalAssetAsync(asset, location, expectedHash: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MaterializedMediaLease> ResolveAnchorSourceAsync(
        VideoProject project,
        ProjectLocation location,
        AnchorMaterializationTarget target,
        CancellationToken cancellationToken)
    {
        var anchor = project.Anchors.SingleOrDefault(candidate => candidate.Id == target.AnchorId)
            ?? throw new InvalidOperationException($"Frame anchor '{target.AnchorId}' no longer exists.");
        var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                candidate.Id == target.AnchorRevisionId && candidate.AnchorId == anchor.Id)
            ?? throw new InvalidOperationException($"Frame anchor revision '{target.AnchorRevisionId}' no longer exists.");
        var source = project.Assets.SingleOrDefault(candidate => candidate.Id == revision.SourceAssetId)
            ?? throw new InvalidOperationException($"Anchor source asset '{revision.SourceAssetId}' no longer exists.");

        await using var verifiedSource = await OpenVerifiedPhysicalAssetAsync(
                source, location, revision.SourceContentHash, cancellationToken)
            .ConfigureAwait(false);
        throw new NotSupportedException(
            "The anchor source was verified, but exact frame extraction is not available until Phase 2C.3.");
    }

    private async Task<MaterializedMediaLease> OpenVerifiedPhysicalAssetAsync(
        ProjectAsset asset,
        ProjectLocation location,
        string? expectedHash,
        CancellationToken cancellationToken)
    {
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException("Materialization requires a durable physical source asset.");

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

        if (expectedHash is not null &&
            !expectedHash.Equals(identity.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"'{asset.EffectiveDisplayName}' no longer matches the content identity pinned by the Saved Frame revision.");
        }

        asset.Physical.Availability = PhysicalAssetAvailability.Available;
        return new MaterializedMediaLease(path, identity, asset.Encoding, isDurableSource: true);
    }
}
