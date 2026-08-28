using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ProjectWorkspaceImportTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReelForge workspace import tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportReservesMissingActivePhysicalAssetPath()
    {
        var originalPath = await WriteIncomingAsync("original", "clip.mp4", "first bytes");
        var replacementPath = await WriteIncomingAsync("replacement", "clip.mp4", "different bytes");
        var store = new PortableProjectStore();
        var workspace = new ProjectWorkspace(store, new AssetImportService(new StubInspector()));
        await workspace.CreateAsync(Path.Combine(_root, "project"), "Import reservation");
        var original = Assert.Single(await workspace.ImportAssetsAsync([originalPath]));
        File.Delete(workspace.GetAbsoluteAssetPath(original));

        var imported = Assert.Single(await workspace.ImportAssetsAsync([replacementPath]));

        Assert.Equal("clip (2).mp4", imported.FileName);
        Assert.Equal("assets/videos/clip (2).mp4", imported.Physical!.RelativePath);
        Assert.Equal(2, workspace.Project!.Assets.Count(asset => !asset.IsDeleted));
        Assert.Equal(
            2,
            workspace.Project.Assets
                .Where(asset => !asset.IsDeleted && asset.Physical is not null)
                .Select(asset => asset.Physical!.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(imported)));
    }

    [Fact]
    public async Task ImportRetainsOrdinaryExistingFileCollisionBehavior()
    {
        var firstPath = await WriteIncomingAsync("first", "clip.mp4", "first bytes");
        var secondPath = await WriteIncomingAsync("second", "clip.mp4", "second bytes");
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new AssetImportService(new StubInspector()));
        await workspace.CreateAsync(Path.Combine(_root, "project"), "Existing collision");

        var first = Assert.Single(await workspace.ImportAssetsAsync([firstPath]));
        var second = Assert.Single(await workspace.ImportAssetsAsync([secondPath]));

        Assert.Equal("clip.mp4", first.FileName);
        Assert.Equal("clip (2).mp4", second.FileName);
    }

    private async Task<string> WriteIncomingAsync(string folder, string fileName, string contents)
    {
        var path = Path.Combine(_root, "incoming", folder, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata { Video = new VideoStreamMetadata() });
    }
}
