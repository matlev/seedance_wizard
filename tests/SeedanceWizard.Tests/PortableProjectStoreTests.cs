using SeedanceWizard.Core;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class PortableProjectStoreTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "Seedance Wizard tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateSaveOpenRoundTripsProjectAndCreatesPortableLayout()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Portable demo");
        project.Assets.Add(new ProjectAsset
        {
            FileName = "clip one.mp4",
            RelativePath = "assets/videos/clip one.mp4",
            MediaType = MediaType.Video,
            Origin = AssetOrigin.Imported,
            DurationSeconds = 12.5
        });
        project.Generations.Add(new GenerationRecord
        {
            ProviderId = "fake.seedance",
            ModelVersion = "development-v1",
            Status = GenerationStatus.Succeeded,
            Request = new GenerationRequest
            {
                Prompt = "A lantern drifting through fog",
                Mode = GenerationMode.TextToVideo,
                DurationSeconds = 15,
                AspectRatio = "16:9",
                Resolution = "720p"
            }
        });

        await store.SaveAsync(project, location);
        var (reopened, reopenedLocation) = await store.OpenAsync(location.ProjectFilePath);

        Assert.Equal(project.Id, reopened.Id);
        Assert.Equal("Portable demo", reopened.Name);
        Assert.Equal("clip one.mp4", Assert.Single(reopened.Assets).FileName);
        Assert.Equal("A lantern drifting through fog", Assert.Single(reopened.Generations).Request.Prompt);
        Assert.Equal(Path.GetFullPath(_temporaryRoot), reopenedLocation.RootDirectory);
        Assert.True(File.Exists(Path.Combine(_temporaryRoot, PortableProjectStore.ProjectFileName)));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "assets", "images")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "assets", "videos")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "assets", "audio")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "generated")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "exports")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "cache")));
    }

    [Fact]
    public void FirstGeneratedVideoBecomesMainVideo()
    {
        var project = new VideoProject();
        var importedVideo = new ProjectAsset { MediaType = MediaType.Video, Origin = AssetOrigin.Imported };
        var generatedVideo = new ProjectAsset { MediaType = MediaType.Video, Origin = AssetOrigin.Generated };

        project.AddAsset(importedVideo);
        project.AddAsset(generatedVideo);

        Assert.Equal(generatedVideo.Id, project.MainVideoAssetId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
