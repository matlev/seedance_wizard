using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class AudioExtractionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ReelForge-audio-extraction-{Guid.NewGuid():N}");

    [Fact]
    public async Task PhysicalVideoExtractionCreatesDurableAudioWithProvenance()
    {
        var (workspace, source, sourcePath) = await CreateProjectWithSourceAsync(hasAudio: true);
        var materializer = new StubMaterializer(sourcePath, source.Encoding!);
        var engine = new StubExtractionEngine();
        var service = CreateService(workspace, materializer, engine);

        var extracted = await service.ExtractAsAssetAsync(source.Id, null, "dialogue.m4a");

        Assert.Equal(MediaType.Audio, extracted.MediaType);
        Assert.Equal(AssetOrigin.ExtractedAudio, extracted.Origin);
        Assert.Equal(AssetStorageKind.Physical, extracted.StorageKind);
        Assert.Equal(PhysicalAssetDurability.Promoted, extracted.Physical!.Durability);
        Assert.Equal("assets/audio/dialogue.m4a", extracted.Physical.RelativePath);
        Assert.Equal(ContentHashStatus.Verified, extracted.Physical.ContentIdentity.Status);
        Assert.Equal("extract-audio", extracted.Provenance!.Operation);
        Assert.Equal(source.Id, Assert.Single(extracted.Provenance.SourceAssetIds));
        Assert.Null(extracted.Provenance.SourceRecipeRevisionId);
        Assert.Equal("aac", extracted.Encoding!.Audio!.Codec);
        Assert.Null(extracted.Encoding.Video);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(extracted)));
        Assert.Equal(1, engine.CallCount);
        var target = Assert.IsType<AssetMaterializationTarget>(materializer.LastRequest!.Target);
        Assert.Equal(source.Id, target.AssetId);
        Assert.Null(target.RecipeRevisionId);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        Assert.Contains(reopened.Assets, asset => asset.Id == extracted.Id);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task SavedClipExtractionPinsTheSelectedRecipeRevision()
    {
        var (workspace, source, sourcePath) = await CreateProjectWithSourceAsync(hasAudio: true);
        var clip = new ProjectAsset
        {
            DisplayName = "Favorite scene",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = source.Encoding
            }
        };
        workspace.Project!.AddAsset(clip);
        var revision = workspace.Project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id }
        });
        await workspace.SaveAsync();
        var materializer = new StubMaterializer(sourcePath, source.Encoding!);
        var service = CreateService(workspace, materializer, new StubExtractionEngine());

        var extracted = await service.ExtractAsAssetAsync(clip.Id, revision.Id, "favorite scene audio.m4a");

        var target = Assert.IsType<AssetMaterializationTarget>(materializer.LastRequest!.Target);
        Assert.Equal(clip.Id, target.AssetId);
        Assert.Equal(revision.Id, target.RecipeRevisionId);
        Assert.Equal(revision.Id, extracted.Provenance!.SourceRecipeRevisionId);
        Assert.Equal(clip.Id, Assert.Single(extracted.Provenance.SourceAssetIds));
        Assert.Empty(ProjectInvariantValidator.Validate(workspace.Project));
    }

    [Fact]
    public async Task SavedClipWithLegacyMissingAudioMetadataInspectsItsMaterializedMedia()
    {
        var (workspace, source, sourcePath) = await CreateProjectWithSourceAsync(hasAudio: true);
        var clip = new ProjectAsset
        {
            DisplayName = "Legacy favorite scene",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata
                {
                    DurationSeconds = source.Encoding!.DurationSeconds,
                    Video = new VideoStreamMetadata { Codec = "h264" }
                }
            }
        };
        workspace.Project!.AddAsset(clip);
        var revision = workspace.Project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id }
        });
        await workspace.SaveAsync();
        var engine = new StubExtractionEngine();
        var inspector = new OutputInspector();
        var extracted = await CreateService(
                workspace,
                new StubMaterializer(sourcePath, new MediaEncodingMetadata
                {
                    DurationSeconds = source.Encoding!.DurationSeconds,
                    Video = new VideoStreamMetadata { Codec = "h264" }
                }),
                engine,
                inspector)
            .ExtractAsAssetAsync(clip.Id, revision.Id, "legacy scene audio.m4a");

        Assert.Equal(1, engine.CallCount);
        Assert.Equal(2, inspector.CallCount);
        Assert.Equal(clip.Id, Assert.Single(extracted.Provenance!.SourceAssetIds));
        Assert.Equal(revision.Id, extracted.Provenance.SourceRecipeRevisionId);
    }

    [Fact]
    public async Task SourceWithoutAudioDoesNotRunFfmpegOrCreateAnAsset()
    {
        var (workspace, source, sourcePath) = await CreateProjectWithSourceAsync(hasAudio: false);
        var materializer = new StubMaterializer(sourcePath, source.Encoding!);
        var engine = new StubExtractionEngine();
        var service = CreateService(workspace, materializer, engine, new NoAudioInspector());
        var originalAssetCount = workspace.Project!.Assets.Count;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExtractAsAssetAsync(source.Id, null, "silent.m4a"));

        Assert.Contains("has no audio stream", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.CallCount);
        Assert.Equal(originalAssetCount, workspace.Project.Assets.Count);
        var audioDirectory = Path.Combine(workspace.Location!.RootDirectory, "assets", "audio");
        Assert.Empty(Directory.EnumerateFiles(audioDirectory));
    }

    [Fact]
    public async Task ExistingAudioFilenameIsPreservedAndAUniqueNameIsUsed()
    {
        var (workspace, source, sourcePath) = await CreateProjectWithSourceAsync(hasAudio: true);
        var audioDirectory = Path.Combine(workspace.Location!.RootDirectory, "assets", "audio");
        Directory.CreateDirectory(audioDirectory);
        await File.WriteAllBytesAsync(Path.Combine(audioDirectory, "dialogue.m4a"), [9]);
        var service = CreateService(
            workspace,
            new StubMaterializer(sourcePath, source.Encoding!),
            new StubExtractionEngine());

        var extracted = await service.ExtractAsAssetAsync(source.Id, null, "dialogue.m4a");

        Assert.Equal("dialogue (2).m4a", extracted.FileName);
        Assert.Equal([9], await File.ReadAllBytesAsync(Path.Combine(audioDirectory, "dialogue.m4a")));
    }

    private static AudioExtractionService CreateService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IAudioExtractionEngine engine,
        IMediaInspectionService? inspector = null) => new(
        workspace,
        materializer,
        engine,
        new Sha256ContentHashService(),
        inspector ?? new OutputInspector());

    private async Task<(ProjectWorkspace Workspace, ProjectAsset Source, string SourcePath)>
        CreateProjectWithSourceAsync(bool hasAudio)
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Audio Extraction");
        var sourcePath = Path.Combine(workspace.Location!.RootDirectory, "assets", "videos", "source.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        var encoding = new MediaEncodingMetadata
        {
            DurationSeconds = 8,
            Video = new VideoStreamMetadata { Codec = "h264", Width = 1280, Height = 720 },
            Audio = hasAudio ? new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 } : null
        };
        var source = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Encoding = encoding,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = new ContentIdentity
                {
                    Sha256 = new string('a', 64),
                    Status = ContentHashStatus.Verified
                }
            }
        };
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        return (workspace, source, sourcePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubMaterializer(string path, MediaEncodingMetadata encoding) : IMediaMaterializer
    {
        public MaterializationRequest? LastRequest { get; private set; }

        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified },
                encoding,
                isDurableSource: true));
        }
    }

    private sealed class StubExtractionEngine : IAudioExtractionEngine
    {
        public int CallCount { get; private set; }

        public async Task ExtractToM4aAsync(
            string inputPath,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await File.WriteAllBytesAsync(outputPath, [4, 5, 6, 7], cancellationToken);
        }

        public Task ExtractExactRangeToM4aAsync(
            string inputPath,
            string outputPath,
            int audioStreamIndex,
            AudioSourceRange sourceRange,
            CancellationToken cancellationToken = default) =>
            ExtractToM4aAsync(inputPath, outputPath, cancellationToken);
    }

    private sealed class OutputInspector : IMediaInspectionService
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
                DurationSeconds = 8,
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

    private sealed class NoAudioInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) => Task.FromResult(new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { Codec = "h264" }
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
