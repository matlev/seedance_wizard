namespace ReelForge.Application;

public enum MediaSplitBehavior
{
    BeforeSelectedFrame,
    AfterSelectedFrame
}

public sealed class MediaToolConfiguration
{
    public const long DefaultCacheSizeBytes = 10L * 1024 * 1024 * 1024;

    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
    public long CacheSizeBytes { get; set; } = DefaultCacheSizeBytes;
    public bool PersistModifiedMediaOnDisk { get; set; }
    public MediaSplitBehavior SplitBehavior { get; set; } = MediaSplitBehavior.BeforeSelectedFrame;
}

public sealed record MediaToolAvailability(
    string? FfmpegPath,
    string? FfprobePath,
    string Summary)
{
    public bool IsReady => FfmpegPath is not null && FfprobePath is not null;
}

public interface IMediaToolDiscovery
{
    MediaToolAvailability Discover(
        string? configuredFfmpegPath = null,
        string? configuredFfprobePath = null);
}
