using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Application;

internal static class GenerationRequestFactory
{
    public static GenerationRequestSnapshot CreateSnapshot(
        IVideoGenerationProvider provider,
        GenerationDraft draft,
        VideoProject project)
    {
        if (!string.IsNullOrWhiteSpace(draft.ProviderId) &&
            !draft.ProviderId.Equals(provider.Capabilities.ProviderId, StringComparison.Ordinal))
            throw new InvalidOperationException("The draft provider does not match the selected provider.");

        var references = draft.References
            .Select((reference, index) => CreateReferenceSnapshot(reference, index, project))
            .OrderBy(reference => reference.Order ?? int.MaxValue)
            .ToArray();
        return new GenerationRequestSnapshot
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Mode = draft.Mode,
            Prompt = draft.Prompt,
            DurationSeconds = draft.DurationSeconds,
            AspectRatio = draft.AspectRatio,
            Resolution = draft.Resolution,
            References = Array.AsReadOnly(references),
            ProviderParameters = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(draft.ProviderParameters, StringComparer.Ordinal))
        };
    }

    public static GenerationRequest CreateProviderRequest(
        GenerationRequestSnapshot snapshot,
        IReadOnlyCollection<ProjectAsset> assets)
    {
        foreach (var reference in snapshot.References)
        {
            EnsureUsableReferenceSource(reference, assets);
        }

        return new GenerationRequest
        {
            Prompt = snapshot.Prompt,
            Mode = snapshot.Mode,
            DurationSeconds = snapshot.DurationSeconds,
            AspectRatio = snapshot.AspectRatio,
            Resolution = snapshot.Resolution,
            ReferenceAssetIds = snapshot.References
                .Where(reference => reference.ObjectKind == GenerationReferenceObjectKind.Asset)
                .Select(reference => reference.LogicalObjectId)
                .ToList(),
            PreparedReferences = snapshot.References.Select(reference => new PreparedGenerationReference(
                reference.ReferenceId,
                reference.ObjectKind,
                reference.LogicalObjectId,
                reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor
                    ? MediaType.Image
                    : EnsureUsableReferenceAsset(reference.LogicalObjectId, assets).MediaType,
                reference.Role,
                reference.Order ?? 0,
                string.Empty)).ToList(),
            ProviderParameters = new Dictionary<string, string>(snapshot.ProviderParameters, StringComparer.Ordinal)
        };
    }

    public static GenerationDraft CreateDerivedDraft(
        GenerationRecord source,
        GenerationRelationshipType relationshipType) => new()
    {
        ProviderId = source.RequestSnapshot.ProviderId,
        ModelVersion = source.RequestSnapshot.ModelVersion,
        Prompt = source.RequestSnapshot.Prompt,
        Mode = source.RequestSnapshot.Mode,
        DurationSeconds = source.RequestSnapshot.DurationSeconds,
        AspectRatio = source.RequestSnapshot.AspectRatio,
        Resolution = source.RequestSnapshot.Resolution,
        References = source.RequestSnapshot.References.Select(reference => new GenerationReferenceDraft
        {
            ReferenceId = Guid.NewGuid(),
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            AnchorRevisionId = reference.Anchor?.AnchorRevisionId,
            Role = reference.Role,
            Order = reference.Order,
            Label = reference.Label,
            Notes = reference.Notes
        }).ToList(),
        ProviderParameters = new Dictionary<string, string>(source.RequestSnapshot.ProviderParameters, StringComparer.Ordinal),
        ParentGenerationId = source.Id,
        RelationshipType = relationshipType,
        ModifiedAt = DateTimeOffset.UtcNow
    };

    private static GenerationReferenceSnapshot CreateReferenceSnapshot(
        GenerationReferenceDraft reference,
        int index,
        VideoProject project)
    {
        if (reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor)
        {
            var anchor = project.Anchors.SingleOrDefault(candidate => candidate.Id == reference.LogicalObjectId);
            if (anchor is null)
                throw new InvalidOperationException($"Frame anchor '{reference.LogicalObjectId}' no longer exists.");
            var revisionId = reference.AnchorRevisionId ?? anchor.CurrentRevisionId
                ?? throw new InvalidOperationException($"Frame anchor '{reference.LogicalObjectId}' has no committed revision.");
            var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                    candidate.Id == revisionId && candidate.AnchorId == anchor.Id)
                ?? throw new InvalidOperationException($"Frame anchor revision '{revisionId}' no longer exists.");
            var source = project.Assets.SingleOrDefault(candidate => candidate.Id == revision.SourceAssetId)
                ?? throw new InvalidOperationException($"Anchor source asset '{revision.SourceAssetId}' no longer exists.");
            EnsureNotDeleted(source);
            return new GenerationReferenceSnapshot
            {
                ReferenceId = reference.ReferenceId,
                ObjectKind = reference.ObjectKind,
                LogicalObjectId = reference.LogicalObjectId,
                ContentHash = revision.SourceContentHash,
                Anchor = new FrameAnchorReferenceSnapshot
                {
                    AnchorRevisionId = revision.Id,
                    SourceAssetId = revision.SourceAssetId,
                    SourceRecipeRevisionId = revision.SourceRecipeRevisionId,
                    SourceContentHash = revision.SourceContentHash,
                    VideoStreamIndex = revision.VideoStreamIndex,
                    PresentationTimestamp = revision.PresentationTimestamp,
                    TimeBaseNumerator = revision.TimeBaseNumerator,
                    TimeBaseDenominator = revision.TimeBaseDenominator,
                    FrameNumber = revision.FrameNumber
                },
                Role = reference.Role,
                Order = reference.Order ?? index,
                Label = reference.Label,
                Notes = reference.Notes
            };
        }

        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == reference.LogicalObjectId)
            ?? throw new InvalidOperationException($"Reference asset '{reference.LogicalObjectId}' no longer exists.");
        EnsureNotDeleted(asset);
        if (asset.StorageKind == AssetStorageKind.Physical &&
            (asset.Physical?.ContentIdentity.Status != ContentHashStatus.Verified ||
             string.IsNullOrWhiteSpace(asset.Physical.ContentIdentity.Sha256)))
        {
            throw new InvalidOperationException(
                $"'{asset.EffectiveDisplayName}' must have a verified SHA-256 identity before submission.");
        }

        return new GenerationReferenceSnapshot
        {
            ReferenceId = reference.ReferenceId,
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            RecipeRevisionId = asset.Virtual?.CurrentRecipeRevisionId,
            ContentHash = asset.Physical?.ContentIdentity.Sha256,
            Role = reference.Role,
            Order = reference.Order ?? index,
            Label = reference.Label,
            Notes = reference.Notes
        };
    }

    private static ProjectAsset EnsureUsableReferenceAsset(Guid assetId, IReadOnlyCollection<ProjectAsset> assets)
    {
        var asset = assets.SingleOrDefault(candidate => candidate.Id == assetId)
            ?? throw new InvalidOperationException($"Reference asset '{assetId}' no longer exists.");
        EnsureNotDeleted(asset);
        return asset;
    }

    private static void EnsureUsableReferenceSource(
        GenerationReferenceSnapshot reference,
        IReadOnlyCollection<ProjectAsset> assets)
    {
        if (reference.ObjectKind == GenerationReferenceObjectKind.Asset)
        {
            EnsureUsableReferenceAsset(reference.LogicalObjectId, assets);
            return;
        }

        if (reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor && reference.Anchor is { } anchor)
            EnsureUsableReferenceAsset(anchor.SourceAssetId, assets);
    }

    private static void EnsureNotDeleted(ProjectAsset asset)
    {
        if (asset.IsDeleted)
            throw new InvalidOperationException(
                $"'{asset.EffectiveDisplayName}' was deleted from the project and cannot be submitted as a generation reference.");
    }
}
