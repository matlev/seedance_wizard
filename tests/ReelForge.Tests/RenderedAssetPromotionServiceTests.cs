using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class RenderedAssetPromotionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ReelForge-promotion-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsAssetCopiesRenderedRevisionAndPersistsProvenance()
    {
        var (workspace, composition, revision, renderedPath) = await CreateCompositionAsync();
        var service = new RenderedAssetPromotionService(
            workspace,
            new StubMaterializer(renderedPath),
            new Sha256ContentHashService(),
            new StubInspector());

        var promoted = await service.SaveAsAssetAsync(
            composition.Id, revision.Id, "My finished composition.mp4");

        Assert.Equal(AssetStorageKind.Physical, promoted.StorageKind);
        Assert.Equal(PhysicalAssetDurability.Promoted, promoted.Physical!.Durability);
        Assert.Equal("promoted-render", promoted.Provenance!.Operation);
        Assert.Equal(composition.Id, Assert.Single(promoted.Provenance.SourceAssetIds));
        Assert.Equal(revision.Id, promoted.Provenance.SourceRecipeRevisionId);
        Assert.Equal(ContentHashStatus.Verified, promoted.Physical.ContentIdentity.Status);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(promoted)));
        Assert.Contains(composition, workspace.Project!.Assets);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var reopenedAsset = reopened.Assets.Single(asset => asset.Id == promoted.Id);
        Assert.Equal(revision.Id, reopenedAsset.Provenance!.SourceRecipeRevisionId);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task SaveAsAssetKeepsExistingFileAndUsesAvailableName()
    {
        var (workspace, composition, revision, renderedPath) = await CreateCompositionAsync();
        var videos = Path.Combine(workspace.Location!.RootDirectory, "assets", "videos");
        Directory.CreateDirectory(videos);
        await File.WriteAllBytesAsync(Path.Combine(videos, "Composition.mp4"), [9]);
        var service = new RenderedAssetPromotionService(
            workspace,
            new StubMaterializer(renderedPath),
            new Sha256ContentHashService(),
            new StubInspector());

        var promoted = await service.SaveAsAssetAsync(composition.Id, revision.Id, "Composition.mp4");

        Assert.Equal("Composition (2).mp4", promoted.FileName);
        Assert.Equal([9], await File.ReadAllBytesAsync(Path.Combine(videos, "Composition.mp4")));
    }

    [Fact]
    public async Task FailedInspectionLeavesNoAssetOrPartialFile()
    {
        var (workspace, composition, revision, renderedPath) = await CreateCompositionAsync();
        var service = new RenderedAssetPromotionService(
            workspace,
            new StubMaterializer(renderedPath),
            new Sha256ContentHashService(),
            new FailingInspector());
        var originalAssetCount = workspace.Project!.Assets.Count;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SaveAsAssetAsync(composition.Id, revision.Id, "Broken.mp4"));

        Assert.Equal(originalAssetCount, workspace.Project.Assets.Count);
        var videos = Path.Combine(workspace.Location!.RootDirectory, "assets", "videos");
        Assert.False(File.Exists(Path.Combine(videos, "Broken.mp4")));
        Assert.False(Directory.Exists(videos) &&
                     Directory.EnumerateFiles(videos, ".promote-*.partial").Any());
    }

    [Fact]
    public async Task ExportWritesOutsideProjectWithoutAddingAnAsset()
    {
        var (workspace, composition, revision, renderedPath) = await CreateCompositionAsync();
        var service = new RenderedAssetPromotionService(
            workspace,
            new StubMaterializer(renderedPath),
            new Sha256ContentHashService(),
            new StubInspector());
        var originalAssetCount = workspace.Project!.Assets.Count;
        var destination = Path.Combine(_root, "delivery", "finished.mp4");

        var exportedPath = await service.ExportAsync(composition.Id, revision.Id, destination);

        Assert.Equal(Path.GetFullPath(destination), exportedPath);
        Assert.Equal(await File.ReadAllBytesAsync(renderedPath), await File.ReadAllBytesAsync(destination));
        Assert.Equal(originalAssetCount, workspace.Project.Assets.Count);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.partial"));
    }

    private async Task<(ProjectWorkspace Workspace, ProjectAsset Composition, RecipeRevision Revision, string RenderedPath)>
        CreateCompositionAsync()
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Promotion");
        var source = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                ContentIdentity = new ContentIdentity
                {
                    Sha256 = new string('a', 64),
                    Status = ContentHashStatus.Verified
                }
            }
        };
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        workspace.Project!.AddAsset(source);
        workspace.Project.AddAsset(composition);
        var revision = workspace.Project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments = [new CompositionSegment { Source = new AssetRevisionReference { AssetId = source.Id } }]
        });
        workspace.Project.WorkingCompositionAssetId = composition.Id;
        await workspace.SaveAsync();
        var renderedPath = Path.Combine(_root, "render-cache.mp4");
        await File.WriteAllBytesAsync(renderedPath, [1, 2, 3, 4, 5]);
        return (workspace, composition, revision, renderedPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubMaterializer(string path) : IMediaMaterializer
    {
        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified },
                null,
                isDurableSource: false));
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata
            {
                DurationSeconds = 5,
                Video = new VideoStreamMetadata { Codec = "h264", Width = 1280, Height = 720, FrameRate = "30/1" },
                Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 }
            });
    }

    private sealed class FailingInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            throw new InvalidDataException("Simulated inspection failure.");
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
