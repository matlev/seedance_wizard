using ReelForge.Core;

namespace ReelForge.Infrastructure;

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
    public WorkingCompositionStateDto? CompositionState { get; set; }
    public string? Profile { get; set; }
}

/// <summary>Portable, ordered composition meaning. Tracks are retained even when empty.</summary>
internal sealed class WorkingCompositionStateDto
{
    public List<CompositionVideoTrackDto> VideoTracks { get; set; } = [];
    public List<CompositionAudioTrackDto> AudioTracks { get; set; } = [];
}

internal sealed class CompositionVideoTrackDto
{
    public Guid Id { get; set; }
    public bool IsLocked { get; set; }
    public bool IsVisible { get; set; }
    public List<CompositionVideoItemDto> Items { get; set; } = [];
}

internal sealed class CompositionAudioTrackDto
{
    public Guid Id { get; set; }
    public bool IsLocked { get; set; }
    public bool IsMuted { get; set; }
    public List<CompositionAudioItemDto> Items { get; set; } = [];
}

internal sealed class CompositionVideoItemDto
{
    public Guid Id { get; set; }
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public int SelectedStreamIndex { get; set; }
    public VideoSourceRangeDto? SourceRange { get; set; }
    public StreamTimingAssessmentPinDto TimingAssessment { get; set; } = new();
    public ExactTimeDto CompositionStart { get; set; } = new();
    public Guid? LinkGroupId { get; set; }
}

internal sealed class CompositionAudioItemDto
{
    public Guid Id { get; set; }
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public int SelectedStreamIndex { get; set; }
    public AudioSourceRangeDto? SourceRange { get; set; }
    public StreamTimingAssessmentPinDto TimingAssessment { get; set; } = new();
    public ExactTimeDto CompositionStart { get; set; } = new();
    public Guid? LinkGroupId { get; set; }
    public bool IsMuted { get; set; }
    public double GainDecibels { get; set; }
    public double Pan { get; set; }
    public ExactTimeDto FadeIn { get; set; } = new();
    public ExactTimeDto FadeOut { get; set; } = new();
}

internal sealed class VideoSourceRangeDto
{
    public VideoPresentationTimeDto Start { get; set; } = new();
    public VideoPresentationTimeDto End { get; set; } = new();
}

internal sealed class VideoPresentationTimeDto
{
    public long PresentationTimestamp { get; set; }
    public int TimeBaseNumerator { get; set; }
    public int TimeBaseDenominator { get; set; }
}

internal sealed class AudioSourceRangeDto
{
    public AudioSampleTimeDto Start { get; set; } = new();
    public AudioSampleTimeDto End { get; set; } = new();
}

internal sealed class AudioSampleTimeDto
{
    public long SampleFrameOffset { get; set; }
    public int SampleRate { get; set; }
}

/// <summary>Frozen placement evidence. It remains independent of later source reassessment.</summary>
internal sealed class StreamTimingAssessmentPinDto
{
    public string SchemaIdentity { get; set; } = string.Empty;
    public Guid AssessmentId { get; set; }
    public string SourceContentHash { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public int SelectedStreamIndex { get; set; }
    public TimingReadiness Readiness { get; set; }
    public bool HasUsableSequentialDecodePath { get; set; }
    public ExactTimeDto TimelineDuration { get; set; } = new();
    public ExactTimeDto? SourcePresentationStart { get; set; }
    public List<TimingIssueClassification> IssueClassifications { get; set; } = [];
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
