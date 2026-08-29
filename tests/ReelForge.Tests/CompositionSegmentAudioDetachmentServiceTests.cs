using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class CompositionSegmentAudioDetachmentServiceTests
{
    [Fact]
    public async Task DetachRefusesBeforeExtractingMediaUntilExactOccurrenceAdapterExists()
    {
        var service = new CompositionSegmentAudioDetachmentService(
            new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter()), null!, null!, null!, null!);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DetachAsync(Guid.NewGuid(), "detached.m4a"));

        Assert.Contains("unsupported until the timeline occurrence adapter", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
