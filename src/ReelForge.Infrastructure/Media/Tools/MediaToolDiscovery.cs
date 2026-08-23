using ReelForge.Application;

namespace ReelForge.Infrastructure;

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
