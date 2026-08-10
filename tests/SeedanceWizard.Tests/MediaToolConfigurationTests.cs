using SeedanceWizard.Application;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class MediaToolConfigurationTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "Seedance Wizard media tool tests",
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
        var settingsPath = Path.Combine(_temporaryRoot, "settings", "settings.json");
        var store = new JsonMediaToolSettingsStore(settingsPath);
        var configuration = new MediaToolConfiguration
        {
            FfmpegPath = @"C:\Tools With Spaces\ffmpeg.exe",
            FfprobePath = @"D:\Video\ffprobe.exe"
        };

        await store.SaveAsync(configuration);
        var loaded = await store.LoadAsync();

        Assert.Equal(configuration.FfmpegPath, loaded.FfmpegPath);
        Assert.Equal(configuration.FfprobePath, loaded.FfprobePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
