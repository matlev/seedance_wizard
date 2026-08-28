using System.IO;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.App.Views.Projects;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Tests;

#pragma warning disable CA1707 // Test names describe behavior with readable clauses.
public sealed class ProjectLifecycleCoordinatorTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), "ReelForge.App.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TryReopenLastProjectAsync_WhenNoLastProject_DoesNothing()
    {
        var fixture = CreateFixture();

        await fixture.Coordinator.TryReopenLastProjectAsync();

        Assert.Empty(fixture.Store.OpenedPaths);
        Assert.Empty(fixture.Host.Statuses);
        Assert.Equal(0, fixture.Host.RefreshCount);
    }

    [Fact]
    public async Task TryReopenLastProjectAsync_WhenLastProjectIsMissing_ReportsStatusWithoutOpening()
    {
        var fixture = CreateFixture();
        fixture.Settings.General.LastProjectFilePath = Path.Combine(_temporaryDirectory, "missing.rfp");

        await fixture.Coordinator.TryReopenLastProjectAsync();

        Assert.Empty(fixture.Store.OpenedPaths);
        Assert.Equal("The last project is unavailable. Use Open to choose its current location or another project.", Assert.Single(fixture.Host.Statuses));
    }

    [Fact]
    public async Task TryReopenLastProjectAsync_WhenValidProjectExists_OpensAndRefreshesUi()
    {
        var projectPath = CreateProjectFile("last.rfp");
        var fixture = CreateFixture();
        fixture.Settings.General.LastProjectFilePath = projectPath;

        await fixture.Coordinator.TryReopenLastProjectAsync();

        Assert.Equal([Path.GetFullPath(projectPath)], fixture.Store.OpenedPaths);
        Assert.Equal(1, fixture.Host.RefreshCount);
        Assert.Contains(fixture.Host.Statuses, status => status.StartsWith("Reopening ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryReopenLastProjectAsync_WhenRecoveryIsAccepted_OpensRecoveredWorkingStateWithoutSaving()
    {
        var projectPath = CreateProjectFile("recovered-last.rfp");
        var fixture = CreateFixture();
        fixture.Settings.General.LastProjectFilePath = projectPath;
        fixture.RecoveryStore.Probe = new ProjectRecoveryProbe(
            new ProjectRecoveryCandidate(new VideoProject { Id = fixture.Store.OpenedProjectId, Name = "Recovered project" }));
        fixture.Dialogs.RecoveryDecision = ProjectRecoveryDecision.OpenRecoveredWorkingState;

        await fixture.Coordinator.TryReopenLastProjectAsync();

        Assert.Equal("Recovered project", fixture.Workspace.Project!.Name);
        Assert.Equal(ProjectWorkspaceState.Recovered, fixture.Workspace.State);
        Assert.Equal(2, fixture.Host.RefreshCount);
        Assert.Equal(0, fixture.Store.SaveCount);
        Assert.Equal(0, fixture.RecoveryStore.DiscardCount);
        Assert.Contains(fixture.Host.Statuses, status => status.Contains("until you explicitly save", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenProjectFromDialogAsync_WhenRecoveryIsDiscarded_LeavesCommittedProjectOpen()
    {
        var projectPath = CreateProjectFile("discard-recovery.rfp");
        var fixture = CreateFixture();
        fixture.Dialogs.ProjectFilePath = projectPath;
        fixture.RecoveryStore.Probe = new ProjectRecoveryProbe(
            new ProjectRecoveryCandidate(new VideoProject { Id = fixture.Store.OpenedProjectId, Name = "Recovered project" }));
        fixture.Dialogs.RecoveryDecision = ProjectRecoveryDecision.DiscardRecovery;

        await fixture.Coordinator.OpenProjectFromDialogAsync();

        Assert.Equal("discard-recovery", fixture.Workspace.Project!.Name);
        Assert.Equal(ProjectWorkspaceState.Clean, fixture.Workspace.State);
        Assert.Equal(1, fixture.RecoveryStore.DiscardCount);
        Assert.Equal(2, fixture.Host.RefreshCount);
        Assert.Equal(0, fixture.Store.SaveCount);
    }

    [Fact]
    public async Task OpenProjectFromDialogAsync_WhenRecoveryIsDeferred_KeepsCandidateAndCommittedProjectOpen()
    {
        var projectPath = CreateProjectFile("defer-recovery.rfp");
        var fixture = CreateFixture();
        fixture.Dialogs.ProjectFilePath = projectPath;
        fixture.RecoveryStore.Probe = new ProjectRecoveryProbe(
            new ProjectRecoveryCandidate(new VideoProject { Id = fixture.Store.OpenedProjectId, Name = "Recovered project" }));
        fixture.Dialogs.RecoveryDecision = ProjectRecoveryDecision.Defer;

        await fixture.Coordinator.OpenProjectFromDialogAsync();

        Assert.Equal("defer-recovery", fixture.Workspace.Project!.Name);
        Assert.NotNull(fixture.Workspace.RecoveryCandidate);
        Assert.Equal(ProjectWorkspaceState.RecoveryAvailable, fixture.Workspace.State);
        Assert.Equal(0, fixture.RecoveryStore.DiscardCount);
        Assert.Equal(1, fixture.Host.RefreshCount);
        Assert.Equal(0, fixture.Store.SaveCount);
        Assert.Contains(fixture.Host.Statuses, status => status.Contains("remains available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryReopenLastProjectAsync_WhenRecoveryIsInvalid_LeavesCommittedProjectInspectableAndReportsStatus()
    {
        var projectPath = CreateProjectFile("invalid-recovery.rfp");
        var fixture = CreateFixture();
        fixture.Settings.General.LastProjectFilePath = projectPath;
        fixture.RecoveryStore.Probe = new ProjectRecoveryProbe(null, "Recovery data was ignored because it is invalid.");

        await fixture.Coordinator.TryReopenLastProjectAsync();

        Assert.Equal("invalid-recovery", fixture.Workspace.Project!.Name);
        Assert.Equal(ProjectWorkspaceState.Failed, fixture.Workspace.State);
        Assert.Equal(1, fixture.Host.RefreshCount);
        Assert.Equal(0, fixture.Store.SaveCount);
        Assert.Contains(fixture.Host.Statuses, status => status.Contains("recovery data was not used", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryReopenLastProjectAsync_WhenOpenFails_ReportsDiagnosticDetails()
    {
        var projectPath = CreateProjectFile("broken.rfp");
        var fixture = CreateFixture();
        fixture.Settings.General.LastProjectFilePath = projectPath;
        fixture.Store.OpenException = new InvalidOperationException("corrupt project");

        await fixture.Coordinator.TryReopenLastProjectAsync();

        Assert.Equal(0, fixture.Host.RefreshCount);
        Assert.Contains(fixture.Host.Statuses, status => status.Contains("could not be reopened: corrupt project", StringComparison.Ordinal));
        Assert.Contains("Automatic project reopen failed", fixture.Host.InspectorText);
        Assert.Contains("corrupt project", fixture.Host.InspectorText);
    }

    [Fact]
    public async Task CreateProjectFromDialogAsync_CreatesRefreshesAndRemembersProject()
    {
        var projectPath = Path.Combine(_temporaryDirectory, "created", "created.rfp");
        var fixture = CreateFixture();
        fixture.Dialogs.NewProjectSelection = new NewProjectSelection(Path.GetDirectoryName(projectPath)!, "created");
        fixture.Store.CreatedLocation = new ProjectLocation(Path.GetDirectoryName(projectPath)!, projectPath);

        await fixture.Coordinator.CreateProjectFromDialogAsync();

        Assert.Equal((Path.GetDirectoryName(projectPath)!, "created"), Assert.Single(fixture.Store.CreateRequests));
        Assert.Equal(1, fixture.Host.RefreshCount);
        Assert.Equal(Path.GetFullPath(projectPath), fixture.Settings.General.LastProjectFilePath);
        Assert.Equal(Path.GetFullPath(projectPath), Assert.Single(fixture.Settings.General.RecentProjectFilePaths));
        Assert.Equal(1, fixture.SettingsStore.SaveCount);
        Assert.Equal("Creating project…", Assert.Single(fixture.Host.RunStatuses));
    }

    [Fact]
    public async Task OpenProjectFromDialogAsync_RefreshesAndRemembersProject()
    {
        var projectPath = CreateProjectFile("chosen.rfp");
        var fixture = CreateFixture();
        fixture.Dialogs.ProjectFilePath = projectPath;

        await fixture.Coordinator.OpenProjectFromDialogAsync();

        Assert.Equal([Path.GetFullPath(projectPath)], fixture.Store.OpenedPaths);
        Assert.Equal(1, fixture.Host.RefreshCount);
        Assert.Equal(Path.GetFullPath(projectPath), fixture.Settings.General.LastProjectFilePath);
        Assert.Equal(Path.GetFullPath(projectPath), Assert.Single(fixture.Settings.General.RecentProjectFilePaths));
        Assert.Equal(1, fixture.SettingsStore.SaveCount);
        Assert.Equal("Opening project…", Assert.Single(fixture.Host.RunStatuses));
    }

    [Fact]
    public async Task OpenProjectFromDialogAsync_ReconcilesMissingSourcesBeforeProjectMediaRefresh()
    {
        var projectPath = CreateProjectFile("missing-source.rfp");
        var source = new ProjectAsset
        {
            FileName = "missing.mp4",
            DisplayName = "missing.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/missing.mp4",
                Availability = PhysicalAssetAvailability.Available
            }
        };
        var clip = new ProjectAsset
        {
            FileName = "Broken clip",
            DisplayName = "Broken clip",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        var project = new VideoProject { Name = "Missing source", Assets = [source, clip] };
        project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = RecipeBoundary.SourceStart,
            End = RecipeBoundary.SourceEnd
        });
        var fixture = CreateFixture();
        fixture.Dialogs.ProjectFilePath = projectPath;
        fixture.Store.ProjectToOpen = project;

        await fixture.Coordinator.OpenProjectFromDialogAsync();

        Assert.Equal(PhysicalAssetAvailability.Missing, source.Physical!.Availability);
        Assert.True(new ProjectDegradationAnalyzer().Analyze(project).IsDegradedAsset(clip.Id));
        Assert.Equal(1, fixture.Store.SaveCount);
        Assert.Equal(1, fixture.Host.RefreshCount);
    }

    [Fact]
    public async Task OpenProjectFromDialogAsync_WhenRememberingFails_LeavesOpenedProjectAndReportsNonFatalStatus()
    {
        var projectPath = CreateProjectFile("chosen.rfp");
        var fixture = CreateFixture();
        fixture.Dialogs.ProjectFilePath = projectPath;
        fixture.SettingsStore.SaveException = new IOException("settings locked");

        await fixture.Coordinator.OpenProjectFromDialogAsync();

        Assert.Equal(1, fixture.Host.RefreshCount);
        Assert.Equal(Path.GetFullPath(projectPath), fixture.Workspace.Location!.ProjectFilePath);
        Assert.Contains(fixture.Host.AppendedStatuses, status => status.Contains("could not remember this project", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAndRestoreProjectUiState_PersistsAndRestoresWorkspaceAndSelectedMedia()
    {
        var fixture = CreateFixture();
        await OpenFixtureProjectAsync(fixture, "state.rfp");
        fixture.Host.ActiveWorkspaceValue = ProjectWorkspaceKind.Edit;
        var selectedId = Guid.NewGuid();

        await fixture.Coordinator.SaveProjectUiStateAsync("asset", selectedId);
        fixture.Host.ProjectMediaItemToFind = new ProjectMediaListItem(new ProjectAsset { Id = selectedId, FileName = "clip", DisplayName = "clip" });
        fixture.Coordinator.RestoreProjectUiState();

        var state = fixture.Settings.General.ProjectStates[fixture.Workspace.Project!.Id.ToString("N")];
        Assert.Equal(ProjectWorkspaceKind.Edit, state.Workspace);
        Assert.Equal("asset", state.SelectedMediaKind);
        Assert.Equal(selectedId, state.SelectedMediaId);
        Assert.Equal(1, fixture.SettingsStore.SaveCount);
        Assert.Equal([ProjectWorkspaceKind.Edit], fixture.Host.RestoredWorkspaces);
        Assert.Equal("asset", fixture.Host.FoundMediaKind);
        Assert.Equal(selectedId, fixture.Host.FoundMediaId);
        Assert.Same(fixture.Host.ProjectMediaItemToFind, fixture.Host.SelectedProjectMediaItem);
        Assert.False(fixture.Coordinator.IsRestoringProjectUiState);
    }

    [Fact]
    public async Task RestoreProjectUiState_WhenHostThrows_ResetsRestorationGuard()
    {
        var fixture = CreateFixture();
        await OpenFixtureProjectAsync(fixture, "restore-failure.rfp");
        fixture.Host.ApplyRestoredWorkspaceException = new InvalidOperationException("host failed");

        Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.RestoreProjectUiState());

        Assert.False(fixture.Coordinator.IsRestoringProjectUiState);
    }

    [Fact]
    public async Task RememberedBakedPreview_MatchesOnlyTheExactCurrentProjectPathCompositionAndRevision()
    {
        var fixture = CreateFixture();
        await OpenFixtureProjectAsync(fixture, "preview.rfp");
        var project = fixture.Workspace.Project!;
        var location = fixture.Workspace.Location!;
        var compositionId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();

        await fixture.Coordinator.RememberBakedCompositionPreviewAsync(project, location, compositionId, revisionId);

        Assert.True(fixture.Coordinator.HasRememberedBakedCompositionPreview(project, location, compositionId, revisionId));
        Assert.False(fixture.Coordinator.HasRememberedBakedCompositionPreview(project, location, compositionId, Guid.NewGuid()));
        Assert.False(fixture.Coordinator.HasRememberedBakedCompositionPreview(
            project,
            new ProjectLocation(location.RootDirectory, Path.Combine(location.RootDirectory, "copied.rfp")),
            compositionId,
            revisionId));
        Assert.Equal(location.ProjectFilePath,
            fixture.Settings.General.ProjectStates[project.Id.ToString("N")].BakedCompositionPreview!.ProjectFilePath);
        Assert.Equal(1, fixture.SettingsStore.SaveCount);
    }

    [Fact]
    public async Task RememberedBakedPreview_DoesNotPersistForAProjectThatIsNoLongerCurrent()
    {
        var fixture = CreateFixture();
        await OpenFixtureProjectAsync(fixture, "current.rfp");
        var staleProject = fixture.Workspace.Project!;
        var staleLocation = fixture.Workspace.Location!;
        await OpenFixtureProjectAsync(fixture, "replacement.rfp");

        await fixture.Coordinator.RememberBakedCompositionPreviewAsync(
            staleProject, staleLocation, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(0, fixture.SettingsStore.SaveCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
            Directory.Delete(_temporaryDirectory, recursive: true);
    }

    private static Fixture CreateFixture()
    {
        var settings = new ApplicationSettings();
        var settingsStore = new FakeSettingsStore();
        var store = new FakeProjectStore();
        var recoveryStore = new FakeRecoveryStore();
        var workspace = new ProjectWorkspace(store, new FakeAssetImporter(), recoveryStore);
        var host = new FakeHost();
        var dialogs = new FakeDialogs();
        return new Fixture(
            settings,
            settingsStore,
            store,
            recoveryStore,
            workspace,
            host,
            dialogs,
            new ProjectLifecycleCoordinator(
                workspace,
                new RecentProjectTracker(settingsStore),
                dialogs,
                settingsStore,
                () => settings,
                new PhysicalAssetSelectionPreparationService(workspace, new FakeInspector()),
                host));
    }

    private async Task OpenFixtureProjectAsync(Fixture fixture, string fileName)
    {
        var path = CreateProjectFile(fileName);
        await fixture.Workspace.OpenAsync(path);
    }

    private string CreateProjectFile(string fileName)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, fileName);
        File.WriteAllText(path, "{}");
        return path;
    }

    private sealed record Fixture(
        ApplicationSettings Settings,
        FakeSettingsStore SettingsStore,
        FakeProjectStore Store,
        FakeRecoveryStore RecoveryStore,
        ProjectWorkspace Workspace,
        FakeHost Host,
        FakeDialogs Dialogs,
        ProjectLifecycleCoordinator Coordinator);

    private sealed class FakeDialogs : IProjectLifecycleDialogs
    {
        public NewProjectSelection? NewProjectSelection { get; set; }
        public string? ProjectFilePath { get; set; }
        public ProjectRecoveryDecision RecoveryDecision { get; set; } = ProjectRecoveryDecision.Defer;
        public NewProjectSelection? SelectNewProject(ApplicationSettings settings) => NewProjectSelection;
        public string? SelectProjectToOpen(ApplicationSettings settings) => ProjectFilePath;
        public ProjectRecoveryDecision DecideRecovery(ProjectRecoveryCandidate candidate) => RecoveryDecision;
    }

    private sealed class FakeSettingsStore : IApplicationSettingsStore
    {
        public string LocalSettingsPath => "settings.json";
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; set; }
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ApplicationSettings());
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
        }
    }

    private sealed class FakeProjectStore : IProjectStore
    {
        public List<(string RootDirectory, string Name)> CreateRequests { get; } = [];
        public List<string> OpenedPaths { get; } = [];
        public ProjectLocation? CreatedLocation { get; set; }
        public Exception? OpenException { get; set; }
        public Guid OpenedProjectId { get; } = Guid.NewGuid();
        public VideoProject? ProjectToOpen { get; set; }
        public int SaveCount { get; private set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default)
        {
            CreateRequests.Add((rootDirectory, name));
            var location = CreatedLocation ?? new ProjectLocation(rootDirectory, Path.Combine(rootDirectory, $"{name}.rfp"));
            return Task.FromResult((new VideoProject { Name = name }, location));
        }

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default)
        {
            OpenedPaths.Add(projectFilePath);
            return OpenException is null
                ? Task.FromResult((ProjectToOpen ?? new VideoProject { Id = OpenedProjectId, Name = Path.GetFileNameWithoutExtension(projectFilePath) }, new ProjectLocation(Path.GetDirectoryName(projectFilePath)!, projectFilePath)))
                : Task.FromException<(VideoProject Project, ProjectLocation Location)>(OpenException);
        }

        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Availability reconciliation must not inspect media encoding.");
    }

    private sealed class FakeRecoveryStore : IProjectRecoveryStore
    {
        public ProjectRecoveryProbe Probe { get; set; } = ProjectRecoveryProbe.None;
        public int DiscardCount { get; private set; }

        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            Task.FromResult(Probe);

        public Task WriteAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DiscardAsync(ProjectLocation location, CancellationToken cancellationToken = default)
        {
            DiscardCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAssetImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectAsset>>([]);
    }

    private sealed class FakeHost : IProjectLifecycleCoordinatorHost
    {
        public ProjectWorkspaceKind ActiveWorkspace { get; set; }
        public ProjectWorkspaceKind ActiveWorkspaceValue { get => ActiveWorkspace; set => ActiveWorkspace = value; }
        public List<string> Statuses { get; } = [];
        public List<string> AppendedStatuses { get; } = [];
        public List<string> RunStatuses { get; } = [];
        public List<ProjectWorkspaceKind> RestoredWorkspaces { get; } = [];
        public int RefreshCount { get; private set; }
        public string InspectorText { get; private set; } = string.Empty;
        public ProjectMediaListItem? ProjectMediaItemToFind { get; set; }
        public ProjectMediaListItem? SelectedProjectMediaItem { get; private set; }
        public string? FoundMediaKind { get; private set; }
        public Guid? FoundMediaId { get; private set; }
        public Exception? ApplyRestoredWorkspaceException { get; set; }

        public async Task RunUiActionAsync(string status, Func<Task> action)
        {
            RunStatuses.Add(status);
            await action();
        }

        public void RefreshProjectUi() => RefreshCount++;
        public void ApplyRestoredWorkspaceMode(ProjectWorkspaceKind workspace)
        {
            RestoredWorkspaces.Add(workspace);
            if (ApplyRestoredWorkspaceException is not null) throw ApplyRestoredWorkspaceException;
        }
        public ProjectMediaListItem? FindProjectMediaItem(string mediaKind, Guid mediaId)
        {
            FoundMediaKind = mediaKind;
            FoundMediaId = mediaId;
            return ProjectMediaItemToFind;
        }
        public void SelectProjectMediaItem(ProjectMediaListItem? item) => SelectedProjectMediaItem = item;
        public void SetStatus(string status) => Statuses.Add(status);
        public void AppendStatus(string status) => AppendedStatuses.Add(status);
        public void SetInspectorText(string text) => InspectorText = text;
    }
}
#pragma warning restore CA1707
