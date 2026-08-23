using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class MaterializationReceiptDto
{
    public string? PlanHash { get; set; }
    public string? SourceContentHash { get; set; }
    public string? ProducedContentHash { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public string? ProviderReferenceId { get; set; }
    public string? ProviderScope { get; set; }
    public DateTimeOffset? ProviderReferenceExpiresAt { get; set; }
}

internal sealed class GenerationErrorDto
{
    public int? HttpStatus { get; set; }
    public string? ProviderCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TechnicalDetails { get; set; }
}

internal sealed class GenerationDraftDto
{
    public string? ProviderId { get; set; }
    public string? ModelVersion { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<GenerationReferenceDraftDto> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class GenerationReferenceDraftDto
{
    public Guid ReferenceId { get; set; }
    public GenerationReferenceObjectKind ObjectKind { get; set; }
    public Guid LogicalObjectId { get; set; }
    public Guid? AnchorRevisionId { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
}

internal sealed class GenerationRecordDto
{
    public Guid Id { get; set; }
    public GenerationRequestSnapshotDto RequestSnapshot { get; set; } = new();
    public DateTimeOffset RequestedAt { get; set; }
    public string? ProviderJobId { get; set; }
    public GenerationStatus Status { get; set; }
    public OutputIngestionStatus IngestionStatus { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<Guid> OutputAssetIds { get; set; } = [];
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, MaterializationReceiptDto> ReferenceMaterializations { get; set; } = [];
    public GenerationErrorDto? Error { get; set; }
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
}

internal sealed class GenerationRequestSnapshotDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<GenerationReferenceSnapshotDto> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GenerationReferenceSnapshotDto
{
    public Guid ReferenceId { get; set; }
    public GenerationReferenceObjectKind ObjectKind { get; set; }
    public Guid LogicalObjectId { get; set; }
    public Guid? RecipeRevisionId { get; set; }
    public FrameAnchorReferenceSnapshotDto? Anchor { get; set; }
    public string? ContentHash { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    public MaterializationReceiptDto? Materialization { get; set; }
}

internal sealed class FrameAnchorReferenceSnapshotDto
{
    public Guid AnchorRevisionId { get; set; }
    public Guid SourceAssetId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public string SourceContentHash { get; set; } = string.Empty;
    public int VideoStreamIndex { get; set; }
    public long PresentationTimestamp { get; set; }
    public int TimeBaseNumerator { get; set; }
    public int TimeBaseDenominator { get; set; }
    public long? FrameNumber { get; set; }
}
