using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReelForge.Infrastructure;

namespace ReelForge.App;

public partial class OpenProjectDialog : Window
{
    private readonly ObservableCollection<RecentProjectListItem> _recentProjects = [];
    private readonly string _initialDirectory;

    public OpenProjectDialog(string initialDirectory, IEnumerable<string>? recentProjectFiles = null)
    {
        InitializeComponent();
        _initialDirectory = Directory.Exists(initialDirectory)
            ? Path.GetFullPath(initialDirectory)
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        RecentProjectsList.ItemsSource = _recentProjects;
        foreach (var path in recentProjectFiles ?? [])
        {
            if (!File.Exists(path) || !ProjectFileLocator.IsSupportedProjectFile(path)) continue;
            _recentProjects.Add(new RecentProjectListItem(Path.GetFullPath(path)));
        }
        RecentProjectsEmptyText.Visibility = _recentProjects.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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
        TryOpenProject(dialog.FileName);
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
            if (projects.Count == 1)
            {
                TryOpenProject(projects[0]);
            }
            else if (projects.Count == 0)
            {
                ValidationErrorText.Text =
                    "No .rfp project was found in that folder or its immediate subfolders.";
            }
            else
            {
                ValidationErrorText.Text =
                    "More than one project was found. Choose the specific project folder or its .rfp file.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ValidationErrorText.Text = $"That folder could not be searched: {exception.Message}";
        }
    }

    private void RecentProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenProjectButton.IsEnabled = RecentProjectsList.SelectedItem is RecentProjectListItem;
        if (OpenProjectButton.IsEnabled) ValidationErrorText.Text = string.Empty;
    }

    private void RecentProjectsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(
                RecentProjectsList,
                e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: RecentProjectListItem selected })
            TryOpenProject(selected.ProjectFilePath);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (RecentProjectsList.SelectedItem is not RecentProjectListItem selectedProject)
        {
            ValidationErrorText.Text = "Select a recent project or choose one using the buttons above.";
            return;
        }

        TryOpenProject(selectedProject.ProjectFilePath);
    }

    private bool TryOpenProject(string selectedProject)
    {
        if (!File.Exists(selectedProject) || !ProjectFileLocator.IsSupportedProjectFile(selectedProject))
        {
            ValidationErrorText.Text = "The selected item is not an available ReelForge project file.";
            return false;
        }

        ProjectFilePath = Path.GetFullPath(selectedProject);
        DialogResult = true;
        return true;
    }
}

public sealed record RecentProjectListItem(string ProjectFilePath)
{
    public string DisplayName => Path.GetFileNameWithoutExtension(ProjectFilePath);
}
