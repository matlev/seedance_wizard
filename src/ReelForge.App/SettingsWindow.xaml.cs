using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReelForge.Application;

namespace ReelForge.App;

public partial class SettingsWindow : Window
{
    private readonly ApplicationSettingsEditor _editor;
    private readonly SecretConfigurationService _secrets;
    private readonly ApplicationConfigurationValidator _validator;
    private readonly IMediaToolDiscovery _mediaToolDiscovery;
    private readonly ITemporaryAssetHost _temporaryAssetHost;
    private readonly Dictionary<string, string> _pendingValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> _visibleEditors = new(StringComparer.Ordinal);
    private Task<bool>? _activeCommit;
    private string? _activeSection;
    private bool _rendering;
    private bool _allowClose;
    private bool _closeCommitScheduled;

    public SettingsWindow(
        IApplicationSettingsStore settingsStore,
        ApplicationSettings settings,
        ISecretStore secretStore,
        IMediaToolDiscovery mediaToolDiscovery,
        ITemporaryAssetHost temporaryAssetHost)
    {
        InitializeComponent();
        _editor = new ApplicationSettingsEditor(settingsStore, settings);
        _secrets = new SecretConfigurationService(secretStore);
        _validator = new ApplicationConfigurationValidator(secretStore);
        _mediaToolDiscovery = mediaToolDiscovery;
        _temporaryAssetHost = temporaryAssetHost;
        GeneralCategory.IsSelected = true;
        StateChanged += SettingsWindow_StateChanged;
    }

    public ApplicationSettings Settings => _editor.Settings;

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

            foreach (var requirement in ApplicationConfigurationCatalog.Requirements.Where(item =>
                         item.Section.Equals(section, StringComparison.Ordinal)))
            {
                SettingsPanel.Children.Add(requirement.Secret
                    ? await CreateSecretEditorAsync(requirement).ConfigureAwait(true)
                    : CreateValueEditor(requirement));
            }

