using System.IO;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Settings;

/// <summary>
/// Shell-owned project actions presented by the Project settings category. These actions deliberately
/// stay outside the application-settings editor because a project move or cleanup is an explicit project operation.
/// </summary>
public interface IProjectSettingsActions
{
    bool HasActiveProject { get; }
    string? CurrentProjectRootDirectory { get; }
    Task<ProjectSettingsActionResult> MoveProjectAsync(string destinationRootDirectory);
    Task<ProjectSettingsActionResult> CleanupProjectAsync();
}

public sealed record ProjectSettingsActionResult(bool Succeeded, string Message);

/// <summary>
/// The deliberately narrow MainWindow surface required by project-settings actions.
/// </summary>
internal interface IProjectSettingsActionsHost
{
    void SetProjectActionsEnabled(bool isEnabled);
    void RefreshProjectUi();
    void ClearProjectMediaSelectionAndPreview();
    void ResetFrameWorkspace();
    void RefreshProjectCollections();
    void SetStatus(string status);
}

internal sealed class ProjectSettingsActions : IProjectSettingsActions
{
    private readonly IProjectSettingsActionsHost _host;
    private readonly ProjectWorkspace _workspace;
    private readonly ProjectRelocationService _projectRelocationService;
    private readonly ProjectCleanupService _projectCleanupService;
    private readonly IPhysicalAssetAvailabilityReconciler _physicalAssetAvailability;
    private readonly RecentProjectTracker _recentProjectTracker;
    private readonly Func<ApplicationSettings> _applicationSettings;
    private readonly Func<IReadOnlyList<TrackedGenerationJob>> _getGenerationJobs;

    public ProjectSettingsActions(
        IProjectSettingsActionsHost host,
        ProjectWorkspace workspace,
        ProjectRelocationService projectRelocationService,
        ProjectCleanupService projectCleanupService,
        IPhysicalAssetAvailabilityReconciler physicalAssetAvailability,
        RecentProjectTracker recentProjectTracker,
        Func<ApplicationSettings> applicationSettings,
        Func<IReadOnlyList<TrackedGenerationJob>> getGenerationJobs)
    {
        _host = host;
        _workspace = workspace;
        _projectRelocationService = projectRelocationService;
        _projectCleanupService = projectCleanupService;
        _physicalAssetAvailability = physicalAssetAvailability;
        _recentProjectTracker = recentProjectTracker;
        _applicationSettings = applicationSettings;
        _getGenerationJobs = getGenerationJobs;
    }

    public bool HasActiveProject => _workspace.Project is not null && _workspace.Location is not null;

    public string? CurrentProjectRootDirectory => _workspace.Location?.RootDirectory;

    public async Task<ProjectSettingsActionResult> MoveProjectAsync(string destinationRootDirectory)
    {
        if (!HasActiveProject)
            return new ProjectSettingsActionResult(false, "Open a project before moving it.");
        if (GetWorkspaceBlocker(_workspace.State, "moving") is { } workspaceBlocker)
            return new ProjectSettingsActionResult(false, workspaceBlocker);

        var formerLocation = _workspace.Location!;
        if (GetGenerationJobBlocker(formerLocation.ProjectFilePath, "moving") is { } jobBlocker)
            return new ProjectSettingsActionResult(false, jobBlocker);

        _host.SetProjectActionsEnabled(false);
        try
        {
            var result = await _projectRelocationService.RelocateAsync(
                new ProjectRelocationRequest(destinationRootDirectory)).ConfigureAwait(true);

            UpdateRelocatedProjectUiPreference(result.Project.Id, result.Location.ProjectFilePath);
            try
            {
                await _recentProjectTracker.RelocateAsync(
                    _applicationSettings(),
                    formerLocation.ProjectFilePath,
                    result.Location.ProjectFilePath).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                _host.RefreshProjectUi();
                var warning = $"Project moved to {result.Location.RootDirectory}, but Recent Projects could not be updated: {exception.Message}";
                _host.SetStatus(warning);
                return new ProjectSettingsActionResult(true, warning);
            }

            _host.RefreshProjectUi();
            var message = result.SourceCleanupCompleted
                ? $"Project moved to {result.Location.RootDirectory}."
                : result.SourceCleanupWarning ??
                  $"Project moved to {result.Location.RootDirectory}, but the original folder could not be removed.";
            _host.SetStatus(message);
            return new ProjectSettingsActionResult(true, message);
        }
        finally
        {
            _host.SetProjectActionsEnabled(true);
        }
    }

