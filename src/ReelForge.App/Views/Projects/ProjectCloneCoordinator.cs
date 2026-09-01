using System.IO;
using System.Windows;
using ReelForge.App.Views.Dialogs;
using ReelForge.Application;

namespace ReelForge.App.Views.Projects;

/// <summary>
/// Keeps clone selection, progress presentation, and shell-local policy out of the
/// project lifecycle/opening workflow. The clone itself remains an Application use case.
/// </summary>
internal sealed class ProjectCloneCoordinator
{
    private readonly Window _owner;
    private readonly ProjectWorkspace _workspace;
    private readonly ProjectCloneService _cloneService;
    private readonly RecentProjectTracker _recentProjectTracker;
    private readonly Func<ApplicationSettings> _settings;
    private readonly ApplicationPaths _paths;
    private readonly Action<string> _setStatus;
    private CloneProjectProgressWindow? _activeProgressWindow;

    public ProjectCloneCoordinator(
        Window owner,
        ProjectWorkspace workspace,
        ProjectCloneService cloneService,
        RecentProjectTracker recentProjectTracker,
        Func<ApplicationSettings> settings,
        ApplicationPaths paths,
        Action<string> setStatus)
    {
        _owner = owner;
        _workspace = workspace;
        _cloneService = cloneService;
        _recentProjectTracker = recentProjectTracker;
        _settings = settings;
        _paths = paths;
        _setStatus = setStatus;
    }

    public async Task CloneFromDialogAsync()
    {
        var settings = _settings();
        var currentProject = _workspace.Location?.ProjectFilePath;
        var initialDirectory = ResolveInitialDirectory(settings, currentProject);
        var dialog = new CloneProjectDialog(
            currentProject ?? string.Empty,
            RecentProjectTracker.GetExistingRecentProjectFiles(settings),
            initialDirectory)
        {
            Owner = _owner
        };
        if (dialog.ShowDialog() != true || dialog.Selection is not { } selection) return;

        if (IsUnsafeCurrentProjectClone(selection.SourceProjectFilePath, out var message))
        {
            MessageBox.Show(_owner, message, "Save or resolve project state", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var progress = new CloneProjectProgressWindow((reporter, token) =>
            _cloneService.CloneAsync(
                new ProjectCloneRequest(selection.SourceProjectFilePath, selection.DestinationParentDirectory, selection.CloneName),
                reporter,
                token))
        {
            Owner = _owner
        };
        _activeProgressWindow = progress;
        try
        {
            var completed = progress.ShowDialog();
            if (ReferenceEquals(_activeProgressWindow, progress)) _activeProgressWindow = null;
            if (completed == true && progress.Result is { } result)
            {
                try
                {
                    await _recentProjectTracker.AddRecentAsync(settings, result.Location.ProjectFilePath);
                }
                catch (Exception exception)
                {
                    _setStatus($"Clone created at {result.Location.RootDirectory}. ReelForge could not add it to Recent Projects: {exception.Message}");
                    return;
                }

                _setStatus($"Clone created at {result.Location.RootDirectory}. The current project remains open.");
                return;
            }

            if (progress.WasCanceled)
            {
                _setStatus("Project clone cancelled. No clone was published.");
                return;
            }

            if (progress.Failure is { } failure)
            {
                _setStatus($"Project clone failed: {failure.Message}");
                MessageBox.Show(_owner, failure.Message, "Project clone failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            if (ReferenceEquals(_activeProgressWindow, progress)) _activeProgressWindow = null;
        }
    }

    public void CancelActiveClone() => _activeProgressWindow?.RequestCancellation();

    private bool IsUnsafeCurrentProjectClone(string sourceProjectFilePath, out string message)
    {
        message = string.Empty;
        if (_workspace.Location is null || !PathsEqual(_workspace.Location.ProjectFilePath, sourceProjectFilePath)) return false;
        switch (_workspace.State)
        {
            case ProjectWorkspaceState.Clean:
            case ProjectWorkspaceState.Saved:
            case ProjectWorkspaceState.Degraded:
                return false;
            case ProjectWorkspaceState.Dirty:
                message = "Save or discard the current unsaved changes before cloning it. Clone Project always copies the last committed project state.";
                return true;
            case ProjectWorkspaceState.Saving:
                message = "Wait for the current project save to finish before cloning it.";
                return true;
            case ProjectWorkspaceState.RecoveryAvailable:
                message = "Choose whether to open or discard the available recovery data before cloning the current project.";
                return true;
            case ProjectWorkspaceState.Recovered:
                message = "Save or discard the recovered working state before cloning the current project.";
                return true;
            case ProjectWorkspaceState.Failed:
                message = "Resolve the current project's recovery or lifecycle failure before cloning it.";
                return true;
            default:
                message = "The current project's state must be resolved before it can be cloned.";
                return true;
        }
    }

    private string ResolveInitialDirectory(ApplicationSettings settings, string? currentProject)
    {
        if (!string.IsNullOrWhiteSpace(settings.General.ProjectsRoot) && Directory.Exists(settings.General.ProjectsRoot))
            return Path.GetFullPath(settings.General.ProjectsRoot);
        if (!string.IsNullOrWhiteSpace(currentProject))
            return Path.GetDirectoryName(Path.GetFullPath(currentProject)) ?? _paths.DefaultProjectsDirectory;
        return _paths.DefaultProjectsDirectory;
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
