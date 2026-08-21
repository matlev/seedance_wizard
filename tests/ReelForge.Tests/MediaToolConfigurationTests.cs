using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class MediaToolConfigurationTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge media tool tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DiscoveryPrefersExplicitExecutablePaths()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var ffmpeg = Path.Combine(_temporaryRoot, "ffmpeg.exe");
        var ffprobe = Path.Combine(_temporaryRoot, "ffprobe.exe");
        File.WriteAllBytes(ffmpeg, []);
        File.WriteAllBytes(ffprobe, []);

        var result = new MediaToolDiscovery().Discover(ffmpeg, ffprobe);

        Assert.True(result.IsReady);
        Assert.Equal(Path.GetFullPath(ffmpeg), result.FfmpegPath);
        Assert.Equal(Path.GetFullPath(ffprobe), result.FfprobePath);
    }

    [Fact]
    public async Task SettingsRoundTripExplicitPaths()
    {
        var settingsPath = Path.Combine(_temporaryRoot, "settings", "appsettings.local.json");
        var store = new JsonApplicationSettingsStore(
            settingsPath,
            Path.Combine(_temporaryRoot, "missing-defaults.json"));
        var configuration = new ApplicationSettings();
        configuration.MediaTools.FfmpegPath = @"C:\Tools With Spaces\ffmpeg.exe";
        configuration.MediaTools.FfprobePath = @"D:\Video\ffprobe.exe";

        await store.SaveAsync(configuration);
        var loaded = await store.LoadAsync();

        Assert.Equal(configuration.MediaTools.FfmpegPath, loaded.MediaTools.FfmpegPath);
        Assert.Equal(configuration.MediaTools.FfprobePath, loaded.MediaTools.FfprobePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
