using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class RenderedAssetPromotionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ReelForge-promotion-{Guid.NewGuid():N}");

    [Fact]
    public async Task CompositionPromotionRefusesUntilMilestone6TrackAwareRendererExists()
    {
        var (workspace, composition, revision, _) = await CreateCompositionAsync();
        var service = new RenderedAssetPromotionService(workspace, new ThrowingMaterializer(), new Sha256ContentHashService(), new StubInspector());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.SaveAsAssetAsync(composition.Id, revision.Id, "finished.mp4"));

        Assert.Contains("track-aware renderer planned for Milestone 6", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public async Task ExportUsesExactMaterializationBeforeConsideringCachedRepresentation()
    {
        var (workspace, composition, revision, renderedPath) = await CreateCompositionAsync();
        var materializer = new FailingOrCachedMaterializer(renderedPath, failExactMaterialization: false);
        var service = new RenderedAssetPromotionService(
            workspace,
            materializer,
            new Sha256ContentHashService(),
            new StubInspector());
        var destination = Path.Combine(_root, "delivery", "exact.mp4");

        await service.ExportAsync(composition.Id, revision.Id, destination);

        Assert.Equal(1, materializer.MaterializeCallCount);
        Assert.Equal(0, materializer.OpenCachedCallCount);
        Assert.Equal(await File.ReadAllBytesAsync(renderedPath), await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DegradedExportCopiesIndexedCachedRepresentationWithoutMutatingProjectOrCache()
    {
        var (workspace, composition, revision, _) = await CreateCompositionAsync();
        var source = workspace.Project!.Assets.Single(asset => asset.StorageKind == AssetStorageKind.Physical);
        source.IsDeleted = true;
        source.Physical!.Availability = PhysicalAssetAvailability.Missing;
        var cachePath = Path.Combine(_root, "cache", "rescued.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllBytesAsync(cachePath, [7, 8, 9]);
        var originalTimestamp = File.GetLastWriteTimeUtc(cachePath);
        var originalAssetCount = workspace.Project.Assets.Count;
        var materializer = new FailingOrCachedMaterializer(cachePath, failExactMaterialization: true);
        var service = new RenderedAssetPromotionService(
            workspace,
            materializer,
            new Sha256ContentHashService(),
            new StubInspector());
        var destination = Path.Combine(_root, "delivery", "rescued.mp4");

        await service.ExportAsync(composition.Id, revision.Id, destination);

        Assert.Equal([7, 8, 9], await File.ReadAllBytesAsync(destination));
        Assert.Equal(1, materializer.MaterializeCallCount);
        Assert.Equal(1, materializer.OpenCachedCallCount);
        Assert.Equal(originalAssetCount, workspace.Project.Assets.Count);
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(cachePath));
    }

    [Fact]
    public async Task DegradedExportPreservesExactFailureWhenCachedRepresentationIsStale()
    {
        var (workspace, composition, revision, _) = await CreateCompositionAsync();
        var source = workspace.Project!.Assets.Single(asset => asset.StorageKind == AssetStorageKind.Physical);
        source.IsDeleted = true;
        source.Physical!.Availability = PhysicalAssetAvailability.Missing;
        var materializer = new FailingOrCachedMaterializer(null, failExactMaterialization: true);
        var service = new RenderedAssetPromotionService(
            workspace,
            materializer,
            new Sha256ContentHashService(),
            new StubInspector());
        var destination = Path.Combine(_root, "delivery", "stale.mp4");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExportAsync(composition.Id, revision.Id, destination));

        Assert.Equal("Exact materialization failed.", failure.Message);
        Assert.Equal(1, materializer.OpenCachedCallCount);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ExportCancellationReachesMaterializerAndLeavesNoDestination()
    {
        var (workspace, composition, revision, _) = await CreateCompositionAsync();
        var materializer = new BlockingMaterializer();
        var service = new RenderedAssetPromotionService(
            workspace,
            materializer,
            new Sha256ContentHashService(),
            new StubInspector());
        var destination = Path.Combine(_root, "delivery", "cancelled.mp4");
        using var cancellation = new CancellationTokenSource();

        var export = service.ExportAsync(composition.Id, revision.Id, destination, cancellation.Token);
        await materializer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.partial"));
    }

    [Fact]
    public async Task SavedFramePromotionCreatesPhysicalPngWithPinnedAnchorProvenance()
    {
        var (workspace, _, _, renderedPath) = await CreateCompositionAsync();
        var source = workspace.Project!.Assets.Single(asset => asset.StorageKind == AssetStorageKind.Physical);
        var anchor = new FrameAnchor { DisplayLabel = "Chosen expression" };
        workspace.Project.Anchors.Add(anchor);
        var revision = workspace.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id,
            source.Physical!.ContentIdentity.Sha256!,
            0,
            90,
            1,
            30,
            90));
        await workspace.SaveAsync();
        var materializer = new StubMaterializer(renderedPath);
        var service = new RenderedAssetPromotionService(
            workspace,
            materializer,
            new Sha256ContentHashService(),
            new StubInspector());

        var promoted = await service.SaveFrameAsAssetAsync(anchor.Id, revision.Id, "expression.png");

        Assert.Equal(MediaType.Image, promoted.MediaType);
        Assert.Equal(AssetOrigin.ExtractedFrame, promoted.Origin);
        Assert.Equal("promoted-saved-frame", promoted.Provenance!.Operation);
        Assert.Equal(anchor.Id.ToString("N"), promoted.Provenance.Parameters["anchorId"]);
        Assert.Equal(revision.Id.ToString("N"), promoted.Provenance.Parameters["anchorRevisionId"]);
        Assert.IsType<AnchorMaterializationTarget>(materializer.LastRequest!.Target);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(promoted)));
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
            },
            Encoding = new MediaEncodingMetadata { Video = new VideoStreamMetadata { StreamIndex = 0 } }
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
            Composition = new WorkingCompositionState(
                [new CompositionVideoTrack(Guid.NewGuid(), false, true,
                [
                    new CompositionVideoItem(
                        Guid.NewGuid(),
                        new AssetRevisionReference { AssetId = source.Id },
                        0,
                        new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30)),
                        new StreamTimingAssessment(
                            Guid.NewGuid(),
                            source.Physical.ContentIdentity.Sha256!,
                            MediaType.Video,
                            0,
                            TimingReadiness.Exact,
                            true,
                            new ExactTime(1, 1),
                            [],
                            new ExactTime(0, 1)).CreatePlacementPin(),
                        new ExactTime(0, 1))
                ])],
                [new CompositionAudioTrack(Guid.NewGuid(), false, false, [])])
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

    private sealed class ThrowingMaterializer : IMediaMaterializer
    {
        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MaterializedMediaLease>(
                new InvalidDataException("Composition requires track-aware renderer planned for Milestone 6."));
    }

    private sealed class StubMaterializer(string path) : IMediaMaterializer
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
                null,
                isDurableSource: false));
        }
    }

    private sealed class BlockingMaterializer : IMediaMaterializer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking materializer should only finish through cancellation.");
        }
    }

    private sealed class FailingOrCachedMaterializer(string? cachedPath, bool failExactMaterialization)
        : IMediaMaterializer, IProjectMediaCacheLeaseSource
    {
        public int MaterializeCallCount { get; private set; }
        public int OpenCachedCallCount { get; private set; }

        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default)
        {
            MaterializeCallCount++;
            if (failExactMaterialization) throw new InvalidOperationException("Exact materialization failed.");
            return Task.FromResult(new MaterializedMediaLease(
                cachedPath!,
                new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified },
                null,
                isDurableSource: false));
        }

        public Task<bool> HasCachedRepresentationAsync(
            VideoProject project,
            MaterializationTarget target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(cachedPath is not null && File.Exists(cachedPath));

        public Task<MaterializedMediaLease?> OpenCachedRepresentationAsync(
            VideoProject project,
            MaterializationTarget target,
            CancellationToken cancellationToken = default)
        {
            OpenCachedCallCount++;
            if (cachedPath is null || !File.Exists(cachedPath))
                return Task.FromResult<MaterializedMediaLease?>(null);
            return Task.FromResult<MaterializedMediaLease?>(new MaterializedMediaLease(
                cachedPath,
                new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified },
                null,
                isDurableSource: false));
        }
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
