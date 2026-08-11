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

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static void ValidateMediaPath(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (path.Contains('\0'))
        {
            throw new ArgumentException("Paths cannot contain null characters.", parameterName);
        }
    }
}

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
