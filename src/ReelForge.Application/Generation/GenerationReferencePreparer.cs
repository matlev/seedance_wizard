using ReelForge.Core;

namespace ReelForge.Application;

internal sealed class GenerationReferencePreparer
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly IProviderAssetPreparationService? _providerPreparation;

    public GenerationReferencePreparer(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IProviderAssetPreparationService? providerPreparation)
    {
        _workspace = workspace;
        _materializer = materializer;
        _providerPreparation = providerPreparation;
    }

    public async Task PrepareAsync(
        IVideoGenerationProvider provider,
        GenerationRequest request,
        GenerationRequestSnapshot snapshot,
        GenerationSubmissionAuthorization? authorization,
        GenerationRecord record,
        IProgress<GenerationWorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (snapshot.References.Count == 0 || provider.CostBehavior == GenerationProviderCostBehavior.NoCharge)
            return;
        if (_providerPreparation is null || authorization is null)
            throw new InvalidOperationException("This provider requires a configured reference preparation service.");
        var project = _workspace.Project
            ?? throw new InvalidOperationException("Create or open a project first.");
        var location = _workspace.Location
            ?? throw new InvalidOperationException("Create or open a project first.");

        request.PreparedReferences.Clear();
        foreach (var reference in snapshot.References.OrderBy(reference => reference.Order ?? int.MaxValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = reference.ObjectKind == GenerationReferenceObjectKind.Asset
                ? project.Assets.Single(candidate => candidate.Id == reference.LogicalObjectId)
                : null;
            if (asset is not null &&
                TryGetReusableProviderReference(provider.Capabilities.ProviderId, asset, reference, out var reusableReference))
            {
                request.PreparedReferences.Add(CreatePreparedReference(reference, asset.MediaType, reusableReference));
                record.ReferenceMaterializations[reference.ReferenceId] = new MaterializationReceipt
                {
                    PlanHash = reference.RecipeRevisionId?.ToString("N"),
                    SourceContentHash = reference.ContentHash,
                    ProviderReferenceId = reusableReference,
                    ProviderScope = "reused-provider-reference"
                };
                record.ResponseMetadata[$"reference.{reference.ReferenceId:N}.preparation"] =
                    "reused-provider-reference";
                continue;
            }

            progress?.Report(new GenerationWorkflowProgress(
                record.Status,
                record.IngestionStatus,
                "Verifying and preparing a logical reference…"));
            await using var media = await _materializer.MaterializeAsync(
                    project,
                    location,
                    new MaterializationRequest(
                        reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor
                            ? new AnchorMaterializationTarget(
                                reference.LogicalObjectId,
                                reference.Anchor?.AnchorRevisionId
                                ?? throw new InvalidOperationException(
                                    "A Saved Frame snapshot must pin an exact revision."))
                            : new AssetMaterializationTarget(reference.LogicalObjectId, reference.RecipeRevisionId),
                        MaterializationPurpose.ProviderUpload,
                        MaterializationRetentionPreference.Ephemeral),
                    cancellationToken)
                .ConfigureAwait(false);
            var prepared = await _providerPreparation
                .PrepareAsync(provider.Capabilities.ProviderId, reference, media, authorization, cancellationToken)
                .ConfigureAwait(false);
            record.ReferenceMaterializations[reference.ReferenceId] = new MaterializationReceipt
            {
                PlanHash = reference.Anchor?.AnchorRevisionId.ToString("N") ??
                           reference.RecipeRevisionId?.ToString("N"),
                SourceContentHash = reference.ContentHash,
                ProducedContentHash = media.ContentIdentity.Sha256,
                Encoding = media.Encoding,
                ProviderReferenceId = prepared.Receipt?.ProviderReferenceId,
                ProviderScope = prepared.Receipt?.ProviderScope,
                ProviderReferenceExpiresAt = prepared.Receipt?.ProviderReferenceExpiresAt
            };
            request.PreparedReferences.Add(CreatePreparedReference(
                reference,
                reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor ? MediaType.Image : asset!.MediaType,
                prepared.ProviderRepresentation));
            record.ResponseMetadata[$"reference.{reference.ReferenceId:N}.preparation"] =
                prepared.Receipt?.ProviderScope ?? "prepared";
        }

        await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static PreparedGenerationReference CreatePreparedReference(
        GenerationReferenceSnapshot reference,
        MediaType mediaType,
        string representation) => new(
        reference.ReferenceId,
        reference.ObjectKind,
        reference.LogicalObjectId,
        mediaType,
        reference.Role,
        reference.Order ?? int.MaxValue,
        representation);

    private static bool TryGetReusableProviderReference(
        string providerId,
        ProjectAsset asset,
        GenerationReferenceSnapshot logicalReference,
        out string providerRepresentation)
    {
        providerRepresentation = string.Empty;
        if (!asset.ProviderReferences.TryGetValue(providerId, out var reference) ||
            string.IsNullOrWhiteSpace(reference.Value) ||
            reference.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow ||
            reference.SourceContentHash is { } sourceHash &&
            !sourceHash.Equals(logicalReference.ContentHash, StringComparison.OrdinalIgnoreCase) ||
            reference.SourceRecipeRevisionId is { } revisionId &&
            revisionId != logicalReference.RecipeRevisionId)
        {
            return false;
        }

        providerRepresentation = reference.Value;
        return true;
    }
}
