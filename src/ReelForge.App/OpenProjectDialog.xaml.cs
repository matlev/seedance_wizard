using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReelForge.Infrastructure;

namespace ReelForge.App;

public partial class OpenProjectDialog : Window
{
    private readonly ObservableCollection<string> _projectFiles = [];
    private readonly string _initialDirectory;

    public OpenProjectDialog(string initialDirectory)
    {
        InitializeComponent();
        _initialDirectory = Directory.Exists(initialDirectory)
            ? Path.GetFullPath(initialDirectory)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ProjectFilesList.ItemsSource = _projectFiles;
    }

    public string ProjectFilePath { get; private set; } = string.Empty;

    private void ChooseProjectFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a ReelForge project file",
            InitialDirectory = _initialDirectory,
            Filter = "ReelForge project (*.rfp)|*.rfp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        ShowProjects([dialog.FileName]);
    }

    private void ChooseProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing ReelForge projects",
            InitialDirectory = _initialDirectory,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var projects = ProjectFileLocator.FindInFolderAndChildren(dialog.FolderName);
            ShowProjects(projects);
            if (projects.Count == 0)
            {
                ValidationErrorText.Text =
                    "No .rfp project was found in that folder or its immediate subfolders.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ValidationErrorText.Text = $"That folder could not be searched: {exception.Message}";
        }
    }

    private void ShowProjects(IEnumerable<string> projectFiles)
    {
        _projectFiles.Clear();
        foreach (var projectFile in projectFiles) _projectFiles.Add(Path.GetFullPath(projectFile));
        ProjectFilesList.SelectedIndex = _projectFiles.Count == 1 ? 0 : -1;
        ValidationErrorText.Text = _projectFiles.Count > 1
            ? "More than one project was found. Select the project you want to open."
            : string.Empty;
    }

    private void ProjectFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectFilesList.SelectedItem is not null) ValidationErrorText.Text = string.Empty;
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectFilesList.SelectedItem is not string selectedProject)
        {
            ValidationErrorText.Text = "Choose or select a project first.";
            return;
        }
        if (!File.Exists(selectedProject) || !ProjectFileLocator.IsSupportedProjectFile(selectedProject))
        {
            ValidationErrorText.Text = "The selected item is not an available ReelForge project file.";
            return;
        }

        ProjectFilePath = Path.GetFullPath(selectedProject);
        DialogResult = true;
    }
}
