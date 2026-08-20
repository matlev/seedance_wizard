using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed record ExternalProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null);

public sealed record ProcessOutputLine(bool IsError, string Text);

public sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalProcessException : Exception
{
    public ExternalProcessException(string tool, ExternalProcessResult result)
        : base($"{Path.GetFileName(tool)} exited with code {result.ExitCode}: {Summarize(result.StandardError)}")
    {
        Tool = tool;
        Result = result;
    }

    public string Tool { get; }
    public ExternalProcessResult Result { get; }

    private static string Summarize(string error)
    {
        var line = error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? "No error details were reported." : line;
    }
}

public sealed class MediaToolUnavailableException : Exception
{
    public MediaToolUnavailableException(string message) : base(message)
    {
    }
}

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{request.ExecutablePath}'.");
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var runningProcess = (Process)state!;
                try
                {
                    if (!runningProcess.HasExited)
                    {
                        runningProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the state check and Kill.
                }
            },
            process);

        var standardOutput = CaptureAsync(process.StandardOutput, isError: false, progress, cancellationToken);
        var standardError = CaptureAsync(process.StandardError, isError: true, progress, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var result = new ExternalProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));

        return result;
    }

    private static async Task<string> CaptureAsync(
        StreamReader reader,
        bool isError,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            progress?.Report(new ProcessOutputLine(isError, line));
        }

        return output.ToString();
    }
}

public sealed class MediaToolDiscovery : IMediaToolDiscovery
{
    public MediaToolAvailability Discover(string? configuredFfmpegPath = null, string? configuredFfprobePath = null)
    {
        var ffmpeg = ResolveTool(configuredFfmpegPath, "ffmpeg.exe");
        var ffprobe = ResolveTool(configuredFfprobePath, "ffprobe.exe");

        var summary = (ffmpeg, ffprobe) switch
        {
            (not null, not null) => "FFmpeg and ffprobe are ready.",
            (null, null) => "FFmpeg and ffprobe were not found. Install them or configure their paths.",
            (null, _) => "ffprobe was found, but FFmpeg was not found.",
            (_, null) => "FFmpeg was found, but ffprobe was not found."
        };

        return new MediaToolAvailability(ffmpeg, ffprobe, summary);
    }

    private static string? ResolveTool(string? configuredPath, string executableName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullConfiguredPath = Path.GetFullPath(configuredPath);
            if (File.Exists(fullConfiguredPath))
            {
                return fullConfiguredPath;
            }
        }

        var appCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, executableName),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", executableName),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin", executableName)
        };

        var pathCandidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, executableName));

        return appCandidates.Concat(pathCandidates).FirstOrDefault(File.Exists);
    }
}

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

public sealed class FfmpegAudioExtractionEngine : IAudioExtractionEngine
{
    private readonly IExternalProcessRunner _runner;
    private string? _ffmpegPath;

    public FfmpegAudioExtractionEngine(string? ffmpegPath, IExternalProcessRunner runner)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public void UpdateExecutablePath(string? ffmpegPath) => _ffmpegPath = ffmpegPath;

    public async Task ExtractToM4aAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to extract audio.");
        var result = await _runner.RunAsync(
                new ExternalProcessRequest(
                    ffmpegPath,
                    FfmpegCommandBuilder.BuildExtractAudioArguments(inputPath, outputPath)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length <= 0)
            throw new InvalidDataException("FFmpeg completed without producing extracted audio.");
    }
}

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

public sealed class FfprobeMediaInspectionService : IMediaInspectionService
{
    private string? _ffprobePath;
    private readonly IExternalProcessRunner _runner;

    public FfprobeMediaInspectionService(string? ffprobePath, IExternalProcessRunner runner)
    {
        _ffprobePath = ffprobePath;
        _runner = runner;
    }

    public void UpdateExecutablePath(string? ffprobePath) => _ffprobePath = ffprobePath;

    public async Task<MediaEncodingMetadata> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        if (_ffprobePath is null)
        {
            throw new MediaToolUnavailableException("ffprobe is not configured. The asset was imported without stream metadata.");
        }

        var arguments = new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            mediaPath
        };

        var result = await _runner
            .RunAsync(new ExternalProcessRequest(_ffprobePath, arguments), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ExternalProcessException(_ffprobePath, result);
        }

        return Parse(result.StandardOutput);
    }

    public static MediaEncodingMetadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var metadata = new MediaEncodingMetadata();

        if (root.TryGetProperty("format", out var format))
        {
            metadata.ContainerFormat = GetString(format, "format_name");
            metadata.DurationSeconds = GetDouble(format, "duration");
            metadata.SizeBytes = GetInt64(format, "size");
            metadata.BitRate = GetInt64(format, "bit_rate");
        }

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = GetString(stream, "codec_type");
                if (codecType == "video" && metadata.Video is null)
                {
                    metadata.Video = new VideoStreamMetadata
                    {
                        Codec = GetString(stream, "codec_name"),
                        CodecProfile = GetString(stream, "profile"),
                        Width = GetInt32(stream, "width"),
                        Height = GetInt32(stream, "height"),
                        PixelFormat = GetString(stream, "pix_fmt"),
                        FrameRate = GetString(stream, "avg_frame_rate") ?? GetString(stream, "r_frame_rate"),
                        TimeBase = GetString(stream, "time_base"),
                        CodecLevel = GetInt32(stream, "level")
                    };
                }
                else if (codecType == "audio" && metadata.Audio is null)
                {
                    metadata.Audio = new AudioStreamMetadata
                    {
                        Codec = GetString(stream, "codec_name"),
                        SampleRate = GetInt32(stream, "sample_rate"),
                        Channels = GetInt32(stream, "channels"),
                        ChannelLayout = GetString(stream, "channel_layout")
                    };
                }
            }
        }

        return metadata;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static int? GetInt32(JsonElement element, string propertyName) =>
        int.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static long? GetInt64(JsonElement element, string propertyName) =>
        long.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? GetDouble(JsonElement element, string propertyName) =>
        double.TryParse(GetString(element, propertyName), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
