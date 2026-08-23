using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class JsonGenerationJobStoreTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge job store tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentSavesLeaveOneCompleteRegistryAndNoTemporaryArtifacts()
    {
        var path = Path.Combine(_temporaryRoot, "active-jobs.json");
        var stores = new[]
        {
            new JsonGenerationJobStore(path),
            new JsonGenerationJobStore(path)
        };

        var snapshots = Enumerable.Range(0, 20)
            .Select(index => (IReadOnlyCollection<TrackedGenerationJob>)[CreateJob(index)])
            .ToArray();

        await Task.WhenAll(snapshots.Select((snapshot, index) =>
            stores[index % stores.Length].SaveAsync(snapshot)));

        var loaded = Assert.Single(await stores[0].LoadAsync());
        Assert.Contains(snapshots, snapshot => snapshot.Single().GenerationId == loaded.GenerationId);
        Assert.Empty(Directory.EnumerateFiles(_temporaryRoot, "*.tmp-*", SearchOption.TopDirectoryOnly));
    }

    private static TrackedGenerationJob CreateJob(int index) => new()
    {
        GenerationId = Guid.NewGuid(),
        ProjectName = $"Project {index}",
        ProjectFilePath = Path.Combine("projects", $"project-{index}.rfp"),
        ProviderId = "network-isolated.test-provider",
        ProviderDisplayName = "Network-isolated test provider",
        ModelVersion = "test-model",
        RequestedAt = DateTimeOffset.UnixEpoch.AddSeconds(index),
        Status = GenerationStatus.Running
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
