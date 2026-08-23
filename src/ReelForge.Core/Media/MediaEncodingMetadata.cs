namespace ReelForge.Core;

public sealed class MediaEncodingMetadata
{
    public string? ContainerFormat { get; set; }
    public double? DurationSeconds { get; set; }
    public long? SizeBytes { get; set; }
    public long? BitRate { get; set; }
    public VideoStreamMetadata? Video { get; set; }
    public AudioStreamMetadata? Audio { get; set; }
}

public sealed class VideoStreamMetadata
{
    public string? Codec { get; set; }
    public string? CodecProfile { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? PixelFormat { get; set; }
    public string? FrameRate { get; set; }
    public string? TimeBase { get; set; }
    public int? CodecLevel { get; set; }
}

public sealed class AudioStreamMetadata
{
    public string? Codec { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
}
