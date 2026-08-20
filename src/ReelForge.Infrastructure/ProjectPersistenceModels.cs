using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class ProjectFileDto
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public List<ProjectAssetDto> Assets { get; set; } = [];
    public List<RecipeRevisionDto> RecipeRevisions { get; set; } = [];
    public List<RecipeDraftDto> RecipeDrafts { get; set; } = [];
    public List<FrameAnchorDto> Anchors { get; set; } = [];
    public List<FrameAnchorRevisionDto> AnchorRevisions { get; set; } = [];
    public Guid? WorkingCompositionAssetId { get; set; }
    public GenerationDraftDto? CurrentGenerationDraft { get; set; }
    public List<GenerationRecordDto> Generations { get; set; } = [];
}

internal sealed class ProjectAssetDto
{
    public Guid Id { get; set; }
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

internal sealed class RecipeRevisionDto
{
    public Guid Id { get; set; }
    public Guid VirtualAssetId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AssetRecipeDto Recipe { get; set; } = new();
}

internal sealed class RecipeDraftDto
{
    public Guid Id { get; set; }
    public Guid? VirtualAssetId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public AssetRecipeDto EditableRecipe { get; set; } = new();
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class AssetRecipeDto
{
    public string Type { get; set; } = "extractFrame";
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public RecipeBoundaryDto? Start { get; set; }
    public RecipeBoundaryDto? End { get; set; }
    public AnchorRevisionReferenceDto? Anchor { get; set; }
    public List<CompositionSegmentDto> Segments { get; set; } = [];
    public List<CompositionAudioClipDto> AudioClips { get; set; } = [];
    public string? Profile { get; set; }
}

internal sealed class CompositionSegmentDto
{
    public Guid Id { get; set; }
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public RecipeBoundaryDto Start { get; set; } = new();
    public RecipeBoundaryDto End { get; set; } = new();
    public bool AudioEnabled { get; set; }
}

internal sealed class CompositionAudioClipDto
{
    public Guid Id { get; set; }
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public long TimelineStartTicks { get; set; }
    public bool IsMuted { get; set; }
    public double GainDecibels { get; set; }
    public double Pan { get; set; }
    public long FadeInMilliseconds { get; set; }
    public long FadeOutMilliseconds { get; set; }
}

internal sealed class RecipeBoundaryDto
{
    public RecipeBoundaryKind Kind { get; set; }
    public AnchorRevisionReferenceDto? Anchor { get; set; }
    public AnchorBoundaryEdge? Edge { get; set; }
    public double? TimestampSeconds { get; set; }
}

internal sealed class AnchorRevisionReferenceDto
{
    public Guid AnchorId { get; set; }
    public Guid AnchorRevisionId { get; set; }
}

internal sealed class FrameAnchorDto
{
    public Guid Id { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public string? DisplayLabel { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class FrameAnchorRevisionDto
{
    public Guid Id { get; set; }
    public Guid AnchorId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public Guid SourceAssetId { get; set; }
    public string SourceContentHash { get; set; } = string.Empty;
    public int VideoStreamIndex { get; set; }
    public long PresentationTimestamp { get; set; }
    public int TimeBaseNumerator { get; set; }
    public int TimeBaseDenominator { get; set; }
    public long? FrameNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
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
    public string SourceContentHash { get; set; } = string.Empty;
    public int VideoStreamIndex { get; set; }
    public long PresentationTimestamp { get; set; }
    public int TimeBaseNumerator { get; set; }
    public int TimeBaseDenominator { get; set; }
    public long? FrameNumber { get; set; }
}

internal static class ProjectPersistenceMapper
{
    public static ProjectFileDto ToDto(VideoProject source) => new()
    {
        FormatVersion = ProjectFileDto.CurrentFormatVersion,
        Id = source.Id,
        Name = source.Name,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        Assets = source.Assets.Select(ToDto).ToList(),
        RecipeRevisions = source.RecipeRevisions.Select(ToDto).ToList(),
        RecipeDrafts = source.RecipeDrafts.Select(ToDto).ToList(),
        Anchors = source.Anchors.Select(ToDto).ToList(),
        AnchorRevisions = source.AnchorRevisions.Select(ToDto).ToList(),
        WorkingCompositionAssetId = source.WorkingCompositionAssetId,
        CurrentGenerationDraft = ToDto(source.CurrentGenerationDraft),
        Generations = source.Generations.Select(ToDto).ToList()
    };

    public static VideoProject FromDto(ProjectFileDto source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        Assets = source.Assets.Select(FromDto).ToList(),
        RecipeRevisions = source.RecipeRevisions.Select(FromDto).ToList(),
        RecipeDrafts = source.RecipeDrafts.Select(FromDto).ToList(),
        Anchors = source.Anchors.Select(FromDto).ToList(),
        AnchorRevisions = source.AnchorRevisions.Select(FromDto).ToList(),
        WorkingCompositionAssetId = source.WorkingCompositionAssetId,
        CurrentGenerationDraft = FromDto(source.CurrentGenerationDraft),
        Generations = source.Generations.Select(FromDto).ToList()
    };

    private static ProjectAssetDto ToDto(ProjectAsset source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        FileName = source.FileName,
        MediaType = source.MediaType,
        StorageKind = source.StorageKind,
        Origin = source.Origin,
        CreatedAt = source.CreatedAt,
        DurationSeconds = source.DurationSeconds,
        Width = source.Width,
        Height = source.Height,
        Encoding = source.Encoding,
        Provenance = ToDto(source.Provenance),
        Physical = ToDto(source.Physical),
        Virtual = source.Virtual is null ? null : new VirtualAssetStateDto
        {
            Kind = source.Virtual.Kind,
            CurrentRecipeRevisionId = source.Virtual.CurrentRecipeRevisionId,
            ExpectedMediaProperties = source.Virtual.ExpectedMediaProperties
        },
        ProviderReferences = source.ProviderReferences.ToDictionary(pair => pair.Key, pair => ToDto(pair.Value), StringComparer.Ordinal)
    };

    private static ProjectAsset FromDto(ProjectAssetDto source) => new()
    {
        Id = source.Id,
        DisplayName = source.DisplayName,
        FileName = source.FileName,
        MediaType = source.MediaType,
        StorageKind = source.StorageKind,
        Origin = source.Origin,
        CreatedAt = source.CreatedAt,
        DurationSeconds = source.DurationSeconds,
        Width = source.Width,
        Height = source.Height,
        Encoding = source.Encoding,
        Provenance = FromDto(source.Provenance),
        Physical = FromDto(source.Physical),
        Virtual = source.Virtual is null ? null : new VirtualAssetState
        {
            Kind = source.Virtual.Kind,
            CurrentRecipeRevisionId = source.Virtual.CurrentRecipeRevisionId,
            ExpectedMediaProperties = source.Virtual.ExpectedMediaProperties
        },
        ProviderReferences = source.ProviderReferences.ToDictionary(pair => pair.Key, pair => FromDto(pair.Value), StringComparer.Ordinal)
    };

    private static PhysicalAssetStorageDto? ToDto(PhysicalAssetStorage? source) => source is null ? null : new()
    {
        RelativePath = source.RelativePath,
        Durability = source.Durability,
        ContentIdentity = new ContentIdentityDto
        {
            Algorithm = source.ContentIdentity.Algorithm,
            Sha256 = source.ContentIdentity.Sha256,
            Status = source.ContentIdentity.Status,
            LengthBytes = source.ContentIdentity.LengthBytes,
            ObservedLastWriteTimeUtc = source.ContentIdentity.ObservedLastWriteTimeUtc
        }
    };

    private static PhysicalAssetStorage? FromDto(PhysicalAssetStorageDto? source) => source is null ? null : new()
    {
        RelativePath = source.RelativePath,
        Durability = source.Durability,
        ContentIdentity = new ContentIdentity
        {
            Algorithm = source.ContentIdentity.Algorithm,
            Sha256 = source.ContentIdentity.Sha256,
            Status = source.ContentIdentity.Status,
            LengthBytes = source.ContentIdentity.LengthBytes,
            ObservedLastWriteTimeUtc = source.ContentIdentity.ObservedLastWriteTimeUtc
        }
    };

    private static ProviderAssetReferenceDto ToDto(ProviderAssetReference source) => new()
    {
        Value = source.Value,
        SourceContentHash = source.SourceContentHash,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Scope = source.Scope,
        ExpiresAt = source.ExpiresAt
    };

    private static ProviderAssetReference FromDto(ProviderAssetReferenceDto source) => new()
    {
        Value = source.Value,
        SourceContentHash = source.SourceContentHash,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Scope = source.Scope,
        ExpiresAt = source.ExpiresAt
    };

    private static AssetProvenanceDto? ToDto(AssetProvenance? source) => source is null ? null : new()
    {
        Operation = source.Operation,
        SourceAssetIds = [.. source.SourceAssetIds],
        GenerationId = source.GenerationId,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Parameters = Copy(source.Parameters)
    };

    private static AssetProvenance? FromDto(AssetProvenanceDto? source) => source is null ? null : new()
    {
        Operation = source.Operation,
        SourceAssetIds = [.. source.SourceAssetIds],
        GenerationId = source.GenerationId,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Parameters = Copy(source.Parameters)
    };

    private static RecipeRevisionDto ToDto(RecipeRevision source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        CreatedAt = source.CreatedAt,
        Recipe = ToDto(source.Recipe)
    };

    private static RecipeRevision FromDto(RecipeRevisionDto source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        CreatedAt = source.CreatedAt,
        Recipe = FromDto(source.Recipe)
    };

    private static RecipeDraftDto ToDto(RecipeDraft source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        BasedOnRevisionId = source.BasedOnRevisionId,
        EditableRecipe = ToDto(source.EditableRecipe),
        ModifiedAt = source.ModifiedAt
    };

    private static RecipeDraft FromDto(RecipeDraftDto source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        BasedOnRevisionId = source.BasedOnRevisionId,
        EditableRecipe = FromDto(source.EditableRecipe),
        ModifiedAt = source.ModifiedAt
    };

    private static AssetRecipeDto ToDto(AssetRecipe source) => source switch
    {
        TrimRecipe trim => new AssetRecipeDto
        {
            Type = "trim",
            Source = ToDto(trim.Source),
            Start = ToDto(trim.Start),
            End = ToDto(trim.End),
            Profile = trim.RenderProfile
        },
        ExtractFrameRecipe frame => new AssetRecipeDto
        {
            Type = "extractFrame",
            Source = ToDto(frame.Source),
            Anchor = ToDto(frame.Anchor),
            Profile = frame.ImageProfile
        },
        CompositionRecipe composition => new AssetRecipeDto
        {
            Type = "composition",
            Segments = composition.Segments.Select(segment => new CompositionSegmentDto
            {
                Id = segment.Id,
                Source = ToDto(segment.Source),
                Start = ToDto(segment.Start),
                End = ToDto(segment.End),
                AudioEnabled = segment.AudioEnabled
            }).ToList(),
            AudioClips = composition.AudioClips.Select(clip => new CompositionAudioClipDto
            {
                Id = clip.Id,
                Source = ToDto(clip.Source),
                TimelineStartTicks = clip.TimelineStartTicks,
                IsMuted = clip.IsMuted,
                GainDecibels = clip.GainDecibels,
                Pan = clip.Pan,
                FadeInMilliseconds = clip.FadeInMilliseconds,
                FadeOutMilliseconds = clip.FadeOutMilliseconds
            }).ToList()
        },
        _ => throw new NotSupportedException($"Recipe type '{source.GetType().Name}' is not supported.")
    };

    private static AssetRecipe FromDto(AssetRecipeDto source) => source.Type switch
    {
        "trim" => new TrimRecipe
        {
            Source = FromDto(source.Source),
            Start = FromDto(source.Start) ?? RecipeBoundary.SourceStart,
            End = FromDto(source.End) ?? RecipeBoundary.SourceEnd,
            RenderProfile = source.Profile
        },
        "extractFrame" => new ExtractFrameRecipe
        {
            Source = FromDto(source.Source),
            Anchor = FromDto(source.Anchor) ?? new AnchorRevisionReference(),
            ImageProfile = source.Profile
        },
        "composition" => new CompositionRecipe
        {
            Segments = source.Segments.Select(segment => new CompositionSegment
            {
                Id = segment.Id,
                Source = FromDto(segment.Source),
                Start = FromDto(segment.Start) ?? RecipeBoundary.SourceStart,
                End = FromDto(segment.End) ?? RecipeBoundary.SourceEnd,
                AudioEnabled = segment.AudioEnabled
            }).ToList(),
            AudioClips = source.AudioClips.Select(clip => new CompositionAudioClip
            {
                Id = clip.Id,
                Source = FromDto(clip.Source),
                TimelineStartTicks = clip.TimelineStartTicks,
                IsMuted = clip.IsMuted,
                GainDecibels = clip.GainDecibels,
                Pan = clip.Pan,
                FadeInMilliseconds = clip.FadeInMilliseconds,
                FadeOutMilliseconds = clip.FadeOutMilliseconds
            }).ToList()
        },
        _ => throw new InvalidDataException($"Recipe type '{source.Type}' is not supported.")
    };

    private static AssetRevisionReferenceDto ToDto(AssetRevisionReference source) => new()
    {
        AssetId = source.AssetId,
        RecipeRevisionId = source.RecipeRevisionId
    };

    private static AssetRevisionReference FromDto(AssetRevisionReferenceDto source) => new()
    {
        AssetId = source.AssetId,
        RecipeRevisionId = source.RecipeRevisionId
    };

    private static RecipeBoundaryDto ToDto(RecipeBoundary source) => new()
    {
        Kind = source.Kind,
        Anchor = ToDto(source.Anchor),
        Edge = source.Edge,
        TimestampSeconds = source.TimestampSeconds
    };

    private static RecipeBoundary? FromDto(RecipeBoundaryDto? source) => source is null ? null : new()
    {
        Kind = source.Kind,
        Anchor = FromDto(source.Anchor),
        Edge = source.Edge,
        TimestampSeconds = source.TimestampSeconds
    };

    private static AnchorRevisionReferenceDto? ToDto(AnchorRevisionReference? source) => source is null ? null : new()
    {
        AnchorId = source.AnchorId,
        AnchorRevisionId = source.AnchorRevisionId
    };

    private static AnchorRevisionReference? FromDto(AnchorRevisionReferenceDto? source) => source is null ? null : new()
    {
        AnchorId = source.AnchorId,
        AnchorRevisionId = source.AnchorRevisionId
    };

    private static FrameAnchorDto ToDto(FrameAnchor source) => new()
    {
        Id = source.Id,
        CurrentRevisionId = source.CurrentRevisionId,
        DisplayLabel = source.DisplayLabel,
        Notes = source.Notes,
        IsArchived = source.IsArchived,
        CreatedAt = source.CreatedAt
    };

    private static FrameAnchor FromDto(FrameAnchorDto source) => new()
    {
        Id = source.Id,
        CurrentRevisionId = source.CurrentRevisionId,
        DisplayLabel = source.DisplayLabel,
        Notes = source.Notes,
        IsArchived = source.IsArchived,
        CreatedAt = source.CreatedAt
    };

    private static FrameAnchorRevisionDto ToDto(FrameAnchorRevision source) => new()
    {
        Id = source.Id,
        AnchorId = source.AnchorId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        FrameNumber = source.FrameNumber,
        CreatedAt = source.CreatedAt
    };

    private static FrameAnchorRevision FromDto(FrameAnchorRevisionDto source) => new()
    {
        Id = source.Id,
        AnchorId = source.AnchorId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        FrameNumber = source.FrameNumber,
        CreatedAt = source.CreatedAt
    };

    private static GenerationDraftDto? ToDto(GenerationDraft? source) => source is null ? null : new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Prompt = source.Prompt,
        Mode = source.Mode,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = source.References.Select(reference => new GenerationReferenceDraftDto
        {
            ReferenceId = reference.ReferenceId,
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            AnchorRevisionId = reference.AnchorRevisionId,
            Role = reference.Role,
            Order = reference.Order,
            Label = reference.Label,
            Notes = reference.Notes
        }).ToList(),
        ProviderParameters = Copy(source.ProviderParameters),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType,
        ModifiedAt = source.ModifiedAt
    };

    private static GenerationDraft? FromDto(GenerationDraftDto? source) => source is null ? null : new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Prompt = source.Prompt,
        Mode = source.Mode,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = source.References.Select(reference => new GenerationReferenceDraft
        {
            ReferenceId = reference.ReferenceId,
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            AnchorRevisionId = reference.AnchorRevisionId,
            Role = reference.Role,
            Order = reference.Order,
            Label = reference.Label,
            Notes = reference.Notes
        }).ToList(),
        ProviderParameters = Copy(source.ProviderParameters),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType,
        ModifiedAt = source.ModifiedAt
    };

    private static GenerationRecordDto ToDto(GenerationRecord source) => new()
    {
        Id = source.Id,
        RequestSnapshot = ToDto(source.RequestSnapshot),
        RequestedAt = source.RequestedAt,
        ProviderJobId = source.ProviderJobId,
        Status = source.Status,
        IngestionStatus = source.IngestionStatus,
        CompletedAt = source.CompletedAt,
        OutputAssetIds = [.. source.OutputAssetIds],
        ResponseMetadata = Copy(source.ResponseMetadata),
        ReferenceMaterializations = source.ReferenceMaterializations.ToDictionary(
            pair => pair.Key,
            pair => ToDto(pair.Value)!),
        Error = ToDto(source.Error),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType
    };

    private static GenerationRecord FromDto(GenerationRecordDto source) => new()
    {
        Id = source.Id,
        RequestSnapshot = FromDto(source.RequestSnapshot),
        RequestedAt = source.RequestedAt,
        ProviderJobId = source.ProviderJobId,
        Status = source.Status,
        IngestionStatus = source.IngestionStatus,
        CompletedAt = source.CompletedAt,
        OutputAssetIds = [.. source.OutputAssetIds],
        ResponseMetadata = Copy(source.ResponseMetadata),
        ReferenceMaterializations = source.ReferenceMaterializations.ToDictionary(
            pair => pair.Key,
            pair => FromDto(pair.Value)!),
        Error = FromDto(source.Error),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType
    };

    private static GenerationRequestSnapshotDto ToDto(GenerationRequestSnapshot source) => new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Mode = source.Mode,
        Prompt = source.Prompt,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = source.References.Select(ToDto).ToList(),
        ProviderParameters = Copy(source.ProviderParameters)
    };

