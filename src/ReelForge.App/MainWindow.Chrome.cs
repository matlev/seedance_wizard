using System.Windows;
using System.Windows.Automation;

namespace ReelForge.App;

/// <summary>Owns only the WPF title-bar and application-menu interaction plumbing.</summary>
public partial class MainWindow
{
    private async void JobsChromeControl_OpenRequested(object? sender, EventArgs e) =>
        await ToggleJobsAsync();

    private async Task ToggleJobsAsync()
    {
        if (JobsPanelControl.IsOpen)
        {
            await JobsPanelControl.HideJobsAsync();
            JobsChromeControl.SetJobsOpen(false);
            JobsMenuItem.IsChecked = false;
            return;
        }

        JobsPanelControl.ShowJobs();
        JobsChromeControl.SetJobsOpen(true);
        JobsMenuItem.IsChecked = true;
    }

    private void JobsPanelControl_Closed(object? sender, EventArgs e)
    {
        JobsChromeControl.SetJobsOpen(false);
        JobsMenuItem.IsChecked = false;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void Exit_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeRestoreButton is null) return;
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.Content = isMaximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore down" : "Maximize";
        AutomationProperties.SetName(MaximizeRestoreButton, isMaximized ? "Restore down" : "Maximize");
    }

    private void GenerateWorkspaceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        GenerateWorkspaceButton.IsChecked = true;
        GenerateWorkspaceMenuItem.IsChecked = true;
        EditWorkspaceMenuItem.IsChecked = false;
    }

    private void EditWorkspaceMenuItem_Click(object sender, RoutedEventArgs e)
    {
        EditWorkspaceButton.IsChecked = true;
        GenerateWorkspaceMenuItem.IsChecked = false;
        EditWorkspaceMenuItem.IsChecked = true;
    }

    private async void JobsMenuItem_Click(object sender, RoutedEventArgs e) =>
        await ToggleJobsAsync();
}
