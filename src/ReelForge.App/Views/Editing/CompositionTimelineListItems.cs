using System.Globalization;
using ReelForge.Core;

namespace ReelForge.App.Views.Editing;

/// <summary>
/// Presentation-only projection of a stable video timeline occurrence. The
/// legacy "segment" name is retained because it is part of the current WPF
/// control contract; it does not represent persisted composition meaning.
/// </summary>
public sealed class CompositionSegmentListItem
{
    public CompositionSegmentListItem(int index, Guid trackId, CompositionVideoItem item, ProjectAsset? source, bool audioEnabled)
    {
        Index = index;
        TrackId = trackId;
        SegmentId = item.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing source";
        DetailText = source is null
            ? $"Source {item.Source.AssetId:N} is unavailable"
            : source.StorageKind == AssetStorageKind.Virtual
                ? $"Saved Clip • {(item.SourceRange is not null ? "exact range • " : string.Empty)}pinned recipe " +
                  (item.Source.RecipeRevisionId?.ToString("N") ?? "missing")
                : $"Physical video • {(item.SourceRange is not null ? "exact range" : "estimated range")} • {item.TimingAssessment.Readiness}";
        AudioText = audioEnabled ? "Linked audio on" : "Linked audio unavailable or muted";
        AudioEnabled = audioEnabled;
        TimelineStart = item.CompositionStart.ToDoubleSeconds();
        DurationSeconds = item.TimingAssessment.TimelineDuration.ToDoubleSeconds();
        DurationText = DurationSeconds > 0 ? FormatDuration(DurationSeconds) : "Duration unavailable";
        IsTimingDegraded = item.TimingAssessment.IsDegraded;
        TimingWarningToolTip = TimingWarningPresentation.FormatOccurrenceTooltip(item.TimingAssessment);
        TimingWarningDetail = IsTimingDegraded
            ? TimingWarningPresentation.FormatPinDetail(item.TimingAssessment)
            : null;
    }

    public int Index { get; }
    public Guid TrackId { get; }
    public Guid SegmentId { get; }
    public string PositionText => $"{Index + 1}.";
    public string DisplayName { get; }
    public string DetailText { get; }
    public string AudioText { get; }
    public bool AudioEnabled { get; }
    public double TimelineStart { get; }
    public double DurationSeconds { get; }
    public string DurationText { get; }
    public bool IsTimingDegraded { get; }
    public string? TimingWarningToolTip { get; }
    public string? TimingWarningDetail { get; }

    internal static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        if (seconds < 10 || Math.Abs(seconds - Math.Round(seconds)) > 0.000_5)
            return time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}

public sealed class CompositionAudioClipListItem
{
    public CompositionAudioClipListItem(Guid trackId, CompositionAudioItem item, ProjectAsset? source)
    {
        TrackId = trackId;
        AudioClipId = item.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing audio source";
        TimelineStart = TimeSpan.FromSeconds(item.CompositionStart.ToDoubleSeconds());
        IsMuted = item.IsMuted;
        GainDecibels = item.GainDecibels;
        Pan = item.Pan;
        FadeIn = TimeSpan.FromSeconds(item.FadeIn.ToDoubleSeconds());
        FadeOut = TimeSpan.FromSeconds(item.FadeOut.ToDoubleSeconds());
        MixText = (IsMuted
            ? "Muted"
            : $"Gain {(GainDecibels > 0 ? "+" : string.Empty)}{GainDecibels:0} dB") +
            (Math.Abs(Pan) > 0.000_001
                ? $" • {Math.Round(Math.Abs(Pan) * 100):0}% {(Pan < 0 ? "left" : "right")}"
                : string.Empty) +
            (FadeIn > TimeSpan.Zero || FadeOut > TimeSpan.Zero
                ? $" • Fade {FadeIn.TotalSeconds:0.###}s in / {FadeOut.TotalSeconds:0.###}s out"
                : string.Empty);
        DurationSeconds = item.TimingAssessment.TimelineDuration.ToDoubleSeconds();
        DurationText = DurationSeconds > 0 ? CompositionSegmentListItem.FormatDuration(DurationSeconds) : "Duration unavailable";
        IsTimingDegraded = item.TimingAssessment.IsDegraded;
        TimingWarningToolTip = TimingWarningPresentation.FormatOccurrenceTooltip(item.TimingAssessment);
        TimingWarningDetail = IsTimingDegraded
            ? TimingWarningPresentation.FormatPinDetail(item.TimingAssessment)
            : null;
    }

    public Guid TrackId { get; }
    public Guid AudioClipId { get; }
    public string DisplayName { get; }
    public TimeSpan TimelineStart { get; }
    public bool IsMuted { get; }
    public double GainDecibels { get; }
    public double Pan { get; }
    public TimeSpan FadeIn { get; }
    public TimeSpan FadeOut { get; }
    public string MixText { get; }
    public double DurationSeconds { get; }
    public string DurationText { get; }
    public bool IsTimingDegraded { get; }
    public string? TimingWarningToolTip { get; }
    public string? TimingWarningDetail { get; }
}