    public async Task<ProjectSettingsActionResult> CleanupProjectAsync()
    {
        if (!HasActiveProject)
            return new ProjectSettingsActionResult(false, "Open a project before cleaning it up.");
        if (GetWorkspaceBlocker(_workspace.State, "cleaning up") is { } workspaceBlocker)
            return new ProjectSettingsActionResult(false, workspaceBlocker);
        if (GetGenerationJobBlocker(_workspace.Location!.ProjectFilePath, "cleaning up") is { } jobBlocker)
            return new ProjectSettingsActionResult(false, jobBlocker);

        _host.SetProjectActionsEnabled(false);
        try
        {
            var reconciliation = await _physicalAssetAvailability.ReconcileActivePhysicalAssetsAsync(
                    _workspace.Project!,
                    _workspace.Location!)
                .ConfigureAwait(true);
            if (reconciliation.Failure is not null)
                return new ProjectSettingsActionResult(
                    false,
                    $"Project media availability could not be checked safely: {reconciliation.Failure.Message}");
            if (reconciliation.IsStale)
                return new ProjectSettingsActionResult(false, "The active project changed before cleanup could begin.");

            var result = await _projectCleanupService.CleanupAsync(_workspace).ConfigureAwait(true);
            _host.ClearProjectMediaSelectionAndPreview();
            _host.ResetFrameWorkspace();
            _host.RefreshProjectCollections();
            var message = result.TotalRemovedFromProjectMedia == 0
                ? "No degraded Project Media items needed cleanup."
                : $"Cleanup Project removed {result.ArchivedSavedFrames} Saved Frame(s), " +
                  $"{result.TombstonedSavedClips} Saved Clip(s), and {result.TombstonedCompositions} Composition(s).";
            _host.SetStatus(message);
            return new ProjectSettingsActionResult(true, message);
        }
        finally
        {
            _host.SetProjectActionsEnabled(true);
        }
    }

    private string? GetGenerationJobBlocker(string projectFilePath, string operation)
    {
        var blockingJob = _getGenerationJobs().FirstOrDefault(job =>
            PathsEqual(job.ProjectFilePath, projectFilePath) &&
            (!IsTerminal(job.Status) || !job.IsReconciled));
        return blockingJob is null
            ? null
            : $"Wait for the active or unreconciled generation job for this project to finish before {operation} the project.";
    }

    private void UpdateRelocatedProjectUiPreference(Guid projectId, string projectFilePath)
    {
        var key = projectId.ToString("N");
        if (!_applicationSettings().General.ProjectStates.TryGetValue(key, out var state) ||
            state.BakedCompositionPreview is null)
            return;

        state.BakedCompositionPreview.ProjectFilePath = projectFilePath;
    }

    private static string? GetWorkspaceBlocker(ProjectWorkspaceState state, string operation) => state switch
    {
        ProjectWorkspaceState.Clean or ProjectWorkspaceState.Saved or ProjectWorkspaceState.Degraded => null,
        ProjectWorkspaceState.Dirty => $"Save or discard unsaved changes before {operation} this project.",
        ProjectWorkspaceState.Saving => $"Wait for the current project save to finish before {operation} this project.",
        ProjectWorkspaceState.RecoveryAvailable => $"Choose whether to open or discard recovery data before {operation} this project.",
        ProjectWorkspaceState.Recovered => $"Save or discard the recovered working state before {operation} this project.",
        ProjectWorkspaceState.Failed => $"Resolve the current project lifecycle failure before {operation} this project.",
        _ => $"The current project state must be resolved before {operation} it."
    };

    private static bool IsTerminal(GenerationStatus status) =>
        status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
