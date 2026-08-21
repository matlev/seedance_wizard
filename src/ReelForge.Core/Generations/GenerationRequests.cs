namespace ReelForge.Core;

// Mutable provider-input object. It is never persisted as generation history directly.
public sealed class GenerationRequest
{
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; } = GenerationMode.ReferenceToVideo;
    public int DurationSeconds { get; set; } = 15;
    public string AspectRatio { get; set; } = "16:9";
    public string Resolution { get; set; } = "720p";
    public List<Guid> ReferenceAssetIds { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    // Transient provider-ready representations. These values are never persisted as logical history.
    public List<PreparedGenerationReference> PreparedReferences { get; set; } = [];
}

public sealed record PreparedGenerationReference(
    Guid ReferenceId,
    GenerationReferenceObjectKind LogicalObjectKind,
    Guid LogicalObjectId,
    MediaType MediaType,
    GenerationReferenceRole? Role,
    int Order,
    string ProviderRepresentation);

public sealed record GenerationRequestReference(
    Guid ReferenceId,
    GenerationReferenceObjectKind LogicalObjectKind,
    Guid LogicalObjectId,
    MediaType MediaType,
    GenerationReferenceRole? Role,
    int Order,
    string DisplayName,
    string? PreparedRepresentation,
    ProjectAsset? Asset);

public static class GenerationRequestReferenceResolver
{
    public static IReadOnlyList<GenerationRequestReference> Resolve(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assets);
        if (request.PreparedReferences.Count > 0)
        {
            return request.PreparedReferences.Select(reference =>
            {
                var asset = reference.LogicalObjectKind == GenerationReferenceObjectKind.Asset
                    ? assets.FirstOrDefault(candidate => candidate.Id == reference.LogicalObjectId)
                    : null;
                return new GenerationRequestReference(
                    reference.ReferenceId,
                    reference.LogicalObjectKind,
                    reference.LogicalObjectId,
                    reference.MediaType,
                    reference.Role,
                    reference.Order,
                    asset?.EffectiveDisplayName ?? "Saved Frame",
                    string.IsNullOrWhiteSpace(reference.ProviderRepresentation)
                        ? null
                        : reference.ProviderRepresentation,
                    asset);
            }).ToArray();
        }

        return request.ReferenceAssetIds.Select((id, index) =>
        {
            var asset = assets.FirstOrDefault(candidate => candidate.Id == id);
            return new GenerationRequestReference(
                Guid.Empty,
                GenerationReferenceObjectKind.Asset,
                id,
                asset?.MediaType ?? MediaType.Image,
                null,
                index,
                asset?.EffectiveDisplayName ?? $"Missing asset {index + 1}",
                null,
                asset);
        }).ToArray();
    }
}

public sealed class GenerationDraft
{
    public string? ProviderId { get; set; }
    public string? ModelVersion { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; } = GenerationMode.ReferenceToVideo;
    public int DurationSeconds { get; set; } = 15;
    public string AspectRatio { get; set; } = "16:9";
    public string Resolution { get; set; } = "720p";
    public List<GenerationReferenceDraft> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GenerationReferenceDraft
{
    public Guid ReferenceId { get; set; } = Guid.NewGuid();
    public GenerationReferenceObjectKind ObjectKind { get; set; } = GenerationReferenceObjectKind.Asset;
    public Guid LogicalObjectId { get; set; }
    public Guid? AnchorRevisionId { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
}