/// <summary>
/// Maps portable timing evidence to concise shell text. This deliberately
/// contains no engine, container, or operating-system terminology.
/// </summary>
internal static class TimingWarningPresentation
{
    public static string FormatAssetTooltip(ProjectAsset asset)
    {
        var assessments = asset.TimingAssessments
            .Where(assessment => assessment.Readiness is TimingReadiness.Estimated or TimingReadiness.Unusable)
            .OrderBy(assessment => assessment.MediaType)
            .ToArray();
        return string.Join("\n", assessments.Select(FormatAssessmentTooltip));
    }

    public static string? FormatOccurrenceTooltip(StreamTimingAssessmentPin pin) =>
        pin.IsDegraded ? FormatPinTooltip(pin) : null;

    public static string FormatAssessmentDetail(StreamTimingAssessment assessment) =>
        FormatDetail(
            assessment.MediaType,
            assessment.Readiness,
            assessment.SelectedStreamIndex,
            assessment.AssessmentId,
            assessment.SchemaIdentity,
            assessment.TimelineDuration,
            assessment.IssueClassifications);

    public static string FormatPinDetail(StreamTimingAssessmentPin pin) =>
        FormatDetail(
            pin.MediaType,
            pin.Readiness,
            pin.SelectedStreamIndex,
            pin.AssessmentId,
            pin.SchemaIdentity,
            pin.TimelineDuration,
            pin.IssueClassifications);

    public static string Guidance(TimingReadiness readiness) => readiness switch
    {
        TimingReadiness.Estimated => "Timing is estimated. Precise editing may require repair or replacement.",
        TimingReadiness.Unusable => "This stream cannot be placed until the media is repaired or replaced.",
        _ => string.Empty
    };

    public static string FormatIssue(TimingIssueClassification issue) => issue switch
    {
        TimingIssueClassification.AnalysisCapabilityUnavailable => "timing analysis unavailable",
        TimingIssueClassification.NativePresentationTimestampUnavailable => "presentation timestamps unavailable",
        TimingIssueClassification.NativeStartUnavailable => "source start unavailable",
        TimingIssueClassification.NativeDurationUnavailable => "source duration unavailable",
        TimingIssueClassification.TerminalBoundaryUnavailable => "ending boundary unavailable",
        TimingIssueClassification.NonmonotonicTimestamps => "timestamps are out of order",
        TimingIssueClassification.DiscontinuousTimestamps => "timestamps are discontinuous",
        TimingIssueClassification.UnresolvedVideoFrameDuration => "frame duration unresolved",
        TimingIssueClassification.UnresolvedAudioSampleBoundary => "audio sample boundary unresolved",
        TimingIssueClassification.UnresolvedAudioPrimingOrPadding => "audio padding unresolved",
        TimingIssueClassification.SequentialDecodeUnavailable => "sequential playback unavailable",
        TimingIssueClassification.NoUsableStream => "no usable stream",
        TimingIssueClassification.FiniteSpanUnavailable => "finite duration unavailable",
        TimingIssueClassification.SourcePresentationStartUnrepresentable => "source start cannot be represented",
        TimingIssueClassification.ProtectedMedia => "protected media",
        TimingIssueClassification.CorruptMedia => "corrupt media",
        TimingIssueClassification.UnsupportedMedia => "unsupported media",
        _ => issue.ToString()
    };

    private static string FormatAssessmentTooltip(StreamTimingAssessment assessment) =>
        $"{assessment.MediaType} timing is {assessment.Readiness}: {Guidance(assessment.Readiness)}";

    private static string FormatPinTooltip(StreamTimingAssessmentPin pin) =>
        $"{pin.MediaType} timing is {pin.Readiness}: {Guidance(pin.Readiness)}";

    private static string FormatDetail(
        MediaType mediaType,
        TimingReadiness readiness,
        int? selectedStreamIndex,
        Guid assessmentId,
        string schemaIdentity,
        ExactTime? timelineDuration,
        IReadOnlyList<TimingIssueClassification> issues)
    {
        var details = new List<string>
        {
            $"{mediaType} timing: {readiness}",
            $"Selected stream: {(selectedStreamIndex is null ? "unavailable" : selectedStreamIndex.Value.ToString(CultureInfo.InvariantCulture))}",
            $"Assessment: {assessmentId}",
            $"Schema: {schemaIdentity}"
        };
        if (timelineDuration is { } duration)
            details.Add($"Assessed duration: {CompositionSegmentListItem.FormatDuration(duration.ToDoubleSeconds())}");
        if (issues.Count > 0)
            details.Add($"Issues: {string.Join(", ", issues.Select(FormatIssue))}");
        var guidance = Guidance(readiness);
        if (!string.IsNullOrEmpty(guidance)) details.Add(guidance);
        return string.Join("\n", details);
    }
}
