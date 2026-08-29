namespace ReelForge.Core;

public enum MediaType { Image, Video, Audio }
public enum AssetOrigin { Imported, Generated, EditorDerived, ExtractedFrame, Exported, ExtractedAudio }
public enum AssetStorageKind { Physical, Virtual }
public enum VirtualAssetKind { Other, SavedClip, Composition, ExtractedFrame }
public enum PhysicalAssetDurability { Source, Generated, Exported, Promoted }
public enum ContentHashStatus { Pending, Verified, Mismatch, Failed }
public enum PhysicalAssetAvailability { Unknown, Available, Missing, Inaccessible, Mismatched }

public sealed class ProjectAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Retains the logical asset record after project media was deliberately removed from active
    /// use so durable recipes, provenance, and generation history remain resolvable. Physical
    /// tombstones also retain their former storage identity; virtual tombstones retain recipes.
    /// Deleted records are not offered as ordinary Project Media or new generation references.
    /// </summary>
    public bool IsDeleted { get; set; }
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
    /// <summary>Current physical-stream timing evidence, independently retained for video and audio.</summary>
    public List<StreamTimingAssessment> TimingAssessments { get; set; } = [];
    public AssetProvenance? Provenance { get; set; }
    public PhysicalAssetStorage? Physical { get; set; } = new();
    public VirtualAssetState? Virtual { get; set; }
    public Dictionary<string, ProviderAssetReference> ProviderReferences { get; set; } = new(StringComparer.Ordinal);

    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? FileName : DisplayName;

    public void SetTimingAssessment(StreamTimingAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (StorageKind != AssetStorageKind.Physical || Physical is null)
            throw new InvalidOperationException("Only physical assets can retain current stream timing assessments.");
        if (Physical.ContentIdentity is not { Status: ContentHashStatus.Verified, Sha256: { } hash } ||
            !ValidationHelpers.IsSha256(hash) ||
            !hash.Equals(assessment.SourceContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A timing assessment must match this physical asset's verified SHA-256 content identity.");
        if (MediaType == MediaType.Audio && assessment.MediaType != MediaType.Audio ||
            MediaType != MediaType.Video && MediaType != MediaType.Audio)
            throw new InvalidOperationException("This asset cannot retain timing evidence for the assessed media type.");

        var expectedStreamIndex = assessment.MediaType == MediaType.Video ? Encoding?.Video?.StreamIndex : Encoding?.Audio?.StreamIndex;
        var hasDescriptor = assessment.MediaType == MediaType.Video ? Encoding?.Video is not null : Encoding?.Audio is not null;
        if (assessment.CanPlace && (!hasDescriptor || assessment.SelectedStreamIndex != expectedStreamIndex))
            throw new InvalidOperationException("The timing assessment must match the asset's selected stream descriptor.");

        TimingAssessments.RemoveAll(existing => existing.MediaType == assessment.MediaType);
        TimingAssessments.Add(assessment);
    }
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
