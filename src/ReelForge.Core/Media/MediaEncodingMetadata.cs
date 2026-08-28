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
    /// <summary>
    /// The selected stream's non-negative container-local index, when inspection reported one.
    /// </summary>
    public int? StreamIndex { get; set; }
    public string? Codec { get; set; }
    public string? CodecProfile { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? PixelFormat { get; set; }
    public string? FrameRate { get; set; }
    public string? TimeBase { get; set; }
    /// <summary>
    /// Numerator of the selected stream's native presentation-time unit in seconds.
    /// Only available when the inspected time base was a positive integer rational.
    /// </summary>
    public int? TimeBaseNumerator { get; set; }
    /// <summary>
    /// Denominator of the selected stream's native presentation-time unit in seconds.
    /// Only available when the inspected time base was a positive integer rational.
    /// </summary>
    public int? TimeBaseDenominator { get; set; }
    /// <summary>
    /// The selected stream's native presentation timestamp at its start, when reported.
    /// This is not a decoded-frame boundary.
    /// </summary>
    public long? StartPresentationTimestamp { get; set; }
    /// <summary>
    /// The selected stream's native presentation duration, when reported as non-negative.
    /// This is not a decoded-frame boundary.
    /// </summary>
    public long? DurationPresentationTimestamp { get; set; }
    public int? CodecLevel { get; set; }
}

public sealed class AudioStreamMetadata
{
    /// <summary>
    /// The selected stream's non-negative container-local index, when inspection reported one.
    /// </summary>
    public int? StreamIndex { get; set; }
    public string? Codec { get; set; }
    public int? SampleRate { get; set; }
    public int? Channels { get; set; }
    public string? ChannelLayout { get; set; }
    /// <summary>
    /// Numerator of the selected stream's native presentation-time unit in seconds.
    /// Only available when the inspected time base was a positive integer rational.
    /// </summary>
    public int? TimeBaseNumerator { get; set; }
    /// <summary>
    /// Denominator of the selected stream's native presentation-time unit in seconds.
    /// Only available when the inspected time base was a positive integer rational.
    /// </summary>
    public int? TimeBaseDenominator { get; set; }
    /// <summary>
    /// The selected stream's native presentation timestamp at its start, when reported.
    /// This is not a decoded-sample boundary.
    /// </summary>
    public long? StartPresentationTimestamp { get; set; }
    /// <summary>
    /// The selected stream's native presentation duration, when reported as non-negative.
    /// This is not a decoded-sample boundary.
    /// </summary>
    public long? DurationPresentationTimestamp { get; set; }
}
