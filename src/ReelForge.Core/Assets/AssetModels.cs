namespace ReelForge.Core;

public enum MediaType { Image, Video, Audio }
public enum AssetOrigin { Imported, Generated, EditorDerived, ExtractedFrame, Exported, ExtractedAudio }
public enum AssetStorageKind { Physical, Virtual }
public enum VirtualAssetKind { Other, SavedClip, Composition, ExtractedFrame }
public enum PhysicalAssetDurability { Source, Generated, Exported, Promoted }
public enum ContentHashStatus { Pending, Verified, Mismatch, Failed }
public enum PhysicalAssetAvailability { Unknown, Available, Missing }

public sealed class ProjectAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public AssetStorageKind StorageKind { get; set; } = AssetStorageKind.Physical;
    public AssetOrigin Origin { get; set; } = AssetOrigin.Imported;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public AssetProvenance? Provenance { get; set; }
    public PhysicalAssetStorage? Physical { get; set; } = new();
    public VirtualAssetState? Virtual { get; set; }
    public Dictionary<string, ProviderAssetReference> ProviderReferences { get; set; } = new(StringComparer.Ordinal);

    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;
}

public sealed class PhysicalAssetStorage
{
    public string RelativePath { get; set; } = string.Empty;
    public PhysicalAssetDurability Durability { get; set; } = PhysicalAssetDurability.Source;
    public ContentIdentity ContentIdentity { get; set; } = new();
    public PhysicalAssetAvailability Availability { get; set; } = PhysicalAssetAvailability.Unknown;
}

public sealed class VirtualAssetState
{
    public VirtualAssetKind Kind { get; set; }
    public Guid? CurrentRecipeRevisionId { get; set; }
    public MediaEncodingMetadata? ExpectedMediaProperties { get; set; }
}

public sealed class ContentIdentity
{
    public const string Sha256Algorithm = "SHA-256";

    public string Algorithm { get; set; } = Sha256Algorithm;
    public string? Sha256 { get; set; }
    public ContentHashStatus Status { get; set; } = ContentHashStatus.Pending;
    public long? LengthBytes { get; set; }
    public DateTimeOffset? ObservedLastWriteTimeUtc { get; set; }
}

public sealed class ProviderAssetReference
{
    public string Value { get; set; } = string.Empty;
    public string? SourceContentHash { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public string? Scope { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class AssetProvenance
{
    public string Operation { get; set; } = string.Empty;
    public List<Guid> SourceAssetIds { get; set; } = [];
    public Guid? GenerationId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}
