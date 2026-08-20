using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class CompositionSegmentAudioDetachmentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"ReelForge-segment-audio-detachment-{Guid.NewGuid():N}");

    [Fact]
    public async Task DetachCreatesPermanentAudioAtSegmentStartAndMutesOnlyItsSourceSegment()
    {
        var workspace = await CreateWorkspaceAsync();
        var first = AddVideo(workspace.Project!, "first.mp4", 4);
        var second = AddVideo(workspace.Project!, "second.mp4", 6);
        var compositionService = new WorkingCompositionService(workspace);
        var composition = await compositionService.CreateInitialAsync(first.Id);
        await compositionService.AddSegmentAsync(second.Id);
        var before = compositionService.GetCurrent();
        var targetSegment = before.Recipe.Segments[1];
        var materializer = new StubSegmentMaterializer(Path.Combine(_root, "selected-segment.mp4"));
        var service = new CompositionSegmentAudioDetachmentService(
            workspace,
            materializer,
            new StubExtractionEngine(),
            new Sha256ContentHashService(),
            new AudioOnlyInspector());

        var result = await service.DetachAsync(targetSegment.Id, "second detached.m4a");

        Assert.Equal(composition.Id, materializer.CompositionAssetId);
        Assert.Equal(before.Revision.Id, materializer.RecipeRevisionId);
        Assert.Equal(targetSegment.Id, materializer.SegmentId);
        Assert.Equal(TimeSpan.FromSeconds(4), result.TimelineStart);
        Assert.Equal("second detached.m4a", result.AudioAsset.FileName);
        Assert.Equal(AssetStorageKind.Physical, result.AudioAsset.StorageKind);
        Assert.Equal(PhysicalAssetDurability.Promoted, result.AudioAsset.Physical!.Durability);
        Assert.Equal("detach-segment-audio", result.AudioAsset.Provenance!.Operation);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(result.AudioAsset)));

        var current = compositionService.GetCurrent();
        Assert.True(current.Recipe.Segments[0].AudioEnabled);
        Assert.False(current.Recipe.Segments[1].AudioEnabled);
        var detachedClip = Assert.Single(current.Recipe.AudioClips);
        Assert.Equal(result.AudioClipId, detachedClip.Id);
        Assert.Equal(result.AudioAsset.Id, detachedClip.Source.AssetId);
        Assert.Equal(TimeSpan.FromSeconds(4).Ticks, detachedClip.TimelineStartTicks);
        Assert.Empty(ProjectInvariantValidator.Validate(workspace.Project!));

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        Assert.Contains(reopened.Assets, asset => asset.Id == result.AudioAsset.Id);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    private async Task<ProjectWorkspace> CreateWorkspaceAsync()
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Segment Audio Detachment");
        return workspace;
    }

    private ProjectAsset AddVideo(VideoProject project, string fileName, double durationSeconds)
    {
        var relativePath = $"assets/videos/{fileName}";
        var fullPath = Path.Combine(_root, "assets", "videos", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, [1, 2, 3]);
        var asset = new ProjectAsset
        {
            DisplayName = fileName,
            FileName = fileName,
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            DurationSeconds = durationSeconds,
            Encoding = new MediaEncodingMetadata
            {
                DurationSeconds = durationSeconds,
                Video = new VideoStreamMetadata { Codec = "h264", Width = 1280, Height = 720 },
                Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 }
            },
            Physical = new PhysicalAssetStorage
            {
                RelativePath = relativePath,
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = new ContentIdentity
                {
                    Sha256 = new string('a', 64),
                    Status = ContentHashStatus.Verified
                }
            }
        };
        project.AddAsset(asset);
        return asset;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubSegmentMaterializer(string path) : ICompositionSegmentMaterializer
    {
        public Guid CompositionAssetId { get; private set; }
        public Guid RecipeRevisionId { get; private set; }
        public Guid SegmentId { get; private set; }

        public Task<MaterializedMediaLease> MaterializeSegmentAsync(
            VideoProject project,
            ProjectLocation location,
            Guid compositionAssetId,
            Guid recipeRevisionId,
            Guid segmentId,
            MaterializationPurpose purpose,
            CancellationToken cancellationToken = default)
        {
            CompositionAssetId = compositionAssetId;
            RecipeRevisionId = recipeRevisionId;
            SegmentId = segmentId;
            File.WriteAllBytes(path, [8, 9]);
            return Task.FromResult(new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified },
                new MediaEncodingMetadata
                {
                    DurationSeconds = 6,
                    Video = new VideoStreamMetadata { Codec = "h264", Width = 1280, Height = 720 },
                    Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 }
                },
                isDurableSource: false));
        }
    }

    private sealed class StubExtractionEngine : IAudioExtractionEngine
    {
        public Task ExtractToM4aAsync(
            string inputPath,
            string outputPath,
            CancellationToken cancellationToken = default) =>
            File.WriteAllBytesAsync(outputPath, [4, 5, 6, 7], cancellationToken);
    }

    private sealed class AudioOnlyInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) => Task.FromResult(new MediaEncodingMetadata
        {
            ContainerFormat = "mov,mp4,m4a,3gp,3g2,mj2",
            DurationSeconds = 6,
            SizeBytes = 4,
            Audio = new AudioStreamMetadata
            {
                Codec = "aac",
                SampleRate = 48000,
                Channels = 2,
                ChannelLayout = "stereo"
            }
        });
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
