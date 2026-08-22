using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ProjectAssetTransferWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge transfer workflow tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CopyCreatesTargetAssetAndKeepsSourcePhysicalAsset()
    {
        var (source, target, asset) = await CreateProjectsAsync();
        var sourcePath = source.GetAbsoluteAssetPath(asset);

        var result = await CreateWorkflow(source).CopyAsync(asset, target.Location!.ProjectFilePath);

        Assert.Equal(target.Project!.Name, result.TargetProjectName);
        Assert.Contains(source.Project!.Assets, candidate => candidate.Id == asset.Id);
        Assert.True(File.Exists(sourcePath));
        var (reopenedTarget, targetLocation) = await new PortableProjectStore().OpenAsync(target.Location.ProjectFilePath);
        var copied = Assert.Single(reopenedTarget.Assets);
        Assert.True(File.Exists(Path.Combine(targetLocation.RootDirectory, copied.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task MoveUnreferencedAssetCopiesThenRemovesSourceFileAndMetadata()
    {
        var (source, target, asset) = await CreateProjectsAsync();
        var sourcePath = source.GetAbsoluteAssetPath(asset);

        var result = await CreateWorkflow(source).MoveAsync(asset, target.Location!.ProjectFilePath);

        Assert.True(result.SourceRemoved);
        Assert.False(result.DependencyReport.IsInUse);
        Assert.Empty(source.Project!.Assets);
        Assert.False(File.Exists(sourcePath));
        var (reopenedSource, _) = await new PortableProjectStore().OpenAsync(source.Location!.ProjectFilePath);
        Assert.Empty(reopenedSource.Assets);
        var (reopenedTarget, _) = await new PortableProjectStore().OpenAsync(target.Location.ProjectFilePath);
        Assert.Single(reopenedTarget.Assets);
    }

    [Fact]
    public async Task MoveReferencedAssetCopiesButRetainsSourceWithExactDependencyReport()
    {
        var (source, target, asset) = await CreateProjectsAsync();
        source.Project!.CurrentGenerationDraft = new GenerationDraft
        {
            References = [new GenerationReferenceDraft
            {
                ObjectKind = GenerationReferenceObjectKind.Asset,
                LogicalObjectId = asset.Id
            }]
        };
        await source.SaveAsync();
        var sourcePath = source.GetAbsoluteAssetPath(asset);

        var result = await CreateWorkflow(source).MoveAsync(asset, target.Location!.ProjectFilePath);

        Assert.False(result.SourceRemoved);
        Assert.Equal([ProjectAssetDependency.CurrentGenerationDraft], result.DependencyReport.Dependencies);
        Assert.Contains(source.Project.Assets, candidate => candidate.Id == asset.Id);
        Assert.True(File.Exists(sourcePath));
        var (reopenedTarget, _) = await new PortableProjectStore().OpenAsync(target.Location.ProjectFilePath);
        Assert.Single(reopenedTarget.Assets);
    }

    [Fact]
    public async Task MoveTargetCopyFailureLeavesSourceUntouched()
    {
        var (source, _, asset) = await CreateProjectsAsync();
        var sourcePath = source.GetAbsoluteAssetPath(asset);
        var missingTarget = Path.Combine(_root, "missing", "missing.rfp");

        await Assert.ThrowsAsync<FileNotFoundException>(() => CreateWorkflow(source).MoveAsync(asset, missingTarget));

        Assert.Contains(source.Project!.Assets, candidate => candidate.Id == asset.Id);
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task MoveProjectSwitchDuringTargetCopyRetainsOriginalSourceAndCapturedProvenance()
    {
        var (source, target, sourceAsset) = await CreateProjectsAsync();
        var markerPath = Path.Combine(_root, "incoming", "target-marker.mp4");
        await File.WriteAllTextAsync(markerPath, "target marker");
        var targetMarker = Assert.Single(await target.ImportAssetsAsync([markerPath]));
        var sourceProjectId = source.Project!.Id;
        var sourceAssetId = sourceAsset.Id;
        var sourcePath = source.GetAbsoluteAssetPath(sourceAsset);
        var targetProjectFilePath = target.Location!.ProjectFilePath;
        var importer = new BlockingCopyImporter();
        var workflow = new ProjectAssetTransferWorkflow(
            source,
            new ProjectAssetTransferService(new PortableProjectStore(), importer),
            new ProjectAssetDependencyAnalyzer(),
            new PhysicalAssetRemovalService());

        var moveTask = workflow.MoveAsync(sourceAsset, targetProjectFilePath);
        await importer.ImportStarted.Task;
        await source.OpenAsync(targetProjectFilePath);
        importer.ReleaseImport();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => moveTask);
        Assert.Contains("target project copy succeeded", exception.Message, StringComparison.OrdinalIgnoreCase);

        var (reopenedSource, _) = await new PortableProjectStore().OpenAsync(
            Path.Combine(_root, "source", "Source.rfp"));
        Assert.Contains(reopenedSource.Assets, asset => asset.Id == sourceAssetId);
        Assert.True(File.Exists(sourcePath));

        var (reopenedTarget, _) = await new PortableProjectStore().OpenAsync(targetProjectFilePath);
        Assert.Contains(reopenedTarget.Assets, asset => asset.Id == targetMarker.Id);
        var copiedAsset = Assert.Single(reopenedTarget.Assets.Where(asset => asset.Id != targetMarker.Id));
        Assert.Equal(sourceProjectId.ToString("D"), copiedAsset.Provenance!.Parameters["sourceProjectId"]);
        Assert.Equal(sourceAssetId.ToString("D"), copiedAsset.Provenance.Parameters["sourceAssetId"]);
    }

    [Fact]
    public async Task MoveSourceRemovalSaveFailureKeepsSourceAndDurableTargetCopy()
    {
        var (createdSource, target, sourceAsset) = await CreateProjectsAsync();
        var sourceProjectFilePath = createdSource.Location!.ProjectFilePath;
        var sourcePath = createdSource.GetAbsoluteAssetPath(sourceAsset);
        var routingStore = new SourceSaveFailingStore(new PortableProjectStore(), sourceProjectFilePath);
        var source = new ProjectWorkspace(routingStore, new AssetImportService(new StubInspector()));
        await source.OpenAsync(sourceProjectFilePath);
        var currentSourceAsset = source.Project!.Assets.Single(asset => asset.Id == sourceAsset.Id);
        var targetProjectFilePath = target.Location!.ProjectFilePath;
        var workflow = new ProjectAssetTransferWorkflow(
            source,
            new ProjectAssetTransferService(routingStore, new AssetImportService(new StubInspector())),
            new ProjectAssetDependencyAnalyzer(),
            new PhysicalAssetRemovalService());

        await Assert.ThrowsAsync<IOException>(() => workflow.MoveAsync(currentSourceAsset, targetProjectFilePath));

        Assert.Contains(source.Project.Assets, asset => asset.Id == sourceAsset.Id);
        Assert.True(File.Exists(sourcePath));
        var (reopenedTarget, _) = await new PortableProjectStore().OpenAsync(targetProjectFilePath);
        Assert.Single(reopenedTarget.Assets);
    }

    [Fact]
    public async Task TransferRejectsVirtualAndMissingAssets()
    {
        var (source, target, _) = await CreateProjectsAsync();
        var virtualAsset = new ProjectAsset
        {
            StorageKind = AssetStorageKind.Virtual,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        source.Project!.Assets.Add(virtualAsset);
        var missing = new ProjectAsset { Id = Guid.NewGuid() };
        var workflow = CreateWorkflow(source);
        var targetProjectFilePath = target.Location!.ProjectFilePath;

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.CopyAsync(virtualAsset, targetProjectFilePath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.MoveAsync(missing, targetProjectFilePath));
    }

    [Fact]
    public void MoveResultCopiesDependencyReportIntoImmutableSnapshot()
    {
        var supplied = new List<ProjectAssetDependency> { ProjectAssetDependency.MediaRecipes };
        var sourceReport = new ProjectAssetDependencyReport(supplied);
        var result = new ProjectAssetMoveResult(
            new ProjectAssetCopyResult("Target", "target.rfp", new ProjectAsset()),
            sourceRemoved: false,
            sourceReport);
        supplied.Clear();

        Assert.Equal([ProjectAssetDependency.MediaRecipes], result.DependencyReport.Dependencies);
        var exposed = Assert.IsAssignableFrom<IList<ProjectAssetDependency>>(result.DependencyReport.Dependencies);
        Assert.Throws<NotSupportedException>(() => exposed[0] = ProjectAssetDependency.SavedFrames);
    }

    private async Task<(ProjectWorkspace Source, ProjectWorkspace Target, ProjectAsset Asset)> CreateProjectsAsync()
    {
        var incomingPath = Path.Combine(_root, "incoming", "source.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(incomingPath)!);
        await File.WriteAllTextAsync(incomingPath, "source media");
        var store = new PortableProjectStore();
        var importer = new AssetImportService(new StubInspector());
        var source = new ProjectWorkspace(store, importer);
        await source.CreateAsync(Path.Combine(_root, "source"), "Source");
        var asset = Assert.Single(await source.ImportAssetsAsync([incomingPath]));
        var target = new ProjectWorkspace(store, importer);
        await target.CreateAsync(Path.Combine(_root, "target"), "Target");
        return (source, target, asset);
    }

    private static ProjectAssetTransferWorkflow CreateWorkflow(ProjectWorkspace workspace)
    {
        var store = new PortableProjectStore();
        return new ProjectAssetTransferWorkflow(
            workspace,
            new ProjectAssetTransferService(store, new AssetImportService(new StubInspector())),
            new ProjectAssetDependencyAnalyzer(),
            new PhysicalAssetRemovalService());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata
            {
                ContainerFormat = "mp4",
                DurationSeconds = 5,
                Video = new VideoStreamMetadata { Width = 1280, Height = 720 }
            });
    }

    private sealed class BlockingCopyImporter : IAssetImportService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ImportStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default)
        {
            ImportStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            var sourcePath = Assert.Single(sourcePaths);
            var fileName = Path.GetFileName(sourcePath);
            var relativePath = $"assets/videos/{fileName}";
            var destinationPath = Path.Combine(location.RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return [new ProjectAsset
            {
                FileName = fileName,
                DisplayName = fileName,
                MediaType = MediaType.Video,
                StorageKind = AssetStorageKind.Physical,
                Physical = new PhysicalAssetStorage { RelativePath = relativePath }
            }];
        }

        public void ReleaseImport() => _release.TrySetResult();
    }

    private sealed class SourceSaveFailingStore : IProjectStore
    {
        private readonly PortableProjectStore _inner;
        private readonly string _sourceProjectFilePath;

        public SourceSaveFailingStore(PortableProjectStore inner, string sourceProjectFilePath)
        {
            _inner = inner;
            _sourceProjectFilePath = Path.GetFullPath(sourceProjectFilePath);
        }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(
            VideoProject project,
            ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            if (Path.GetFullPath(location.ProjectFilePath)
                .Equals(_sourceProjectFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromException(new IOException("Simulated source project save failure."));
            }

            return _inner.SaveAsync(project, location, cancellationToken);
        }
    }
}
