using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReelForge.App.Views.Dialogs;
using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Settings;

public partial class SettingsWindow : Window
{
    private readonly ApplicationSettingsEditor _editor;
    private readonly SecretConfigurationService _secrets;
    private readonly ApplicationConfigurationValidator _validator;
    private readonly IMediaToolDiscovery _mediaToolDiscovery;
    private readonly ITemporaryAssetHost _temporaryAssetHost;
    private readonly FileApplicationDiagnosticLog _diagnosticLog;
    private readonly ISecretStore _secretStore;
    private readonly SettingsFieldFactory _fieldFactory;
    private readonly Dictionary<string, string> _pendingValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _visibleEditors = new(StringComparer.Ordinal);
    private Task<bool>? _activeCommit;
    private string? _activeSection;
    private bool _rendering;
    private bool _allowClose;
    private bool _closeCommitScheduled;
    private readonly IProjectSettingsActions? _projectActions;

    public SettingsWindow(
        IApplicationSettingsStore settingsStore,
        ApplicationSettings settings,
        ISecretStore secretStore,
        IMediaToolDiscovery mediaToolDiscovery,
        ITemporaryAssetHost temporaryAssetHost,
        FileApplicationDiagnosticLog diagnosticLog,
        IProjectSettingsActions? projectActions = null)
    {
        InitializeComponent();
        _editor = new ApplicationSettingsEditor(settingsStore, settings);
        _secrets = new SecretConfigurationService(secretStore);
        _validator = new ApplicationConfigurationValidator(secretStore);
        _mediaToolDiscovery = mediaToolDiscovery;
        _temporaryAssetHost = temporaryAssetHost;
        _diagnosticLog = diagnosticLog;
        _projectActions = projectActions;
        _secretStore = secretStore;
        _fieldFactory = new SettingsFieldFactory(
            _editor,
            _secrets,
            _secretStore,
            _pendingValues,
            _visibleEditors,
            FindResource,
            () => _rendering,
            CommitVisibleAsync,
            BrowseSetting_Click,
            AutoDetectMediaTool_Click,
            UpdateSecret_Click,
            RemoveSecret_Click);
        GeneralCategory.IsSelected = true;
        ProjectCategory.IsEnabled = _projectActions?.HasActiveProject == true;
        if (!ProjectCategory.IsEnabled)
            ProjectCategory.ToolTip = "Open a project to change project settings.";
        StateChanged += SettingsWindow_StateChanged;
    }

    public ApplicationSettings Settings => _editor.Settings;
    public bool ProjectActionExecuted { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && HasUncommittedValues())
        {
            e.Cancel = true;
            if (!_closeCommitScheduled)
            {
                _closeCommitScheduled = true;
                Dispatcher.BeginInvoke(new Action(() => _ = CommitAndCloseAsync()));
            }
        }

