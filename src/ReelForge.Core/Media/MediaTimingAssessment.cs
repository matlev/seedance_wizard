using System.Collections.ObjectModel;

namespace ReelForge.Core;

/// <summary>Whether the selected stream has sufficient evidence for timeline use.</summary>
public enum TimingReadiness
{
    Exact,
    Estimated,
    Unusable
}

/// <summary>
/// Engine-neutral reasons why source timing cannot be treated as exact. Values are durable project meaning.
/// </summary>
public enum TimingIssueClassification
{
    NativeStartUnavailable,
    NativeDurationUnavailable,
    TerminalBoundaryUnavailable,
    NonmonotonicTimestamps,
    DiscontinuousTimestamps,
    UnresolvedVideoFrameDuration,
    UnresolvedAudioSampleBoundary,
    UnresolvedAudioPrimingOrPadding,
    SequentialDecodeUnavailable,
    NoUsableStream,
    FiniteSpanUnavailable,
    ProtectedMedia,
    CorruptMedia,
    UnsupportedMedia
}

/// <summary>
/// Immutable, versioned timing evidence for one selected source stream. It is not itself a timeline occurrence.
/// </summary>
public sealed class StreamTimingAssessment
{
    public const string CurrentSchemaIdentity = "reelforge.stream-timing-assessment.v1";

    // Once a schema identity has been persisted in a supported project format, retain it here so
    // frozen historical occurrence pins remain representable. Unknown schemas are rejected rather
    // than interpreted as current meaning; DTO migration owns any deliberate schema conversion.
    private static readonly HashSet<string> SupportedSchemaIdentities = new(StringComparer.Ordinal)
    {
        CurrentSchemaIdentity
    };

    private static readonly HashSet<TimingIssueClassification> PlacementFatalIssues =
        new()
        {
            TimingIssueClassification.SequentialDecodeUnavailable,
            TimingIssueClassification.NoUsableStream,
            TimingIssueClassification.FiniteSpanUnavailable,
            TimingIssueClassification.ProtectedMedia,
            TimingIssueClassification.CorruptMedia,
            TimingIssueClassification.UnsupportedMedia
        };

    public StreamTimingAssessment(
        Guid assessmentId,
        string sourceContentHash,
        MediaType mediaType,
        int? selectedStreamIndex,
        TimingReadiness readiness,
        bool hasUsableSequentialDecodePath,
        ExactTime? timelineDuration,
        IEnumerable<TimingIssueClassification> issueClassifications)
        : this(
            assessmentId,
            CurrentSchemaIdentity,
            sourceContentHash,
            mediaType,
            selectedStreamIndex,
            readiness,
            hasUsableSequentialDecodePath,
            timelineDuration,
            issueClassifications)
    {
    }

    public StreamTimingAssessment(
        Guid assessmentId,
        string schemaIdentity,
        string sourceContentHash,
        MediaType mediaType,
        int? selectedStreamIndex,
        TimingReadiness readiness,
        bool hasUsableSequentialDecodePath,
        ExactTime? timelineDuration,
        IEnumerable<TimingIssueClassification> issueClassifications)
    {
        if (assessmentId == Guid.Empty)
            throw new ArgumentException("A stable assessment identifier is required.", nameof(assessmentId));

        AssessmentId = assessmentId;
        SchemaIdentity = RequireSchemaIdentity(schemaIdentity, nameof(schemaIdentity));
        SourceContentHash = NormalizeSha256(sourceContentHash, nameof(sourceContentHash));
        MediaType = RequireMediaType(mediaType, nameof(mediaType));
        SelectedStreamIndex = RequireOptionalStreamIndex(selectedStreamIndex, nameof(selectedStreamIndex));
        Readiness = RequireReadiness(readiness, nameof(readiness));
        HasUsableSequentialDecodePath = hasUsableSequentialDecodePath;
        TimelineDuration = RequireOptionalPositiveDuration(timelineDuration, nameof(timelineDuration));
        IssueClassifications = CopyIssues(issueClassifications, nameof(issueClassifications));

        ValidateReadiness();
    }

    public Guid AssessmentId { get; }
    public string SchemaIdentity { get; }
    public string SourceContentHash { get; }
    public MediaType MediaType { get; }
    public int? SelectedStreamIndex { get; }
    public TimingReadiness Readiness { get; }
    public bool HasUsableSequentialDecodePath { get; }
    public ExactTime? TimelineDuration { get; }
    public IReadOnlyList<TimingIssueClassification> IssueClassifications { get; }
    public bool CanPlace => Readiness is TimingReadiness.Exact or TimingReadiness.Estimated;
    public bool IsDegraded => Readiness == TimingReadiness.Estimated;

    public StreamTimingAssessmentPin CreatePlacementPin() => new(this);

