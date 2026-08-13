using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class ProjectV1Dto
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public Guid? MainVideoAssetId { get; set; }
    public List<ProjectAssetV1Dto> Assets { get; set; } = [];
    public List<GenerationRecordV1Dto> Generations { get; set; } = [];
    public TimelineV1Dto Timeline { get; set; } = new();
}

internal sealed class ProjectAssetV1Dto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public AssetOrigin Origin { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public AssetProvenanceV2Dto? Provenance { get; set; }
    public Dictionary<string, string> ProviderReferences { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GenerationRecordV1Dto
{
    public Guid Id { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public GenerationRequestV1Dto Request { get; set; } = new();
    public DateTimeOffset RequestedAt { get; set; }
    public string? ProviderJobId { get; set; }
    public GenerationStatus Status { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? OutputAssetId { get; set; }
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
    public GenerationErrorV2Dto? Error { get; set; }
    public Guid? ParentGenerationId { get; set; }
}

internal sealed class GenerationRequestV1Dto
{
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<Guid> ReferenceAssetIds { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class TimelineV1Dto { public List<TimelineClipV1Dto> Clips { get; set; } = []; }
internal sealed class TimelineClipV1Dto
{
    public Guid Id { get; set; }
    public Guid SourceAssetId { get; set; }
    public double InPointSeconds { get; set; }
    public double OutPointSeconds { get; set; }
    public double TimelinePositionSeconds { get; set; }
    public bool AudioEnabled { get; set; }
}

internal sealed class ProjectV2Dto
{
    public int SchemaVersion { get; set; } = 2;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public Guid? MainVideoAssetId { get; set; }
    public List<ProjectAssetV2Dto> Assets { get; set; } = [];
    public List<RecipeRevisionV2Dto> RecipeRevisions { get; set; } = [];
    public List<RecipeDraftV2Dto> RecipeDrafts { get; set; } = [];
    public List<FrameAnchorV2Dto> Anchors { get; set; } = [];
    public GenerationDraftV2Dto? CurrentGenerationDraft { get; set; }
    public List<GenerationRecordV2Dto> Generations { get; set; } = [];
    public TimelineV2Dto Timeline { get; set; } = new();
}

internal sealed class ProjectAssetV2Dto
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
    public AssetProvenanceV2Dto? Provenance { get; set; }
    public PhysicalAssetStorageV2Dto? Physical { get; set; }
    public VirtualAssetStateV2Dto? Virtual { get; set; }
    public Dictionary<string, ProviderAssetReferenceV2Dto> ProviderReferences { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class PhysicalAssetStorageV2Dto
{
    public string RelativePath { get; set; } = string.Empty;
    public PhysicalAssetDurability Durability { get; set; }
    public ContentIdentityV2Dto ContentIdentity { get; set; } = new();
}

internal sealed class ContentIdentityV2Dto
{
    public string Algorithm { get; set; } = ContentIdentity.Sha256Algorithm;
    public string? Sha256 { get; set; }
    public ContentHashStatus Status { get; set; }
    public long? LengthBytes { get; set; }
    public DateTimeOffset? ObservedLastWriteTimeUtc { get; set; }
}

internal sealed class VirtualAssetStateV2Dto
{
    public Guid? CurrentRecipeRevisionId { get; set; }
    public MediaEncodingMetadata? ExpectedMediaProperties { get; set; }
}

internal sealed class ProviderAssetReferenceV2Dto
{
    public string Value { get; set; } = string.Empty;
    public string? SourceContentHash { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public string? Scope { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

internal sealed class AssetProvenanceV2Dto
{
    public string Operation { get; set; } = string.Empty;
    public List<Guid> SourceAssetIds { get; set; } = [];
    public Guid? GenerationId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class RecipeRevisionV2Dto
{
    public Guid Id { get; set; }
    public Guid VirtualAssetId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AssetRecipeV2Dto Recipe { get; set; } = new();
}

internal sealed class RecipeDraftV2Dto
{
    public Guid Id { get; set; }
    public Guid? VirtualAssetId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public AssetRecipeV2Dto EditableRecipe { get; set; } = new();
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class AssetRecipeV2Dto
{
    public string Type { get; set; } = "extractFrame";
    public int RecipeSchemaVersion { get; set; } = 1;
    public AssetRevisionReferenceV2Dto Source { get; set; } = new();
    public RecipeBoundaryV2Dto? Start { get; set; }
    public RecipeBoundaryV2Dto? End { get; set; }
    public Guid? AnchorId { get; set; }
    public string? Profile { get; set; }
}

internal sealed class RecipeBoundaryV2Dto
{
    public RecipeBoundaryKind Kind { get; set; }
    public Guid? AnchorId { get; set; }
    public double? TimestampSeconds { get; set; }
}

internal sealed class AssetRevisionReferenceV2Dto
{
    public Guid AssetId { get; set; }
    public Guid? RecipeRevisionId { get; set; }
}

internal sealed class FrameAnchorV2Dto
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public long? FrameNumber { get; set; }
    public double TimestampSeconds { get; set; }
    public string? TimeBase { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
}

internal sealed class GenerationDraftV2Dto
{
    public string? ProviderId { get; set; }
    public string? ModelVersion { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<GenerationReferenceDraftV2Dto> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
    public Guid? ParentGenerationId { get; set; }
    public GenerationRelationshipType? RelationshipType { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class GenerationReferenceDraftV2Dto
{
    public GenerationReferenceObjectKind ObjectKind { get; set; }
    public Guid LogicalObjectId { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
}

internal sealed class GenerationRecordV2Dto
{
    public Guid Id { get; set; }
    public GenerationRequestSnapshotV2Dto RequestSnapshot { get; set; } = new();
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

internal sealed class GenerationRequestSnapshotV2Dto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public GenerationMode Mode { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public List<GenerationReferenceSnapshotV2Dto> References { get; set; } = [];
    public Dictionary<string, string> ProviderParameters { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class GenerationReferenceSnapshotV2Dto
{
    public GenerationReferenceObjectKind ObjectKind { get; set; }
    public Guid LogicalObjectId { get; set; }
    public Guid? RecipeRevisionId { get; set; }
    public string? ContentHash { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int? Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    public MaterializationReceiptV2Dto? Materialization { get; set; }
}

internal sealed class MaterializationReceiptV2Dto
{
    public string? PlanHash { get; set; }
    public string? SourceContentHash { get; set; }
    public string? ProducedContentHash { get; set; }
    public MediaEncodingMetadata? Encoding { get; set; }
    public string? ProviderReferenceId { get; set; }
    public string? ProviderScope { get; set; }
    public DateTimeOffset? ProviderReferenceExpiresAt { get; set; }
}

internal sealed class GenerationErrorV2Dto
{
    public int? HttpStatus { get; set; }
    public string? ProviderCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TechnicalDetails { get; set; }
}

internal sealed class TimelineV2Dto { public List<TimelineClipV2Dto> Clips { get; set; } = []; }
internal sealed class TimelineClipV2Dto
{
    public Guid Id { get; set; }
    public Guid SourceAssetId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public double InPointSeconds { get; set; }
    public double OutPointSeconds { get; set; }
    public double TimelinePositionSeconds { get; set; }
    public int Track { get; set; }
    public bool AudioEnabled { get; set; }
}

internal static class ProjectPersistenceMapper
{
    public static VideoProject Migrate(ProjectV1Dto source)
    {
        var project = new VideoProject
        {
            SchemaVersion = VideoProject.CurrentSchemaVersion,
            Id = source.Id,
            Name = source.Name,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            MainVideoAssetId = source.MainVideoAssetId,
            Assets = source.Assets.Select(MigrateAsset).ToList(),
            Timeline = new Timeline
            {
                Clips = source.Timeline.Clips.Select(clip => new TimelineClip
                {
                    Id = clip.Id,
                    SourceAssetId = clip.SourceAssetId,
                    InPointSeconds = clip.InPointSeconds,
                    OutPointSeconds = clip.OutPointSeconds,
                    TimelinePositionSeconds = clip.TimelinePositionSeconds,
                    AudioEnabled = clip.AudioEnabled
                }).ToList()
            }
        };

        foreach (var sourceGeneration in source.Generations)
        {
            var generation = new GenerationRecord
            {
                Id = sourceGeneration.Id,
                RequestSnapshot = new GenerationRequestSnapshot
                {
                    ProviderId = sourceGeneration.ProviderId,
                    ModelVersion = sourceGeneration.ModelVersion,
                    Mode = sourceGeneration.Request.Mode,
                    Prompt = sourceGeneration.Request.Prompt,
                    DurationSeconds = sourceGeneration.Request.DurationSeconds,
                    AspectRatio = sourceGeneration.Request.AspectRatio,
                    Resolution = sourceGeneration.Request.Resolution,
                    References = Array.AsReadOnly(sourceGeneration.Request.ReferenceAssetIds.Select(
                        (id, index) => new GenerationReferenceSnapshot
                        {
                            ObjectKind = GenerationReferenceObjectKind.Asset,
                            LogicalObjectId = id,
                            Role = GenerationReferenceRole.GeneralReference,
                            Order = index
                        }).ToArray()),
                    ProviderParameters = ReadOnly(sourceGeneration.Request.ProviderParameters)
                },
                RequestedAt = sourceGeneration.RequestedAt,
                ProviderJobId = sourceGeneration.ProviderJobId,
                Status = sourceGeneration.Status,
                IngestionStatus = sourceGeneration.OutputAssetId is null ? OutputIngestionStatus.NotRequired : OutputIngestionStatus.Succeeded,
                CompletedAt = sourceGeneration.CompletedAt,
                OutputAssetIds = sourceGeneration.OutputAssetId is { } singleOutputId ? [singleOutputId] : [],
                ResponseMetadata = Copy(sourceGeneration.ResponseMetadata),
                Error = FromDto(sourceGeneration.Error),
                ParentGenerationId = sourceGeneration.ParentGenerationId,
                RelationshipType = sourceGeneration.ParentGenerationId is null ? null : GenerationRelationshipType.BasedOn
            };
            project.Generations.Add(generation);

            foreach (var outputId in generation.OutputAssetIds)
            {
                var output = project.Assets.SingleOrDefault(asset => asset.Id == outputId);
                if (output is not null)
                {
                    output.Provenance ??= new AssetProvenance { Operation = "legacy-generation-output" };
                    output.Provenance.GenerationId = generation.Id;
                }
            }
        }

        return project;
    }

    public static VideoProject Migrate(ProjectV2Dto source)
    {
        var anchorRevisionIds = source.Anchors.ToDictionary(anchor => anchor.Id, _ => Guid.NewGuid());
        var assets = source.Assets.Select(FromDto).ToList();
        var assetById = assets.ToDictionary(asset => asset.Id);

        FrameAnchorRevision MigrateAnchorRevision(FrameAnchorV2Dto anchor)
        {
            assetById.TryGetValue(anchor.AssetId, out var asset);
            var verifiedHash = asset?.Physical?.ContentIdentity is
                { Status: ContentHashStatus.Verified, Sha256.Length: 64 } identity
                    ? identity.Sha256
                    : null;
            return new FrameAnchorRevision
            {
                Id = anchorRevisionIds[anchor.Id],
                AnchorId = anchor.Id,
                RevisionNumber = 1,
                SourceAssetId = anchor.AssetId,
                SourceContentHash = verifiedHash,
                TimingPrecision = AnchorTimingPrecision.LegacyTimestampSeconds,
                LegacyTimestampSeconds = anchor.TimestampSeconds,
                FrameNumber = anchor.FrameNumber,
                CreatedAt = source.CreatedAt
            };
        }

        FrameAnchorReferenceSnapshot? MigrateAnchorSnapshot(GenerationReferenceSnapshotV2Dto reference)
        {
            if (reference.ObjectKind != GenerationReferenceObjectKind.FrameAnchor ||
                !anchorRevisionIds.TryGetValue(reference.LogicalObjectId, out var revisionId))
            {
                return null;
            }

            var legacy = source.Anchors.Single(anchor => anchor.Id == reference.LogicalObjectId);
            var revision = MigrateAnchorRevision(legacy);
            return new FrameAnchorReferenceSnapshot
            {
                AnchorRevisionId = revisionId,
                SourceAssetId = revision.SourceAssetId,
                SourceContentHash = revision.SourceContentHash,
                TimingPrecision = revision.TimingPrecision,
                LegacyTimestampSeconds = revision.LegacyTimestampSeconds,
                FrameNumber = revision.FrameNumber
            };
        }

        var project = new VideoProject
        {
            SchemaVersion = VideoProject.CurrentSchemaVersion,
            Id = source.Id,
            Name = source.Name,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            MainVideoAssetId = source.MainVideoAssetId,
            Assets = assets,
            Anchors = source.Anchors.Select(anchor => new FrameAnchor
            {
                Id = anchor.Id,
                CurrentRevisionId = anchorRevisionIds[anchor.Id],
                DisplayLabel = anchor.Label,
                Notes = anchor.Notes,
                CreatedAt = source.CreatedAt
            }).ToList(),
            AnchorRevisions = source.Anchors.Select(MigrateAnchorRevision).ToList(),
            RecipeRevisions = source.RecipeRevisions.Select(revision => new RecipeRevision
            {
                Id = revision.Id,
                VirtualAssetId = revision.VirtualAssetId,
                RevisionNumber = revision.RevisionNumber,
                PreviousRevisionId = revision.PreviousRevisionId,
                CreatedAt = revision.CreatedAt,
                Recipe = MigrateRecipe(revision.Recipe, anchorRevisionIds)
            }).ToList(),
            RecipeDrafts = source.RecipeDrafts.Select(draft => new RecipeDraft
            {
                Id = draft.Id,
                VirtualAssetId = draft.VirtualAssetId,
                BasedOnRevisionId = draft.BasedOnRevisionId,
                EditableRecipe = MigrateRecipe(draft.EditableRecipe, anchorRevisionIds),
                ModifiedAt = draft.ModifiedAt
            }).ToList(),
            CurrentGenerationDraft = source.CurrentGenerationDraft is null ? null : new GenerationDraft
            {
                ProviderId = source.CurrentGenerationDraft.ProviderId,
                ModelVersion = source.CurrentGenerationDraft.ModelVersion,
                Prompt = source.CurrentGenerationDraft.Prompt,
                Mode = source.CurrentGenerationDraft.Mode,
                DurationSeconds = source.CurrentGenerationDraft.DurationSeconds,
                AspectRatio = source.CurrentGenerationDraft.AspectRatio,
                Resolution = source.CurrentGenerationDraft.Resolution,
                References = source.CurrentGenerationDraft.References.Select(reference => new GenerationReferenceDraft
                {
                    ReferenceId = Guid.NewGuid(),
                    ObjectKind = reference.ObjectKind,
                    LogicalObjectId = reference.LogicalObjectId,
                    AnchorRevisionId = reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                        anchorRevisionIds.TryGetValue(reference.LogicalObjectId, out var revisionId)
                            ? revisionId
                            : null,
                    Role = reference.Role,
                    Order = reference.Order,
                    Label = reference.Label,
                    Notes = reference.Notes
                }).ToList(),
                ProviderParameters = Copy(source.CurrentGenerationDraft.ProviderParameters),
                ParentGenerationId = source.CurrentGenerationDraft.ParentGenerationId,
                RelationshipType = source.CurrentGenerationDraft.RelationshipType,
                ModifiedAt = source.CurrentGenerationDraft.ModifiedAt
            },
            Generations = source.Generations.Select(generation => new GenerationRecord
            {
                Id = generation.Id,
                RequestSnapshot = new GenerationRequestSnapshot
                {
                    ProviderId = generation.RequestSnapshot.ProviderId,
                    ModelVersion = generation.RequestSnapshot.ModelVersion,
                    Mode = generation.RequestSnapshot.Mode,
                    Prompt = generation.RequestSnapshot.Prompt,
                    DurationSeconds = generation.RequestSnapshot.DurationSeconds,
                    AspectRatio = generation.RequestSnapshot.AspectRatio,
                    Resolution = generation.RequestSnapshot.Resolution,
                    References = Array.AsReadOnly(generation.RequestSnapshot.References.Select(reference =>
                        new GenerationReferenceSnapshot
                        {
                            ReferenceId = Guid.NewGuid(),
                            ObjectKind = reference.ObjectKind,
                            LogicalObjectId = reference.LogicalObjectId,
                            RecipeRevisionId = reference.RecipeRevisionId,
                            Anchor = MigrateAnchorSnapshot(reference),
                            ContentHash = reference.ContentHash,
                            Role = reference.Role,
                            Order = reference.Order,
                            Label = reference.Label,
                            Notes = reference.Notes,
                            Materialization = FromDto(reference.Materialization)
                        }).ToArray()),
                    ProviderParameters = ReadOnly(generation.RequestSnapshot.ProviderParameters)
                },
                RequestedAt = generation.RequestedAt,
                ProviderJobId = generation.ProviderJobId,
                Status = generation.Status,
                IngestionStatus = generation.IngestionStatus,
                CompletedAt = generation.CompletedAt,
                OutputAssetIds = [.. generation.OutputAssetIds],
                ResponseMetadata = Copy(generation.ResponseMetadata),
                Error = FromDto(generation.Error),
                ParentGenerationId = generation.ParentGenerationId,
                RelationshipType = generation.RelationshipType
            }).ToList(),
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

        return project;
    }

    private static AssetRecipe MigrateRecipe(
        AssetRecipeV2Dto source,
        Dictionary<Guid, Guid> anchorRevisionIds) => source.Type switch
    {
        "trim" => new TrimRecipe
        {
            RecipeSchemaVersion = source.RecipeSchemaVersion,
            Source = FromDto(source.Source),
            Start = MigrateBoundary(source.Start ?? new RecipeBoundaryV2Dto { Kind = RecipeBoundaryKind.SourceStart }, anchorRevisionIds),
            End = MigrateBoundary(source.End ?? new RecipeBoundaryV2Dto { Kind = RecipeBoundaryKind.SourceEnd }, anchorRevisionIds),
            RenderProfile = source.Profile
        },
        "extractFrame" when source.AnchorId is { } anchorId && anchorRevisionIds.TryGetValue(anchorId, out var revisionId) =>
            new ExtractFrameRecipe
            {
                RecipeSchemaVersion = source.RecipeSchemaVersion,
                Source = FromDto(source.Source),
                Anchor = new AnchorRevisionReference { AnchorId = anchorId, AnchorRevisionId = revisionId },
                ImageProfile = source.Profile
            },
        "extractFrame" => throw new InvalidDataException("A schema-v2 extract-frame recipe references an unknown anchor."),
        _ => throw new InvalidDataException($"Recipe type '{source.Type}' is not supported.")
    };

    private static RecipeBoundary MigrateBoundary(
        RecipeBoundaryV2Dto source,
        Dictionary<Guid, Guid> anchorRevisionIds)
    {
        if (source.Kind != RecipeBoundaryKind.Anchor)
            return new RecipeBoundary { Kind = source.Kind, TimestampSeconds = source.TimestampSeconds };
        if (source.AnchorId is not { } anchorId || !anchorRevisionIds.TryGetValue(anchorId, out var revisionId))
            throw new InvalidDataException("A schema-v2 recipe boundary references an unknown anchor.");
        return new RecipeBoundary
        {
            Kind = RecipeBoundaryKind.Anchor,
            Anchor = new AnchorRevisionReference
            {
                AnchorId = anchorId,
                AnchorRevisionId = revisionId
            },
            Edge = AnchorBoundaryEdge.LegacyUnspecified
        };
    }

    public static ProjectV2Dto ToDto(VideoProject source) => new()
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

    public static VideoProject FromDto(ProjectV2Dto source) => new()
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

    private static ProjectAsset MigrateAsset(ProjectAssetV1Dto source) => new()
    {
        Id = source.Id,
        DisplayName = source.FileName,
        FileName = source.FileName,
        MediaType = source.MediaType,
        StorageKind = AssetStorageKind.Physical,
        Origin = source.Origin,
        CreatedAt = source.CreatedAt,
        DurationSeconds = source.DurationSeconds,
        Width = source.Width,
        Height = source.Height,
        Encoding = source.Encoding,
        Provenance = FromDto(source.Provenance),
        Physical = new PhysicalAssetStorage
        {
            RelativePath = source.RelativePath,
            Durability = source.Origin switch
            {
                AssetOrigin.Generated => PhysicalAssetDurability.Generated,
                AssetOrigin.Exported => PhysicalAssetDurability.Exported,
                _ => PhysicalAssetDurability.Source
            },
            ContentIdentity = new ContentIdentity
            {
                Status = ContentHashStatus.Pending,
                LengthBytes = source.Encoding?.SizeBytes
            }
        },
        Virtual = null,
        ProviderReferences = source.ProviderReferences.ToDictionary(
            pair => pair.Key,
            pair => new ProviderAssetReference { Value = pair.Value },
            StringComparer.Ordinal)
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

    private static RecipeRevisionV2Dto ToDto(RecipeRevision source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        CreatedAt = source.CreatedAt,
        Recipe = ToDto(source.Recipe)
    };

    private static RecipeRevision FromDto(RecipeRevisionV2Dto source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        RevisionNumber = source.RevisionNumber,
        PreviousRevisionId = source.PreviousRevisionId,
        CreatedAt = source.CreatedAt,
        Recipe = FromDto(source.Recipe)
    };

    private static RecipeDraftV2Dto ToDto(RecipeDraft source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        BasedOnRevisionId = source.BasedOnRevisionId,
        EditableRecipe = ToDto(source.EditableRecipe),
        ModifiedAt = source.ModifiedAt
    };

    private static RecipeDraft FromDto(RecipeDraftV2Dto source) => new()
    {
        Id = source.Id,
        VirtualAssetId = source.VirtualAssetId,
        BasedOnRevisionId = source.BasedOnRevisionId,
        EditableRecipe = FromDto(source.EditableRecipe),
        ModifiedAt = source.ModifiedAt
    };

    private static AssetRecipeV2Dto ToDto(AssetRecipe source) => source switch
    {
        TrimRecipe trim => new AssetRecipeV2Dto
        {
            Type = "trim",
            RecipeSchemaVersion = trim.RecipeSchemaVersion,
            Source = ToDto(trim.Source),
            Start = ToV2Dto(trim.Start),
            End = ToV2Dto(trim.End),
            Profile = trim.RenderProfile
        },
        ExtractFrameRecipe frame => new AssetRecipeV2Dto
        {
            Type = "extractFrame",
            RecipeSchemaVersion = frame.RecipeSchemaVersion,
            Source = ToDto(frame.Source),
            AnchorId = frame.Anchor.AnchorId,
            Profile = frame.ImageProfile
        },
        _ => throw new NotSupportedException($"Recipe type '{source.GetType().Name}' is not supported.")
    };

    private static AssetRecipe FromDto(AssetRecipeV2Dto source) => source.Type switch
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
            Anchor = new AnchorRevisionReference
            {
                AnchorId = source.AnchorId ?? Guid.Empty,
                AnchorRevisionId = Guid.Empty
            },
            ImageProfile = source.Profile
        },
        _ => throw new InvalidDataException($"Recipe type '{source.Type}' is not supported.")
    };

    private static RecipeBoundaryV2Dto ToV2Dto(RecipeBoundary source) => new()
    {
        Kind = source.Kind,
        AnchorId = source.Anchor?.AnchorId,
        TimestampSeconds = source.TimestampSeconds
    };

    private static RecipeBoundary? FromDto(RecipeBoundaryV2Dto? source) => source is null ? null : new()
    {
        Kind = source.Kind,
        Anchor = source.AnchorId is { } anchorId
            ? new AnchorRevisionReference { AnchorId = anchorId, AnchorRevisionId = Guid.Empty }
            : null,
        TimestampSeconds = source.TimestampSeconds
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

    private static FrameAnchorV2Dto ToDto(FrameAnchor source) => new()
    {
        Id = source.Id,
        AssetId = Guid.Empty,
        TimestampSeconds = 0,
        Label = source.DisplayLabel,
        Notes = source.Notes
    };

    private static FrameAnchor FromDto(FrameAnchorV2Dto source) => new()
    {
        Id = source.Id,
        DisplayLabel = source.Label,
        Notes = source.Notes
    };

    private static GenerationDraftV2Dto? ToDto(GenerationDraft? source) => source is null ? null : new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Prompt = source.Prompt,
        Mode = source.Mode,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = source.References.Select(reference => new GenerationReferenceDraftV2Dto
        {
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
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

    private static GenerationDraft? FromDto(GenerationDraftV2Dto? source) => source is null ? null : new()
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
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
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

    private static GenerationRecordV2Dto ToDto(GenerationRecord source) => new()
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

    private static GenerationRecord FromDto(GenerationRecordV2Dto source) => new()
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

    private static GenerationRequestSnapshotV2Dto ToDto(GenerationRequestSnapshot source) => new()
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

    private static GenerationRequestSnapshot FromDto(GenerationRequestSnapshotV2Dto source) => new()
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

    private static GenerationReferenceSnapshotV2Dto ToDto(GenerationReferenceSnapshot source) => new()
    {
        ObjectKind = source.ObjectKind,
        LogicalObjectId = source.LogicalObjectId,
        RecipeRevisionId = source.RecipeRevisionId,
        ContentHash = source.ContentHash,
        Role = source.Role,
        Order = source.Order,
        Label = source.Label,
        Notes = source.Notes,
        Materialization = ToDto(source.Materialization)
    };

    private static GenerationReferenceSnapshot FromDto(GenerationReferenceSnapshotV2Dto source) => new()
    {
        ObjectKind = source.ObjectKind,
        LogicalObjectId = source.LogicalObjectId,
        RecipeRevisionId = source.RecipeRevisionId,
        ContentHash = source.ContentHash,
        Role = source.Role,
        Order = source.Order,
        Label = source.Label,
        Notes = source.Notes,
        Materialization = FromDto(source.Materialization)
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
        new ReadOnlyDictionary<string, string>(Copy(source));
}