        base.OnClosing(e);
    }

    private bool HasUncommittedValues() =>
        _editor.IsDirty ||
        _pendingValues.Count > 0 ||
        _visibleEditors.Any(pair =>
            !pair.Value.Text.Trim().Equals(
                ApplicationSettingsAccessor.Get(_editor.Settings, pair.Key),
                StringComparison.Ordinal));

    private async Task CommitAndCloseAsync()
    {
        try
        {
            if (!await CommitVisibleAsync().ConfigureAwait(true)) return;
            _allowClose = true;
            Close();
        }
        finally
        {
            _closeCommitScheduled = false;
        }
    }

    private async void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_rendering || e.NewValue is not TreeViewItem { Tag: string selected }) return;
        if (_activeSection is not null && !await CommitVisibleAsync().ConfigureAwait(true))
        {
            PersistenceStatusText.Text = "Fix the current setting or close the window after resolving the error.";
            return;
        }

        _activeSection = selected;
        await RenderSectionAsync(selected).ConfigureAwait(true);
    }

    private async Task RenderSectionAsync(string section)
    {
        _rendering = true;
        try
        {
            SettingsPanel.Children.Clear();
            _visibleEditors.Clear();
            SettingsPanel.Children.Add(new TextBlock
            {
                Text = section,
                Style = (Style)FindResource("ApplicationTextBlockStyle"),
                FontSize = 21,
                Margin = new Thickness(0, 0, 0, 5)
            });
            var statusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 0, 0, 18)
            };
            SettingsPanel.Children.Add(statusText);

            if (section == "Project")
            {
                RenderProjectSection(statusText);
                return;
            }

            foreach (var requirement in ApplicationConfigurationCatalog.Requirements.Where(item =>
                         item.Section.Equals(section, StringComparison.Ordinal)))
            {
                SettingsPanel.Children.Add(requirement.Secret
                    ? await _fieldFactory.CreateSecretEditorAsync(requirement).ConfigureAwait(true)
                    : _fieldFactory.CreateValueEditor(requirement));
            }

            if (section == ApplicationConfigurationCatalog.R2Section)
            {
                var testButton = new Button
                {
                    Content = "Test R2 Connection",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3, 14, 3, 3)
                };
                testButton.Click += TestR2Connection_Click;
                SettingsPanel.Children.Add(testButton);
                SettingsPanel.Children.Add(new TextBlock
                {
                    Text = "This explicit action performs a read-only bucket access check. It does not upload an object.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                    Margin = new Thickness(3, 3, 0, 0)
                });
            }

            var status = await _validator.ValidateSectionAsync(_editor.Settings, section).ConfigureAwait(true);
            statusText.Text = status.IsConfigured ? "✓ Required configuration exists" : $"⚠ {status.Summary}";
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderProjectSection(TextBlock statusText)
    {
        if (_projectActions is not { HasActiveProject: true, CurrentProjectRootDirectory: { } rootDirectory })
        {
            statusText.Text = "Open a project to manage its location or clean up degraded project media.";
            return;
        }

        statusText.Text = "These actions apply only to the currently open project. They are not application settings.";
        SettingsPanel.Children.Add(new TextBlock
        {
            Text = "Project location",
            Style = (Style)FindResource("ApplicationTextBlockStyle"),
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 5)
        });
        SettingsPanel.Children.Add(new TextBlock
        {
            Text = "Move this project's entire folder to an exact new folder location.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        var locationEditor = new TextBox
        {
            Text = rootDirectory,
            MinWidth = 420,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var locationPanel = new Grid { Margin = new Thickness(0, 0, 0, 24) };
        locationPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        locationPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        locationPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        locationPanel.Children.Add(locationEditor);
        var browseButton = new Button { Content = "Browse…", Margin = new Thickness(8, 3, 3, 3) };
        browseButton.Click += (_, _) => BrowseProjectLocation(locationEditor);
        Grid.SetColumn(browseButton, 1);
        locationPanel.Children.Add(browseButton);
        var moveButton = new Button { Content = "Move Project", Margin = new Thickness(3) };
        moveButton.Click += async (_, _) => await MoveProjectAsync(locationEditor, moveButton, browseButton).ConfigureAwait(true);
        Grid.SetColumn(moveButton, 2);
        locationPanel.Children.Add(moveButton);
        SettingsPanel.Children.Add(locationPanel);

        SettingsPanel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 20) });
        SettingsPanel.Children.Add(new TextBlock
        {
            Text = "Cleanup Project",
            Style = (Style)FindResource("ApplicationTextBlockStyle"),
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 5)
        });
        SettingsPanel.Children.Add(new TextBlock
        {
            Text = "This will PERMANENTLY DELETE all orphaned Frames, Audio clips, Saved clips, and Compositions tied to the project. This is irreversible, and should only be done if you know what you’re doing.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.Bold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 10)
        });
        var cleanupButton = new Button
        {
            Content = "Cleanup Project",
            Style = (Style)FindResource("DangerButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        cleanupButton.Click += async (_, _) => await CleanupProjectAsync(cleanupButton).ConfigureAwait(true);
        SettingsPanel.Children.Add(cleanupButton);
    }

    private void BrowseProjectLocation(TextBox locationEditor)
    {
        var currentName = Path.GetFileName(Path.TrimEndingDirectorySeparator(locationEditor.Text.Trim()));
        var dialog = new OpenFolderDialog
        {
            Title = "Select the parent folder for the moved project",
            Multiselect = false,
            InitialDirectory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(locationEditor.Text.Trim()))
        };
        if (dialog.ShowDialog(this) == true)
            locationEditor.Text = Path.Combine(dialog.FolderName, currentName);
    }

    private async Task MoveProjectAsync(TextBox locationEditor, Button moveButton, Button browseButton)
    {
        if (_projectActions is not { HasActiveProject: true }) return;
        var destination = locationEditor.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination))
        {
            PersistenceStatusText.Text = "Choose the exact new project folder location.";
            return;
        }

        moveButton.IsEnabled = false;
        browseButton.IsEnabled = false;
        try
        {
            PersistenceStatusText.Text = "Moving project…";
            var result = await _projectActions.MoveProjectAsync(destination).ConfigureAwait(true);
            PersistenceStatusText.Text = result.Message;
            ProjectActionExecuted |= result.Succeeded;
            if (result.Succeeded && _projectActions.CurrentProjectRootDirectory is { } rootDirectory)
                locationEditor.Text = rootDirectory;
        }
        catch (Exception exception)
        {
            PersistenceStatusText.Text = $"Project move failed: {exception.Message}";
            MessageBox.Show(this, exception.Message, "Project move failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            moveButton.IsEnabled = true;
            browseButton.IsEnabled = true;
        }
    }

    private async Task CleanupProjectAsync(Button cleanupButton)
    {
        if (_projectActions is not { HasActiveProject: true }) return;
        var confirmation = MessageBox.Show(
            this,
            "Cleanup permanently deletes degraded project media. Missing source files could otherwise be relinked, restoring the media that depends on them.\n\nDo you want to permanently delete the orphaned project media now?",
            "Cleanup Project",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        cleanupButton.IsEnabled = false;
        try
        {
            PersistenceStatusText.Text = "Cleaning up degraded project media…";
            var result = await _projectActions.CleanupProjectAsync().ConfigureAwait(true);
            PersistenceStatusText.Text = result.Message;
            ProjectActionExecuted |= result.Succeeded;
        }
        catch (Exception exception)
        {
            PersistenceStatusText.Text = $"Project cleanup failed: {exception.Message}";
            MessageBox.Show(this, exception.Message, "Project cleanup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            cleanupButton.IsEnabled = true;
        }
    }

    private Task<bool> CommitVisibleAsync()
    {
        if (_activeCommit is { IsCompleted: false }) return _activeCommit;
        _activeCommit = CommitVisibleCoreAsync();
        return _activeCommit;
    }

    private async Task<bool> CommitVisibleCoreAsync()
    {
        try
        {
            foreach (var (key, textBox) in _visibleEditors)
                _pendingValues[key] = textBox.Text;

            var savedAny = false;
            while (_pendingValues.Count > 0)
            {
                var valuesBeingCommitted = _pendingValues.ToArray();
                var logDirectoryValue = valuesBeingCommitted
                    .FirstOrDefault(pair => pair.Key == "General.LogDirectory").Value;
                string? resolvedLogDirectory = null;
                var moveExistingLogs = false;
                if (logDirectoryValue is not null)
                {
                    resolvedLogDirectory = FileApplicationDiagnosticLog.ResolveLogDirectory(logDirectoryValue);
                    if (!PathsEqual(_diagnosticLog.LogDirectory, resolvedLogDirectory))
                    {
                        var existingLogs = FileApplicationDiagnosticLog.FindExistingLogs(_diagnosticLog.LogDirectory);
                        if (existingLogs.Count > 0)
                        {
                            moveExistingLogs = MessageBox.Show(
                                this,
                                $"There are existing logs in the previous location \"{_diagnosticLog.LogDirectory}\". " +
                                $"Would you like to move them to the new location \"{resolvedLogDirectory}\"?\n\n" +
                                "This helps prevent accidental, orphaned artifacts.",
                                "Move existing logs?",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question,
                                MessageBoxResult.Yes) == MessageBoxResult.Yes;
                        }
                    }
                }
                foreach (var (key, value) in valuesBeingCommitted)
                    _editor.Update(key, value);

                savedAny |= await _editor.CommitAsync().ConfigureAwait(true);
                if (resolvedLogDirectory is not null && !PathsEqual(_diagnosticLog.LogDirectory, resolvedLogDirectory))
                {
                    try
                    {
                        await _diagnosticLog.RelocateAsync(resolvedLogDirectory, moveExistingLogs).ConfigureAwait(true);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        PersistenceStatusText.Text =
                            $"The log setting was saved, but existing logs could not be relocated: {exception.Message}";
                    }
                }
                foreach (var (key, value) in valuesBeingCommitted)
                {
                    if (_pendingValues.TryGetValue(key, out var current) && current.Equals(value, StringComparison.Ordinal))
                        _pendingValues.Remove(key);
                }
            }

            if (savedAny)
            {
                if (!PersistenceStatusText.Text.StartsWith("The log setting was saved", StringComparison.Ordinal))
                    PersistenceStatusText.Text = "Changes saved to the local application settings file.";
            }
            return true;
        }
        catch (Exception exception)
        {
            PersistenceStatusText.Text = $"Settings were not saved: {exception.Message}";
            return false;
        }
    }

    private async void UpdateSecret_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConfigurationRequirement requirement) return;
        var dialog = new SecretEntryDialog(requirement) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var value = dialog.TakeSecret();
        try
        {
            await _secrets.ReplaceAsync(requirement, value).ConfigureAwait(true);
            value = string.Empty;
            PersistenceStatusText.Text = $"{requirement.DisplayName} stored in {_secretStore.DisplayName}.";
            await RenderSectionAsync(_activeSection!).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            value = string.Empty;
            MessageBox.Show(this, exception.Message, "Credential storage failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RemoveSecret_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConfigurationRequirement requirement) return;
        if (MessageBox.Show(
                this,
                $"Remove {requirement.DisplayName} from {_secretStore.DisplayName}?",
                "Remove credential",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _secrets.RemoveAsync(requirement).ConfigureAwait(true);
            PersistenceStatusText.Text = $"{requirement.DisplayName} removed.";
            await RenderSectionAsync(_activeSection!).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Credential removal failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseSetting_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConfigurationRequirement requirement ||
            !_visibleEditors.TryGetValue(requirement.Key, out var textBox)) return;
        if (requirement.Key is "General.ProjectsRoot" or "General.LogDirectory")
        {
            var dialog = new OpenFolderDialog
            {
                Title = requirement.Key == "General.ProjectsRoot"
                    ? "Select the default ReelForge projects folder"
                    : "Select the ReelForge diagnostic log folder",
                Multiselect = false
            };
            if (dialog.ShowDialog(this) == true) textBox.Text = dialog.FolderName;
            return;
        }

        var fileName = requirement.Key.EndsWith("FfmpegPath", StringComparison.Ordinal) ? "ffmpeg.exe" : "ffprobe.exe";
        var fileDialog = new OpenFileDialog
        {
            Title = $"Select {fileName}",
            Filter = $"{fileName}|{fileName}|Executables (*.exe)|*.exe|All files|*.*",
            CheckFileExists = true
        };
        if (fileDialog.ShowDialog(this) == true) textBox.Text = fileDialog.FileName;
    }

    private async void AutoDetectMediaTool_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConfigurationRequirement requirement ||
            !_visibleEditors.TryGetValue(requirement.Key, out var editor)) return;

        var detected = _mediaToolDiscovery.Discover();
        var isFfmpeg = requirement.Key == "MediaTools.FfmpegPath";
        var executableName = isFfmpeg ? "FFmpeg" : "ffprobe";
        var detectedPath = isFfmpeg ? detected.FfmpegPath : detected.FfprobePath;
        if (detectedPath is not null) editor.Text = detectedPath;
        PersistenceStatusText.Text = detectedPath is null
            ? $"{executableName} was not found on PATH. The current setting was left unchanged."
            : $"{executableName} was auto-detected at {detectedPath}.";
        await CommitVisibleAsync().ConfigureAwait(true);
    }

    private async void TestR2Connection_Click(object sender, RoutedEventArgs e)
    {
        if (!await CommitVisibleAsync().ConfigureAwait(true)) return;
        PersistenceStatusText.Text = "Testing read-only R2 bucket access…";
        var result = await _temporaryAssetHost.TestConnectionAsync().ConfigureAwait(true);
        PersistenceStatusText.Text = result.Message;
        MessageBox.Show(
            this,
            result.Message,
            result.Succeeded ? "R2 connection succeeded" : "R2 connection failed",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void SettingsWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) await CommitVisibleAsync().ConfigureAwait(true);
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);
}
