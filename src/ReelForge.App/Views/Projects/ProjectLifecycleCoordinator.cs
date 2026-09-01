using System.Globalization;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Projects;

/// <summary>
/// Coordinates project creation/opening and project-local shell state. WPF controls
/// remain behind <see cref="IProjectLifecycleCoordinatorHost"/> so this class owns
/// lifecycle policy without taking ownership of shell presentation.
/// </summary>
internal sealed class ProjectLifecycleCoordinator
{
    private readonly ProjectWorkspace _workspace;
    private readonly RecentProjectTracker _recentProjectTracker;
    private readonly IProjectLifecycleDialogs _dialogs;
    private readonly IApplicationSettingsStore _settingsStore;
    private readonly Func<ApplicationSettings> _currentSettings;
    private readonly IPhysicalAssetAvailabilityReconciler _physicalAssetAvailability;
    private readonly IProjectLifecycleCoordinatorHost _host;

    public ProjectLifecycleCoordinator(
        ProjectWorkspace workspace,
        RecentProjectTracker recentProjectTracker,
        IProjectLifecycleDialogs dialogs,
        IApplicationSettingsStore settingsStore,
        Func<ApplicationSettings> currentSettings,
        IPhysicalAssetAvailabilityReconciler physicalAssetAvailability,
        IProjectLifecycleCoordinatorHost host)
    {
        _workspace = workspace;
        _recentProjectTracker = recentProjectTracker;
        _dialogs = dialogs;
        _settingsStore = settingsStore;
        _currentSettings = currentSettings;
        _physicalAssetAvailability = physicalAssetAvailability;
        _host = host;
    }

    public bool IsRestoringProjectUiState { get; private set; }

    public async Task TryReopenLastProjectAsync()
    {
        var settings = _currentSettings();
        if (string.IsNullOrWhiteSpace(settings.General.LastProjectFilePath)) return;

        var projectFilePath = RecentProjectTracker.GetExistingProjectFile(settings);
        if (projectFilePath is null)
        {
            _host.SetStatus("The last project is unavailable. Use Open to choose its current location or another project.");
            return;
        }

        _host.SetStatus($"Reopening {projectFilePath}…");
        try
        {
            await _workspace.OpenAsync(projectFilePath);
            await ReconcilePhysicalAssetAvailabilityIfSafeAsync();
            _host.RefreshProjectUi();
            await OfferRecoveryAsync();
        }
        catch (Exception exception)
        {
            _host.SetStatus($"The last project could not be reopened: {exception.Message}");
            _host.SetInspectorText($"Automatic project reopen failed\n\n{exception}");
        }
    }

    public async Task CreateProjectFromDialogAsync()
    {
        var selection = _dialogs.SelectNewProject(_currentSettings());
        if (selection is null) return;

        await _host.RunUiActionAsync(
            "Creating project…",
            async () =>
            {
                await _workspace.CreateAsync(selection.ProjectDirectory, selection.ProjectName);
                _host.RefreshProjectUi();
                await RememberCurrentProjectAsync();
            });
    }

    public async Task OpenProjectFromDialogAsync()
    {
        var projectFilePath = _dialogs.SelectProjectToOpen(_currentSettings());
        if (projectFilePath is null) return;

        await _host.RunUiActionAsync(
            "Opening project…",
            async () =>
            {
                await _workspace.OpenAsync(projectFilePath);
                await ReconcilePhysicalAssetAvailabilityIfSafeAsync();
                _host.RefreshProjectUi();
                await RememberCurrentProjectAsync();
                await OfferRecoveryAsync();
            });
    }

    public async Task SaveProjectUiStateAsync(string? mediaKind = null, Guid? mediaId = null)
    {
        if (_workspace.Project is null) return;

        var state = GetOrCreateCurrentProjectState();
        if (state is null) return;

        state.Workspace = _host.ActiveWorkspace;
        if (mediaKind is not null)
        {
            state.SelectedMediaKind = mediaKind;
            state.SelectedMediaId = mediaId;
        }

        await _settingsStore.SaveAsync(_currentSettings());
    }

    /// <summary>
    /// Records only the user's intent to reopen this exact rendered composition. The cache path
    /// is intentionally not part of settings: materialization may reuse or rebuild it later.
    /// </summary>
    public async Task RememberBakedCompositionPreviewAsync(
        VideoProject project,
        ProjectLocation location,
        Guid compositionAssetId,
        Guid recipeRevisionId)
    {
        if (!ReferenceEquals(_workspace.Project, project) || !ReferenceEquals(_workspace.Location, location))
            return;

        var state = GetOrCreateCurrentProjectState();
        if (state is null) return;

        state.BakedCompositionPreview = new BakedCompositionPreviewPreference
        {
            ProjectFilePath = location.ProjectFilePath,
            CompositionAssetId = compositionAssetId,
            RecipeRevisionId = recipeRevisionId
        };
        await _settingsStore.SaveAsync(_currentSettings());
    }

