using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PortableProjectRelocationFileSystemTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge relocation tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SameVolumeMovePreservesCompleteProjectFolderIncludingCacheAndRecovery()
    {
        var source = await CreateProjectAsync("Source");
        await WriteAsync(source.Root, "assets/videos/source.mp4", "source");
        await WriteAsync(source.Root, "cache/materialized.bin", "cache");
        await File.WriteAllTextAsync(PortableProjectStore.GetRecoveryFilePath(source.Location), "recovery");
        var destination = Path.Combine(_root, "Moved");
        var fileSystem = new PortableProjectRelocationFileSystem();

        var plan = await fileSystem.PrepareAsync(source.Location, destination, null, CancellationToken.None);

        Assert.False(plan.UsesStaging);
        await fileSystem.PublishAsync(plan, CancellationToken.None);

        Assert.False(Directory.Exists(source.Root));
        Assert.True(File.Exists(plan.FinalLocation.ProjectFilePath));
        Assert.Equal("source", await File.ReadAllTextAsync(Path.Combine(destination, "assets", "videos", "source.mp4")));
        Assert.Equal("cache", await File.ReadAllTextAsync(Path.Combine(destination, "cache", "materialized.bin")));
        Assert.True(File.Exists(PortableProjectStore.GetRecoveryFilePath(plan.FinalLocation)));
    }

    [Fact]
    public async Task RejectsSameOrNestedDestinationAndExistingDestination()
    {
        var source = await CreateProjectAsync("Source");
        var fileSystem = new PortableProjectRelocationFileSystem();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fileSystem.PrepareAsync(
            source.Location, Path.Combine(source.Root, "Nested"), null, CancellationToken.None));

        var existing = Path.Combine(_root, "Existing");
        Directory.CreateDirectory(existing);
        await Assert.ThrowsAsync<IOException>(() => fileSystem.PrepareAsync(
            source.Location, existing, null, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<SourceProject> CreateProjectAsync(string name)
    {
        var store = new PortableProjectStore();
        var root = Path.Combine(_root, name);
        var (_, location) = await store.CreateAsync(root, name);
        return new SourceProject(root, location);
    }

    private static async Task WriteAsync(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
    }

    private sealed record SourceProject(string Root, ProjectLocation Location);
}
