using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class MaterializedProjectMediaTransferServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge materialized transfer tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SavedFrameCopyMaterializesExactAnchorRevisionAsPermanentPng()
    {
        var (source, target, sourceAsset) = await CreateProjectsAsync();
        var anchor = new FrameAnchor { DisplayLabel = "Saved frame" };
        source.Project!.Anchors.Add(anchor);
        var revision = source.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id, sourceAsset.Physical!.ContentIdentity.Sha256!, 0, 42, 1, 24));
        var materializedPath = await CreateMaterializedFileAsync("frame.png");
        var materializer = new RecordingMaterializer(materializedPath);
        var service = CreateService(source, materializer);

        var result = await service.CopySavedFrameAsync(
            anchor, revision, "Great frame.jpeg", target.Location!.ProjectFilePath);

        var targetAsset = result.CopiedAsset;
        Assert.Equal(MediaType.Image, targetAsset.MediaType);
        Assert.Equal(AssetOrigin.ExtractedFrame, targetAsset.Origin);
        Assert.EndsWith(".png", targetAsset.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("copied-materialized-from-project", targetAsset.Provenance!.Operation);
        Assert.Empty(targetAsset.Provenance.SourceAssetIds);
        Assert.Equal(source.Project.Id.ToString("D"), targetAsset.Provenance.Parameters["sourceProjectId"]);
        Assert.Equal(anchor.Id.ToString("D"), targetAsset.Provenance.Parameters["sourceAnchorId"]);
        Assert.Equal(revision.Id.ToString("D"), targetAsset.Provenance.Parameters["sourceAnchorRevisionId"]);
        Assert.False(string.IsNullOrEmpty(targetAsset.Provenance.Parameters["materializedContentHash"]));
        var materializationTarget = Assert.IsType<AnchorMaterializationTarget>(materializer.LastRequest!.Target);
        Assert.Equal(anchor.Id, materializationTarget.AnchorId);
        Assert.Equal(revision.Id, materializationTarget.AnchorRevisionId);
        Assert.Contains(sourceAsset, source.Project.Assets);
        Assert.Contains(source.Project.AnchorRevisions, candidate => candidate.Id == revision.Id);

        var second = await service.CopySavedFrameAsync(
            anchor, revision, "Great frame.png", target.Location!.ProjectFilePath);
        Assert.NotEqual(targetAsset.FileName, second.CopiedAsset.FileName);
    }

    [Theory]
    [InlineData(VirtualAssetKind.SavedClip)]
    [InlineData(VirtualAssetKind.Composition)]
    public async Task VirtualVideoCopyMaterializesPinnedRevisionAsPermanentMp4(VirtualAssetKind kind)
    {
        var (source, target, sourceAsset) = await CreateProjectsAsync();
        var virtualAsset = new ProjectAsset
        {
            DisplayName = kind == VirtualAssetKind.SavedClip ? "Saved clip" : "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Origin = AssetOrigin.EditorDerived,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = kind }
        };
        source.Project!.AddAsset(virtualAsset);
        var revision = source.Project.CommitRecipe(virtualAsset.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = sourceAsset.Id }
        });
        var materializer = new RecordingMaterializer(await CreateMaterializedFileAsync("render.mp4"));
        var service = CreateService(source, materializer);

        var result = await service.CopyVirtualVideoAsync(
            virtualAsset, revision.Id, "A friendly copied clip.mov", target.Location!.ProjectFilePath);

        Assert.Equal(MediaType.Video, result.CopiedAsset.MediaType);
        Assert.Equal(AssetOrigin.EditorDerived, result.CopiedAsset.Origin);
        Assert.EndsWith(".mp4", result.CopiedAsset.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(kind == VirtualAssetKind.SavedClip ? "saved-clip" : "working-composition",
            result.CopiedAsset.Provenance!.Parameters["sourceKind"]);
        Assert.Equal(virtualAsset.Id.ToString("D"), result.CopiedAsset.Provenance.Parameters["sourceVirtualAssetId"]);
        Assert.Equal(revision.Id.ToString("D"), result.CopiedAsset.Provenance.Parameters["sourceRecipeRevisionId"]);
        Assert.Empty(result.CopiedAsset.Provenance.SourceAssetIds);
        var materializationTarget = Assert.IsType<AssetMaterializationTarget>(materializer.LastRequest!.Target);
        Assert.Equal(virtualAsset.Id, materializationTarget.AssetId);
        Assert.Equal(revision.Id, materializationTarget.RecipeRevisionId);
        Assert.Contains(virtualAsset, source.Project.Assets);
        Assert.Contains(source.Project.RecipeRevisions, candidate => candidate.Id == revision.Id);
    }

    [Fact]
    public async Task MaterializedCopyUsesCapturedSourceWhenWorkspaceChangesDuringMaterialization()
    {
        var (source, target, sourceAsset) = await CreateProjectsAsync();
        var other = new ProjectWorkspace(new PortableProjectStore(), new AssetImportService(new StubInspector()));
        await other.CreateAsync(Path.Combine(_root, "other"), "Other");
        var anchor = new FrameAnchor { DisplayLabel = "Captured frame" };
        source.Project!.Anchors.Add(anchor);
        var revision = source.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id, sourceAsset.Physical!.ContentIdentity.Sha256!, 0, 10, 1, 24));
        var originalProject = source.Project;
        var originalLocation = source.Location!;
        var materializer = new BlockingMaterializer(await CreateMaterializedFileAsync("frame.png"));
        var copy = CreateService(source, materializer).CopySavedFrameAsync(
            anchor, revision, "Captured frame.png", target.Location!.ProjectFilePath);

        await materializer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await source.OpenAsync(other.Location!.ProjectFilePath);
        materializer.Release();
        var result = await copy;

        Assert.Same(originalProject, materializer.CapturedProject);
        Assert.Same(originalLocation, materializer.CapturedLocation);
        var targetRequest = Assert.IsType<AnchorMaterializationTarget>(materializer.LastRequest!.Target);
        Assert.Equal(anchor.Id, targetRequest.AnchorId);
        Assert.Equal(revision.Id, targetRequest.AnchorRevisionId);
        Assert.Contains(originalProject.AnchorRevisions, candidate => candidate.Id == revision.Id);
        Assert.Contains(originalProject.Assets, candidate => candidate.Id == sourceAsset.Id);
        Assert.Equal(MediaType.Image, result.CopiedAsset.MediaType);
    }

    [Fact]
    public async Task MaterializedCopyRejectsSameProjectBeforeMaterialization()
    {
        var (source, _, sourceAsset) = await CreateProjectsAsync();
        var anchor = new FrameAnchor { DisplayLabel = "Saved frame" };
        source.Project!.Anchors.Add(anchor);
        var revision = source.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id, sourceAsset.Physical!.ContentIdentity.Sha256!, 0, 0, 1, 24));
        var materializer = new RecordingMaterializer(await CreateMaterializedFileAsync("frame.png"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(source, materializer).CopySavedFrameAsync(
            anchor, revision, "frame.png", source.Location!.ProjectFilePath));
        Assert.Null(materializer.LastRequest);
    }

    [Fact]
    public async Task TargetSaveFailureRemovesMaterializedTargetImport()
    {
        var (source, target, sourceAsset) = await CreateProjectsAsync();
        var anchor = new FrameAnchor { DisplayLabel = "Saved frame" };
        source.Project!.Anchors.Add(anchor);
        var revision = source.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id, sourceAsset.Physical!.ContentIdentity.Sha256!, 0, 0, 1, 24));
        var materializer = new RecordingMaterializer(await CreateMaterializedFileAsync("frame.png"));
        var service = new MaterializedProjectMediaTransferService(
            source,
            materializer,
            new ProjectAssetTransferService(
                new TargetSaveFailingStore(new PortableProjectStore(), target.Location!.ProjectFilePath),
                new AssetImportService(new StubInspector())));

        await Assert.ThrowsAsync<IOException>(() => service.CopySavedFrameAsync(
            anchor, revision, "Saved frame.png", target.Location.ProjectFilePath));

        var imagesDirectory = Path.Combine(target.Location.RootDirectory, "assets", "images");
        Assert.False(Directory.Exists(imagesDirectory) && Directory.EnumerateFiles(imagesDirectory).Any());
        Assert.Contains(sourceAsset, source.Project.Assets);
        Assert.Contains(source.Project.AnchorRevisions, candidate => candidate.Id == revision.Id);
    }

    private static MaterializedProjectMediaTransferService CreateService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer) =>
        new(workspace, materializer, new ProjectAssetTransferService(
            new PortableProjectStore(), new AssetImportService(new StubInspector())));

    private async Task<(ProjectWorkspace Source, ProjectWorkspace Target, ProjectAsset SourceAsset)> CreateProjectsAsync()
    {
        var store = new PortableProjectStore();
        var importer = new AssetImportService(new StubInspector());
        var source = new ProjectWorkspace(store, importer);
        await source.CreateAsync(Path.Combine(_root, "source"), "Source");
        var incoming = await CreateMaterializedFileAsync("source.mp4");
        var sourceAsset = Assert.Single(await source.ImportAssetsAsync([incoming]));
        var target = new ProjectWorkspace(store, importer);
        await target.CreateAsync(Path.Combine(_root, "target"), "Target");
        return (source, target, sourceAsset);
    }

    private async Task<string> CreateMaterializedFileAsync(string fileName)
    {
        var directory = Path.Combine(_root, "materialized");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        await File.WriteAllTextAsync(path, "materialized media");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingMaterializer(string path) : IMediaMaterializer
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
                new ContentIdentity { Sha256 = "materialized-hash", Status = ContentHashStatus.Verified },
                encoding: null,
                isDurableSource: false));
        }
    }

    private sealed class BlockingMaterializer(string path) : IMediaMaterializer
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public VideoProject? CapturedProject { get; private set; }
        public ProjectLocation? CapturedLocation { get; private set; }
        public MaterializationRequest? LastRequest { get; private set; }

        public async Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default)
        {
            CapturedProject = project;
            CapturedLocation = location;
            LastRequest = request;
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = "materialized-hash", Status = ContentHashStatus.Verified },
                encoding: null,
                isDurableSource: false);
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default)
        {
            var isImage = Path.GetExtension(mediaPath).Equals(".png", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new MediaEncodingMetadata
            {
                ContainerFormat = isImage ? "png" : "mp4",
                DurationSeconds = isImage ? null : 5,
                Video = new VideoStreamMetadata { Width = 1280, Height = 720 }
            });
        }
    }

    private sealed class TargetSaveFailingStore(PortableProjectStore inner, string failingTargetPath) : IProjectStore
    {
        private readonly string _failingTargetPath = Path.GetFullPath(failingTargetPath);

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath, CancellationToken cancellationToken = default) =>
            inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default) =>
            Path.GetFullPath(location.ProjectFilePath).Equals(_failingTargetPath, StringComparison.OrdinalIgnoreCase)
                ? Task.FromException(new IOException("Simulated target save failure."))
                : inner.SaveAsync(project, location, cancellationToken);
    }
}
