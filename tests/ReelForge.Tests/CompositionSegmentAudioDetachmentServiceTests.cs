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

    [Fact]
    public async Task DetachFromSavedClipWithLegacyMissingAudioMetadataInspectsTheMaterializedSegment()
    {
        var workspace = await CreateWorkspaceAsync();
        var project = workspace.Project!;
        var physical = AddVideo(project, "source.mp4", 6);
        var clip = new ProjectAsset
        {
            DisplayName = "Legacy saved clip",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata
                {
                    DurationSeconds = 6,
                    Video = new VideoStreamMetadata { Codec = "h264" }
                }
            }
        };
        project.AddAsset(clip);
        project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = physical.Id }
        });
        await workspace.SaveAsync();
        var composition = await new WorkingCompositionService(workspace).CreateInitialAsync(clip.Id);
        var segment = new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single();
        var extraction = new StubExtractionEngine();
        var inspector = new AudioOnlyInspector();
        var result = await new CompositionSegmentAudioDetachmentService(
                workspace,
                new StubSegmentMaterializer(
                    Path.Combine(_root, "legacy-saved-clip.mp4"),
                    new MediaEncodingMetadata
                    {
                        DurationSeconds = 6,
                        Video = new VideoStreamMetadata { Codec = "h264" }
                    }),
                extraction,
                new Sha256ContentHashService(),
                inspector)
            .DetachAsync(segment.Id, "legacy clip audio.m4a");

        Assert.Equal(composition.Id, result.CompositionRevision.VirtualAssetId);
        Assert.Single(extraction.OutputPaths);
        Assert.Equal(2, inspector.CallCount);
        Assert.Equal(clip.Id, Assert.Single(result.AudioAsset.Provenance!.SourceAssetIds));
        Assert.False(new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single().AudioEnabled);
    }

    [Fact]
    public async Task SaveFailureRollsBackDetachedAudioAndCompositionState()
    {
        var store = new ToggleFailingProjectStore();
        var workspace = await CreateWorkspaceAsync(store);
        var project = workspace.Project!;
        var first = AddVideo(project, "rollback-first.mp4", 4);
        var second = AddVideo(project, "rollback-second.mp4", 6);
        var compositionService = new WorkingCompositionService(workspace);
        var composition = await compositionService.CreateInitialAsync(first.Id);
        await compositionService.AddSegmentAsync(second.Id);
        var before = compositionService.GetCurrent();
        var draft = Assert.Single(project.RecipeDrafts);
        var originalModifiedAt = project.ModifiedAt;
        var originalAssets = project.Assets.ToArray();
        var originalSources = composition.Provenance!.SourceAssetIds.ToArray();
        var originalDraftBasedOnRevisionId = draft.BasedOnRevisionId;
        var originalDraftRecipe = draft.EditableRecipe;
        var originalDraftModifiedAt = draft.ModifiedAt;
        var extraction = new StubExtractionEngine();
        store.FailSaves = true;
        var service = new CompositionSegmentAudioDetachmentService(
            workspace,
            new StubSegmentMaterializer(Path.Combine(_root, "rollback-selected-segment.mp4")),
            extraction,
            new Sha256ContentHashService(),
            new AudioOnlyInspector());

        await Assert.ThrowsAsync<IOException>(() =>
            service.DetachAsync(before.Recipe.Segments[1].Id, "rollback detached.m4a"));

        var after = compositionService.GetCurrent();
        Assert.Equal(originalModifiedAt, project.ModifiedAt);
        Assert.Equal(originalAssets, project.Assets);
        Assert.Same(before.Asset, after.Asset);
        Assert.Same(before.Revision, after.Revision);
        Assert.Same(before.Recipe, after.Recipe);
        Assert.Equal(originalDraftBasedOnRevisionId, draft.BasedOnRevisionId);
        Assert.Same(originalDraftRecipe, draft.EditableRecipe);
        Assert.Equal(originalDraftModifiedAt, draft.ModifiedAt);
        Assert.Equal(originalSources, composition.Provenance.SourceAssetIds);
        Assert.DoesNotContain(project.Assets, asset => asset.Provenance?.Operation == "detach-segment-audio");
        Assert.Empty(after.Recipe.AudioClips);
        Assert.False(File.Exists(Path.Combine(_root, "assets", "audio", "rollback detached.m4a")));
        Assert.All(extraction.OutputPaths, path => Assert.False(File.Exists(path)));
    }

    private async Task<ProjectWorkspace> CreateWorkspaceAsync(IProjectStore? store = null)
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(store ?? new PortableProjectStore(), new UnusedImporter());
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

    private sealed class StubSegmentMaterializer(string path, MediaEncodingMetadata? encoding = null) : ICompositionSegmentMaterializer
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
                encoding ?? new MediaEncodingMetadata
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
        public List<string> OutputPaths { get; } = [];

        public Task ExtractToM4aAsync(
            string inputPath,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            OutputPaths.Add(outputPath);
            return File.WriteAllBytesAsync(outputPath, [4, 5, 6, 7], cancellationToken);
        }
    }

    private sealed class AudioOnlyInspector : IMediaInspectionService
    {
        public int CallCount { get; private set; }

        public Task<MediaEncodingMetadata> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new MediaEncodingMetadata
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
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ToggleFailingProjectStore : IProjectStore
    {
        private readonly PortableProjectStore _inner = new();

        public bool FailSaves { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(
            VideoProject project,
            ProjectLocation location,
            CancellationToken cancellationToken = default) =>
            FailSaves
                ? Task.FromException(new IOException("Simulated project save failure."))
                : _inner.SaveAsync(project, location, cancellationToken);
    }
}