    private void ValidateReadiness()
    {
        var hasPlacementFatalIssue = IssueClassifications.Any(PlacementFatalIssues.Contains);

        if (Readiness is TimingReadiness.Exact or TimingReadiness.Estimated)
        {
            if (SelectedStreamIndex is null || !HasUsableSequentialDecodePath || TimelineDuration is null)
                throw new ArgumentException("Exact and Estimated timing assessments require a selected stream, sequential decode path, and positive timeline duration.");

            if (hasPlacementFatalIssue)
                throw new ArgumentException("Exact and Estimated timing assessments cannot carry an issue that makes timeline placement unusable.", nameof(IssueClassifications));
        }

        if (Readiness == TimingReadiness.Exact && IssueClassifications.Count != 0)
            throw new ArgumentException("Exact timing assessments cannot carry timing issues.", nameof(IssueClassifications));

        if (Readiness == TimingReadiness.Estimated && IssueClassifications.Count == 0)
            throw new ArgumentException("Estimated timing assessments require one or more non-fatal issue classifications.", nameof(IssueClassifications));

        if (Readiness == TimingReadiness.Unusable && !hasPlacementFatalIssue)
            throw new ArgumentException("Unusable timing assessments require an issue that specifically prevents timeline placement.", nameof(IssueClassifications));
    }

    internal static string NormalizeSha256(string value, string parameterName)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A verified SHA-256 hash is required.", parameterName);
        return value.ToUpperInvariant();
    }

    internal static string RequireSchemaIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !SupportedSchemaIdentities.Contains(value))
            throw new ArgumentException("A supported timing assessment schema identity is required.", parameterName);
        return value;
    }

    internal static MediaType RequireMediaType(MediaType mediaType, string parameterName)
    {
        if (mediaType is not MediaType.Video and not MediaType.Audio)
            throw new ArgumentOutOfRangeException(parameterName, "Timing assessments apply only to video or audio streams.");
        return mediaType;
    }

    internal static int? RequireOptionalStreamIndex(int? selectedStreamIndex, string parameterName)
    {
        if (selectedStreamIndex < 0)
            throw new ArgumentOutOfRangeException(parameterName, "Selected stream indices cannot be negative.");
        return selectedStreamIndex;
    }

    internal static ExactTime? RequireOptionalPositiveDuration(ExactTime? duration, string parameterName)
    {
        if (duration is not null && duration <= new ExactTime(0, 1))
            throw new ArgumentOutOfRangeException(parameterName, "Timeline duration must be positive.");
        return duration;
    }

    private static TimingReadiness RequireReadiness(TimingReadiness readiness, string parameterName)
    {
        if (!Enum.IsDefined(readiness))
            throw new ArgumentOutOfRangeException(parameterName);
        return readiness;
    }

    private static ReadOnlyCollection<TimingIssueClassification> CopyIssues(
        IEnumerable<TimingIssueClassification> issues,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(issues, parameterName);
        var copy = issues.ToArray();
        if (copy.Any(issue => !Enum.IsDefined(issue)))
            throw new ArgumentOutOfRangeException(parameterName, "Unknown timing issue classification.");
        if (copy.Distinct().Count() != copy.Length)
            throw new ArgumentException("Timing issue classifications must be distinct.", parameterName);
        return Array.AsReadOnly(copy);
    }
}

/// <summary>Project-specific acknowledgement of an unchanged timing assessment.</summary>
public sealed class TimingAssessmentAcknowledgement
{
    public TimingAssessmentAcknowledgement(Guid assessmentId, DateTimeOffset acknowledgedAt)
    {
        if (assessmentId == Guid.Empty)
            throw new ArgumentException("An assessment identifier is required.", nameof(assessmentId));
        AssessmentId = assessmentId;
        AcknowledgedAt = acknowledgedAt;
    }

    public Guid AssessmentId { get; }
    public DateTimeOffset AcknowledgedAt { get; }
}

/// <summary>
/// Immutable occurrence snapshot of the timing evidence accepted at placement. Later source reassessment cannot change it.
/// </summary>
public sealed class StreamTimingAssessmentPin
{
    public StreamTimingAssessmentPin(StreamTimingAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!assessment.CanPlace)
            throw new ArgumentException("Unusable timing assessments cannot be pinned to a timeline occurrence.", nameof(assessment));

        SchemaIdentity = assessment.SchemaIdentity;
        AssessmentId = assessment.AssessmentId;
        SourceContentHash = assessment.SourceContentHash;
        MediaType = assessment.MediaType;
        SelectedStreamIndex = assessment.SelectedStreamIndex!.Value;
        Readiness = assessment.Readiness;
        HasUsableSequentialDecodePath = assessment.HasUsableSequentialDecodePath;
        TimelineDuration = assessment.TimelineDuration!;
        IssueClassifications = Array.AsReadOnly(assessment.IssueClassifications.ToArray());
    }

    public string SchemaIdentity { get; }
    public Guid AssessmentId { get; }
    public string SourceContentHash { get; }
    public MediaType MediaType { get; }
    public int SelectedStreamIndex { get; }
    public TimingReadiness Readiness { get; }
    public bool HasUsableSequentialDecodePath { get; }
    public ExactTime TimelineDuration { get; }
    public IReadOnlyList<TimingIssueClassification> IssueClassifications { get; }
    public bool IsDegraded => Readiness == TimingReadiness.Estimated;
}
