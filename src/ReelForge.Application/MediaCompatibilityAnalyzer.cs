using ReelForge.Core;

namespace ReelForge.Application;

public enum CompositionCompatibilityDecision
{
    Compatible,
    RequiresNormalization,
    Unknown
}

public sealed record MediaCompatibilityIssue(
    string Property,
    string? Expected,
    string? Actual);

public sealed record CompositionCompatibilityReport(
    CompositionCompatibilityDecision Decision,
    IReadOnlyList<MediaCompatibilityIssue> Issues)
{
    public bool CanConcatWithoutNormalization => Decision == CompositionCompatibilityDecision.Compatible;
}

public static class MediaCompatibilityAnalyzer
{
    public static CompositionCompatibilityReport Analyze(IReadOnlyList<MediaEncodingMetadata?> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
            return new CompositionCompatibilityReport(
                CompositionCompatibilityDecision.Unknown,
                [new MediaCompatibilityIssue("inputs", "at least one video", "none")]);
        if (inputs.Any(input => !HasRequiredStreamMetadata(input)))
            return new CompositionCompatibilityReport(
                CompositionCompatibilityDecision.Unknown,
                [new MediaCompatibilityIssue("video metadata", "known", "missing")]);

        var baseline = inputs[0]!;
        var issues = new List<MediaCompatibilityIssue>();
        for (var index = 1; index < inputs.Count; index++)
        {
            var candidate = inputs[index]!;
            Compare(issues, index, "video codec", baseline.Video!.Codec, candidate.Video!.Codec);
            Compare(issues, index, "width", baseline.Video.Width, candidate.Video.Width);
            Compare(issues, index, "height", baseline.Video.Height, candidate.Video.Height);
            Compare(issues, index, "pixel format", baseline.Video.PixelFormat, candidate.Video.PixelFormat);
            Compare(issues, index, "frame rate", baseline.Video.FrameRate, candidate.Video.FrameRate);
            Compare(issues, index, "audio presence", baseline.Audio is not null, candidate.Audio is not null);
            if (baseline.Audio is not null && candidate.Audio is not null)
            {
                Compare(issues, index, "audio codec", baseline.Audio.Codec, candidate.Audio.Codec);
                Compare(issues, index, "sample rate", baseline.Audio.SampleRate, candidate.Audio.SampleRate);
                Compare(issues, index, "channels", baseline.Audio.Channels, candidate.Audio.Channels);
                Compare(issues, index, "channel layout", baseline.Audio.ChannelLayout, candidate.Audio.ChannelLayout);
            }
        }

        return new CompositionCompatibilityReport(
            issues.Count == 0
                ? CompositionCompatibilityDecision.Compatible
                : CompositionCompatibilityDecision.RequiresNormalization,
            issues);
    }

    private static bool HasRequiredStreamMetadata(MediaEncodingMetadata? input) =>
        input?.Video is
        {
            Codec: not null,
            Width: not null,
            Height: not null,
            PixelFormat: not null,
            FrameRate: not null
        } &&
        (input.Audio is null || input.Audio is
        {
            Codec: not null,
            SampleRate: not null,
            Channels: not null
        });

    private static void Compare<T>(
        List<MediaCompatibilityIssue> issues,
        int inputIndex,
        string property,
        T expected,
        T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        issues.Add(new MediaCompatibilityIssue(
            $"input {inputIndex + 1} {property}",
            expected?.ToString(),
            actual?.ToString()));
    }
}
