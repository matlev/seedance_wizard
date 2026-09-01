using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class FfprobeStreamTimingAssessmentServiceTests
{
    [Fact]
    public async Task ExactVideoUsesPersistedNumericStreamAndNativeFrameBoundaries()
    {
        var runner = Frames("""
            {"frames":[
              {"media_type":"video","stream_index":7,"pts":100,"duration":40},
              {"media_type":"video","stream_index":7,"pts":140,"duration":40}]}
            """);
        var result = await Service(runner).AssessAsync(VideoRequest(7));

        Assert.Equal(TimingReadiness.Exact, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(80, 1000), result.Assessment.TimelineDuration);
        Assert.Equal(100, result.VideoFullRange!.Start.PresentationTimestamp);
        Assert.Equal(180, result.VideoFullRange.End.PresentationTimestamp);
        Assert.Contains("-select_streams", runner.Request!.Arguments);
        Assert.Contains("7", runner.Request.Arguments);
        Assert.DoesNotContain("v:0", runner.Request.Arguments);
    }

    [Fact]
    public async Task OneNativeTickVideoCadenceQuantizationRemainsExact()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":33},{"media_type":"video","stream_index":0,"pts":33,"duration":33},{"media_type":"video","stream_index":0,"pts":67,"duration":33},{"media_type":"video","stream_index":0,"pts":100,"duration":33}]}"""))
            .AssessAsync(VideoRequest(0, duration: 133));

        Assert.Equal(TimingReadiness.Exact, result.Assessment.Readiness);
        Assert.DoesNotContain(TimingIssueClassification.DiscontinuousTimestamps, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task OneSecondNativeTickDeviationRemainsDiscontinuous()
    {
        var request = new StreamTimingAssessmentRequest("x", Identity(), MediaType.Video, new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 0, TimeBaseNumerator = 1, TimeBaseDenominator = 1, DurationPresentationTimestamp = 3 }
        });
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":1},{"media_type":"video","stream_index":0,"pts":2,"duration":1}]}"""))
            .AssessAsync(request);

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.DiscontinuousTimestamps, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task LargerVideoTimestampGapRemainsEstimated()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":33},{"media_type":"video","stream_index":0,"pts":300,"duration":33},{"media_type":"video","stream_index":0,"pts":667,"duration":33}]}"""))
            .AssessAsync(VideoRequest(0, duration: 700));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.DiscontinuousTimestamps, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task LegacyPacketTimestampFieldsRemainSupported()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":7,"pkt_pts":100,"pkt_duration":40},{"media_type":"video","stream_index":7,"pkt_pts":140,"pkt_duration":40}]}"""))
            .AssessAsync(VideoRequest(7));

        Assert.Equal(TimingReadiness.Exact, result.Assessment.Readiness);
    }

    [Fact]
    public async Task MissingNativeTimestampWithBestEffortFallbackIsExplicitlyDegraded()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"best_effort_timestamp":0,"duration":40},{"media_type":"video","stream_index":0,"pts":40,"duration":40}]}"""))
            .AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.NativePresentationTimestampUnavailable, result.Assessment.IssueClassifications);
        Assert.DoesNotContain(TimingIssueClassification.NativeStartUnavailable, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task EstimatedVideoIncludesInferredFinalFrameDuration()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":100},{"media_type":"video","stream_index":0,"pts":140}]}"""))
            .AssessAsync(VideoRequest(0, duration: null));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(80, 1000), result.Assessment.TimelineDuration);
    }

    [Fact]
    public async Task OutOfOrderVideoUsesCompletePresentationEnvelope()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":40},{"media_type":"video","stream_index":0,"pts":100,"duration":40},{"media_type":"video","stream_index":0,"pts":40,"duration":40}]}"""))
            .AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(140, 1000), result.Assessment.TimelineDuration);
    }

    [Fact]
    public async Task CombinedPacketsAndFramesVideoUsesFrameEntriesForDiscontinuousTiming()
    {
        var result = await Service(Frames("""{"packets_and_frames":[{"type":"packet","stream_index":9},{"type":"frame","media_type":"video","stream_index":9,"pts":0,"duration":5000},{"type":"packet","stream_index":0},{"type":"frame","media_type":"video","stream_index":0,"pts":0,"duration":40},{"type":"frame","media_type":"video","stream_index":0,"pts":100,"duration":40},{"type":"frame","media_type":"video","stream_index":0,"pts":40,"duration":40}]}"""))
            .AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(140, 1000), result.Assessment.TimelineDuration);
        Assert.Contains(TimingIssueClassification.DiscontinuousTimestamps, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task GeneratedDegradedTimingFixtureTranscriptMatchesItsAcceptanceAssessment()
    {
        var transcriptPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "degraded_timing_gap.ffprobe.json");
        var transcript = await File.ReadAllTextAsync(transcriptPath);
        var identity = new ContentIdentity
        {
            Status = ContentHashStatus.Verified,
            Sha256 = "F005A77C048912A6964DF6C492A9D66E11FBD473B45ABFC691E536D854339FC7",
            LengthBytes = 27_343
        };
        var runner = Frames(transcript);

        var result = await Service(runner).AssessAsync(VideoRequest(0, duration: 1333, identity: identity));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.True(result.Assessment.CanPlace);
        Assert.Equal(new ExactTime(1333, 1000), result.Assessment.TimelineDuration);
        Assert.Equal(identity.Sha256, result.Assessment.SourceContentHash);
        Assert.Equal(0, result.Assessment.SelectedStreamIndex);
        Assert.Equal([TimingIssueClassification.DiscontinuousTimestamps], result.Assessment.IssueClassifications);
        Assert.Null(result.VideoFullRange);
        Assert.Contains("-show_frames", runner.Request!.Arguments);
        Assert.Contains("-show_packets", runner.Request.Arguments);
        Assert.Contains("packets_and_frames", transcript);
    }

    [Fact]
    public async Task VideoEndingBeforeFirstReportedFrameStillUsesCompletePresentationEnvelope()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":100,"duration":40},{"media_type":"video","stream_index":0,"pts":0,"duration":40}]}"""))
            .AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(140, 1000), result.Assessment.TimelineDuration);
        Assert.Equal(new ExactTime(0, 1), result.Assessment.SourcePresentationStart);
    }

    [Fact]
    public async Task UnrepresentableVideoSourceStartIsUnusableInsteadOfThrowing()
    {
        var request = new StreamTimingAssessmentRequest("x", Identity(), MediaType.Video, new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata
            {
                StreamIndex = 0,
                TimeBaseNumerator = int.MaxValue,
                TimeBaseDenominator = 1
            }
        });

        var result = await Service(Frames($$"""{"frames":[{"media_type":"video","stream_index":0,"pts":{{long.MaxValue - 1}},"duration":1}]}"""))
            .AssessAsync(request);

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Equal([TimingIssueClassification.SourcePresentationStartUnrepresentable], result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task BestEffortVideoWithNoTerminalBoundaryIsEstimatedNotExact()
    {
        var result = await Service(Frames("""
            {"frames":[
              {"media_type":"video","stream_index":1,"best_effort_timestamp":10},
              {"media_type":"video","stream_index":1,"best_effort_timestamp":50}]}
            """)).AssessAsync(VideoRequest(1, duration: null));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Null(result.VideoFullRange);
        Assert.Contains(TimingIssueClassification.NativePresentationTimestampUnavailable, result.Assessment.IssueClassifications);
        Assert.Contains(TimingIssueClassification.TerminalBoundaryUnavailable, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task ExactAudioPinsZeroBasedDecodedSampleRange()
    {
        var result = await Service(Frames("""
            {"frames":[
              {"media_type":"audio","stream_index":3,"pts":1000,"nb_samples":1024},
              {"media_type":"audio","stream_index":3,"pts":2024,"nb_samples":1024}]}
            """)).AssessAsync(AudioRequest(3));

        Assert.Equal(TimingReadiness.Exact, result.Assessment.Readiness);
        Assert.Equal(0, result.AudioFullRange!.Start.SampleFrameOffset);
        Assert.Equal(2048, result.AudioFullRange.End.SampleFrameOffset);
        Assert.Equal(new ExactTime(2048, 48000), result.Assessment.TimelineDuration);
    }

    [Fact]
    public async Task CombinedPacketsAndFramesReadsAudioPacketPriming()
    {
        var result = await Service(Frames("""{"packets_and_frames":[{"type":"frame","media_type":"audio","stream_index":3,"pts":0,"nb_samples":1024},{"type":"frame","media_type":"audio","stream_index":3,"pts":1024,"nb_samples":1024},{"type":"packet","stream_index":3,"side_data_list":[{"side_data_type":"Skip Samples","skip_samples":120}]}]}"""))
            .AssessAsync(AudioRequest(3));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.UnresolvedAudioPrimingOrPadding, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task ReconciledAudioPrimingMetadataRemainsExact()
    {
        var result = await Service(Frames("""{"packets_and_frames":[{"type":"packet","stream_index":3,"pts":-1024,"duration":1024,"side_data_list":[{"side_data_type":"Skip Samples","skip_samples":1024}]},{"type":"frame","media_type":"audio","stream_index":3,"pts":0,"duration":1024,"nb_samples":1024},{"type":"frame","media_type":"audio","stream_index":3,"pts":1024,"duration":416,"nb_samples":1024}]}"""))
            .AssessAsync(AudioRequest(3, duration: 1440));

        Assert.Equal(TimingReadiness.Exact, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(1440, 48000), result.Assessment.TimelineDuration);
        Assert.Equal(1440, result.AudioFullRange!.End.SampleFrameOffset);
        Assert.DoesNotContain(TimingIssueClassification.UnresolvedAudioPrimingOrPadding, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task MismatchedAudioPrimingMetadataRemainsEstimated()
    {
        var result = await Service(Frames("""{"packets_and_frames":[{"type":"packet","stream_index":3,"pts":-1000,"duration":1024,"side_data_list":[{"side_data_type":"Skip Samples","skip_samples":1024}]},{"type":"frame","media_type":"audio","stream_index":3,"pts":0,"duration":1024,"nb_samples":1024},{"type":"frame","media_type":"audio","stream_index":3,"pts":1024,"duration":416,"nb_samples":1024}]}"""))
            .AssessAsync(AudioRequest(3, duration: 1440));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.UnresolvedAudioPrimingOrPadding, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task ReconciledAudioDiscardPaddingUsesPresentedTerminalBoundary()
    {
        var result = await Service(Frames("""{"packets_and_frames":[{"type":"frame","media_type":"audio","stream_index":3,"pts":0,"duration":1024,"nb_samples":1024},{"type":"packet","stream_index":3,"pts":1024,"duration":1024,"side_data_list":[{"side_data_type":"Skip Samples","discard_padding":608}]},{"type":"frame","media_type":"audio","stream_index":3,"pts":1024,"duration":416,"nb_samples":1024}]}"""))
            .AssessAsync(AudioRequest(3, duration: 1440));

        Assert.Equal(TimingReadiness.Exact, result.Assessment.Readiness);
        Assert.Equal(1440, result.AudioFullRange!.End.SampleFrameOffset);
        Assert.DoesNotContain(TimingIssueClassification.UnresolvedAudioPrimingOrPadding, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task UnrepresentableAudioSourceStartIsUnusableInsteadOfThrowing()
    {
        var request = new StreamTimingAssessmentRequest("x", Identity(), MediaType.Audio, new MediaEncodingMetadata
        {
            Audio = new AudioStreamMetadata
            {
                StreamIndex = 3,
                SampleRate = 48000,
                TimeBaseNumerator = int.MaxValue,
                TimeBaseDenominator = 1
            }
        });

        var result = await Service(Frames($$"""{"frames":[{"media_type":"audio","stream_index":3,"pts":{{long.MaxValue}},"nb_samples":1}]}"""))
            .AssessAsync(request);

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Equal([TimingIssueClassification.SourcePresentationStartUnrepresentable], result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task AudioPrimingOrDiscontinuityIsEstimated()
    {
        var runner = Frames("""{"frames":[{"media_type":"audio","stream_index":3,"pts":0,"nb_samples":1024},{"media_type":"audio","stream_index":3,"pts":3000,"nb_samples":1024}],"packets":[{"stream_index":3,"side_data_list":[{"side_data_type":"Skip Samples","skip_samples":120}]}]}""");
        var result = await Service(runner).AssessAsync(AudioRequest(3));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Null(result.AudioFullRange);
        Assert.Contains(TimingIssueClassification.UnresolvedAudioPrimingOrPadding, result.Assessment.IssueClassifications);
        Assert.Contains(TimingIssueClassification.DiscontinuousTimestamps, result.Assessment.IssueClassifications);
        Assert.Contains("-show_packets", runner.Request!.Arguments);
        Assert.Contains("packet_side_data", runner.Request.Arguments.Single(argument => argument.Contains("packet_side_data")));
    }

    [Fact]
    public async Task DiscontinuousAudioFreezesGapInclusivePresentationSpan()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"audio","stream_index":3,"pts":0,"nb_samples":1024},{"media_type":"audio","stream_index":3,"pts":3000,"nb_samples":1024}]}"""))
            .AssessAsync(AudioRequest(3));

        Assert.Equal(TimingReadiness.Estimated, result.Assessment.Readiness);
        Assert.Equal(new ExactTime(4024, 48000), result.Assessment.TimelineDuration);
    }

    [Fact]
    public async Task MissingToolIsUnusable()
    {
        var result = await new FfprobeStreamTimingAssessmentService(null, Frames("{}")).AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.AnalysisCapabilityUnavailable, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task ToolLaunchFailureIsAnalysisCapabilityUnavailable()
    {
        var runner = new StubRunner(new ExternalProcessResult(0, "{}", "")) { ExceptionToThrow = new System.ComponentModel.Win32Exception() };
        var result = await Service(runner).AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.AnalysisCapabilityUnavailable, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task MissingSelectedStreamIsUnusableWithoutScanning()
    {
        var runner = new StubRunner(new ExternalProcessResult(0, "{}", ""));
        var result = await Service(runner).AssessAsync(new StreamTimingAssessmentRequest("x", Identity(), MediaType.Video, new MediaEncodingMetadata()));

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Equal([TimingIssueClassification.NoUsableStream], result.Assessment.IssueClassifications);
        Assert.Null(runner.Request);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(0, "decode warning")]
    public async Task DecodeFailureOrErrorOutputIsUnusable(int exitCode, string stderr)
    {
        var result = await Service(new StubRunner(new ExternalProcessResult(exitCode, "{}", stderr))).AssessAsync(VideoRequest(0));

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Contains(result.Assessment.IssueClassifications, issue => issue is TimingIssueClassification.SequentialDecodeUnavailable or TimingIssueClassification.CorruptMedia);
    }

    [Fact]
    public async Task CorruptFrameFlagsAreUnusable()
    {
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":1,"decode_error_flags":1}]}"""))
            .AssessAsync(VideoRequest(0));

        Assert.Equal([TimingIssueClassification.CorruptMedia], result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task ContainerDurationAloneCannotMakePlacementEligible()
    {
        var request = new StreamTimingAssessmentRequest("x", Identity(), MediaType.Video, new MediaEncodingMetadata
        {
            DurationSeconds = 99,
            Video = new VideoStreamMetadata { StreamIndex = 0, TimeBaseNumerator = 1, TimeBaseDenominator = 1000 }
        });
        var result = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"best_effort_timestamp":0}]}"""))
            .AssessAsync(request);

        Assert.Equal(TimingReadiness.Unusable, result.Assessment.Readiness);
        Assert.Contains(TimingIssueClassification.FiniteSpanUnavailable, result.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task UnverifiedHashIsRejectedBeforeRunnerInvocation()
    {
        var runner = Frames("{}");
        var identity = Identity(); identity.Status = ContentHashStatus.Pending;
        await Assert.ThrowsAsync<ArgumentException>(() => Service(runner).AssessAsync(new StreamTimingAssessmentRequest("x", identity, MediaType.Video, new MediaEncodingMetadata())));
        Assert.Null(runner.Request);
    }

    [Fact]
    public async Task RequestSnapshotsHashAndSelectedDescriptor()
    {
        var identity = Identity();
        var encoding = new MediaEncodingMetadata { Video = new VideoStreamMetadata { StreamIndex = 2, TimeBaseNumerator = 1, TimeBaseDenominator = 1000, DurationPresentationTimestamp = 80 } };
        var request = new StreamTimingAssessmentRequest("x", identity, MediaType.Video, encoding);
        identity.Sha256 = new string('b', 64);
        encoding.Video!.StreamIndex = 9;
        encoding.Video.TimeBaseDenominator = 1;

        var runner = Frames("""{"frames":[{"media_type":"video","stream_index":2,"pts":0,"duration":40},{"media_type":"video","stream_index":2,"pts":40,"duration":40}]}""");
        var result = await Service(runner).AssessAsync(request);

        Assert.Equal(new string('A', 64), result.Assessment.SourceContentHash);
        Assert.Contains("2", runner.Request!.Arguments);
        Assert.Equal(new ExactTime(80, 1000), result.Assessment.TimelineDuration);
    }

    [Fact]
    public async Task UnchangedEvidenceReusesPriorAssessmentIdentity()
    {
        var initial = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":40},{"media_type":"video","stream_index":0,"pts":40,"duration":40}]}"""))
            .AssessAsync(VideoRequest(0));
        var request = new StreamTimingAssessmentRequest("x", Identity(), MediaType.Video, new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 0, TimeBaseNumerator = 1, TimeBaseDenominator = 1000, DurationPresentationTimestamp = 80 }
        }, initial.Assessment);

        var repeated = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":40},{"media_type":"video","stream_index":0,"pts":40,"duration":40}]}""")).AssessAsync(request);

        Assert.Same(initial.Assessment, repeated.Assessment);
    }

    [Fact]
    public async Task ChangedEvidenceGetsANewAssessmentIdentity()
    {
        var initial = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":40},{"media_type":"video","stream_index":0,"pts":40,"duration":40}]}"""))
            .AssessAsync(VideoRequest(0));
        var request = new StreamTimingAssessmentRequest("x", Identity(), MediaType.Video, new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 0, TimeBaseNumerator = 1, TimeBaseDenominator = 1000, DurationPresentationTimestamp = 80 }
        }, initial.Assessment);
        var changed = await Service(Frames("""{"frames":[{"media_type":"video","stream_index":0,"pts":0,"duration":40},{"media_type":"video","stream_index":0,"pts":40,"duration":50}]}""")).AssessAsync(request);

        Assert.NotEqual(initial.Assessment.AssessmentId, changed.Assessment.AssessmentId);
    }

    [Fact]
    public void RequestAcceptsCaseInsensitiveSha256AlgorithmIdentity()
    {
        var identity = Identity();
        identity.Algorithm = ContentIdentity.Sha256Algorithm.ToLowerInvariant();

        var request = VideoRequest(0, identity: identity);

        Assert.Equal(new string('A', 64), request.SourceContentHash);
    }

    [Fact]
    public async Task MissingAudioSampleCountNeedsAStreamDurationToRemainEstimated()
    {
        var withoutDuration = await Service(Frames("""{"frames":[{"media_type":"audio","stream_index":3,"pts":0}]}"""))
            .AssessAsync(AudioRequest(3));
        var withDuration = await Service(Frames("""{"frames":[{"media_type":"audio","stream_index":3,"pts":0}]}"""))
            .AssessAsync(AudioRequest(3, duration: 48000));

        Assert.Equal(TimingReadiness.Unusable, withoutDuration.Assessment.Readiness);
        Assert.Equal(TimingReadiness.Estimated, withDuration.Assessment.Readiness);
        Assert.Equal(new ExactTime(1, 1), withDuration.Assessment.TimelineDuration);
        Assert.Contains(TimingIssueClassification.UnresolvedAudioSampleBoundary, withDuration.Assessment.IssueClassifications);
    }

    [Fact]
    public async Task CancellationFlowsToRunner()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new StubRunner(new ExternalProcessResult(0, "{}", "")) { ThrowCancellation = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(runner).AssessAsync(VideoRequest(0), cancellation.Token));
        Assert.True(runner.ObservedCancellation.IsCancellationRequested);
    }

    [Fact]
    public void ResultRequiresMatchingExactRangeAndDoesNotExposeOneForEstimatedTiming()
    {
        var exact = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, 0,
            TimingReadiness.Exact, true, new ExactTime(1, 1), [], new ExactTime(0, 1));
        var estimated = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, 0,
            TimingReadiness.Estimated, true, new ExactTime(1, 1), [TimingIssueClassification.TerminalBoundaryUnavailable]);
        var range = new VideoSourceRange(new VideoPresentationTime(0, 1, 1), new VideoPresentationTime(1, 1, 1));

        Assert.Throws<ArgumentException>(() => new StreamTimingAssessmentResult(exact));
        Assert.Throws<ArgumentException>(() => new StreamTimingAssessmentResult(estimated, range));
        Assert.NotNull(new StreamTimingAssessmentResult(exact, range).VideoFullRange);
        var shifted = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, 0,
            TimingReadiness.Exact, true, new ExactTime(1, 1), [], new ExactTime(1, 1));
        Assert.Throws<ArgumentException>(() => new StreamTimingAssessmentResult(shifted, range));
    }

    private static FfprobeStreamTimingAssessmentService Service(IExternalProcessRunner runner) => new("ffprobe", runner);
    private static StubRunner Frames(string json) => new(new ExternalProcessResult(0, json, ""));
    private static ContentIdentity Identity() => new() { Status = ContentHashStatus.Verified, Sha256 = new string('a', 64) };
    private static StreamTimingAssessmentRequest VideoRequest(int index, long? duration = 80, ContentIdentity? identity = null) => new("x", identity ?? Identity(), MediaType.Video, new MediaEncodingMetadata
    {
        Video = new VideoStreamMetadata { StreamIndex = index, TimeBaseNumerator = 1, TimeBaseDenominator = 1000, DurationPresentationTimestamp = duration }
    });
    private static StreamTimingAssessmentRequest AudioRequest(int index, long? duration = null) => new("x", Identity(), MediaType.Audio, new MediaEncodingMetadata
    {
        Audio = new AudioStreamMetadata { StreamIndex = index, SampleRate = 48000, TimeBaseNumerator = 1, TimeBaseDenominator = 48000, DurationPresentationTimestamp = duration }
    });

    private sealed class StubRunner(ExternalProcessResult result) : IExternalProcessRunner
    {
        public ExternalProcessRequest? Request { get; private set; }
        public bool ThrowCancellation { get; init; }
        public Exception? ExceptionToThrow { get; init; }
        public CancellationToken ObservedCancellation { get; private set; }
        public Task<ExternalProcessResult> RunAsync(ExternalProcessRequest request, IProgress<ProcessOutputLine>? progress = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            ObservedCancellation = cancellationToken;
            if (ThrowCancellation) return Task.FromCanceled<ExternalProcessResult>(cancellationToken);
            if (ExceptionToThrow is not null) return Task.FromException<ExternalProcessResult>(ExceptionToThrow);
            return Task.FromResult(result);
        }
    }
}
