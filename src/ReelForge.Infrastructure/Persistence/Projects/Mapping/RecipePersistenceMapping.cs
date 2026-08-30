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
            CompositionState = new WorkingCompositionStateDto
            {
                VideoTracks = composition.Composition.VideoTracks.Select(track => new CompositionVideoTrackDto
                {
                    Id = track.Id,
                    Name = track.Name,
                    IsLocked = track.IsLocked,
                    IsVisible = track.IsVisible,
                    Items = track.Items.Select(item => new CompositionVideoItemDto
                    {
                        Id = item.Id,
                        Source = ToDto(item.Source),
                        SelectedStreamIndex = item.SelectedStreamIndex,
                        SourceRange = ToDto(item.SourceRange),
                        TimingAssessment = ToDto(item.TimingAssessment),
                        CompositionStart = ToDto(item.CompositionStart)!,
                        LinkGroupId = item.LinkGroupId
                    }).ToList()
                }).ToList(),
                AudioTracks = composition.Composition.AudioTracks.Select(track => new CompositionAudioTrackDto
                {
                    Id = track.Id,
                    Name = track.Name,
                    IsLocked = track.IsLocked,
                    IsMuted = track.IsMuted,
                    Items = track.Items.Select(item => new CompositionAudioItemDto
                    {
                        Id = item.Id,
                        Source = ToDto(item.Source),
                        SelectedStreamIndex = item.SelectedStreamIndex,
                        SourceRange = ToDto(item.SourceRange),
                        TimingAssessment = ToDto(item.TimingAssessment),
                        CompositionStart = ToDto(item.CompositionStart)!,
                        LinkGroupId = item.LinkGroupId,
                        IsMuted = item.IsMuted,
                        GainDecibels = item.GainDecibels,
                        Pan = item.Pan,
                        FadeIn = ToDto(item.FadeIn)!,
                        FadeOut = ToDto(item.FadeOut)!
                    }).ToList()
                }).ToList()
            }
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
            Composition = FromDto(source.CompositionState
                ?? throw new InvalidDataException("A composition recipe requires its multitrack state."))
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

    private static VideoSourceRangeDto? ToDto(VideoSourceRange? source) => source is null ? null : new()
    {
        Start = ToDto(source.Start),
        End = ToDto(source.End)
    };

    private static VideoSourceRange? FromDto(VideoSourceRangeDto? source) => source is null ? null : new(
        FromDto(source.Start ?? throw new InvalidDataException("A video range start is required.")),
        FromDto(source.End ?? throw new InvalidDataException("A video range end is required.")));

    private static VideoPresentationTimeDto ToDto(VideoPresentationTime source) => new()
    {
        PresentationTimestamp = source.PresentationTimestamp,
        TimeBaseNumerator = source.TimeBaseNumerator,
        TimeBaseDenominator = source.TimeBaseDenominator
    };

    private static VideoPresentationTime FromDto(VideoPresentationTimeDto source) => new(
        source.PresentationTimestamp,
        source.TimeBaseNumerator,
        source.TimeBaseDenominator);

    private static AudioSourceRangeDto? ToDto(AudioSourceRange? source) => source is null ? null : new()
    {
        Start = ToDto(source.Start),
        End = ToDto(source.End)
    };

    private static AudioSourceRange? FromDto(AudioSourceRangeDto? source) => source is null ? null : new(
        FromDto(source.Start ?? throw new InvalidDataException("An audio range start is required.")),
        FromDto(source.End ?? throw new InvalidDataException("An audio range end is required.")));

    private static AudioSampleTimeDto ToDto(AudioSampleTime source) => new()
    {
        SampleFrameOffset = source.SampleFrameOffset,
        SampleRate = source.SampleRate
    };

    private static AudioSampleTime FromDto(AudioSampleTimeDto source) => new(source.SampleFrameOffset, source.SampleRate);

    private static WorkingCompositionState FromDto(WorkingCompositionStateDto source)
    {
        try
        {
            return new WorkingCompositionState(
                (source.VideoTracks ?? throw new InvalidDataException("Composition video tracks are required.")).Select((track, index) => new CompositionVideoTrack(
                    track.Id,
                    track.IsLocked,
                    track.IsVisible,
                    (track.Items ?? throw new InvalidDataException("Composition video items are required.")).Select(item => new CompositionVideoItem(
                        item.Id,
                        FromDto(item.Source ?? throw new InvalidDataException("A video item source is required.")),
                        item.SelectedStreamIndex,
                        FromDto(item.SourceRange),
                        FromDto(item.TimingAssessment ?? throw new InvalidDataException("A video timing pin is required.")),
                        FromDto(item.CompositionStart) ?? throw new InvalidDataException("A video composition start is required."),
                        item.LinkGroupId)),
                    track.Name ?? $"Video {index + 1}")),
                (source.AudioTracks ?? throw new InvalidDataException("Composition audio tracks are required.")).Select((track, index) => new CompositionAudioTrack(
                    track.Id,
                    track.IsLocked,
                    track.IsMuted,
                    (track.Items ?? throw new InvalidDataException("Composition audio items are required.")).Select(item => new CompositionAudioItem(
                        item.Id,
                        FromDto(item.Source ?? throw new InvalidDataException("An audio item source is required.")),
                        item.SelectedStreamIndex,
                        FromDto(item.SourceRange),
                        FromDto(item.TimingAssessment ?? throw new InvalidDataException("An audio timing pin is required.")),
                        FromDto(item.CompositionStart) ?? throw new InvalidDataException("An audio composition start is required."),
                        item.LinkGroupId,
                        item.IsMuted,
                        item.GainDecibels,
                        item.Pan,
                        FromDto(item.FadeIn) ?? throw new InvalidDataException("An audio fade-in is required."),
                        FromDto(item.FadeOut) ?? throw new InvalidDataException("An audio fade-out is required."))),
                    track.Name ?? $"Audio {index + 1}")));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The Working Composition payload is invalid.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The Working Composition exact-time payload is invalid.", exception);
        }
    }

    private static StreamTimingAssessmentPinDto ToDto(StreamTimingAssessmentPin source) => new()
    {
        SchemaIdentity = source.SchemaIdentity,
        AssessmentId = source.AssessmentId,
        SourceContentHash = source.SourceContentHash,
        MediaType = source.MediaType,
        SelectedStreamIndex = source.SelectedStreamIndex,
        Readiness = source.Readiness,
        HasUsableSequentialDecodePath = source.HasUsableSequentialDecodePath,
        TimelineDuration = ToDto(source.TimelineDuration)!,
        SourcePresentationStart = ToDto(source.SourcePresentationStart),
        IssueClassifications = source.IssueClassifications.ToList()
    };

    private static StreamTimingAssessmentPin FromDto(StreamTimingAssessmentPinDto source) => new(new StreamTimingAssessment(
        source.AssessmentId,
        source.SchemaIdentity,
        source.SourceContentHash,
        source.MediaType,
        source.SelectedStreamIndex,
        source.Readiness,
        source.HasUsableSequentialDecodePath,
        FromDto(source.TimelineDuration) ?? throw new InvalidDataException("A timing-pin duration is required."),
        source.IssueClassifications ?? throw new InvalidDataException("Timing-pin issues are required."),
        FromDto(source.SourcePresentationStart)));
}
