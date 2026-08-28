using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReelForge.Application;

namespace ReelForge.App.Views.Dialogs;

public sealed record CloneProjectSelection(string SourceProjectFilePath, string DestinationParentDirectory, string CloneName);

public partial class CloneProjectDialog : Window
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
    private readonly ObservableCollection<RecentProjectListItem> _projects = [];
    private readonly string _initialDirectory;

    public CloneProjectDialog(string currentProjectFilePath, IEnumerable<string> recentProjectFiles, string initialDirectory)
    {
        InitializeComponent();
        _initialDirectory = Directory.Exists(initialDirectory) ? Path.GetFullPath(initialDirectory) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ProjectsList.ItemsSource = _projects;
        AddProject(currentProjectFilePath);
        foreach (var path in recentProjectFiles) AddProject(path);
        ProjectsList.SelectedIndex = _projects.Count > 0 ? 0 : -1;
        CloneLocationTextBox.Text = _initialDirectory;
        CloneNameTextBox.Text = _projects.Count > 0 ? $"{_projects[0].DisplayName} copy" : string.Empty;
        Loaded += (_, _) => CloneNameTextBox.Focus();
        UpdatePreview();
    }

    public CloneProjectSelection? Selection { get; private set; }

    private void ChooseProjectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a ReelForge project to clone", InitialDirectory = _initialDirectory,
            Filter = "ReelForge project (*.rfp)|*.rfp", CheckFileExists = true, Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!IsSupportedProjectFile(dialog.FileName))
        {
            SetValidationError("Choose an available ReelForge .rfp project file.");
            return;
        }
        var fullPath = Path.GetFullPath(dialog.FileName);
        AddProject(fullPath);
        ProjectsList.SelectedItem = _projects.First(item => item.ProjectFilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
    }

    private void BrowseCloneLocation_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to create the clone", InitialDirectory = CloneLocationTextBox.Text, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        CloneLocationTextBox.Text = Path.GetFullPath(dialog.FolderName);
        UpdatePreview();
    }

    private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetValidationError(null);
    }

    private void CloneSettingsChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void ConfirmClone_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not RecentProjectListItem source)
        {
            SetValidationError("Choose the project to clone.");
            return;
        }
        var name = CloneNameTextBox.Text.Trim();
        var nameError = ValidateName(name);
        if (nameError is not null) { SetValidationError(nameError); CloneNameTextBox.Focus(); return; }
        if (!Directory.Exists(CloneLocationTextBox.Text)) { SetValidationError("The clone location no longer exists. Choose another location."); return; }
        var destination = Path.GetFullPath(Path.Combine(CloneLocationTextBox.Text, name));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            SetValidationError($"An item named '{name}' already exists at this location. Choose another clone name.");
            return;
        }
        Selection = new CloneProjectSelection(source.ProjectFilePath, Path.GetFullPath(CloneLocationTextBox.Text), name);
        DialogResult = true;
    }

    private void AddProject(string path)
    {
        if (!File.Exists(path) || !IsSupportedProjectFile(path)) return;
        var fullPath = Path.GetFullPath(path);
        if (_projects.Any(item => item.ProjectFilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))) return;
        _projects.Add(new RecentProjectListItem(fullPath));
    }

    private void UpdatePreview()
    {
        if (CloneFolderPreviewText is null) return;
        var name = CloneNameTextBox?.Text.Trim();
        CloneFolderPreviewText.Text = string.IsNullOrWhiteSpace(name)
            ? Path.Combine(CloneLocationTextBox.Text, "Your clone name")
            : Path.Combine(CloneLocationTextBox.Text, name);
        SetValidationError(null);
    }

    private void SetValidationError(string? message)
    {
        if (ValidationErrorText is null) return;
        ValidationErrorText.Text = message ?? string.Empty;
        ValidationErrorText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name for the clone.";
        if (name is "." or "..") return "Choose a clone name other than '.' or '..'.";
        if (name.EndsWith(' ') || name.EndsWith('.')) return "A clone name cannot end with a space or period on Windows.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The clone name contains a character Windows cannot use in a folder name.";
        if (ReservedWindowsNames.Contains(name.Split('.')[0])) return $"'{name}' is reserved by Windows. Choose another clone name.";
        return null;
    }

    private static bool IsSupportedProjectFile(string path) =>
        Path.GetExtension(path).Equals(".rfp", StringComparison.OrdinalIgnoreCase) &&
        !ProjectCloneArtifactPolicy.IsStagingProjectFile(path);
}
