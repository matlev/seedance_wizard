using ReelForge.Core;

namespace ReelForge.Core.Tests;

public sealed class MediaTimingAssessmentTests
{
    [Fact]
    public void ExactAssessmentIsPlaceableAndNormalizesVerifiedHash()
    {
        var assessment = Assessment(TimingReadiness.Exact, issues: [], hash: new string('a', 64));

        Assert.True(assessment.CanPlace);
        Assert.False(assessment.IsDegraded);
        Assert.Equal(new string('A', 64), assessment.SourceContentHash);
        Assert.Equal(StreamTimingAssessment.CurrentSchemaIdentity, assessment.SchemaIdentity);
    }

    [Fact]
    public void EstimatedAssessmentRequiresAndRetainsSpecificIssues()
    {
        var input = new List<TimingIssueClassification> { TimingIssueClassification.TerminalBoundaryUnavailable };
        var assessment = Assessment(TimingReadiness.Estimated, issues: input);
        input.Clear();

        Assert.True(assessment.CanPlace);
        Assert.True(assessment.IsDegraded);
        Assert.Equal([TimingIssueClassification.TerminalBoundaryUnavailable], assessment.IssueClassifications);
        Assert.Throws<NotSupportedException>(() => ((IList<TimingIssueClassification>)assessment.IssueClassifications).Add(TimingIssueClassification.CorruptMedia));
    }

    [Fact]
    public void UnusableAssessmentMayLackPlacementEvidenceButRequiresIssue()
    {
        var assessment = new StreamTimingAssessment(
            Guid.NewGuid(), Hash(), MediaType.Video, null, TimingReadiness.Unusable, false, null,
            [TimingIssueClassification.NoUsableStream]);

        Assert.False(assessment.CanPlace);
        Assert.False(assessment.IsDegraded);
        Assert.Null(assessment.SelectedStreamIndex);
        Assert.Null(assessment.TimelineDuration);
    }

