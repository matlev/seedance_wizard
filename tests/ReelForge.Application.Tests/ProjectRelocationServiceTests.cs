using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class ProjectRelocationServiceTests
{
    [Fact]
    public async Task RelocationRebindsActiveWorkspaceAndRemovesCrossVolumeSourceAfterPublish()
    {
        var source = new ProjectLocation("source", "source/Project.rfp");
        var destination = new ProjectLocation("destination", "destination/Project.rfp");
        var project = new VideoProject { Id = Guid.NewGuid(), Name = "Project" };
        var store = new RelocationStore(source, destination, project);
        var coordinator = new ProjectSaveCoordinator();
        var workspace = new ProjectWorkspace(store, new NoOpImporter(), saveCoordinator: coordinator);
        await workspace.OpenAsync(source.ProjectFilePath);
        var files = new RecordingRelocationFileSystem(source, destination);
        var service = new ProjectRelocationService(workspace, store, files, coordinator);

        var result = await service.RelocateAsync(new ProjectRelocationRequest(destination.RootDirectory));

        Assert.Equal(project.Id, result.Project.Id);
        Assert.Equal(destination, workspace.Location);
        Assert.Equal(["prepare", "publish", "remove-source"], files.Operations);
        Assert.DoesNotContain(destination.ProjectFilePath, store.OpenedProjectFiles);
        Assert.True(result.SourceCleanupCompleted);
    }

    [Fact]
    public async Task RelocationRejectsDirtyWorkspaceBeforeFilesystemWork()
    {
        var source = new ProjectLocation("source", "source/Project.rfp");
        var destination = new ProjectLocation("destination", "destination/Project.rfp");
        var project = new VideoProject { Id = Guid.NewGuid(), Name = "Project" };
        var store = new RelocationStore(source, destination, project);
        var coordinator = new ProjectSaveCoordinator();
        var workspace = new ProjectWorkspace(store, new NoOpImporter(), saveCoordinator: coordinator);
        await workspace.OpenAsync(source.ProjectFilePath);
        project.Touch();
        // A normal save transition is asynchronous and unnecessary here: Dirty is the conservative
        // state the workspace uses while a mutation is pending.
        typeof(ProjectWorkspace).GetProperty(nameof(ProjectWorkspace.State))!.SetValue(workspace, ProjectWorkspaceState.Dirty);
        var files = new RecordingRelocationFileSystem(source, destination);
        var service = new ProjectRelocationService(workspace, store, files, coordinator);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RelocateAsync(new ProjectRelocationRequest(destination.RootDirectory)));
        Assert.Empty(files.Operations);
    }

    [Fact]
    public async Task CallerCancellationDuringPublishStillRebindsAndRemovesSource()
    {
        var source = new ProjectLocation("source", "source/Project.rfp");
        var destination = new ProjectLocation("destination", "destination/Project.rfp");
        var store = new RelocationStore(source, destination, new VideoProject { Id = Guid.NewGuid(), Name = "Project" });
        var coordinator = new ProjectSaveCoordinator();
        var workspace = new ProjectWorkspace(store, new NoOpImporter(), saveCoordinator: coordinator);
        await workspace.OpenAsync(source.ProjectFilePath);
        using var cancellation = new CancellationTokenSource();
        var files = new RecordingRelocationFileSystem(source, destination) { CancelDuringPublish = cancellation };
        var service = new ProjectRelocationService(workspace, store, files, coordinator);

        var result = await service.RelocateAsync(
            new ProjectRelocationRequest(destination.RootDirectory), cancellationToken: cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(destination, workspace.Location);
        Assert.True(result.SourceCleanupCompleted);
        Assert.Equal(["prepare", "publish", "remove-source"], files.Operations);
        Assert.False(files.PublishTokenCanBeCanceled);
    }

    [Fact]
    public async Task PublishFailureRollsBackStagingAndLeavesSourceWorkspaceBound()
    {
        var source = new ProjectLocation("source", "source/Project.rfp");
        var destination = new ProjectLocation("destination", "destination/Project.rfp");
        var store = new RelocationStore(source, destination, new VideoProject { Id = Guid.NewGuid(), Name = "Project" });
        var coordinator = new ProjectSaveCoordinator();
        var workspace = new ProjectWorkspace(store, new NoOpImporter(), saveCoordinator: coordinator);
        await workspace.OpenAsync(source.ProjectFilePath);
        var files = new RecordingRelocationFileSystem(source, destination) { FailPublish = true };
        var service = new ProjectRelocationService(workspace, store, files, coordinator);

        await Assert.ThrowsAsync<IOException>(() => service.RelocateAsync(new ProjectRelocationRequest(destination.RootDirectory)));

        Assert.Equal(source, workspace.Location);
        Assert.Equal(["prepare", "publish", "rollback"], files.Operations);
    }

    [Fact]
    public async Task SourceRemovalFailureKeepsPublishedDestinationBoundAndReturnsWarning()
    {
        var source = new ProjectLocation("source", "source/Project.rfp");
        var destination = new ProjectLocation("destination", "destination/Project.rfp");
        var store = new RelocationStore(source, destination, new VideoProject { Id = Guid.NewGuid(), Name = "Project" });
        var coordinator = new ProjectSaveCoordinator();
        var workspace = new ProjectWorkspace(store, new NoOpImporter(), saveCoordinator: coordinator);
        await workspace.OpenAsync(source.ProjectFilePath);
        var files = new RecordingRelocationFileSystem(source, destination) { FailSourceRemoval = true };
        var service = new ProjectRelocationService(workspace, store, files, coordinator);

        var result = await service.RelocateAsync(new ProjectRelocationRequest(destination.RootDirectory));

        Assert.Equal(destination, workspace.Location);
        Assert.False(result.SourceCleanupCompleted);
        Assert.NotNull(result.SourceCleanupWarning);
    }

    private sealed class RelocationStore(ProjectLocation source, ProjectLocation destination, VideoProject project) : IProjectStore
    {
        public List<string> OpenedProjectFiles { get; } = [];

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default)
        {
            OpenedProjectFiles.Add(projectFilePath);
            var location = projectFilePath.Equals(destination.ProjectFilePath, StringComparison.OrdinalIgnoreCase)
                ? destination
                : source;
            return Task.FromResult((new VideoProject { Id = project.Id, Name = project.Name }, location));
        }

        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingRelocationFileSystem(ProjectLocation source, ProjectLocation destination) : IProjectRelocationFileSystem
    {
        public List<string> Operations { get; } = [];
        public CancellationTokenSource? CancelDuringPublish { get; init; }
        public bool FailPublish { get; init; }
        public bool FailSourceRemoval { get; init; }
        public bool PublishTokenCanBeCanceled { get; private set; }

        public Task<ProjectRelocationPlan> PrepareAsync(ProjectLocation sourceLocation, string destinationRootDirectory,
            IProgress<ProjectRelocationProgress>? progress, CancellationToken cancellationToken)
        {
            Operations.Add("prepare");
            return Task.FromResult(new ProjectRelocationPlan(source, destination,
                new ProjectLocation("staging", "staging/Project.rfp"), true, 1, 1));
        }

        public Task PublishAsync(ProjectRelocationPlan plan, CancellationToken cancellationToken)
        {
            Operations.Add("publish");
            PublishTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            CancelDuringPublish?.Cancel();
            if (FailPublish) throw new IOException("Publish failed.");
            return Task.CompletedTask;
        }

        public Task RemoveSourceAsync(ProjectRelocationPlan plan, CancellationToken cancellationToken)
        {
            Operations.Add("remove-source");
            if (FailSourceRemoval) throw new IOException("Source folder is locked.");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(ProjectRelocationPlan plan)
        {
            Operations.Add("rollback");
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectAsset>>([]);
    }
}
