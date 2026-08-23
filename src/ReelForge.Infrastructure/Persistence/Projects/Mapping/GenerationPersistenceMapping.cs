using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal static partial class ProjectPersistenceMapper
{
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
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
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
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
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
}