    private static GenerationRequestSnapshot FromDto(GenerationRequestSnapshotDto source) => new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Mode = source.Mode,
        Prompt = source.Prompt,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = Array.AsReadOnly(source.References.Select(FromDto).ToArray()),
        ProviderParameters = ReadOnly(source.ProviderParameters)
    };

    private static GenerationReferenceSnapshotDto ToDto(GenerationReferenceSnapshot source) => new()
    {
        ReferenceId = source.ReferenceId,
        ObjectKind = source.ObjectKind,
        LogicalObjectId = source.LogicalObjectId,
        RecipeRevisionId = source.RecipeRevisionId,
        Anchor = ToDto(source.Anchor),
        ContentHash = source.ContentHash,
        Role = source.Role,
        Order = source.Order,
        Label = source.Label,
        Notes = source.Notes,
        Materialization = ToDto(source.Materialization)
    };

    private static GenerationReferenceSnapshot FromDto(GenerationReferenceSnapshotDto source) => new()
    {
        ReferenceId = source.ReferenceId,
        ObjectKind = source.ObjectKind,
        LogicalObjectId = source.LogicalObjectId,
        RecipeRevisionId = source.RecipeRevisionId,
        Anchor = FromDto(source.Anchor),
        ContentHash = source.ContentHash,
        Role = source.Role,
        Order = source.Order,
        Label = source.Label,
        Notes = source.Notes,
        Materialization = FromDto(source.Materialization)
    };

