using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class FrameAnchorMaterializer
{
    private readonly PhysicalAssetMaterializer _physicalMaterializer;
    private readonly IExactVideoFrameService _exactFrameService;

    public FrameAnchorMaterializer(
        PhysicalAssetMaterializer physicalMaterializer,
        IExactVideoFrameService exactFrameService)
    {
        _physicalMaterializer = physicalMaterializer;
        _exactFrameService = exactFrameService;
    }

    public async Task<MaterializedMediaLease> MaterializeAsync(
        VideoProject project,
        ProjectLocation location,
        AnchorMaterializationTarget target,
        MaterializationPurpose purpose,
        string? profile,
        Func<
            VideoProject,
            ProjectLocation,
            MaterializationRequest,
            CancellationToken,
            Task<MaterializedMediaLease>> materializeAssetAsync,
        CancellationToken cancellationToken)
    {
        var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                candidate.Id == target.AnchorRevisionId && candidate.AnchorId == target.AnchorId)
            ?? throw new InvalidOperationException($"Frame anchor revision '{target.AnchorRevisionId}' no longer exists.");
        var sourceAsset = project.Assets.SingleOrDefault(candidate => candidate.Id == revision.SourceAssetId)
            ?? throw new InvalidOperationException($"Anchor source asset '{revision.SourceAssetId}' no longer exists.");
        if (sourceAsset.StorageKind == AssetStorageKind.Physical)
            return await _physicalMaterializer.MaterializeAsync(
                    project,
                    location,
                    new MaterializationRequest(target, purpose, Profile: profile),
                    cancellationToken)
                .ConfigureAwait(false);
        if (revision.SourceRecipeRevisionId is not { } sourceRevisionId)
            throw new InvalidDataException("An exact position in virtual media is missing its pinned source revision.");

        await using var source = await materializeAssetAsync(
                project,
                location,
                new MaterializationRequest(
                    new AssetMaterializationTarget(sourceAsset.Id, sourceRevisionId),
                    MaterializationPurpose.FrameExtraction),
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                source.ContentIdentity.Sha256,
                revision.SourceContentHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The materialized virtual source no longer matches the content identity pinned by the exact position.");
        return await _exactFrameService.ExtractAsync(
                source.Path,
                revision.SourceContentHash,
                revision,
                purpose,
                profile,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
