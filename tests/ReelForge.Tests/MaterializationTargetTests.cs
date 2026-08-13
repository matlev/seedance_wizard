using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class MaterializationTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ReelForge materialization target tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AssetTargetReturnsVerifiedDurableSource()
    {
        var (project, location, asset) = await CreateProjectSourceAsync();
        var materializer = new PhysicalAssetMaterializer();

        await using var lease = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(asset.Id),
                MaterializationPurpose.Preview));

        Assert.True(lease.IsDurableSource);
        Assert.Equal(Path.Combine(_root, "assets", "videos", "source.mp4"), lease.Path);
        Assert.Equal(ContentHashStatus.Verified, lease.ContentIdentity.Status);
        Assert.Equal(lease.ContentIdentity.Sha256, asset.Physical?.ContentIdentity.Sha256);
    }

    [Fact]
    public async Task AnchorTargetPinsRevisionAndVerifiesSourceBeforeExtraction()
    {
        var (project, location, asset) = await CreateProjectSourceAsync();
        var materializer = new PhysicalAssetMaterializer();
        await using (var source = await materializer.MaterializeAsync(
                         project,
                         location,
                         new MaterializationRequest(
                             new AssetMaterializationTarget(asset.Id),
                             MaterializationPurpose.Preview)))
        {
            var anchor = new FrameAnchor();
            project.Anchors.Add(anchor);
            var revision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
                asset.Id, source.ContentIdentity.Sha256!, 0, 30, 1, 30, 30));

            var exception = await Assert.ThrowsAsync<MediaToolUnavailableException>(() => materializer.MaterializeAsync(
                project,
                location,
                new MaterializationRequest(
                    new AnchorMaterializationTarget(anchor.Id, revision.Id),
                    MaterializationPurpose.FrameExtraction)));

            Assert.Contains("FFmpeg", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SavedClipMaterializationResolvesAfterFrameAndReusesDeterministicCache()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        sourceAsset.DurationSeconds = 8;
        sourceAsset.Physical!.ContentIdentity = await new Sha256ContentHashService()
            .ComputeAsync(Path.Combine(_root, sourceAsset.Physical.RelativePath));
        var anchor = new FrameAnchor { IsArchived = true };
        project.Anchors.Add(anchor);
        var anchorRevision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id,
            sourceAsset.Physical.ContentIdentity.Sha256!,
            0,
            30,
            1,
            10,
            30));
        var clip = new ProjectAsset
        {
            DisplayName = "Opening",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        project.AddAsset(clip);
        var recipeRevision = project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = sourceAsset.Id },
            Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary
            {
                Kind = RecipeBoundaryKind.Anchor,
                Anchor = new AnchorRevisionReference
                {
                    AnchorId = anchor.Id,
                    AnchorRevisionId = anchorRevision.Id
                },
                Edge = AnchorBoundaryEdge.AfterFrame
            }
        });
        var runner = new TrimRunner();
        var frames = new StubExactFrameService([
            new VideoPresentationFrame(0, 30, 1, 10),
            new VideoPresentationFrame(0, 31, 1, 10)
        ]);
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, frames, Path.Combine(_root, "cache"));
        var request = new MaterializationRequest(
            new AssetMaterializationTarget(clip.Id, recipeRevision.Id),
            MaterializationPurpose.Preview);

        await using var first = await materializer.MaterializeAsync(project, location, request);
        await using var second = await materializer.MaterializeAsync(project, location, request);

        Assert.False(first.IsDurableSource);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(1, runner.TrimCount);
        Assert.Equal(1, frames.WindowIndexCount);
        Assert.Contains("3.1", runner.TrimRequest!.Arguments);
    }

    [Fact]
    public async Task InitialWorkingCompositionPreviewsItsPinnedSingleSourceDirectly()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(composition);
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment
                {
                    Source = new AssetRevisionReference { AssetId = sourceAsset.Id },
                    Start = RecipeBoundary.SourceStart,
                    End = RecipeBoundary.SourceEnd
                }
            ]
        });
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", new TrimRunner(), new StubExactFrameService([]), Path.Combine(_root, "cache"));

        await using var preview = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(composition.Id, revision.Id),
                MaterializationPurpose.Preview));

        Assert.True(preview.IsDurableSource);
        Assert.EndsWith("source.mp4", preview.Path, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(VideoProject Project, ProjectLocation Location, ProjectAsset Asset)> CreateProjectSourceAsync()
    {
        var relativePath = Path.Combine("assets", "videos", "source.mp4");
        var absolutePath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, [1, 2, 3, 4, 5]);
        var asset = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = relativePath,
                ContentIdentity = new ContentIdentity { Status = ContentHashStatus.Pending }
            }
        };
        var project = new VideoProject { Assets = [asset] };
        return (project, new ProjectLocation(_root, Path.Combine(_root, "Test.rfp")), asset);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubExactFrameService(IReadOnlyList<VideoPresentationFrame> frames) : IExactVideoFrameService
    {
        public int WindowIndexCount { get; private set; }

        public Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) => Task.FromResult(frames);

        public Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
            string mediaPath,
            double centerSeconds,
            double radiusSeconds = 2,
            CancellationToken cancellationToken = default)
        {
            WindowIndexCount++;
            return Task.FromResult(frames);
        }

        public Task<MaterializedMediaLease> ExtractAsync(
            string mediaPath,
            string sourceContentHash,
            FrameAnchorRevision revision,
            MaterializationPurpose purpose,
            string? profile = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TrimRunner : IExternalProcessRunner
    {
        public int TrimCount { get; private set; }
        public ExternalProcessRequest? TrimRequest { get; private set; }

        public async Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request.Arguments.SequenceEqual(["-version"]))
                return new ExternalProcessResult(0, "ffmpeg version test-1\n", string.Empty);
            TrimCount++;
            TrimRequest = request;
            Directory.CreateDirectory(Path.GetDirectoryName(request.Arguments[^1])!);
            await File.WriteAllBytesAsync(request.Arguments[^1], [1, 2, 3], cancellationToken);
            return new ExternalProcessResult(0, string.Empty, string.Empty);
        }
    }
}