    private static FrameAnchorReferenceSnapshotDto? ToDto(FrameAnchorReferenceSnapshot? source) => source is null ? null : new()
    {
        AnchorRevisionId = source.AnchorRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        FrameNumber = source.FrameNumber
    };

    private static FrameAnchorReferenceSnapshot? FromDto(FrameAnchorReferenceSnapshotDto? source) => source is null ? null : new()
    {
        AnchorRevisionId = source.AnchorRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        FrameNumber = source.FrameNumber
    };

    private static MaterializationReceiptDto? ToDto(MaterializationReceipt? source) => source is null ? null : new()
    {
        PlanHash = source.PlanHash,
        SourceContentHash = source.SourceContentHash,
        ProducedContentHash = source.ProducedContentHash,
        Encoding = source.Encoding,
        ProviderReferenceId = source.ProviderReferenceId,
        ProviderScope = source.ProviderScope,
        ProviderReferenceExpiresAt = source.ProviderReferenceExpiresAt
    };

    private static MaterializationReceipt? FromDto(MaterializationReceiptDto? source) => source is null ? null : new()
    {
        PlanHash = source.PlanHash,
        SourceContentHash = source.SourceContentHash,
        ProducedContentHash = source.ProducedContentHash,
        Encoding = source.Encoding,
        ProviderReferenceId = source.ProviderReferenceId,
        ProviderScope = source.ProviderScope,
        ProviderReferenceExpiresAt = source.ProviderReferenceExpiresAt
    };

    private static GenerationErrorDto? ToDto(GenerationError? source) => source is null ? null : new()
    {
        HttpStatus = source.HttpStatus,
        ProviderCode = source.ProviderCode,
        Message = source.Message,
        TechnicalDetails = source.TechnicalDetails
    };

    private static GenerationError? FromDto(GenerationErrorDto? source) => source is null ? null : new()
    {
        HttpStatus = source.HttpStatus,
        ProviderCode = source.ProviderCode,
        Message = source.Message,
        TechnicalDetails = source.TechnicalDetails
    };

    private static Dictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static ReadOnlyDictionary<string, string> ReadOnly(IEnumerable<KeyValuePair<string, string>> source) =>
        new(Copy(source));
}
