using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class ProjectAssetDto
{
    public Guid Id { get; set; }
    public bool IsDeleted { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public AssetStorageKind StorageKind { get; set; }
    public AssetOrigin Origin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public AssetProvenanceDto? Provenance { get; set; }
    public PhysicalAssetStorageDto? Physical { get; set; }
    public VirtualAssetStateDto? Virtual { get; set; }
    public Dictionary<string, ProviderAssetReferenceDto> ProviderReferences { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class PhysicalAssetStorageDto
{
    public string RelativePath { get; set; } = string.Empty;
    public PhysicalAssetDurability Durability { get; set; }
    public PhysicalAssetAvailability Availability { get; set; } = PhysicalAssetAvailability.Unknown;
    public ContentIdentityDto ContentIdentity { get; set; } = new();
}

internal sealed class ContentIdentityDto
{
    public string Algorithm { get; set; } = ContentIdentity.Sha256Algorithm;
    public string? Sha256 { get; set; }
    public ContentHashStatus Status { get; set; }
    public long? LengthBytes { get; set; }
    public DateTimeOffset? ObservedLastWriteTimeUtc { get; set; }
}

internal sealed class VirtualAssetStateDto
{
    public VirtualAssetKind Kind { get; set; }
    public Guid? CurrentRecipeRevisionId { get; set; }
    public MediaEncodingMetadata? ExpectedMediaProperties { get; set; }
}

internal sealed class ProviderAssetReferenceDto
{
    public string Value { get; set; } = string.Empty;
    public string? SourceContentHash { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public string? Scope { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

internal sealed class AssetProvenanceDto
{
    public string Operation { get; set; } = string.Empty;
    public List<Guid> SourceAssetIds { get; set; } = [];
    public Guid? GenerationId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class AssetRevisionReferenceDto
{
    public Guid AssetId { get; set; }
    public Guid? RecipeRevisionId { get; set; }
}
