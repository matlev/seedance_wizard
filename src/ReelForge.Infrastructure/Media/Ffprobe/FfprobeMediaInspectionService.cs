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
        if (root.ValueKind != JsonValueKind.Object)
        {
            return metadata;
        }

        if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
        {
            metadata.ContainerFormat = GetString(format, "format_name");
            metadata.DurationSeconds = GetDouble(format, "duration");
            metadata.SizeBytes = GetInt64(format, "size");
            metadata.BitRate = GetInt64(format, "bit_rate");
        }

        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            var videoCandidates = new List<StreamCandidate>();
            var audioCandidates = new List<StreamCandidate>();

            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var codecType = GetString(stream, "codec_type");
                var streamIndex = GetInt32(stream, "index");
                if (streamIndex is not >= 0)
                {
                    continue;
                }

                var candidate = new StreamCandidate(stream, streamIndex.Value, IsDefault(stream));
                if (codecType == "video" && !IsAttachedPicture(stream))
                {
                    videoCandidates.Add(candidate);
                }
                else if (codecType == "audio")
                {
                    audioCandidates.Add(candidate);
                }
            }

            var video = ResolveSelectedStream(videoCandidates);
            if (video is not null)
            {
                var timeBase = GetString(video.Stream, "time_base");
                var (numerator, denominator) = ParsePositiveTimeBase(timeBase);
                metadata.Video = new VideoStreamMetadata
                {
                    StreamIndex = video.Index,
                    Codec = GetString(video.Stream, "codec_name"),
                    CodecProfile = GetString(video.Stream, "profile"),
                    Width = GetInt32(video.Stream, "width"),
                    Height = GetInt32(video.Stream, "height"),
                    PixelFormat = GetString(video.Stream, "pix_fmt"),
                    FrameRate = GetString(video.Stream, "avg_frame_rate") ?? GetString(video.Stream, "r_frame_rate"),
                    TimeBase = timeBase,
                    TimeBaseNumerator = numerator,
                    TimeBaseDenominator = denominator,
                    StartPresentationTimestamp = GetInt64(video.Stream, "start_pts"),
                    DurationPresentationTimestamp = GetNonNegativeInt64(video.Stream, "duration_ts"),
                    CodecLevel = GetInt32(video.Stream, "level")
                };
            }

            var audio = ResolveSelectedStream(audioCandidates);
            if (audio is not null)
            {
                var (numerator, denominator) = ParsePositiveTimeBase(GetString(audio.Stream, "time_base"));
                metadata.Audio = new AudioStreamMetadata
                {
                    StreamIndex = audio.Index,
                    Codec = GetString(audio.Stream, "codec_name"),
                    SampleRate = GetInt32(audio.Stream, "sample_rate"),
                    Channels = GetInt32(audio.Stream, "channels"),
                    ChannelLayout = GetString(audio.Stream, "channel_layout"),
                    TimeBaseNumerator = numerator,
                    TimeBaseDenominator = denominator,
                    StartPresentationTimestamp = GetInt64(audio.Stream, "start_pts"),
                    DurationPresentationTimestamp = GetNonNegativeInt64(audio.Stream, "duration_ts")
                };
            }
        }

        return metadata;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind != JsonValueKind.Null
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

    private static long? GetNonNegativeInt64(JsonElement element, string propertyName) =>
        GetInt64(element, propertyName) is { } value && value >= 0 ? value : null;

    private static bool IsDefault(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition) &&
        disposition.ValueKind == JsonValueKind.Object &&
        GetInt32(disposition, "default") is > 0;

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var disposition) &&
        disposition.ValueKind == JsonValueKind.Object &&
        GetInt32(disposition, "attached_pic") is > 0;

    private static StreamCandidate? ResolveSelectedStream(IEnumerable<StreamCandidate> candidates) =>
        candidates
            .OrderByDescending(candidate => candidate.IsDefault)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();

    private static (int? Numerator, int? Denominator) ParsePositiveTimeBase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var parts = value.Split('/');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numerator) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var denominator) ||
            numerator <= 0 || denominator <= 0)
        {
            return (null, null);
        }

        return (numerator, denominator);
    }

    private static double? GetDouble(JsonElement element, string propertyName) =>
        double.TryParse(GetString(element, propertyName), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private sealed record StreamCandidate(JsonElement Stream, int Index, bool IsDefault);
}
