using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Declares only properties guaranteed by ReelForge's Saved Clip trim render.
/// Source encoding metadata describes the input and must not be reused as if it
/// described the transcoded MP4 output.
/// </summary>
public static class SavedClipOutputMetadataFactory
{
    public static MediaEncodingMetadata Create(
        MediaEncodingMetadata? sourceEncoding,
        double? durationSeconds) => new()
    {
        ContainerFormat = "mp4",
        DurationSeconds = durationSeconds,
        Video = sourceEncoding?.Video is { } sourceVideo
            ? new VideoStreamMetadata
            {
                Codec = "h264",
                Width = sourceVideo.Width,
                Height = sourceVideo.Height,
                FrameRate = sourceVideo.FrameRate
            }
            : null,
        Audio = sourceEncoding?.Audio is null
            ? null
            : new AudioStreamMetadata { Codec = "aac" }
    };
}
