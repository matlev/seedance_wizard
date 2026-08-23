using System.Collections.ObjectModel;

namespace ReelForge.Core;

public sealed class GenerationRequestSnapshot
{
    public string ProviderId { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public GenerationMode Mode { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public int DurationSeconds { get; init; }
    public string AspectRatio { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public IReadOnlyList<GenerationReferenceSnapshot> References { get; init; } =
        Array.Empty<GenerationReferenceSnapshot>();
    public IReadOnlyDictionary<string, string> ProviderParameters { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
}

public sealed class GenerationReferenceSnapshot
{
    public Guid ReferenceId { get; init; } = Guid.NewGuid();
    public GenerationReferenceObjectKind ObjectKind { get; init; }
    public Guid LogicalObjectId { get; init; }
    public Guid? RecipeRevisionId { get; init; }
    public FrameAnchorReferenceSnapshot? Anchor { get; init; }
    public string? ContentHash { get; init; }
    public GenerationReferenceRole? Role { get; init; }
    public int? Order { get; init; }
    public string? Label { get; init; }
    public string? Notes { get; init; }
    public MaterializationReceipt? Materialization { get; init; }
}

public sealed record FrameAnchorReferenceSnapshot
{
    public Guid AnchorRevisionId { get; init; }
    public Guid SourceAssetId { get; init; }
    public Guid? SourceRecipeRevisionId { get; init; }
    public string SourceContentHash { get; init; } = string.Empty;
    public int VideoStreamIndex { get; init; }
    public long PresentationTimestamp { get; init; }
    public int TimeBaseNumerator { get; init; }
    public int TimeBaseDenominator { get; init; }
    public long? FrameNumber { get; init; }
}

public sealed class MaterializationReceipt
{
    public string? PlanHash { get; init; }
    public string? SourceContentHash { get; init; }
    public string? ProducedContentHash { get; init; }
    public MediaEncodingMetadata? Encoding { get; init; }
    public string? ProviderReferenceId { get; init; }
    public string? ProviderScope { get; init; }
    public DateTimeOffset? ProviderReferenceExpiresAt { get; init; }
}

public sealed class GenerationRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public GenerationRequestSnapshot RequestSnapshot { get; init; } = new();
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ProviderJobId { get; set; }
    public GenerationStatus Status { get; set; } = GenerationStatus.Draft;
    public OutputIngestionStatus IngestionStatus { get; set; } = OutputIngestionStatus.NotRequired;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<Guid> OutputAssetIds { get; set; } = [];
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, MaterializationReceipt> ReferenceMaterializations { get; set; } = [];
    public GenerationError? Error { get; set; }
    public Guid? ParentGenerationId { get; init; }
    public GenerationRelationshipType? RelationshipType { get; init; }
}

public sealed class GenerationError
{
    public int? HttpStatus { get; set; }
    public string? ProviderCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TechnicalDetails { get; set; }
}
