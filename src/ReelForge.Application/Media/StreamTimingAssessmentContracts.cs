using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Assesses one already-selected source stream for timeline timing. The path is transient
/// operational input; returned Core values are portable project meaning.
/// </summary>
public interface IStreamTimingAssessmentService
{
    Task<StreamTimingAssessmentResult> AssessAsync(
        StreamTimingAssessmentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StreamTimingAssessmentRequest
{
    public StreamTimingAssessmentRequest(
        string mediaPath,
        ContentIdentity contentIdentity,
        MediaType mediaType,
        MediaEncodingMetadata encoding,
        StreamTimingAssessment? priorAssessment = null)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            throw new ArgumentException("A transient media path is required.", nameof(mediaPath));
        if (contentIdentity is null ||
            contentIdentity.Status != ContentHashStatus.Verified ||
            !string.Equals(contentIdentity.Algorithm, ContentIdentity.Sha256Algorithm, StringComparison.OrdinalIgnoreCase) ||
            contentIdentity.Sha256 is not { } hash ||
            hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new ArgumentException("A verified SHA-256 content identity is required.", nameof(contentIdentity));
        if (mediaType is not MediaType.Video and not MediaType.Audio)
            throw new ArgumentOutOfRangeException(nameof(mediaType), "Timing assessment applies only to video or audio streams.");

        MediaPath = mediaPath;
        SourceContentHash = contentIdentity.Sha256.ToUpperInvariant();
        MediaType = mediaType;
        ArgumentNullException.ThrowIfNull(encoding);
        SelectedStream = mediaType == MediaType.Video
            ? StreamTimingDescriptor.From(encoding.Video)
            : StreamTimingDescriptor.From(encoding.Audio);
        if (priorAssessment is not null &&
            (!string.Equals(priorAssessment.SourceContentHash, SourceContentHash, StringComparison.OrdinalIgnoreCase) ||
             priorAssessment.MediaType != MediaType || priorAssessment.SelectedStreamIndex != SelectedStream.StreamIndex))
            throw new ArgumentException("The prior assessment must match the selected verified stream.", nameof(priorAssessment));
        PriorAssessment = priorAssessment;
    }

    public string MediaPath { get; }
    public string SourceContentHash { get; }
    public MediaType MediaType { get; }
    public StreamTimingDescriptor SelectedStream { get; }
    public StreamTimingAssessment? PriorAssessment { get; }
}

/// <summary>Immutable primitive snapshot of the already selected stream used by a timing assessment.</summary>
public sealed record StreamTimingDescriptor(
    int? StreamIndex,
    int? TimeBaseNumerator,
    int? TimeBaseDenominator,
    int? SampleRate,
    long? DurationPresentationTimestamp)
{
    public static StreamTimingDescriptor From(VideoStreamMetadata? video) => new(
        video?.StreamIndex, video?.TimeBaseNumerator, video?.TimeBaseDenominator, null,
        video?.DurationPresentationTimestamp);

    public static StreamTimingDescriptor From(AudioStreamMetadata? audio) => new(
        audio?.StreamIndex, audio?.TimeBaseNumerator, audio?.TimeBaseDenominator, audio?.SampleRate,
        audio?.DurationPresentationTimestamp);
}

/// <summary>One full-span assessment and exact source range when, and only when, it is established.</summary>
public sealed class StreamTimingAssessmentResult
{
    public StreamTimingAssessmentResult(
        StreamTimingAssessment assessment,
        VideoSourceRange? videoFullRange = null,
        AudioSourceRange? audioFullRange = null)
    {
        Assessment = assessment ?? throw new ArgumentNullException(nameof(assessment));
        if (videoFullRange is not null && audioFullRange is not null)
            throw new ArgumentException("A timing assessment result cannot contain both video and audio ranges.");
        if (Assessment.MediaType == MediaType.Video && audioFullRange is not null ||
            Assessment.MediaType == MediaType.Audio && videoFullRange is not null)
            throw new ArgumentException("The exact range must match the assessed media type.");
        if (Assessment.Readiness == TimingReadiness.Exact &&
            (Assessment.MediaType == MediaType.Video ? videoFullRange is null : audioFullRange is null))
            throw new ArgumentException("An exact assessment requires its matching exact full range.");
        if (Assessment.Readiness != TimingReadiness.Exact && (videoFullRange is not null || audioFullRange is not null))
            throw new ArgumentException("Estimated and unusable assessments cannot expose an exact full range.");

        var rangeDuration = videoFullRange?.Duration ?? audioFullRange?.Duration;
        if (rangeDuration is not null && rangeDuration != Assessment.TimelineDuration)
            throw new ArgumentException("The exact full range must match the assessed timeline duration.");
        if (videoFullRange is not null)
        {
            ExactTime videoStart;
            try
            {
                videoStart = videoFullRange.Start.ToExactTime();
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException("The exact video range start is outside the supported project time domain.", nameof(videoFullRange), exception);
            }
            if (videoStart != Assessment.SourcePresentationStart)
                throw new ArgumentException("The exact video range start must match the assessed source presentation start.", nameof(videoFullRange));
        }

        VideoFullRange = videoFullRange;
        AudioFullRange = audioFullRange;
    }

    public StreamTimingAssessment Assessment { get; }
    public VideoSourceRange? VideoFullRange { get; }
    public AudioSourceRange? AudioFullRange { get; }
}
