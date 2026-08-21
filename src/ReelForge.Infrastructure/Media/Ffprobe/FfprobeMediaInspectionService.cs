using System.Globalization;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

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