    [Fact]
    public void ReadinessEligibilityAndIssueInvariantsAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Exact, issues: [TimingIssueClassification.NativeDurationUnavailable]));
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Estimated, issues: []));
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Unusable, issues: []));
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Exact, stream: null));
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Estimated, decode: false));
        Assert.Throws<ArgumentException>(() => new StreamTimingAssessment(
            Guid.NewGuid(), Hash(), MediaType.Video, 0, TimingReadiness.Estimated, true, null,
            [TimingIssueClassification.NativeDurationUnavailable]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(TimingReadiness.Unusable, stream: -1, issues: [TimingIssueClassification.CorruptMedia]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(TimingReadiness.Unusable, duration: new ExactTime(0, 1), issues: [TimingIssueClassification.CorruptMedia]));
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Estimated, issues: [TimingIssueClassification.CorruptMedia, TimingIssueClassification.CorruptMedia]));
    }

    [Theory]
    [InlineData(TimingIssueClassification.SequentialDecodeUnavailable)]
    [InlineData(TimingIssueClassification.NoUsableStream)]
    [InlineData(TimingIssueClassification.FiniteSpanUnavailable)]
    [InlineData(TimingIssueClassification.ProtectedMedia)]
    [InlineData(TimingIssueClassification.CorruptMedia)]
    [InlineData(TimingIssueClassification.UnsupportedMedia)]
    public void PlacementFatalIssuesCanOnlyBeUnusable(TimingIssueClassification issue)
    {
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Estimated, issues: [issue]));

        var unusable = Assessment(TimingReadiness.Unusable, issues: [issue]);

        Assert.False(unusable.CanPlace);
        Assert.Equal([issue], unusable.IssueClassifications);
    }

    [Fact]
    public void UnusableRequiresASpecificPlacementFatalIssue()
    {
        Assert.Throws<ArgumentException>(() => Assessment(
            TimingReadiness.Unusable,
            issues: [TimingIssueClassification.NativeStartUnavailable]));
    }

    [Fact]
    public void AssessmentRejectsInvalidIdentifiersSchemaInputsAndTypes()
    {
        Assert.Throws<ArgumentException>(() => new StreamTimingAssessment(Guid.Empty, Hash(), MediaType.Video, 0, TimingReadiness.Exact, true, new ExactTime(1, 1), []));
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Exact, hash: "not-a-hash"));
        Assert.Throws<ArgumentException>(() => new StreamTimingAssessment(
            Guid.NewGuid(), "reelforge.stream-timing-assessment.v999", Hash(), MediaType.Video, 0,
            TimingReadiness.Exact, true, new ExactTime(1, 1), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(TimingReadiness.Exact, mediaType: MediaType.Image));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment((TimingReadiness)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assessment(TimingReadiness.Estimated, issues: [(TimingIssueClassification)99]));
    }

    [Fact]
    public void AcknowledgementIsBoundToAssessmentIdAndRetainsTimestamp()
    {
        var acknowledgedAt = DateTimeOffset.UtcNow;
        var assessmentId = Guid.NewGuid();
        var acknowledgement = new TimingAssessmentAcknowledgement(assessmentId, acknowledgedAt);

        Assert.Equal(assessmentId, acknowledgement.AssessmentId);
        Assert.Equal(acknowledgedAt, acknowledgement.AcknowledgedAt);
        Assert.Throws<ArgumentException>(() => new TimingAssessmentAcknowledgement(Guid.Empty, acknowledgedAt));
    }

    [Fact]
    public void PinCopiesAssessmentSnapshotAndRejectsUnusableAssessment()
    {
        var assessment = Assessment(TimingReadiness.Estimated, issues: [TimingIssueClassification.UnresolvedVideoFrameDuration]);
        var pin = assessment.CreatePlacementPin();

        Assert.NotSame(assessment.IssueClassifications, pin.IssueClassifications);
        Assert.Equal(assessment.AssessmentId, pin.AssessmentId);
        Assert.Equal(assessment.TimelineDuration, pin.TimelineDuration);
        Assert.True(pin.IsDegraded);
        Assert.Throws<NotSupportedException>(() => ((IList<TimingIssueClassification>)pin.IssueClassifications).Clear());
        Assert.Throws<ArgumentException>(() => Assessment(TimingReadiness.Unusable, issues: [TimingIssueClassification.CorruptMedia]).CreatePlacementPin());
    }

    [Fact]
    public void ItemsRequireMatchingPinsAndExactRangeEvidenceButEstimatedMayOmitIt()
    {
        var source = new AssetRevisionReference { AssetId = Guid.NewGuid(), RecipeRevisionId = Guid.NewGuid() };
        var videoRange = new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30));
        var audioRange = new AudioSourceRange(new AudioSampleTime(0, 48_000), new AudioSampleTime(48_000, 48_000));
        var exactVideoPin = Pin(MediaType.Video, 1, videoRange.Duration, TimingReadiness.Exact);
        var estimatedVideoPin = Pin(MediaType.Video, 1, videoRange.Duration, TimingReadiness.Estimated);
        var exactAudioPin = Pin(MediaType.Audio, 2, audioRange.Duration, TimingReadiness.Exact);

        Assert.Throws<ArgumentException>(() => new CompositionVideoItem(Guid.NewGuid(), source, 1, videoRange, exactAudioPin, new ExactTime(0, 1)));
        Assert.Throws<ArgumentException>(() => new CompositionVideoItem(Guid.NewGuid(), source, 2, videoRange, exactVideoPin, new ExactTime(0, 1)));
        Assert.Throws<ArgumentException>(() => new CompositionVideoItem(Guid.NewGuid(), source, 1, null, exactVideoPin, new ExactTime(0, 1)));
        Assert.Throws<ArgumentException>(() => new CompositionAudioItem(Guid.NewGuid(), source, 2, null, exactAudioPin, new ExactTime(0, 1)));

        var estimated = new CompositionVideoItem(Guid.NewGuid(), source, 1, null, estimatedVideoPin, new ExactTime(0, 1));
        Assert.Null(estimated.SourceRange);
    }

    [Fact]
    public void ExactRangesMustMatchFrozenPinDuration()
    {
        var source = new AssetRevisionReference { AssetId = Guid.NewGuid(), RecipeRevisionId = Guid.NewGuid() };
        var videoRange = new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30));
        var audioRange = new AudioSourceRange(new AudioSampleTime(0, 48_000), new AudioSampleTime(48_000, 48_000));

        Assert.Throws<ArgumentException>(() => new CompositionVideoItem(Guid.NewGuid(), source, 0, videoRange, Pin(MediaType.Video, 0, new ExactTime(2, 1), TimingReadiness.Exact), new ExactTime(0, 1)));
        Assert.Throws<ArgumentException>(() => new CompositionAudioItem(Guid.NewGuid(), source, 0, audioRange, Pin(MediaType.Audio, 0, new ExactTime(2, 1), TimingReadiness.Exact), new ExactTime(0, 1)));
    }

    [Fact]
    public void LinkGroupsRequireTheSamePinnedSourceHash()
    {
        var source = new AssetRevisionReference { AssetId = Guid.NewGuid(), RecipeRevisionId = Guid.NewGuid() };
        var link = Guid.NewGuid();
        var videoRange = new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30));
        var audioRange = new AudioSourceRange(new AudioSampleTime(0, 48_000), new AudioSampleTime(48_000, 48_000));
        var video = new CompositionVideoItem(Guid.NewGuid(), source, 0, videoRange,
            Pin(MediaType.Video, 0, videoRange.Duration, TimingReadiness.Exact), new ExactTime(0, 1), link);
        var audioPin = new StreamTimingAssessment(
            Guid.NewGuid(), new string('b', 64), MediaType.Audio, 0, TimingReadiness.Exact, true, audioRange.Duration, []).CreatePlacementPin();
        var audio = new CompositionAudioItem(Guid.NewGuid(), source, 0, audioRange, audioPin, new ExactTime(0, 1), link);

        Assert.Throws<ArgumentException>(() => new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [video])],
            [new CompositionAudioTrack(Guid.NewGuid(), false, false, [audio])]));
    }

    private static StreamTimingAssessment Assessment(
        TimingReadiness readiness,
        IEnumerable<TimingIssueClassification>? issues = null,
        string? hash = null,
        MediaType mediaType = MediaType.Video,
        int? stream = 0,
        bool decode = true,
        ExactTime? duration = null) => new(
            Guid.NewGuid(), hash ?? Hash(), mediaType, stream, readiness, decode, duration ?? new ExactTime(1, 1),
            issues ?? (readiness == TimingReadiness.Exact ? [] : [TimingIssueClassification.NativeDurationUnavailable]));

    private static StreamTimingAssessmentPin Pin(MediaType type, int stream, ExactTime duration, TimingReadiness readiness) => new(
        Assessment(readiness, mediaType: type, stream: stream, duration: duration));

    private static string Hash() => new('a', 64);
}