    /// <summary>
    /// Requires the exact currently-opened project location in addition to logical IDs, so a
    /// copied project with the same project ID cannot inherit a cached-preview preference.
    /// </summary>
    public bool HasRememberedBakedCompositionPreview(
        VideoProject? project,
        ProjectLocation? location,
        Guid compositionAssetId,
        Guid recipeRevisionId)
    {
        if (project is null || location is null ||
            !ReferenceEquals(_workspace.Project, project) || !ReferenceEquals(_workspace.Location, location))
        {
            return false;
        }

        var key = project.Id.ToString("N", CultureInfo.InvariantCulture);
        return _currentSettings().General.ProjectStates.TryGetValue(key, out var state) &&
               state.BakedCompositionPreview is { } preference &&
               preference.Matches(location.ProjectFilePath, compositionAssetId, recipeRevisionId);
    }

    public void RestoreProjectUiState()
    {
        if (_workspace.Project is null) return;

        var settings = _currentSettings();
        var key = _workspace.Project.Id.ToString("N", CultureInfo.InvariantCulture);
        settings.General.ProjectStates.TryGetValue(key, out var state);

        IsRestoringProjectUiState = true;
        try
        {
            _host.ApplyRestoredWorkspaceMode(state?.Workspace ?? ProjectWorkspaceKind.Generate);
            if (state is { SelectedMediaKind: { } kind, SelectedMediaId: { } mediaId })
                _host.SelectProjectMediaItem(_host.FindProjectMediaItem(kind, mediaId));
        }
        finally
        {
            IsRestoringProjectUiState = false;
        }
    }

    private async Task RememberCurrentProjectAsync()
    {
        if (_workspace.Location is null) return;

        try
        {
            await _recentProjectTracker.RememberAsync(
                _currentSettings(),
                _workspace.Location.ProjectFilePath);
        }
        catch (Exception exception)
        {
            _host.AppendStatus($" ReelForge could not remember this project for the next launch: {exception.Message}");
        }
    }

    private async Task OfferRecoveryAsync()
    {
        if (_workspace.RecoveryCandidate is not { } candidate)
        {
            if (_workspace.State == ProjectWorkspaceState.Failed && !string.IsNullOrWhiteSpace(_workspace.FailureDetail))
            {
                _host.SetStatus(
                    $"The saved project opened, but recovery data was not used: {_workspace.FailureDetail}");
            }

            return;
        }

        switch (_dialogs.DecideRecovery(candidate))
        {
            case ProjectRecoveryDecision.OpenRecoveredWorkingState:
                await _workspace.AcceptRecoveryAsync();
                _host.RefreshProjectUi();
                _host.SetStatus(
                    "Recovered working state opened. The saved project remains unchanged until you explicitly save.");
                break;

            case ProjectRecoveryDecision.DiscardRecovery:
                await _workspace.DiscardRecoveryAsync();
                await ReconcilePhysicalAssetAvailabilityIfSafeAsync();
                _host.RefreshProjectUi();
                _host.SetStatus("Recovery data discarded. The saved project remains open.");
                break;

            case ProjectRecoveryDecision.Defer:
                _host.SetStatus("Recovery data remains available. The saved project remains open.");
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private async Task ReconcilePhysicalAssetAvailabilityIfSafeAsync()
    {
        if (_workspace.Project is not { } project || _workspace.Location is not { } location ||
            _workspace.State is not (ProjectWorkspaceState.Clean or ProjectWorkspaceState.Saved or ProjectWorkspaceState.Degraded))
            return;

        var result = await _physicalAssetAvailability
            .ReconcileActivePhysicalAssetsAsync(project, location)
            .ConfigureAwait(true);
        if (result.Failure is not null)
            throw new InvalidOperationException(
                "Project media availability could not be reconciled safely.", result.Failure);
    }

    private ProjectUserInterfaceState? GetOrCreateCurrentProjectState()
    {
        if (_workspace.Project is null) return null;

        var settings = _currentSettings();
        var key = _workspace.Project.Id.ToString("N", CultureInfo.InvariantCulture);
        if (!settings.General.ProjectStates.TryGetValue(key, out var state))
        {
            state = new ProjectUserInterfaceState();
            settings.General.ProjectStates[key] = state;
        }

        return state;
    }
}

internal interface IProjectLifecycleCoordinatorHost
{
    ProjectWorkspaceKind ActiveWorkspace { get; }
    Task RunUiActionAsync(string status, Func<Task> action);
    void RefreshProjectUi();
    void ApplyRestoredWorkspaceMode(ProjectWorkspaceKind workspace);
    ProjectMediaListItem? FindProjectMediaItem(string mediaKind, Guid mediaId);
    void SelectProjectMediaItem(ProjectMediaListItem? item);
    void SetStatus(string status);
    void AppendStatus(string status);
    void SetInspectorText(string text);
}
