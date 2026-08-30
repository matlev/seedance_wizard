using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// Concrete stream scanner. ffprobe terminology is contained here; its output is converted to
/// engine-neutral timing assessments before crossing into Application/Core.
/// </summary>
public sealed class FfprobeStreamTimingAssessmentService : IStreamTimingAssessmentService
{
    private string? _ffprobePath;
    private readonly IExternalProcessRunner _runner;

    public FfprobeStreamTimingAssessmentService(string? ffprobePath, IExternalProcessRunner runner)
    {
        _ffprobePath = ffprobePath;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public void UpdateExecutablePath(string? ffprobePath) => _ffprobePath = ffprobePath;

    public async Task<StreamTimingAssessmentResult> AssessAsync(
        StreamTimingAssessmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hash = request.SourceContentHash;
        var streamIndex = request.SelectedStream.StreamIndex;
        if (streamIndex is not >= 0)
            return Unusable(hash, request.MediaType, null, TimingIssueClassification.NoUsableStream);

        if (string.IsNullOrWhiteSpace(_ffprobePath))
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.AnalysisCapabilityUnavailable);

        ExternalProcessResult process;
        try
        {
            process = await _runner.RunAsync(
                new ExternalProcessRequest(_ffprobePath, BuildArguments(request.MediaPath, streamIndex.Value)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.AnalysisCapabilityUnavailable);
        }
        catch (InvalidOperationException)
        {
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.AnalysisCapabilityUnavailable);
        }
        catch (IOException)
        {
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.AnalysisCapabilityUnavailable);
        }
        catch (UnauthorizedAccessException) { return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.AnalysisCapabilityUnavailable); }
        catch (SecurityException) { return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.AnalysisCapabilityUnavailable); }

        if (!process.Succeeded)
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.SequentialDecodeUnavailable);
        if (!string.IsNullOrWhiteSpace(process.StandardError))
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.CorruptMedia);

        try
        {
            return request.MediaType == MediaType.Video
                ? AssessVideo(request, hash, request.SelectedStream, process.StandardOutput)
                : AssessAudio(request, hash, request.SelectedStream, process.StandardOutput);
        }
        catch (JsonException)
        {
            return Unusable(hash, request.MediaType, streamIndex, TimingIssueClassification.SequentialDecodeUnavailable);
        }
    }

    private static IReadOnlyList<string> BuildArguments(string mediaPath, int streamIndex) =>
    [
        "-v", "error",
        "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture),
        "-show_frames",
        "-show_packets",
        "-show_entries", "frame=media_type,stream_index,pts,pkt_pts,best_effort_timestamp,duration,pkt_duration,nb_samples,decode_error_flags,flags:frame_side_data=side_data_type,skip_samples,discard_padding:packet=stream_index,pts,duration:packet_side_data=side_data_type,skip_samples,discard_padding",
        "-of", "json",
        mediaPath
    ];

    private static StreamTimingAssessmentResult AssessVideo(StreamTimingAssessmentRequest request, string hash, StreamTimingDescriptor descriptor, string json)
    {
        var index = descriptor.StreamIndex!.Value;
        if (descriptor.TimeBaseNumerator is not > 0 || descriptor.TimeBaseDenominator is not > 0)
            return Unusable(hash, MediaType.Video, index, TimingIssueClassification.FiniteSpanUnavailable);

        var frames = ParseFrames(json, index, "video");
        if (frames.Count == 0)
            return Unusable(hash, MediaType.Video, index, TimingIssueClassification.SequentialDecodeUnavailable);
        if (frames.Any(IsCorrupt))
            return Unusable(hash, MediaType.Video, index, TimingIssueClassification.CorruptMedia);

        var issues = new SortedSet<TimingIssueClassification>();
        var nativePts = frames.Select(frame => GetFirstInt64(frame, "pts", "pkt_pts")).ToArray();
        var observedPts = nativePts.Select((value, i) => value ?? GetInt64(frames[i], "best_effort_timestamp")).ToArray();
        if (nativePts.Any(value => value is null) && observedPts.Any(value => value is not null))
            issues.Add(TimingIssueClassification.NativePresentationTimestampUnavailable);
        if (observedPts[0] is null)
            issues.Add(TimingIssueClassification.NativeStartUnavailable);
        var durations = frames.Select(frame => GetFirstPositiveInt64(frame, "duration", "pkt_duration")).ToArray();
        if (durations.Any(value => value is null))
            issues.Add(TimingIssueClassification.UnresolvedVideoFrameDuration);

        var usablePts = observedPts.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (usablePts.Length == 0)
            return Unusable(hash, MediaType.Video, index, TimingIssueClassification.FiniteSpanUnavailable);
        if (HasNonmonotonic(usablePts)) issues.Add(TimingIssueClassification.NonmonotonicTimestamps);

        var first = usablePts.Min();
        var last = usablePts.Max();
        var inferredDuration = InferPositiveFrameDuration(observedPts);
        var candidateEnds = new List<long>();
        for (var i = 0; i < observedPts.Length; i++)
        {
            if (observedPts[i] is not { } timestamp) continue;
            var frameDuration = durations[i] ?? inferredDuration;
            if (frameDuration is { } value && TryAdd(timestamp, value) is { } candidateEnd) candidateEnds.Add(candidateEnd);
        }
        long? terminal = candidateEnds.Count > 0 ? candidateEnds.Max() : null;
        if (terminal is null && descriptor.DurationPresentationTimestamp is { } streamDuration)
            terminal = TryAdd(first, streamDuration);
        if (terminal is null || durations.Any(value => value is null))
            issues.Add(TimingIssueClassification.TerminalBoundaryUnavailable);

        for (var i = 1; i < observedPts.Length; i++)
        {
            if (observedPts[i - 1] is not { } prior || observedPts[i] is not { } current || durations[i - 1] is not { } frameDuration ||
                TryAdd(prior, frameDuration) is not { } expected || !IsWithinOneNativeTick(current, expected, descriptor))
                issues.Add(TimingIssueClassification.DiscontinuousTimestamps);
        }

        var duration = terminal is { } end && TryPositiveDifference(end, first, out var span)
            ? TryTimeFromTicks(span, descriptor.TimeBaseNumerator.Value, descriptor.TimeBaseDenominator.Value)
            : null;
        if (duration is null)
            return Unusable(hash, MediaType.Video, index, TimingIssueClassification.FiniteSpanUnavailable);
        var sourceStart = TryTimeFromTicksSigned(
            first,
            descriptor.TimeBaseNumerator.Value,
            descriptor.TimeBaseDenominator.Value);
        if (sourceStart is null)
            return Unusable(hash, MediaType.Video, index, TimingIssueClassification.SourcePresentationStartUnrepresentable);

        var exactEnd = terminal.GetValueOrDefault();
        var exact = issues.Count == 0 && nativePts.All(value => value.HasValue) && terminal is not null &&
                    IsStrictlyIncreasing(nativePts.Select(value => value!.Value)) && IsCoherent(nativePts, durations, descriptor);
        if (exact)
        {
            var range = new VideoSourceRange(
                new VideoPresentationTime(first, descriptor.TimeBaseNumerator.Value, descriptor.TimeBaseDenominator.Value),
                new VideoPresentationTime(exactEnd, descriptor.TimeBaseNumerator.Value, descriptor.TimeBaseDenominator.Value));
            return new StreamTimingAssessmentResult(Assessment(request, hash, MediaType.Video, index, TimingReadiness.Exact, duration, [], sourceStart), range);
        }

        if (issues.Count == 0) issues.Add(TimingIssueClassification.TerminalBoundaryUnavailable);
        return new StreamTimingAssessmentResult(Assessment(request, hash, MediaType.Video, index, TimingReadiness.Estimated, duration, issues, sourceStart));
    }

    private static StreamTimingAssessmentResult AssessAudio(StreamTimingAssessmentRequest request, string hash, StreamTimingDescriptor descriptor, string json)
    {
        var index = descriptor.StreamIndex!.Value;
        if (descriptor.SampleRate is not > 0 || descriptor.TimeBaseNumerator is not > 0 || descriptor.TimeBaseDenominator is not > 0)
            return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.FiniteSpanUnavailable);

        var frames = ParseFrames(json, index, "audio");
        if (frames.Count == 0)
            return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.SequentialDecodeUnavailable);
        if (frames.Any(IsCorrupt))
            return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.CorruptMedia);

        var issues = new SortedSet<TimingIssueClassification>();
        var samples = frames.Select(frame => GetPositiveInt64(frame, "nb_samples")).ToArray();
        var frameDurations = frames.Select(frame => GetFirstPositiveInt64(frame, "duration", "pkt_duration")).ToArray();
        var nativePts = frames.Select(frame => GetFirstInt64(frame, "pts", "pkt_pts")).ToArray();
        var pts = nativePts.Select((value, i) => value ?? GetInt64(frames[i], "best_effort_timestamp")).ToArray();
        if (samples.Any(value => value is null)) issues.Add(TimingIssueClassification.UnresolvedAudioSampleBoundary);
        if (nativePts.Any(value => value is null) && pts.Any(value => value is not null)) issues.Add(TimingIssueClassification.NativePresentationTimestampUnavailable);
        if (pts[0] is null) issues.Add(TimingIssueClassification.NativeStartUnavailable);
        var packets = ParsePackets(json, index);
        var hasPrimingOrPadding = frames.Any(HasPrimingOrPadding) || packets.Any(HasPrimingOrPadding);

        var hasCompleteSampleCounts = samples.All(value => value.HasValue);
        long totalSamples = 0;
        if (hasCompleteSampleCounts)
        {
            try
            {
                foreach (var count in samples) totalSamples = checked(totalSamples + count!.Value);
            }
            catch (OverflowException)
            {
                return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.FiniteSpanUnavailable);
            }
        }
        if (hasCompleteSampleCounts && totalSamples <= 0)
            return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.FiniteSpanUnavailable);

        var contiguous = pts.All(value => value.HasValue) && hasCompleteSampleCounts &&
                         AreAudioTimestampsContiguous(pts!, samples!, descriptor);
        if (!contiguous && pts.All(value => value.HasValue)) issues.Add(TimingIssueClassification.DiscontinuousTimestamps);
        var presentationContiguous = pts.All(value => value.HasValue) && frameDurations.All(value => value.HasValue) &&
                                     ArePresentationDurationsContiguous(pts, frameDurations);
        var presentedSampleCount = presentationContiguous
            ? TryAudioPresentationSampleCount(pts, frameDurations, descriptor)
            : null;
        if (hasPrimingOrPadding &&
            (!presentationContiguous || presentedSampleCount is null ||
             !PrimingOrPaddingIsReconciled(frames, packets, pts, frameDurations, descriptor)))
            issues.Add(TimingIssueClassification.UnresolvedAudioPrimingOrPadding);
        var observedAudioPts = pts.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (!IsStrictlyIncreasing(observedAudioPts))
            issues.Add(TimingIssueClassification.NonmonotonicTimestamps);

        var durationFromFrames = frameDurations.All(value => value.HasValue) && pts.All(value => value.HasValue)
            ? TryAudioPresentationSpanFromDurations(pts, frameDurations, descriptor)
            : null;
        var duration = durationFromFrames ?? (!hasCompleteSampleCounts
            ? TryDurationFromDescriptor(descriptor)
            : !contiguous && pts.All(value => value.HasValue)
            ? TryAudioPresentationSpan(pts!, samples!, descriptor)
            : TryTimeFromTicks(totalSamples, 1, descriptor.SampleRate.Value));
        if (duration is null)
            return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.FiniteSpanUnavailable);
        var exactSampleCount = hasPrimingOrPadding ? presentedSampleCount : totalSamples;
        if (issues.Count == 0 && contiguous && exactSampleCount is > 0)
        {
            var exactAudioStart = TryTimeFromTicksSigned(
                pts[0]!.Value,
                descriptor.TimeBaseNumerator.Value,
                descriptor.TimeBaseDenominator.Value);
            if (exactAudioStart is null)
                return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.SourcePresentationStartUnrepresentable);
            var range = new AudioSourceRange(new AudioSampleTime(0, descriptor.SampleRate.Value), new AudioSampleTime(exactSampleCount.Value, descriptor.SampleRate.Value));
            return new StreamTimingAssessmentResult(Assessment(request, hash, MediaType.Audio, index, TimingReadiness.Exact, duration, [], exactAudioStart), audioFullRange: range);
        }
        var sourceStart = pts.All(value => value.HasValue)
            ? TryTimeFromTicksSigned(pts.Select(value => value!.Value).Min(), descriptor.TimeBaseNumerator.Value, descriptor.TimeBaseDenominator.Value)
            : null;
        if (pts.All(value => value.HasValue) && sourceStart is null)
            return Unusable(hash, MediaType.Audio, index, TimingIssueClassification.SourcePresentationStartUnrepresentable);
        return new StreamTimingAssessmentResult(Assessment(request, hash, MediaType.Audio, index, TimingReadiness.Estimated, duration, issues, sourceStart));
    }

    private static bool AreAudioTimestampsContiguous(long?[] pts, long?[] samples, StreamTimingDescriptor descriptor)
    {
        var first = pts[0]!.Value;
        long expected = 0;
        for (var i = 0; i < pts.Length; i++)
        {
            var delta = ((BigInteger)pts[i]!.Value - first) * descriptor.TimeBaseNumerator!.Value * descriptor.SampleRate!.Value;
            if (delta % descriptor.TimeBaseDenominator!.Value != 0 || delta / descriptor.TimeBaseDenominator.Value != expected)
                return false;
            try { expected = checked(expected + samples[i]!.Value); }
            catch (OverflowException) { return false; }
        }
        return true;
    }

    private static bool PrimingOrPaddingIsReconciled(
        IReadOnlyList<JsonElement> frames,
        IReadOnlyList<JsonElement> packets,
        long?[] pts,
        long?[] frameDurations,
        StreamTimingDescriptor descriptor)
    {
        if (frames.Any(HasPrimingOrPadding) ||
            descriptor.DurationPresentationTimestamp is not > 0 ||
            descriptor.TimeBaseNumerator is not > 0 ||
            descriptor.TimeBaseDenominator is not > 0 ||
            descriptor.SampleRate is not > 0 ||
            pts.Length == 0 || frameDurations.Length == 0 ||
            pts.Any(value => value is null) || frameDurations.Any(value => value is null))
            return false;

        var terminal = TryAdd(pts[^1]!.Value, frameDurations[^1]!.Value);
        if (terminal is null ||
            (BigInteger)terminal.Value - pts[0]!.Value != descriptor.DurationPresentationTimestamp.Value)
            return false;

        var foundTrim = false;
        foreach (var packet in packets)
        {
            foreach (var (skipSamples, discardPadding) in ParseTrimSideData(packet))
            {
                foundTrim = true;
                var packetPts = GetInt64(packet, "pts");
                if (packetPts is null) return false;

                if (skipSamples > 0)
                {
                    var presentationOffset = ((BigInteger)pts[0]!.Value - packetPts.Value) *
                                             descriptor.TimeBaseNumerator.Value * descriptor.SampleRate.Value;
                    if (presentationOffset != (BigInteger)skipSamples * descriptor.TimeBaseDenominator.Value)
                        return false;
                }

                if (discardPadding > 0)
                {
                    var packetDuration = GetPositiveInt64(packet, "duration");
                    if (packetDuration is null) return false;
                    var packetEndOffset = ((BigInteger)packetPts.Value + packetDuration.Value - terminal.Value) *
                                          descriptor.TimeBaseNumerator.Value * descriptor.SampleRate.Value;
                    if (packetEndOffset != (BigInteger)discardPadding * descriptor.TimeBaseDenominator.Value)
                        return false;
                }
            }
        }

        return foundTrim;
    }

    private static bool ArePresentationDurationsContiguous(long?[] pts, long?[] durations)
    {
        for (var index = 1; index < pts.Length; index++)
            if (TryAdd(pts[index - 1]!.Value, durations[index - 1]!.Value) != pts[index]!.Value)
                return false;
        return true;
    }

    private static long? TryAudioPresentationSampleCount(
        long?[] pts,
        long?[] durations,
        StreamTimingDescriptor descriptor)
    {
        var terminal = TryAdd(pts[^1]!.Value, durations[^1]!.Value);
        if (terminal is null) return null;
        var scaledSpan = ((BigInteger)terminal.Value - pts[0]!.Value) *
                         descriptor.TimeBaseNumerator!.Value * descriptor.SampleRate!.Value;
        if (scaledSpan <= 0 || scaledSpan % descriptor.TimeBaseDenominator!.Value != 0)
            return null;
        var samples = scaledSpan / descriptor.TimeBaseDenominator.Value;
        return samples <= long.MaxValue ? (long)samples : null;
    }

    private static ExactTime? TryAudioPresentationSpanFromDurations(
        long?[] pts,
        long?[] durations,
        StreamTimingDescriptor descriptor)
    {
        var earliest = pts.Select(value => value!.Value).Min();
        long? latest = null;
        foreach (var (timestamp, duration) in pts.Zip(durations))
        {
            var end = TryAdd(timestamp!.Value, duration!.Value);
            if (end is null) return null;
            latest = latest is null || end > latest ? end : latest;
        }
        return latest is { } terminal && TryPositiveDifference(terminal, earliest, out var span)
            ? TryTimeFromTicks(span, descriptor.TimeBaseNumerator!.Value, descriptor.TimeBaseDenominator!.Value)
            : null;
    }

    private static bool IsCoherent(long?[] pts, long?[] durations, StreamTimingDescriptor descriptor)
    {
        for (var i = 1; i < pts.Length; i++)
            if (durations[i - 1] is not { } duration || TryAdd(pts[i - 1]!.Value, duration) is not { } expected || !IsWithinOneNativeTick(pts[i]!.Value, expected, descriptor))
                return false;
        return true;
    }

    private static bool IsWithinOneNativeTick(long actual, long expected, StreamTimingDescriptor descriptor)
    {
        var deviation = BigInteger.Abs((BigInteger)actual - expected);
        if (deviation.IsZero) return true;
        return deviation == BigInteger.One && descriptor.TimeBaseNumerator is > 0 && descriptor.TimeBaseDenominator is > 0 &&
               (BigInteger)descriptor.TimeBaseNumerator.Value * 1000 <= descriptor.TimeBaseDenominator.Value;
    }

    private static long? InferPositiveFrameDuration(long?[] pts)
    {
        long? inferred = null;
        for (var i = 1; i < pts.Length; i++)
        {
            if (pts[i - 1] is not { } prior || pts[i] is not { } current) continue;
            var delta = (BigInteger)current - prior;
            if (delta > 0 && delta <= long.MaxValue)
                inferred = (long)delta;
        }
        return inferred;
    }

    private static ExactTime? TryAudioPresentationSpan(long?[] pts, long?[] samples, StreamTimingDescriptor descriptor)
    {
        var earliest = pts.Select(value => value!.Value).Min();
        BigInteger? latestEndNumerator = null;
        var commonDenominator = (BigInteger)descriptor.TimeBaseDenominator!.Value * descriptor.SampleRate!.Value;
        foreach (var (timestamp, sampleCount) in pts.Zip(samples))
        {
            var frameEndNumerator = ((BigInteger)timestamp!.Value * descriptor.TimeBaseNumerator!.Value * descriptor.SampleRate.Value) +
                                    ((BigInteger)sampleCount!.Value * descriptor.TimeBaseDenominator.Value);
            latestEndNumerator = latestEndNumerator is null || frameEndNumerator > latestEndNumerator
                ? frameEndNumerator
                : latestEndNumerator;
        }

        var earliestNumerator = (BigInteger)earliest * descriptor.TimeBaseNumerator!.Value * descriptor.SampleRate!.Value;
        var span = latestEndNumerator!.Value - earliestNumerator;
        return span > 0 ? TryExactTime(span, commonDenominator) : null;
    }

    private static ExactTime? TryDurationFromDescriptor(StreamTimingDescriptor descriptor) =>
        descriptor.DurationPresentationTimestamp is > 0 &&
        descriptor.TimeBaseNumerator is > 0 &&
        descriptor.TimeBaseDenominator is > 0
            ? TryTimeFromTicks(
                descriptor.DurationPresentationTimestamp.Value,
                descriptor.TimeBaseNumerator.Value,
                descriptor.TimeBaseDenominator.Value)
            : null;

    private static ExactTime? TryTimeFromTicks(long ticks, int numerator, int denominator) =>
        TryExactTime((BigInteger)ticks * numerator, denominator);

    private static ExactTime? TryTimeFromTicksSigned(long ticks, int numerator, int denominator) =>
        TryExactTimeSigned((BigInteger)ticks * numerator, denominator);

    private static ExactTime? TryExactTime(BigInteger numerator, BigInteger denominator)
    {
        if (numerator <= 0 || denominator <= 0) return null;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        var normalizedNumerator = numerator / divisor;
        var normalizedDenominator = denominator / divisor;
        if (normalizedNumerator > long.MaxValue || normalizedDenominator > long.MaxValue) return null;
        return new ExactTime((long)normalizedNumerator, (long)normalizedDenominator);
    }

    private static ExactTime? TryExactTimeSigned(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= 0) return null;
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        var normalizedNumerator = numerator / divisor;
        var normalizedDenominator = denominator / divisor;
        if (normalizedNumerator < long.MinValue || normalizedNumerator > long.MaxValue || normalizedDenominator > long.MaxValue) return null;
        return new ExactTime((long)normalizedNumerator, (long)normalizedDenominator);
    }

    private static long? TryAdd(long left, long right)
    {
        var sum = (BigInteger)left + right;
        return sum < long.MinValue || sum > long.MaxValue ? null : (long)sum;
    }

    private static bool TryPositiveDifference(long end, long start, out long difference)
    {
        var result = (BigInteger)end - start;
        if (result <= 0 || result > long.MaxValue) { difference = 0; return false; }
        difference = (long)result;
        return true;
    }

    private static StreamTimingAssessmentResult Unusable(string hash, MediaType type, int? index, TimingIssueClassification issue) =>
        new(new StreamTimingAssessment(Guid.NewGuid(), hash, type, index, TimingReadiness.Unusable, false, null, [issue]));

    private static StreamTimingAssessment Assessment(StreamTimingAssessmentRequest request, string hash, MediaType type, int? index, TimingReadiness readiness, ExactTime? duration, IEnumerable<TimingIssueClassification> issues, ExactTime? sourceStart = null)
    {
        var orderedIssues = issues.OrderBy(issue => issue).ToArray();
        var prior = request.PriorAssessment;
        if (prior is not null && prior.SchemaIdentity == StreamTimingAssessment.CurrentSchemaIdentity && prior.SourceContentHash == hash && prior.MediaType == type &&
            prior.SelectedStreamIndex == index && prior.Readiness == readiness && prior.HasUsableSequentialDecodePath == (readiness != TimingReadiness.Unusable) &&
            prior.TimelineDuration == duration && prior.SourcePresentationStart == sourceStart && prior.IssueClassifications.SequenceEqual(orderedIssues))
            return prior;
        return new StreamTimingAssessment(Guid.NewGuid(), hash, type, index, readiness, readiness != TimingReadiness.Unusable, duration, orderedIssues, sourceStart);
    }

    private static List<JsonElement> ParseFrames(string json, int streamIndex, string mediaType)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
        var entries = document.RootElement.TryGetProperty("frames", out var frames) && frames.ValueKind == JsonValueKind.Array
            ? frames
            : document.RootElement.TryGetProperty("packets_and_frames", out var combined) && combined.ValueKind == JsonValueKind.Array
                ? combined
                : default;
        if (entries.ValueKind != JsonValueKind.Array) return [];
        var isCombined = document.RootElement.TryGetProperty("packets_and_frames", out _);
        return entries.EnumerateArray()
            .Where(frame => frame.ValueKind == JsonValueKind.Object)
            .Where(frame => !isCombined || GetString(frame, "type") == "frame")
            .Where(frame => GetInt32(frame, "stream_index") == streamIndex && GetString(frame, "media_type") == mediaType)
            .Select(frame => frame.Clone()).ToList();
    }

    private static List<JsonElement> ParsePackets(string json, int streamIndex)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
        var isCombined = document.RootElement.TryGetProperty("packets_and_frames", out var combined) && combined.ValueKind == JsonValueKind.Array;
        var entries = document.RootElement.TryGetProperty("packets", out var packets) && packets.ValueKind == JsonValueKind.Array
            ? packets
            : isCombined ? combined : default;
        if (entries.ValueKind != JsonValueKind.Array) return [];
        return entries.EnumerateArray()
            .Where(packet => packet.ValueKind == JsonValueKind.Object)
            .Where(packet => !isCombined || GetString(packet, "type") == "packet")
            .Where(packet => GetInt32(packet, "stream_index") == streamIndex)
            .Select(packet => packet.Clone()).ToList();
    }

    private static bool IsCorrupt(JsonElement frame) =>
        GetInt64(frame, "decode_error_flags") is > 0 ||
        (GetString(frame, "flags")?.Contains('C', StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool HasPrimingOrPadding(JsonElement frame)
    {
        if (!frame.TryGetProperty("side_data_list", out var sideData) || sideData.ValueKind != JsonValueKind.Array) return false;
        return sideData.EnumerateArray().Any(data =>
            data.ValueKind == JsonValueKind.Object &&
            (GetString(data, "side_data_type")?.Contains("skip", StringComparison.OrdinalIgnoreCase) ?? false) &&
            (GetPositiveInt64(data, "skip_samples") is not null || GetPositiveInt64(data, "discard_padding") is not null));
    }

    private static IEnumerable<(long SkipSamples, long DiscardPadding)> ParseTrimSideData(JsonElement element)
    {
        if (!element.TryGetProperty("side_data_list", out var sideData) || sideData.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var data in sideData.EnumerateArray())
        {
            if (data.ValueKind != JsonValueKind.Object ||
                !(GetString(data, "side_data_type")?.Contains("skip", StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            var skip = GetPositiveInt64(data, "skip_samples") ?? 0;
            var discard = GetPositiveInt64(data, "discard_padding") ?? 0;
            if (skip > 0 || discard > 0)
                yield return (skip, discard);
        }
    }

    private static bool IsStrictlyIncreasing(IEnumerable<long> values)
    {
        long? prior = null;
        foreach (var value in values) { if (prior is not null && value <= prior) return false; prior = value; }
        return true;
    }

    private static bool HasNonmonotonic(IReadOnlyList<long> values) => !IsStrictlyIncreasing(values);
    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;
    private static long? GetInt64(JsonElement element, string property) => long.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static long? GetFirstInt64(JsonElement element, string currentProperty, string legacyProperty) =>
        GetInt64(element, currentProperty) ?? GetInt64(element, legacyProperty);
    private static long? GetPositiveInt64(JsonElement element, string property) => GetInt64(element, property) is { } value && value > 0 ? value : null;
    private static long? GetFirstPositiveInt64(JsonElement element, string currentProperty, string legacyProperty) =>
        GetPositiveInt64(element, currentProperty) ?? GetPositiveInt64(element, legacyProperty);
    private static int? GetInt32(JsonElement element, string property) => int.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
