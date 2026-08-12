using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PhysicalAssetFileRenameServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge filename tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenamePreservesExtensionIdentityAndContentHash()
    {
        var sourcePath = Path.Combine(_temporaryRoot, "incoming", "hamster_dance_1.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllTextAsync(sourcePath, "stable media bytes");
        var store = new PortableProjectStore();
        var workspace = new ProjectWorkspace(store, new AssetImportService(new StubInspector()));
        await workspace.CreateAsync(Path.Combine(_temporaryRoot, "project"), "Filename test");
        var asset = Assert.Single(await workspace.ImportAssetsAsync([sourcePath]));
        var originalId = asset.Id;
        var originalHash = asset.Physical!.ContentIdentity.Sha256;
        var oldPath = workspace.GetAbsoluteAssetPath(asset);

        await PhysicalAssetFileRenameService.RenameAsync(workspace, asset, "hamster_final.mp4");

        Assert.Equal(originalId, asset.Id);
        Assert.Equal(originalHash, asset.Physical.ContentIdentity.Sha256);
        Assert.Equal("hamster_final.mp4", asset.FileName);
        Assert.Equal("hamster_final.mp4", asset.DisplayName);
        Assert.EndsWith("assets/videos/hamster_final.mp4", asset.Physical.RelativePath, StringComparison.Ordinal);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
        var (reopened, _) = await store.OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal("hamster_final.mp4", Assert.Single(reopened.Assets).FileName);
    }

    [Fact]
    public async Task RenameRejectsAFileTypeChange()
    {
        var workspace = await CreateWorkspaceWithAssetAsync();
        var asset = Assert.Single(workspace.Project!.Assets);
        var oldPath = workspace.GetAbsoluteAssetPath(asset);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            PhysicalAssetFileRenameService.RenameAsync(workspace, asset, "foo.mov"));

        Assert.Contains("file type cannot be changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("source.mp4", asset.FileName);
        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(oldPath)!, "foo.mov")));
    }

    [Fact]
    public async Task SaveFailureRollsBackFileAndProjectMetadata()
    {
        var projectRoot = Path.Combine(_temporaryRoot, "rollback-project");
        var mediaPath = Path.Combine(projectRoot, "assets", "videos", "source.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
        await File.WriteAllTextAsync(mediaPath, "stable media bytes");
        var asset = CreatePhysicalAsset();
        var project = new VideoProject { Name = "Rollback test" };
        project.AddAsset(asset);
        var location = new ProjectLocation(projectRoot, Path.Combine(projectRoot, "Rollback test.rfp"));
        var workspace = new ProjectWorkspace(new FailingSaveProjectStore(project, location), new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);

        await Assert.ThrowsAsync<IOException>(() =>
            PhysicalAssetFileRenameService.RenameAsync(workspace, asset, "renamed.mp4"));

        Assert.Equal("source.mp4", asset.FileName);
        Assert.Equal("source.mp4", asset.DisplayName);
        Assert.Equal("assets/videos/source.mp4", asset.Physical!.RelativePath);
        Assert.True(File.Exists(mediaPath));
        Assert.False(File.Exists(Path.Combine(projectRoot, "assets", "videos", "renamed.mp4")));
    }

    private async Task<ProjectWorkspace> CreateWorkspaceWithAssetAsync()
    {
        var projectRoot = Path.Combine(_temporaryRoot, "type-project");
        var store = new PortableProjectStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.CreateAsync(projectRoot, "Type test");
        var asset = CreatePhysicalAsset();
        var absolutePath = Path.Combine(projectRoot, asset.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllTextAsync(absolutePath, "stable media bytes");
        workspace.Project!.AddAsset(asset);
        await workspace.SaveAsync();
        return workspace;
    }

    private static ProjectAsset CreatePhysicalAsset() => new()
    {
        FileName = "source.mp4",
        DisplayName = "source.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
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

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata { Video = new VideoStreamMetadata() });
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not import assets.");
    }

    private sealed class FailingSaveProjectStore(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test opens an existing project.");

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task SaveAsync(
            VideoProject savedProject,
            ProjectLocation savedLocation,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated project save failure."));
    }
}
