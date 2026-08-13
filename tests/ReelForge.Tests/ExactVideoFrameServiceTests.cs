using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ExactVideoFrameServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ReelForge exact frame tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void IndexParserSortsAndDeduplicatesDecodedPresentationFrames()
    {
        const string json = """
            {
              "frames": [
                { "best_effort_timestamp": "60" },
                { "best_effort_timestamp": "0" },
                { "best_effort_timestamp": "30" },
                { "best_effort_timestamp": "30" }
              ],
              "streams": [{ "index": 2, "time_base": "1/30" }]
            }
            """;

        var frames = ExactVideoFrameService.ParseIndex(json);

        Assert.Equal([0L, 30L, 60L], frames.Select(frame => frame.PresentationTimestamp));
        Assert.All(frames, frame =>
        {
            Assert.Equal(2, frame.VideoStreamIndex);
            Assert.Equal(1, frame.TimeBaseNumerator);
            Assert.Equal(30, frame.TimeBaseDenominator);
        });
    }

    [Fact]
    public async Task WindowIndexUsesBoundedReadIntervalAroundRequestedPosition()
    {
        var runner = new WindowIndexRunner();
        using var service = new ExactVideoFrameService("ffmpeg.exe", "ffprobe.exe", runner, _root);

        var frames = await service.IndexWindowAsync("long source.mp4", 120, 2);

        Assert.Single(frames);
        Assert.Contains("118%+4", runner.Request!.Arguments);
        Assert.Equal("long source.mp4", runner.Request.Arguments[^1]);
    }

    [Fact]
    public async Task ExtractionUsesDeterministicCacheAndReconstructsDeletedEntry()
    {
        var runner = new FrameRunner();
        using var service = CreateService(runner);
        var revision = CreateRevision();

        await using var first = await service.ExtractAsync(
            "source.mp4", revision.SourceContentHash, revision, MaterializationPurpose.ProviderUpload);
        var firstPath = first.Path;
        await using var cached = await service.ExtractAsync(
            "source.mp4", revision.SourceContentHash, revision, MaterializationPurpose.ProviderUpload);

        Assert.Equal(firstPath, cached.Path);
        Assert.Equal(1, runner.ExtractionCount);

        File.Delete(firstPath);
        await using var reconstructed = await service.ExtractAsync(
            "source.mp4", revision.SourceContentHash, revision, MaterializationPurpose.ProviderUpload);

        Assert.Equal(firstPath, reconstructed.Path);
        Assert.Equal(2, runner.ExtractionCount);
    }

    [Fact]
    public async Task ConcurrentExtractionOfSameFrameIsCoalesced()
    {
        var runner = new FrameRunner(TimeSpan.FromMilliseconds(75));
        using var service = CreateService(runner);
        var revision = CreateRevision();

        var first = service.ExtractAsync(
            "source.mp4", revision.SourceContentHash, revision, MaterializationPurpose.Thumbnail);
        var second = service.ExtractAsync(
            "source.mp4", revision.SourceContentHash, revision, MaterializationPurpose.Thumbnail);
        var leases = await Task.WhenAll(first, second);
        await leases[0].DisposeAsync();
        await leases[1].DisposeAsync();

        Assert.Equal(1, runner.ExtractionCount);
        Assert.Equal(leases[0].Path, leases[1].Path);
    }

    [Fact]
    public async Task CancelledExtractionRemovesUniqueTemporaryFile()
    {
        var runner = new FrameRunner(waitForCancellation: true);
        using var service = CreateService(runner);
        var revision = CreateRevision();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExtractAsync(
            "source.mp4",
            revision.SourceContentHash,
            revision,
            MaterializationPurpose.FrameExtraction,
            cancellationToken: cancellation.Token));

        var frameCache = Path.Combine(_root, "frames");
        Assert.Empty(Directory.Exists(frameCache)
            ? Directory.EnumerateFiles(frameCache, "*.tmp.png", SearchOption.TopDirectoryOnly)
            : []);
    }

    [Fact]
    public async Task CacheLimitEvictsOldestUnleasedFrameWithoutRemovingActiveLease()
    {
        var runner = new FrameRunner();
        using var service = new ExactVideoFrameService(
            "ffmpeg.exe",
            "ffprobe.exe",
            runner,
            _root,
            maximumCacheBytes: 5);
        var firstRevision = CreateRevision();
        var secondRevision = CreateRevision();

        var first = await service.ExtractAsync(
            "source.mp4", firstRevision.SourceContentHash, firstRevision, MaterializationPurpose.Thumbnail);
        var firstPath = first.Path;
        await first.DisposeAsync();
        await Task.Delay(20);

        await using var second = await service.ExtractAsync(
            "source.mp4", secondRevision.SourceContentHash, secondRevision, MaterializationPurpose.Thumbnail);

        Assert.False(File.Exists(firstPath));
        Assert.True(File.Exists(second.Path));
    }

    private ExactVideoFrameService CreateService(FrameRunner runner) =>
        new("ffmpeg.exe", "ffprobe.exe", runner, _root);

    private static FrameAnchorRevision CreateRevision() => new()
    {
        AnchorId = Guid.NewGuid(),
        SourceAssetId = Guid.NewGuid(),
        SourceContentHash = new string('a', 64),
        VideoStreamIndex = 0,
        PresentationTimestamp = 90,
        TimeBaseNumerator = 1,
        TimeBaseDenominator = 30,
        RevisionNumber = 1
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FrameRunner : IExternalProcessRunner
    {
        private readonly TimeSpan _delay;
        private readonly bool _waitForCancellation;
        private int _extractionCount;

        public FrameRunner(TimeSpan? delay = null, bool waitForCancellation = false)
        {
            _delay = delay ?? TimeSpan.Zero;
            _waitForCancellation = waitForCancellation;
        }

        public int ExtractionCount => _extractionCount;

        public async Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request.Arguments.SequenceEqual(["-version"]))
                return new ExternalProcessResult(0, "ffmpeg version test-1\n", string.Empty);

            Interlocked.Increment(ref _extractionCount);
            var outputPath = request.Arguments[^1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllBytesAsync(outputPath, [137, 80, 78, 71], cancellationToken);
            if (_waitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, cancellationToken);
            return new ExternalProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class WindowIndexRunner : IExternalProcessRunner
    {
        public ExternalProcessRequest? Request { get; private set; }

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            const string json = """
                {
                  "frames": [{ "best_effort_timestamp": "3600" }],
                  "streams": [{ "index": 0, "time_base": "1/30" }]
                }
                """;
            return Task.FromResult(new ExternalProcessResult(0, json, string.Empty));
        }
    }
}
