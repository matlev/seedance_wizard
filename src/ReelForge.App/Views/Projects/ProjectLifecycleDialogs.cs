using System.IO;
using System.Windows;
using Microsoft.Win32;
using ReelForge.App.Views.Dialogs;
using ReelForge.Application;

namespace ReelForge.App.Views.Projects;

internal sealed record NewProjectSelection(string ProjectDirectory, string ProjectName);

internal enum ProjectRecoveryDecision
{
    OpenRecoveredWorkingState,
    DiscardRecovery,
    Defer
}

/// <summary>
/// Narrows project-creation and project-opening dialog policy for lifecycle coordination.
/// Import dialogs deliberately remain outside this contract because they belong to the
/// separate media-import workflow.
/// </summary>
internal interface IProjectLifecycleDialogs
{
    NewProjectSelection? SelectNewProject(ApplicationSettings settings);
    string? SelectProjectToOpen(ApplicationSettings settings);
    ProjectRecoveryDecision DecideRecovery(ProjectRecoveryCandidate candidate);
}

/// <summary>
/// Owns Windows dialog policy for choosing project and import locations. Workspace
/// mutation stays with the caller so this service cannot create, open, or alter a project.
/// </summary>
internal sealed class ProjectLifecycleDialogs(Window owner, ApplicationPaths paths) : IProjectLifecycleDialogs
{
    private const string SupportedMediaFilter =
        "Supported media|*.bmp;*.gif;*.heic;*.heif;*.jpeg;*.jpg;*.png;*.tif;*.tiff;*.webp;" +
        "*.avi;*.m4v;*.mkv;*.mov;*.mp4;*.webm;*.wmv;*.aac;*.flac;*.m4a;*.mp3;*.ogg;*.wav;*.wma|" +
        "All files|*.*";

    public NewProjectSelection? SelectNewProject(ApplicationSettings settings)
    {
        var projectsLocation = GetDefaultProjectsDirectory(settings);
        if (!Directory.Exists(projectsLocation))
        {
            var choice = MessageBox.Show(
                owner,
                $"ReelForge's recommended projects folder does not exist yet:\n\n{projectsLocation}" +
                "\n\nCreate it now?",
                "Create projects folder",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (choice == MessageBoxResult.Cancel) return null;
            if (choice == MessageBoxResult.Yes)
            {
                Directory.CreateDirectory(projectsLocation);
            }
            else
            {
                var locationDialog = new OpenFolderDialog
                {
                    Title = "Choose the folder that will contain ReelForge projects",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Multiselect = false
                };
                if (locationDialog.ShowDialog(owner) != true) return null;
                projectsLocation = Path.GetFullPath(locationDialog.FolderName);
            }
        }

        var dialog = new NewProjectDialog(projectsLocation) { Owner = owner };
        return dialog.ShowDialog() == true
            ? new NewProjectSelection(dialog.ProjectDirectory, dialog.ProjectName)
            : null;
    }

    public string? SelectProjectToOpen(ApplicationSettings settings)
    {
        var dialog = new OpenProjectDialog(
            GetDefaultProjectsDirectory(settings),
            RecentProjectTracker.GetExistingRecentProjectFiles(settings))
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true ? dialog.ProjectFilePath : null;
    }

    public ProjectRecoveryDecision DecideRecovery(ProjectRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var choice = MessageBox.Show(
            owner,
            "ReelForge found recovery data from an earlier interrupted session.\n\n" +
            "Yes opens the recovered working state. It does not replace the saved project until you explicitly Save.\n\n" +
            "No permanently discards the recovery data and keeps the saved project open.\n\n" +
            "Cancel keeps the saved project open and leaves recovery data available for a later decision.",
            "Project recovery available",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        return choice switch
        {
            MessageBoxResult.Yes => ProjectRecoveryDecision.OpenRecoveredWorkingState,
            MessageBoxResult.No => ProjectRecoveryDecision.DiscardRecovery,
            _ => ProjectRecoveryDecision.Defer
        };
    }

    public IReadOnlyList<string> SelectMediaToImport()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import image, video, or audio assets",
            Filter = SupportedMediaFilter,
            CheckFileExists = true,
            Multiselect = true
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileNames : [];
    }

    private string GetDefaultProjectsDirectory(ApplicationSettings settings)
    {
        var configured = settings.General.ProjectsRoot;
        return ApplicationPathResolver.ResolveDirectory(
            string.IsNullOrWhiteSpace(configured) ? paths.DefaultProjectsDirectory : configured);
    }
}
