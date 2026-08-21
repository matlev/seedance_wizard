using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal static partial class ProjectPersistenceMapper
{
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
}
