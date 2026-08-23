using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class GenerationJobFinalizerTests
{
    [Fact]
    public async Task SuccessfulJobUpdatesActiveProjectAndPublishesOutcome()
    {
        var projectPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "active-finalizer.rfp"));
        var generation = new GenerationRecord { Status = GenerationStatus.Running };
        var project = new VideoProject { Name = "Active project", Generations = [generation] };
        var store = new RecordingProjectStore((projectPath, project));
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(projectPath);
        var finalizer = new GenerationJobFinalizer(workspace, store, new SuccessfulIngestion());
        GenerationJobFinalizedEventArgs? outcome = null;
        finalizer.Finalized += (_, args) => outcome = args;

        await finalizer.FinalizeAsync(new TrackedGenerationJob
        {
            GenerationId = generation.Id,
            ProjectFilePath = projectPath,
            Status = GenerationStatus.Succeeded,
            Outputs = [new ProviderGenerationOutput("https://output.example/generated.mp4")]
        });

        Assert.Equal(GenerationStatus.Succeeded, generation.Status);
        Assert.Equal(OutputIngestionStatus.Succeeded, generation.IngestionStatus);
        Assert.Single(generation.OutputAssetIds);
        Assert.Single(project.Assets);
        Assert.Equal("application-job-coordinator", generation.ResponseMetadata["localMonitoring"]);
        Assert.True(store.SaveCount >= 2);
        Assert.NotNull(outcome);
        Assert.True(outcome.ActiveProjectUpdated);
        Assert.Equal(generation.Id, outcome.GenerationId);
    }

    [Fact]
    public async Task FailedJobUpdatesBackgroundProjectWithoutChangingActiveProject()
    {
        var activePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "active-project.rfp"));
        var backgroundPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "background-project.rfp"));
        var activeProject = new VideoProject { Name = "Active project" };
        var generation = new GenerationRecord { Status = GenerationStatus.Running };
        var backgroundProject = new VideoProject { Name = "Background project", Generations = [generation] };
        var store = new RecordingProjectStore(
            (activePath, activeProject),
            (backgroundPath, backgroundProject));
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(activePath);
        var finalizer = new GenerationJobFinalizer(workspace, store, new UnexpectedIngestion());
        GenerationJobFinalizedEventArgs? outcome = null;
        finalizer.Finalized += (_, args) => outcome = args;
        var error = new GenerationError { ProviderCode = "provider_failed", Message = "Nope" };

        await finalizer.FinalizeAsync(new TrackedGenerationJob
        {
            GenerationId = generation.Id,
            ProjectFilePath = backgroundPath,
            Status = GenerationStatus.Failed,
            Error = error
        });

        Assert.Same(activeProject, workspace.Project);
        Assert.Empty(activeProject.Generations);
        Assert.Equal(GenerationStatus.Failed, generation.Status);
        Assert.Same(error, generation.Error);
        Assert.NotNull(generation.CompletedAt);
        Assert.NotNull(outcome);
        Assert.False(outcome.ActiveProjectUpdated);
        Assert.Equal(backgroundPath, outcome.ProjectFilePath);
    }

    private sealed class RecordingProjectStore : IProjectStore
    {
        private readonly Dictionary<string, VideoProject> _projects;

        public RecordingProjectStore(params (string Path, VideoProject Project)[] projects)
        {
            _projects = projects.ToDictionary(
                item => Path.GetFullPath(item.Path),
                item => item.Project,
                StringComparer.OrdinalIgnoreCase);
        }

        public int SaveCount { get; private set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(projectFilePath);
            return Task.FromResult((_projects[fullPath], new ProjectLocation(Path.GetDirectoryName(fullPath)!, fullPath)));
        }

        public Task SaveAsync(
            VideoProject project,
            ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            _projects[Path.GetFullPath(location.ProjectFilePath)] = project;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class SuccessfulIngestion : IGeneratedOutputIngestionService
    {
        public Task<IReadOnlyList<ProjectAsset>> IngestAsync(
            ProjectLocation location,
            Guid generationId,
            IReadOnlyList<ProviderGenerationOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ProjectAsset> assets =
            [
                new()
                {
                    FileName = "generated.mp4",
                    DisplayName = "generated.mp4",
                    MediaType = MediaType.Video,
                    Origin = AssetOrigin.Generated,
                    Physical = new PhysicalAssetStorage
                    {
                        RelativePath = "generated/generated.mp4",
                        Durability = PhysicalAssetDurability.Generated,
                        Availability = PhysicalAssetAvailability.Available,
                        ContentIdentity = new ContentIdentity
                        {
                            Sha256 = new string('a', 64),
                            Status = ContentHashStatus.Verified
                        }
                    }
                }
            ];
            return Task.FromResult(assets);
        }
    }

    private sealed class UnexpectedIngestion : IGeneratedOutputIngestionService
    {
        public Task<IReadOnlyList<ProjectAsset>> IngestAsync(
            ProjectLocation location,
            Guid generationId,
            IReadOnlyList<ProviderGenerationOutput> outputs,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Failed jobs must not ingest output.");
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
