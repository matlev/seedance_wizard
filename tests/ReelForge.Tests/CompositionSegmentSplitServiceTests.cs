using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class CompositionSegmentSplitServiceTests
{
    [Fact]
    public async Task SplitRefusesBeforeMaterializationUntilExactOccurrenceAdapterExists()
    {
        var service = new CompositionSegmentSplitService(
            new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter()), null!, null!);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SplitAsync(Guid.NewGuid(), TimeSpan.FromSeconds(1)));

        Assert.Contains("unsupported until the timeline occurrence adapter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
