using ReelForge.App.Views.Settings;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class ProjectSettingsActionsTests
{
    [Fact]
    public async Task CleanupBlocksActiveProjectJobBeforeReconciliation()
    {
        var fixture = await CreateFixtureAsync();
        var activeJob = new TrackedGenerationJob
        {
            ProjectFilePath = fixture.Workspace.Location!.ProjectFilePath,
            Status = GenerationStatus.Running,
            IsReconciled = false
        };
        var actions = CreateActions(fixture, [activeJob]);

        var result = await actions.CleanupProjectAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("active or unreconciled generation job", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Reconciler.CallCount);
        Assert.False(fixture.Clip.IsDeleted);
        Assert.Equal(0, fixture.Host.RefreshCollectionsCount);
    }

    [Fact]
    public async Task CleanupReconcilesMissingSourceBeforeRemovingDerivedMedia()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Reconciler.OnReconcile = () =>
        {
            fixture.Source.Physical!.Availability = PhysicalAssetAvailability.Missing;
            return new PhysicalAssetAvailabilityReconciliationResult(true, false);
        };
        var actions = CreateActions(fixture, []);

        var result = await actions.CleanupProjectAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, fixture.Reconciler.CallCount);
        Assert.True(fixture.Clip.IsDeleted);
        Assert.Equal(1, fixture.Store.SaveCount);
        Assert.Equal(1, fixture.Host.ClearSelectionCount);
        Assert.Equal(1, fixture.Host.ResetFrameWorkspaceCount);
        Assert.Equal(1, fixture.Host.RefreshCollectionsCount);
        Assert.Equal([false, true], fixture.Host.EnabledStates);
    }

    private static ProjectSettingsActions CreateActions(Fixture fixture, IReadOnlyList<TrackedGenerationJob> jobs) =>
        new(
            fixture.Host,
            fixture.Workspace,
            null!,
            new ProjectCleanupService(),
            fixture.Reconciler,
            null!,
            () => new ApplicationSettings(),
            () => jobs);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var source = new ProjectAsset
        {
            FileName = "source.mp4",
            DisplayName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Available
            }
        };
        var clip = new ProjectAsset
        {
            FileName = "Saved clip",
            DisplayName = "Saved clip",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        var project = new VideoProject { Name = "Project", Assets = [source, clip] };
        project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = RecipeBoundary.SourceStart,
            End = RecipeBoundary.SourceEnd
        });
        var location = new ProjectLocation("C:\\project", "C:\\project\\Project.rfp");
        var store = new Store(project, location);
        var workspace = new ProjectWorkspace(store, new Importer());
        await workspace.OpenAsync(location.ProjectFilePath);
        return new Fixture(workspace, source, clip, store, new Reconciler(), new Host());
    }

    private sealed record Fixture(
        ProjectWorkspace Workspace,
        ProjectAsset Source,
        ProjectAsset Clip,
        Store Store,
        Reconciler Reconciler,
        Host Host);

    private sealed class Reconciler : IPhysicalAssetAvailabilityReconciler
    {
        public int CallCount { get; private set; }
        public Func<PhysicalAssetAvailabilityReconciliationResult>? OnReconcile { get; set; }

        public Task<PhysicalAssetAvailabilityReconciliationResult> ReconcileActivePhysicalAssetsAsync(
            VideoProject selectedProject,
            ProjectLocation selectedLocation,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(OnReconcile?.Invoke() ?? PhysicalAssetAvailabilityReconciliationResult.Unchanged);
        }
    }

    private sealed class Host : IProjectSettingsActionsHost
    {
        public List<bool> EnabledStates { get; } = [];
        public int ClearSelectionCount { get; private set; }
        public int ResetFrameWorkspaceCount { get; private set; }
        public int RefreshCollectionsCount { get; private set; }

        public void SetProjectActionsEnabled(bool isEnabled) => EnabledStates.Add(isEnabled);
        public void RefreshProjectUi() { }
        public void ClearProjectMediaSelectionAndPreview() => ClearSelectionCount++;
        public void ResetFrameWorkspace() => ResetFrameWorkspaceCount++;
        public void RefreshProjectCollections() => RefreshCollectionsCount++;
        public void SetStatus(string status) { }
    }

    private sealed class Store(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public int SaveCount { get; private set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) => Task.FromResult((project, location));

        public Task SaveAsync(
            VideoProject savedProject,
            ProjectLocation savedLocation,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class Importer : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
