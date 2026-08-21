using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal static partial class ProjectPersistenceMapper
{
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
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
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
        SourceRecipeRevisionId = source.SourceRecipeRevisionId,
        SourceContentHash = source.SourceContentHash,
        VideoStreamIndex = source.VideoStreamIndex,
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator,
        FrameNumber = source.FrameNumber,
        CreatedAt = source.CreatedAt
    };
}