            if (section == ApplicationConfigurationCatalog.MediaToolsSection)
            {
                var detectButton = new Button { Content = "Auto-detect from PATH", HorizontalAlignment = HorizontalAlignment.Left };
                detectButton.Click += AutoDetectMediaTools_Click;
                SettingsPanel.Children.Add(detectButton);
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

    private FrameworkElement CreateValueEditor(ConfigurationRequirement requirement)
    {
        if (requirement.Key.EndsWith(".Enabled", StringComparison.Ordinal))
            return CreateBooleanEditor(requirement);

        var panel = CreateFieldPanel(requirement);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var supportsBrowse = requirement.Key is "MediaTools.FfmpegPath" or "MediaTools.FfprobePath" or "General.ProjectsRoot";
        if (supportsBrowse) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var textBox = new TextBox
        {
            Text = _pendingValues.TryGetValue(requirement.Key, out var pending)
                ? pending
                : ApplicationSettingsAccessor.Get(_editor.Settings, requirement.Key),
            Tag = requirement,
            ToolTip = requirement.Placeholder,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        textBox.TextChanged += (_, _) => _pendingValues[requirement.Key] = textBox.Text;
        textBox.LostKeyboardFocus += ValueEditor_LostKeyboardFocus;
        row.Children.Add(textBox);
        _visibleEditors[requirement.Key] = textBox;

        if (supportsBrowse)
        {
            var browse = new Button { Content = "Browse…", Tag = requirement, Margin = new Thickness(7, 0, 0, 0) };
            browse.Click += BrowseSetting_Click;
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);
        }

        panel.Children.Add(row);
        return panel;
    }

    private FrameworkElement CreateBooleanEditor(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        var currentValue = _pendingValues.TryGetValue(requirement.Key, out var pending)
            ? pending
            : ApplicationSettingsAccessor.Get(_editor.Settings, requirement.Key);
        var isEnabled = bool.TryParse(currentValue, out var parsed) && parsed;
        var group = new StackPanel { Orientation = Orientation.Horizontal };
        var enabled = new RadioButton
        {
            Content = "Enabled",
            IsChecked = isEnabled,
            GroupName = requirement.Key,
            Style = (Style)FindResource("SettingsBooleanChoiceStyle"),
            Tag = new BooleanChoice(requirement, true)
        };
        var disabled = new RadioButton
        {
            Content = "Disabled",
            IsChecked = !isEnabled,
            GroupName = requirement.Key,
            Style = (Style)FindResource("SettingsBooleanChoiceStyle"),
            Tag = new BooleanChoice(requirement, false)
        };
        enabled.Checked += BooleanEditor_Checked;
        disabled.Checked += BooleanEditor_Checked;
        group.Children.Add(enabled);
        group.Children.Add(disabled);
        panel.Children.Add(group);
        return panel;
    }

    private async void BooleanEditor_Checked(object sender, RoutedEventArgs e)
    {
        if (_rendering || (sender as FrameworkElement)?.Tag is not BooleanChoice choice) return;
        _pendingValues[choice.Requirement.Key] = choice.Enabled.ToString().ToLowerInvariant();
        await CommitVisibleAsync().ConfigureAwait(true);
    }

    private async Task<FrameworkElement> CreateSecretEditorAsync(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        var configured = await _secrets.IsConfiguredAsync(requirement).ConfigureAwait(true);
        var row = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var replace = new Button { Content = configured ? "Replace" : "Configure", Tag = requirement };
        replace.Click += ReplaceSecret_Click;
        buttons.Children.Add(replace);
        var remove = new Button { Content = "Remove", Tag = requirement, IsEnabled = configured };
        remove.Click += RemoveSecret_Click;
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(new TextBox
        {
            Text = configured ? "*****" : string.Empty,
            IsReadOnly = true,
            ToolTip = configured ? "A credential is configured; its value is not loaded." : requirement.Placeholder,
            VerticalContentAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(row);
        return panel;
    }

    private StackPanel CreateFieldPanel(ConfigurationRequirement requirement)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 17) };
        panel.Children.Add(new TextBlock
        {
            Text = requirement.Required ? $"{requirement.DisplayName} (required)" : requirement.DisplayName,
            Style = (Style)FindResource("ApplicationTextBlockStyle"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        panel.Children.Add(new TextBlock
        {
            Text = requirement.Secret
                ? $"{requirement.Description} Windows Credential Manager key: ReelForge:{requirement.CredentialManagerKey}"
                : requirement.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 5)
        });
        return panel;
    }

    private async void ValueEditor_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) =>
        await CommitVisibleAsync().ConfigureAwait(true);

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
                foreach (var (key, value) in valuesBeingCommitted)
                    _editor.Update(key, value);

                savedAny |= await _editor.CommitAsync().ConfigureAwait(true);
                foreach (var (key, value) in valuesBeingCommitted)
                {
                    if (_pendingValues.TryGetValue(key, out var current) && current.Equals(value, StringComparison.Ordinal))
                        _pendingValues.Remove(key);
                }
            }

            if (savedAny)
            {
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

    private async void ReplaceSecret_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ConfigurationRequirement requirement) return;
        var dialog = new SecretEntryDialog(requirement) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var value = dialog.TakeSecret();
        try
        {
            await _secrets.ReplaceAsync(requirement, value).ConfigureAwait(true);
            value = string.Empty;
            PersistenceStatusText.Text = $"{requirement.DisplayName} stored in Windows Credential Manager.";
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
                $"Remove {requirement.DisplayName} from Windows Credential Manager?",
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
        if (requirement.Key == "General.ProjectsRoot")
        {
            var dialog = new OpenFolderDialog { Title = "Select the default ReelForge projects folder", Multiselect = false };
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

    private async void AutoDetectMediaTools_Click(object sender, RoutedEventArgs e)
    {
        var detected = _mediaToolDiscovery.Discover();
        if (_visibleEditors.TryGetValue("MediaTools.FfmpegPath", out var ffmpeg)) ffmpeg.Text = detected.FfmpegPath ?? string.Empty;
        if (_visibleEditors.TryGetValue("MediaTools.FfprobePath", out var ffprobe)) ffprobe.Text = detected.FfprobePath ?? string.Empty;
        PersistenceStatusText.Text = detected.Summary;
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

    private sealed record BooleanChoice(ConfigurationRequirement Requirement, bool Enabled);
}
