using System.Diagnostics;
using System.Globalization;
using System.Text;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildExtractFrameArguments(
        string inputPath,
        string outputPath,
        double timestampSeconds)
    {
        ValidateMediaPath(inputPath, nameof(inputPath));
        ValidateMediaPath(outputPath, nameof(outputPath));
        if (timestampSeconds < 0 || double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(timestampSeconds));
        }

        return
        [
            "-hide_banner", "-y",
            "-ss", FormatSeconds(timestampSeconds),
            "-i", inputPath,
            "-frames:v", "1",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildFrameAccurateTrimArguments(
        string inputPath,
        string outputPath,
        double startSeconds,
        double endSeconds)
    {
        ValidateMediaPath(inputPath, nameof(inputPath));
        ValidateMediaPath(outputPath, nameof(outputPath));
        if (startSeconds < 0 || endSeconds <= startSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "The end time must be after a non-negative start time.");
        }

        return
        [
            "-hide_banner", "-y",
            "-ss", FormatSeconds(startSeconds),
            "-i", inputPath,
            "-t", FormatSeconds(endSeconds - startSeconds),
            "-c:v", "libx264",
            "-c:a", "aac",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildVideoWithoutAudioArguments(
        string inputPath,
        string outputPath)
    {
        ValidateMediaPath(inputPath, nameof(inputPath));
        ValidateMediaPath(outputPath, nameof(outputPath));
        return
        [
            "-hide_banner", "-y",
            "-i", inputPath,
            "-map", "0:v:0",
            "-c:v", "copy",
            "-an",
            "-movflags", "+faststart",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildExtractAudioArguments(string inputPath, string outputPath)
    {
        ValidateMediaPath(inputPath, nameof(inputPath));
        ValidateMediaPath(outputPath, nameof(outputPath));
        if (!Path.GetExtension(outputPath).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Extracted audio output must use the .m4a file type.", nameof(outputPath));

        return
        [
            "-hide_banner", "-y",
            "-i", inputPath,
            "-map", "0:a:0",
            "-vn",
            "-c:a", "aac",
            "-b:a", "192k",
            "-movflags", "+faststart",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildExtractExactAudioRangeArguments(
        string inputPath,
        string outputPath,
        int audioStreamIndex,
        AudioSourceRange sourceRange)
    {
        ValidateMediaPath(inputPath, nameof(inputPath));
        ValidateMediaPath(outputPath, nameof(outputPath));
        ArgumentOutOfRangeException.ThrowIfNegative(audioStreamIndex);
        ArgumentNullException.ThrowIfNull(sourceRange);
        if (!Path.GetExtension(outputPath).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Detached audio output must use the .m4a file type.", nameof(outputPath));

        return
        [
            "-hide_banner", "-y",
            "-i", inputPath,
            "-map", $"0:{audioStreamIndex}",
            "-af", $"atrim=start_sample={sourceRange.Start.SampleFrameOffset.ToString(CultureInfo.InvariantCulture)}:end_sample={sourceRange.End.SampleFrameOffset.ToString(CultureInfo.InvariantCulture)},asetpts=PTS-STARTPTS",
            "-vn",
            "-ar", sourceRange.Start.SampleRate.ToString(CultureInfo.InvariantCulture),
            "-c:a", "aac",
            "-b:a", "192k",
            "-movflags", "+faststart",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildExtractExactFrameArguments(
        string inputPath,
        string outputPath,
        int videoStreamIndex,
        long presentationTimestamp)
    {
        ValidateMediaPath(inputPath, nameof(inputPath));
        ValidateMediaPath(outputPath, nameof(outputPath));
        ArgumentOutOfRangeException.ThrowIfNegative(videoStreamIndex);

        return
        [
            "-hide_banner", "-y",
            "-i", inputPath,
            "-map", $"0:{videoStreamIndex}",
            "-vf", $"select=eq(pts\\,{presentationTimestamp.ToString(CultureInfo.InvariantCulture)})",
            "-frames:v", "1",
            "-fps_mode", "vfr",
            "-update", "1",
            outputPath
        ];
    }

    public static IReadOnlyList<string> BuildCompatibleConcatArguments(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        bool includeAudio)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        if (inputPaths.Count < 2)
            throw new ArgumentException("Concat requires at least two inputs.", nameof(inputPaths));
        foreach (var inputPath in inputPaths) ValidateMediaPath(inputPath, nameof(inputPaths));
        ValidateMediaPath(outputPath, nameof(outputPath));

        var arguments = new List<string> { "-hide_banner", "-y" };
        foreach (var inputPath in inputPaths)
        {
            arguments.Add("-i");
            arguments.Add(inputPath);
        }

        var filters = new List<string>();
        var concatInputs = new StringBuilder();
        for (var index = 0; index < inputPaths.Count; index++)
        {
            filters.Add($"[{index}:v:0]setpts=PTS-STARTPTS[v{index}]");
            concatInputs.Append(CultureInfo.InvariantCulture, $"[v{index}]");
            if (!includeAudio) continue;
            filters.Add($"[{index}:a:0]asetpts=PTS-STARTPTS[a{index}]");
            concatInputs.Append(CultureInfo.InvariantCulture, $"[a{index}]");
        }
        filters.Add(includeAudio
            ? $"{concatInputs}concat=n={inputPaths.Count}:v=1:a=1[v][a]"
            : $"{concatInputs}concat=n={inputPaths.Count}:v=1:a=0[v]");
        arguments.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[v]"]);
        if (includeAudio) arguments.AddRange(["-map", "[a]"]);
        arguments.AddRange(["-c:v", "libx264"]);
        if (includeAudio) arguments.AddRange(["-c:a", "aac"]);
        arguments.AddRange(["-movflags", "+faststart", outputPath]);
        return arguments;
    }

    public static IReadOnlyList<string> BuildAudioOverlayArguments(
        string videoPath,
        bool videoHasAudio,
        IReadOnlyList<AudioOverlayInput> audioInputs,
        string outputPath)
    {
        ValidateMediaPath(videoPath, nameof(videoPath));
        ArgumentNullException.ThrowIfNull(audioInputs);
        if (audioInputs.Count == 0)
            throw new ArgumentException("At least one audio overlay is required.", nameof(audioInputs));
        foreach (var input in audioInputs)
        {
            ValidateMediaPath(input.Path, nameof(audioInputs));
            if (input.TimelineStart < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audio overlay start times cannot be negative.");
            if (!double.IsFinite(input.GainDecibels) || input.GainDecibels is < -60 or > 12)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audio overlay gain must be between -60 dB and +12 dB.");
            if (!double.IsFinite(input.Pan) || input.Pan is < -1 or > 1)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audio pan must be between -1 and +1.");
            if (input.FadeIn < TimeSpan.Zero || input.FadeOut < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audio fades cannot be negative.");
            var requiresFadeDuration = input.FadeIn > TimeSpan.Zero || input.FadeOut > TimeSpan.Zero;
            if (requiresFadeDuration &&
                (input.AudibleDurationSeconds is not { } duration ||
                 duration <= 0 || !double.IsFinite(duration)))
                throw new ArgumentOutOfRangeException(
                    nameof(audioInputs),
                    "Audio fades require a finite positive audible duration.");
            if (input.AudibleDurationSeconds is { } audibleDuration &&
                (input.FadeIn.TotalSeconds > audibleDuration || input.FadeOut.TotalSeconds > audibleDuration))
                throw new ArgumentOutOfRangeException(
                    nameof(audioInputs),
                    "Audio fades cannot be longer than the audible clip duration.");
        }
        ValidateMediaPath(outputPath, nameof(outputPath));

        var arguments = new List<string> { "-hide_banner", "-y", "-i", videoPath };
        foreach (var input in audioInputs) arguments.AddRange(["-i", input.Path]);

        var filters = new List<string>();
        var mixInputs = new StringBuilder();
        var streamIndex = 0;
        const string stereoProfile = "aresample=48000,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,";
        if (videoHasAudio)
        {
            filters.Add($"[0:a:0]{stereoProfile}asetpts=PTS-STARTPTS[baseaudio]");
            mixInputs.Append("[baseaudio]");
            streamIndex++;
        }
        for (var index = 0; index < audioInputs.Count; index++)
        {
            var delayMilliseconds = Math.Max(0, (long)Math.Round(
                audioInputs[index].TimelineStart.TotalMilliseconds,
                MidpointRounding.AwayFromZero));
            var volume = audioInputs[index].IsMuted
                ? "volume=0,"
                : Math.Abs(audioInputs[index].GainDecibels) > 0.000_001
                    ? $"volume={audioInputs[index].GainDecibels.ToString("0.###", CultureInfo.InvariantCulture)}dB,"
                    : string.Empty;
            var pan = string.Empty;
            if (Math.Abs(audioInputs[index].Pan) > 0.000_001)
            {
                var leftGain = audioInputs[index].Pan > 0 ? 1 - audioInputs[index].Pan : 1;
                var rightGain = audioInputs[index].Pan < 0 ? 1 + audioInputs[index].Pan : 1;
                pan = $"pan=stereo|c0={FormatUnitValue(leftGain)}*c0|c1={FormatUnitValue(rightGain)}*c1,";
            }
            var fade = new StringBuilder();
            if (audioInputs[index].AudibleDurationSeconds is { } durationSeconds &&
                (audioInputs[index].FadeIn > TimeSpan.Zero || audioInputs[index].FadeOut > TimeSpan.Zero))
                fade.Append(CultureInfo.InvariantCulture, $"atrim=duration={FormatSeconds(durationSeconds)},");
            if (audioInputs[index].FadeIn > TimeSpan.Zero)
                fade.Append(CultureInfo.InvariantCulture,
                    $"afade=t=in:st=0:d={FormatSeconds(audioInputs[index].FadeIn.TotalSeconds)},");
            if (audioInputs[index].FadeOut > TimeSpan.Zero)
            {
                var fadeOutStart = audioInputs[index].AudibleDurationSeconds!.Value -
                                   audioInputs[index].FadeOut.TotalSeconds;
                fade.Append(CultureInfo.InvariantCulture,
                    $"afade=t=out:st={FormatSeconds(fadeOutStart)}:d={FormatSeconds(audioInputs[index].FadeOut.TotalSeconds)},");
            }
            filters.Add($"[{index + 1}:a:0]{stereoProfile}{volume}{pan}{fade}adelay={delayMilliseconds}:all=1,asetpts=PTS-STARTPTS[overlay{index}]");
            mixInputs.Append(CultureInfo.InvariantCulture, $"[overlay{index}]");
            streamIndex++;
        }

        filters.Add(streamIndex == 1
            ? $"{mixInputs}anull,apad[aout]"
            : $"{mixInputs}amix=inputs={streamIndex}:duration=longest:dropout_transition=0,apad[aout]");
        arguments.AddRange([
            "-filter_complex", string.Join(';', filters),
            "-map", "0:v:0",
            "-map", "[aout]",
            "-c:v", "copy",
            "-c:a", "aac",
            "-ar", "48000",
            "-shortest",
            "-movflags", "+faststart",
            outputPath
        ]);
        return arguments;
    }

    public static IReadOnlyList<string> BuildAuditionAudioMixArguments(
        IReadOnlyList<AudioOverlayInput> audioInputs,
        double compositionDurationSeconds,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(audioInputs);
        if (audioInputs.Count == 0)
            throw new ArgumentException("At least one audition audio input is required.", nameof(audioInputs));
        if (!double.IsFinite(compositionDurationSeconds) || compositionDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(compositionDurationSeconds));
        foreach (var input in audioInputs)
        {
            ValidateMediaPath(input.Path, nameof(audioInputs));
            if (input.TimelineStart < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audition audio start times cannot be negative.");
            if (!double.IsFinite(input.GainDecibels) || input.GainDecibels is < -60 or > 12)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audition audio gain must be between -60 dB and +12 dB.");
            if (!double.IsFinite(input.Pan) || input.Pan is < -1 or > 1)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audition audio pan must be between -1 and +1.");
            if (input.FadeIn < TimeSpan.Zero || input.FadeOut < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audition audio fades cannot be negative.");
            if ((input.FadeIn > TimeSpan.Zero || input.FadeOut > TimeSpan.Zero) &&
                input.AudibleDurationSeconds is not > 0)
                throw new ArgumentOutOfRangeException(nameof(audioInputs), "Audition audio fades require a known audible duration.");
        }
        ValidateMediaPath(outputPath, nameof(outputPath));
        if (!Path.GetExtension(outputPath).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Audition audio output must use the .m4a file type.", nameof(outputPath));

        var arguments = new List<string> { "-hide_banner", "-y" };
        foreach (var input in audioInputs) arguments.AddRange(["-i", input.Path]);

        var filters = new List<string>();
        var mixInputs = new StringBuilder();
        const string stereoProfile = "aresample=48000,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,";
        for (var index = 0; index < audioInputs.Count; index++)
        {
            var input = audioInputs[index];
            var delayMilliseconds = Math.Max(0, (long)Math.Round(
                input.TimelineStart.TotalMilliseconds,
                MidpointRounding.AwayFromZero));
            var volume = input.IsMuted
                ? "volume=0,"
                : Math.Abs(input.GainDecibels) > 0.000_001
                    ? $"volume={input.GainDecibels.ToString("0.###", CultureInfo.InvariantCulture)}dB,"
                    : string.Empty;
            var pan = string.Empty;
            if (Math.Abs(input.Pan) > 0.000_001)
            {
                var leftGain = input.Pan > 0 ? 1 - input.Pan : 1;
                var rightGain = input.Pan < 0 ? 1 + input.Pan : 1;
                pan = $"pan=stereo|c0={FormatUnitValue(leftGain)}*c0|c1={FormatUnitValue(rightGain)}*c1,";
            }
            var fade = new StringBuilder();
            if (input.AudibleDurationSeconds is { } durationSeconds)
                fade.Append(CultureInfo.InvariantCulture, $"atrim=duration={FormatSeconds(durationSeconds)},");
            if (input.FadeIn > TimeSpan.Zero)
                fade.Append(CultureInfo.InvariantCulture,
                    $"afade=t=in:st=0:d={FormatSeconds(input.FadeIn.TotalSeconds)},");
            if (input.FadeOut > TimeSpan.Zero)
            {
                var fadeOutStart = input.AudibleDurationSeconds!.Value - input.FadeOut.TotalSeconds;
                fade.Append(CultureInfo.InvariantCulture,
                    $"afade=t=out:st={FormatSeconds(fadeOutStart)}:d={FormatSeconds(input.FadeOut.TotalSeconds)},");
            }
            filters.Add(
                $"[{index}:a:0]{stereoProfile}{volume}{pan}{fade}" +
                $"adelay={delayMilliseconds}:all=1,asetpts=PTS-STARTPTS[audition{index}]");
            mixInputs.Append(CultureInfo.InvariantCulture, $"[audition{index}]");
        }

        filters.Add(audioInputs.Count == 1
            ? $"{mixInputs}anull,apad,atrim=duration={FormatSeconds(compositionDurationSeconds)}[aout]"
            : $"{mixInputs}amix=inputs={audioInputs.Count}:duration=longest:dropout_transition=0," +
              $"apad,atrim=duration={FormatSeconds(compositionDurationSeconds)}[aout]");
        arguments.AddRange([
            "-filter_complex", string.Join(';', filters),
            "-map", "[aout]",
            "-vn",
            "-c:a", "aac",
            "-ar", "48000",
            "-movflags", "+faststart",
            outputPath
        ]);
        return arguments;
    }

    public static IReadOnlyList<string> BuildNormalizedConcatArguments(
        IReadOnlyList<NormalizedConcatInput> inputs,
        string outputPath,
        NormalizedConcatProfile profile)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(profile);
        if (inputs.Count < 2)
            throw new ArgumentException("Concat requires at least two inputs.", nameof(inputs));
        if (profile.Width <= 0 || profile.Height <= 0 || profile.FramesPerSecond <= 0 ||
            double.IsNaN(profile.FramesPerSecond) || double.IsInfinity(profile.FramesPerSecond))
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (profile.AudioSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(profile));
        foreach (var input in inputs)
        {
            ValidateMediaPath(input.Path, nameof(inputs));
            if (input.DurationSeconds <= 0 || double.IsNaN(input.DurationSeconds) || double.IsInfinity(input.DurationSeconds))
                throw new ArgumentOutOfRangeException(nameof(inputs), "Every normalized concat input requires a positive duration.");
        }
        ValidateMediaPath(outputPath, nameof(outputPath));

        var includeAudio = inputs.Any(input => input.AudioEnabled);
        var arguments = new List<string> { "-hide_banner", "-y" };
        foreach (var input in inputs)
            arguments.AddRange(["-i", input.Path]);

        var width = profile.Width - profile.Width % 2;
        var height = profile.Height - profile.Height % 2;
        var fps = profile.FramesPerSecond.ToString("0.###", CultureInfo.InvariantCulture);
        var filters = new List<string>();
        var concatInputs = new StringBuilder();
        for (var index = 0; index < inputs.Count; index++)
        {
            filters.Add(
                $"[{index}:v:0]scale={width}:{height}:force_original_aspect_ratio=decrease," +
                $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps},format=yuv420p," +
                $"setpts=PTS-STARTPTS[v{index}]");
            concatInputs.Append(CultureInfo.InvariantCulture, $"[v{index}]");
            if (!includeAudio) continue;

            var duration = FormatSeconds(inputs[index].DurationSeconds);
            filters.Add(inputs[index].HasAudio && inputs[index].AudioEnabled
                ? $"[{index}:a:0]aresample={profile.AudioSampleRate},aformat=channel_layouts=stereo," +
                  $"apad,atrim=duration={duration},asetpts=PTS-STARTPTS[a{index}]"
                : $"anullsrc=r={profile.AudioSampleRate}:cl=stereo,atrim=duration={duration}," +
                  $"asetpts=PTS-STARTPTS[a{index}]");
            concatInputs.Append(CultureInfo.InvariantCulture, $"[a{index}]");
        }

        filters.Add(includeAudio
            ? $"{concatInputs}concat=n={inputs.Count}:v=1:a=1[v][a]"
            : $"{concatInputs}concat=n={inputs.Count}:v=1:a=0[v]");
        arguments.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[v]"]);
        if (includeAudio) arguments.AddRange(["-map", "[a]", "-c:a", "aac"]);
        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-movflags", "+faststart", outputPath]);
        return arguments;
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatUnitValue(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void ValidateMediaPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path.Contains('\0'))
        {
            throw new ArgumentException("Paths cannot contain null characters.", parameterName);
        }
    }
}

public sealed record AudioOverlayInput(
    string Path,
    TimeSpan TimelineStart,
    bool IsMuted = false,
    double GainDecibels = 0,
    double Pan = 0,
    TimeSpan FadeIn = default,
    TimeSpan FadeOut = default,
    double? AudibleDurationSeconds = null);

public sealed record NormalizedConcatInput(
    string Path,
    double DurationSeconds,
    bool HasAudio,
    bool AudioEnabled);

public sealed record NormalizedConcatProfile(
    int Width,
    int Height,
    double FramesPerSecond,
    int AudioSampleRate = 48000);

