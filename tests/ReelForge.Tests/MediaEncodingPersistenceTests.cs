using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class MediaEncodingPersistenceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PortableProjectStoreRoundTripsResolvedStreamDescriptors()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Stream descriptors");
        var asset = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                ContentIdentity = new ContentIdentity
                {
                    Sha256 = new string('a', 64),
                    Status = ContentHashStatus.Verified,
                    LengthBytes = 42
                }
            },
            Encoding = new MediaEncodingMetadata
            {
                Video = new VideoStreamMetadata
                {
                    StreamIndex = 4,
                    TimeBase = "1/90000",
                    TimeBaseNumerator = 1,
                    TimeBaseDenominator = 90000,
                    StartPresentationTimestamp = -180000,
                    DurationPresentationTimestamp = 405405
                },
                Audio = new AudioStreamMetadata
                {
                    StreamIndex = 7,
                    TimeBaseNumerator = 1,
                    TimeBaseDenominator = 48000,
                    StartPresentationTimestamp = 1024,
                    DurationPresentationTimestamp = 721920
                }
            }
        };
        project.AddAsset(asset);

        await store.SaveAsync(project, location);
        var (reopened, _) = await store.OpenAsync(location.ProjectFilePath);
        var encoding = Assert.Single(reopened.Assets).Encoding!;

        Assert.Equal(4, encoding.Video?.StreamIndex);
        Assert.Equal("1/90000", encoding.Video?.TimeBase);
        Assert.Equal(1, encoding.Video?.TimeBaseNumerator);
        Assert.Equal(90000, encoding.Video?.TimeBaseDenominator);
        Assert.Equal(-180000, encoding.Video?.StartPresentationTimestamp);
        Assert.Equal(405405, encoding.Video?.DurationPresentationTimestamp);
        Assert.Equal(7, encoding.Audio?.StreamIndex);
        Assert.Equal(1, encoding.Audio?.TimeBaseNumerator);
        Assert.Equal(48000, encoding.Audio?.TimeBaseDenominator);
        Assert.Equal(1024, encoding.Audio?.StartPresentationTimestamp);
        Assert.Equal(721920, encoding.Audio?.DurationPresentationTimestamp);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
