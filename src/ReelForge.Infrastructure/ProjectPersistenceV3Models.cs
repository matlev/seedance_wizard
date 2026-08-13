using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class ProjectV3Dto
{
    public int SchemaVersion { get; set; } = 3;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public Guid? MainVideoAssetId { get; set; }
    public List<ProjectAssetV2Dto> Assets { get; set; } = [];
    public List<RecipeRevisionV3Dto> RecipeRevisions { get; set; } = [];
    public List<RecipeDraftV3Dto> RecipeDrafts { get; set; } = [];
    public List<FrameAnchorV3Dto> Anchors { get; set; } = [];
    public List<FrameAnchorRevisionV3Dto> AnchorRevisions { get; set; } = [];
    public GenerationDraftV3Dto? CurrentGenerationDraft { get; set; }
    public List<GenerationRecordV3Dto> Generations { get; set; } = [];
    public TimelineV2Dto Timeline { get; set; } = new();
}

internal sealed class RecipeRevisionV3Dto
{
    public Guid Id { get; set; }
    public Guid VirtualAssetId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AssetRecipeV3Dto Recipe { get; set; } = new();
}

internal sealed class RecipeDraftV3Dto
{
    public Guid Id { get; set; }
    public Guid? VirtualAssetId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public AssetRecipeV3Dto EditableRecipe { get; set; } = new();
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class AssetRecipeV3Dto
{
    public string Type { get; set; } = "extractFrame";
    public int RecipeSchemaVersion { get; set; } = 1;
    public AssetRevisionReferenceV2Dto Source { get; set; } = new();
    public RecipeBoundaryV3Dto? Start { get; set; }
    public RecipeBoundaryV3Dto? End { get; set; }
    public AnchorRevisionReferenceV3Dto? Anchor { get; set; }
    public string? Profile { get; set; }
}

internal sealed class RecipeBoundaryV3Dto
{
    public RecipeBoundaryKind Kind { get; set; }
    public AnchorRevisionReferenceV3Dto? Anchor { get; set; }
    public AnchorBoundaryEdge? Edge { get; set; }
    public double? TimestampSeconds { get; set; }
}

internal sealed class AnchorRevisionReferenceV3Dto
{
    public Guid AnchorId { get; set; }
    public Guid AnchorRevisionId { get; set; }
}

internal sealed class FrameAnchorV3Dto
{
    public Guid Id { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public string? DisplayLabel { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class FrameAnchorRevisionV3Dto
{
    public Guid Id { get; set; }
    public Guid AnchorId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public Guid SourceAssetId { get; set; }
    public string? SourceContentHash { get; set; }
    public int? VideoStreamIndex { get; set; }
    public AnchorTimingPrecision TimingPrecision { get; set; }
    public long? PresentationTimestamp { get; set; }
    public int? TimeBaseNumerator { get; set; }
    public int? TimeBaseDenominator { get; set; }
    public double? LegacyTimestampSeconds { get; set; }
    public long? FrameNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class GenerationDraftV3Dto
{
    public string? ProviderId { get; set; }
    public string? ModelVersion { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<GenerationReferenceDraftV3Dto> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class GenerationReferenceDraftV3Dto
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

internal sealed class GenerationRecordV3Dto
{
    public Guid Id { get; set; }
    public GenerationRequestSnapshotV3Dto RequestSnapshot { get; set; } = new();
    public DateTimeOffset RequestedAt { get; set; }
    public string? ProviderJobId { get; set; }
    public GenerationStatus Status { get; set; }
    public OutputIngestionStatus IngestionStatus { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<Guid> OutputAssetIds { get; set; } = [];
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
    public GenerationErrorV2Dto? Error { get; set; }
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
}

internal sealed class GenerationRequestSnapshotV3Dto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<GenerationReferenceSnapshotV3Dto> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GenerationReferenceSnapshotV3Dto
{
    public Guid ReferenceId { get; set; }
    public GenerationReferenceObjectKind ObjectKind { get; set; }
    public Guid LogicalObjectId { get; set; }
    public Guid? RecipeRevisionId { get; set; }
    public FrameAnchorReferenceSnapshotV3Dto? Anchor { get; set; }
    public string? ContentHash { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    public MaterializationReceiptV2Dto? Materialization { get; set; }
}

internal sealed class FrameAnchorReferenceSnapshotV3Dto
{
    public Guid AnchorRevisionId { get; set; }
    public Guid SourceAssetId { get; set; }
    public string? SourceContentHash { get; set; }
    public int? VideoStreamIndex { get; set; }
    public AnchorTimingPrecision TimingPrecision { get; set; }
    public long? PresentationTimestamp { get; set; }
    public int? TimeBaseNumerator { get; set; }
    public int? TimeBaseDenominator { get; set; }
    public double? LegacyTimestampSeconds { get; set; }
    public long? FrameNumber { get; set; }
}

internal static class ProjectPersistenceV3Mapper
{
    public static ProjectV3Dto ToDto(VideoProject source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Id = source.Id,
        Name = source.Name,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        MainVideoAssetId = source.MainVideoAssetId,
        Assets = source.Assets.Select(ToDto).ToList(),
        RecipeRevisions = source.RecipeRevisions.Select(ToDto).ToList(),
        RecipeDrafts = source.RecipeDrafts.Select(ToDto).ToList(),
        Anchors = source.Anchors.Select(ToDto).ToList(),
        AnchorRevisions = source.AnchorRevisions.Select(ToDto).ToList(),
        CurrentGenerationDraft = ToDto(source.CurrentGenerationDraft),
        Generations = source.Generations.Select(ToDto).ToList(),
        Timeline = new TimelineV2Dto
        {
            Clips = source.Timeline.Clips.Select(clip => new TimelineClipV2Dto
            {
                Id = clip.Id,
                SourceAssetId = clip.SourceAssetId,
                SourceRecipeRevisionId = clip.SourceRecipeRevisionId,
                InPointSeconds = clip.InPointSeconds,
                OutPointSeconds = clip.OutPointSeconds,
                TimelinePositionSeconds = clip.TimelinePositionSeconds,
                Track = clip.Track,
                AudioEnabled = clip.AudioEnabled
            }).ToList()
        }
    };

    public static VideoProject FromDto(ProjectV3Dto source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Id = source.Id,
        Name = source.Name,
        CreatedAt = source.CreatedAt,
        ModifiedAt = source.ModifiedAt,
        MainVideoAssetId = source.MainVideoAssetId,
        Assets = source.Assets.Select(FromDto).ToList(),
        RecipeRevisions = source.RecipeRevisions.Select(FromDto).ToList(),
        RecipeDrafts = source.RecipeDrafts.Select(FromDto).ToList(),
        Anchors = source.Anchors.Select(FromDto).ToList(),
        AnchorRevisions = source.AnchorRevisions.Select(FromDto).ToList(),
        CurrentGenerationDraft = FromDto(source.CurrentGenerationDraft),
        Generations = source.Generations.Select(FromDto).ToList(),
        Timeline = new Timeline
        {
            Clips = source.Timeline.Clips.Select(clip => new TimelineClip
            {
                Id = clip.Id,
                SourceAssetId = clip.SourceAssetId,
                SourceRecipeRevisionId = clip.SourceRecipeRevisionId,
                InPointSeconds = clip.InPointSeconds,
                OutPointSeconds = clip.OutPointSeconds,
                TimelinePositionSeconds = clip.TimelinePositionSeconds,
                Track = clip.Track,
                AudioEnabled = clip.AudioEnabled
            }).ToList()
        }
    };

    private static ProjectAssetV2Dto ToDto(ProjectAsset source) => new()
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
        Virtual = source.Virtual is null ? null : new VirtualAssetStateV2Dto
        {
            CurrentRecipeRevisionId = source.Virtual.CurrentRecipeRevisionId,
            ExpectedMediaProperties = source.Virtual.ExpectedMediaProperties
        },
        ProviderReferences = source.ProviderReferences.ToDictionary(pair => pair.Key, pair => ToDto(pair.Value), StringComparer.Ordinal)
    };

    private static ProjectAsset FromDto(ProjectAssetV2Dto source) => new()
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
            CurrentRecipeRevisionId = source.Virtual.CurrentRecipeRevisionId,
            ExpectedMediaProperties = source.Virtual.ExpectedMediaProperties
        },
        ProviderReferences = source.ProviderReferences.ToDictionary(pair => pair.Key, pair => FromDto(pair.Value), StringComparer.Ordinal)
    };

    private static PhysicalAssetStorageV2Dto? ToDto(PhysicalAssetStorage? source) => source is null ? null : new()
    {
        RelativePath = source.RelativePath,
        Durability = source.Durability,
        ContentIdentity = new ContentIdentityV2Dto
        {
            Algorithm = source.ContentIdentity.Algorithm,
            Sha256 = source.ContentIdentity.Sha256,
            Status = source.ContentIdentity.Status,
            LengthBytes = source.ContentIdentity.LengthBytes,
            ObservedLastWriteTimeUtc = source.ContentIdentity.ObservedLastWriteTimeUtc
        }
    };

    private static PhysicalAssetStorage? FromDto(PhysicalAssetStorageV2Dto? source) => source is null ? null : new()
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

    private static ProviderAssetReferenceV2Dto ToDto(ProviderAssetReference source) => new()
    {
        Value = source.Value,
        SourceContentHash = source.SourceContentHash,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Scope = source.Scope,
        ExpiresAt = source.ExpiresAt
    };

    private static ProviderAssetReference FromDto(ProviderAssetReferenceV2Dto source) => new()
    {
        Value = source.Value,
        SourceContentHash = source.SourceContentHash,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Scope = source.Scope,
        ExpiresAt = source.ExpiresAt
    };

    private static AssetProvenanceV2Dto? ToDto(AssetProvenance? source) => source is null ? null : new()
    {
        Operation = source.Operation,
        SourceAssetIds = [.. source.SourceAssetIds],
        GenerationId = source.GenerationId,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Parameters = Copy(source.Parameters)
    };

    private static AssetProvenance? FromDto(AssetProvenanceV2Dto? source) => source is null ? null : new()
    {
        Operation = source.Operation,
        SourceAssetIds = [.. source.SourceAssetIds],
        GenerationId = source.GenerationId,
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        Parameters = Copy(source.Parameters)
    };

    private static RecipeRevisionV3Dto ToDto(RecipeRevision source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        CreatedAt = source.CreatedAt,
        Recipe = ToDto(source.Recipe)
    };

    private static RecipeRevision FromDto(RecipeRevisionV3Dto source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        CreatedAt = source.CreatedAt,
        Recipe = FromDto(source.Recipe)
    };

    private static RecipeDraftV3Dto ToDto(RecipeDraft source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        BasedOnRevisionId = source.BasedOnRevisionId,
        EditableRecipe = ToDto(source.EditableRecipe),
        ModifiedAt = source.ModifiedAt
    };

    private static RecipeDraft FromDto(RecipeDraftV3Dto source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        BasedOnRevisionId = source.BasedOnRevisionId,
        EditableRecipe = FromDto(source.EditableRecipe),
        ModifiedAt = source.ModifiedAt
    };

    private static AssetRecipeV3Dto ToDto(AssetRecipe source) => source switch
    {
        TrimRecipe trim => new AssetRecipeV3Dto
        {
            Type = "trim",
            RecipeSchemaVersion = trim.RecipeSchemaVersion,
            Source = ToDto(trim.Source),
            Start = ToDto(trim.Start),
            End = ToDto(trim.End),
            Profile = trim.RenderProfile
        },
        ExtractFrameRecipe frame => new AssetRecipeV3Dto
        {
            Type = "extractFrame",
            RecipeSchemaVersion = frame.RecipeSchemaVersion,
            Source = ToDto(frame.Source),
            Anchor = ToDto(frame.Anchor),
            Profile = frame.ImageProfile
        },
        _ => throw new NotSupportedException($"Recipe type '{source.GetType().Name}' is not supported.")
    };

    private static AssetRecipe FromDto(AssetRecipeV3Dto source) => source.Type switch
    {
        "trim" => new TrimRecipe
        {
            RecipeSchemaVersion = source.RecipeSchemaVersion,
            Source = FromDto(source.Source),
            Start = FromDto(source.Start) ?? RecipeBoundary.SourceStart,
            End = FromDto(source.End) ?? RecipeBoundary.SourceEnd,
            RenderProfile = source.Profile
        },
        "extractFrame" => new ExtractFrameRecipe
        {
            RecipeSchemaVersion = source.RecipeSchemaVersion,
            Source = FromDto(source.Source),
            Anchor = FromDto(source.Anchor) ?? new AnchorRevisionReference(),
            ImageProfile = source.Profile
        },
        _ => throw new InvalidDataException($"Recipe type '{source.Type}' is not supported.")
    };

    private static AssetRevisionReferenceV2Dto ToDto(AssetRevisionReference source) => new()
    {
        AssetId = source.AssetId,
        RecipeRevisionId = source.RecipeRevisionId
    };

    private static AssetRevisionReference FromDto(AssetRevisionReferenceV2Dto source) => new()
    {
        AssetId = source.AssetId,
        RecipeRevisionId = source.RecipeRevisionId
    };

    private static RecipeBoundaryV3Dto ToDto(RecipeBoundary source) => new()
    {
        Kind = source.Kind,
        Anchor = ToDto(source.Anchor),
        Edge = source.Edge,
        TimestampSeconds = source.TimestampSeconds
    };

    private static RecipeBoundary? FromDto(RecipeBoundaryV3Dto? source) => source is null ? null : new()
    {
        Kind = source.Kind,
        Anchor = FromDto(source.Anchor),
        Edge = source.Edge,
        TimestampSeconds = source.TimestampSeconds
    };

    private static AnchorRevisionReferenceV3Dto? ToDto(AnchorRevisionReference? source) => source is null ? null : new()
    {
        AnchorId = source.AnchorId,
        AnchorRevisionId = source.AnchorRevisionId
    };

    private static AnchorRevisionReference? FromDto(AnchorRevisionReferenceV3Dto? source) => source is null ? null : new()
    {
        AnchorId = source.AnchorId,
        AnchorRevisionId = source.AnchorRevisionId
    };

    private static FrameAnchorV3Dto ToDto(FrameAnchor source) => new()
    {
        Id = source.Id,
        CurrentRevisionId = source.CurrentRevisionId,
        DisplayLabel = source.DisplayLabel,
        Notes = source.Notes,
        IsArchived = source.IsArchived,
        CreatedAt = source.CreatedAt
    };

    private static FrameAnchor FromDto(FrameAnchorV3Dto source) => new()
    {
        Id = source.Id,
        CurrentRevisionId = source.CurrentRevisionId,
        DisplayLabel = source.DisplayLabel,
        Notes = source.Notes,
        IsArchived = source.IsArchived,
        CreatedAt = source.CreatedAt
    };

    private static FrameAnchorRevisionV3Dto ToDto(FrameAnchorRevision source) => new()
    {
        Id = source.Id,
        AnchorId = source.AnchorId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        TimingPrecision = source.TimingPrecision,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        LegacyTimestampSeconds = source.LegacyTimestampSeconds,
        FrameNumber = source.FrameNumber,
        CreatedAt = source.CreatedAt
    };

    private static FrameAnchorRevision FromDto(FrameAnchorRevisionV3Dto source) => new()
    {
        Id = source.Id,
        AnchorId = source.AnchorId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        TimingPrecision = source.TimingPrecision,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        LegacyTimestampSeconds = source.LegacyTimestampSeconds,
        FrameNumber = source.FrameNumber,
        CreatedAt = source.CreatedAt
    };

    private static GenerationDraftV3Dto? ToDto(GenerationDraft? source) => source is null ? null : new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Prompt = source.Prompt,
        Mode = source.Mode,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = source.References.Select(reference => new GenerationReferenceDraftV3Dto
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

    private static GenerationDraft? FromDto(GenerationDraftV3Dto? source) => source is null ? null : new()
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

    private static GenerationRecordV3Dto ToDto(GenerationRecord source) => new()
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
        Error = ToDto(source.Error),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType
    };

    private static GenerationRecord FromDto(GenerationRecordV3Dto source) => new()
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
        Error = FromDto(source.Error),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType
    };

    private static GenerationRequestSnapshotV3Dto ToDto(GenerationRequestSnapshot source) => new()
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

    private static GenerationRequestSnapshot FromDto(GenerationRequestSnapshotV3Dto source) => new()
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

    private static GenerationReferenceSnapshotV3Dto ToDto(GenerationReferenceSnapshot source) => new()
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

    private static GenerationReferenceSnapshot FromDto(GenerationReferenceSnapshotV3Dto source) => new()
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

    private static FrameAnchorReferenceSnapshotV3Dto? ToDto(FrameAnchorReferenceSnapshot? source) => source is null ? null : new()
    {
        AnchorRevisionId = source.AnchorRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        TimingPrecision = source.TimingPrecision,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        LegacyTimestampSeconds = source.LegacyTimestampSeconds,
        FrameNumber = source.FrameNumber
    };

    private static FrameAnchorReferenceSnapshot? FromDto(FrameAnchorReferenceSnapshotV3Dto? source) => source is null ? null : new()
    {
        AnchorRevisionId = source.AnchorRevisionId,
        SourceAssetId = source.SourceAssetId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        TimingPrecision = source.TimingPrecision,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        LegacyTimestampSeconds = source.LegacyTimestampSeconds,
        FrameNumber = source.FrameNumber
    };

    private static MaterializationReceiptV2Dto? ToDto(MaterializationReceipt? source) => source is null ? null : new()
    {
        PlanHash = source.PlanHash,
        SourceContentHash = source.SourceContentHash,
        ProducedContentHash = source.ProducedContentHash,
        Encoding = source.Encoding,
        ProviderReferenceId = source.ProviderReferenceId,
        ProviderScope = source.ProviderScope,
        ProviderReferenceExpiresAt = source.ProviderReferenceExpiresAt
    };

    private static MaterializationReceipt? FromDto(MaterializationReceiptV2Dto? source) => source is null ? null : new()
    {
        PlanHash = source.PlanHash,
        SourceContentHash = source.SourceContentHash,
        ProducedContentHash = source.ProducedContentHash,
        Encoding = source.Encoding,
        ProviderReferenceId = source.ProviderReferenceId,
        ProviderScope = source.ProviderScope,
        ProviderReferenceExpiresAt = source.ProviderReferenceExpiresAt
    };

    private static GenerationErrorV2Dto? ToDto(GenerationError? source) => source is null ? null : new()
    {
        HttpStatus = source.HttpStatus,
        ProviderCode = source.ProviderCode,
        Message = source.Message,
        TechnicalDetails = source.TechnicalDetails
    };

    private static GenerationError? FromDto(GenerationErrorV2Dto? source) => source is null ? null : new()
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
