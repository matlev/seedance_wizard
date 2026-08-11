using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ReelForge.App;

public partial class NewProjectDialog : Window
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public NewProjectDialog(string projectsLocation)
    {
        InitializeComponent();
        ProjectsLocationTextBox.Text = Path.GetFullPath(projectsLocation);
        Loaded += (_, _) => ProjectNameTextBox.Focus();
        UpdateProjectFolderPreview();
    }

    public string ProjectName { get; private set; } = string.Empty;
    public string ProjectDirectory { get; private set; } = string.Empty;

    private void BrowseProjectsLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder that will contain ReelForge projects",
            InitialDirectory = ProjectsLocationTextBox.Text,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        ProjectsLocationTextBox.Text = Path.GetFullPath(dialog.FolderName);
        UpdateProjectFolderPreview();
    }

    private void ProjectSettingsChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateProjectFolderPreview();

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        var name = ProjectNameTextBox.Text.Trim();
        var validationError = ValidateProjectName(name);
        if (validationError is not null)
        {
            ValidationErrorText.Text = validationError;
            ProjectNameTextBox.Focus();
            return;
        }

        var parentDirectory = Path.GetFullPath(ProjectsLocationTextBox.Text);
        if (!Directory.Exists(parentDirectory))
        {
            ValidationErrorText.Text = "The projects location no longer exists. Choose another location.";
            return;
        }

        var targetDirectory = Path.GetFullPath(Path.Combine(parentDirectory, name));
        if (File.Exists(targetDirectory) ||
            (Directory.Exists(targetDirectory) && Directory.EnumerateFileSystemEntries(targetDirectory).Any()))
        {
            ValidationErrorText.Text =
                $"A non-empty item named '{name}' already exists in this projects location. Choose another project name.";
            return;
        }

        ProjectName = name;
        ProjectDirectory = targetDirectory;
        DialogResult = true;
    }

    private void UpdateProjectFolderPreview()
    {
        if (ProjectFolderPreviewText is null || ProjectsLocationTextBox is null ||
            ProjectNameTextBox is null || ValidationErrorText is null) return;
        var name = ProjectNameTextBox.Text.Trim();
        ProjectFolderPreviewText.Text = string.IsNullOrWhiteSpace(name)
            ? Path.Combine(ProjectsLocationTextBox.Text, "Your project name")
            : Path.Combine(ProjectsLocationTextBox.Text, name);
        ValidationErrorText.Text = string.Empty;
    }

    private static string? ValidateProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a project name.";
        if (name is "." or "..") return "Choose a project name other than '.' or '..'.";
        if (name.EndsWith(' ') || name.EndsWith('.'))
            return "A project name cannot end with a space or period on Windows.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "The project name contains a character that Windows cannot use in a folder name.";
        if (ReservedWindowsNames.Contains(name.Split('.')[0]))
            return $"'{name}' is reserved by Windows. Choose another project name.";
        return null;
    }
}
