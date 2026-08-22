using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ReelForge.Application;

namespace ReelForge.App.Views.Settings;

/// <summary>
/// Builds the dynamic controls for one settings catalog requirement. Persistence and
/// window lifecycle remain with <see cref="SettingsWindow"/>; this type owns only the
/// mapping from requirement metadata to WPF editors and their local interaction rules.
/// </summary>
internal sealed class SettingsFieldFactory
{
    private const long Megabyte = 1024L * 1024;
    private const long Gigabyte = 1024L * 1024 * 1024;
    private readonly ApplicationSettingsEditor _editor;
    private readonly SecretConfigurationService _secrets;
    private readonly ISecretStore _secretStore;
    private readonly IDictionary<string, string> _pendingValues;
    private readonly IDictionary<string, TextBox> _visibleEditors;
    private readonly Func<string, object> _findResource;
    private readonly Func<bool> _isRendering;
    private readonly Func<Task<bool>> _commitVisibleAsync;
    private readonly RoutedEventHandler _browseSetting;
    private readonly RoutedEventHandler _autoDetectMediaTool;
    private readonly RoutedEventHandler _updateSecret;
    private readonly RoutedEventHandler _removeSecret;

    public SettingsFieldFactory(
        ApplicationSettingsEditor editor,
        SecretConfigurationService secrets,
        ISecretStore secretStore,
        IDictionary<string, string> pendingValues,
        IDictionary<string, TextBox> visibleEditors,
        Func<string, object> findResource,
        Func<bool> isRendering,
        Func<Task<bool>> commitVisibleAsync,
        RoutedEventHandler browseSetting,
        RoutedEventHandler autoDetectMediaTool,
        RoutedEventHandler updateSecret,
        RoutedEventHandler removeSecret)
    {
        _editor = editor;
        _secrets = secrets;
        _secretStore = secretStore;
        _pendingValues = pendingValues;
        _visibleEditors = visibleEditors;
        _findResource = findResource;
        _isRendering = isRendering;
        _commitVisibleAsync = commitVisibleAsync;
        _browseSetting = browseSetting;
        _autoDetectMediaTool = autoDetectMediaTool;
        _updateSecret = updateSecret;
        _removeSecret = removeSecret;
    }

    public FrameworkElement CreateValueEditor(ConfigurationRequirement requirement)
    {
        if (requirement.Key.EndsWith(".Enabled", StringComparison.Ordinal) ||
            requirement.Key == "MediaTools.PersistModifiedMediaOnDisk")
            return CreateBooleanEditor(requirement);
        if (requirement.Key is "MediaTools.LogFfmpegCommands" or "MediaTools.LogFfprobeCommands")
            return CreateCommandLoggingCheckbox(requirement);
        if (requirement.Key == "General.UndoSendSeconds") return CreateUndoSendEditor(requirement);
        if (requirement.Key == "MediaTools.CacheSizeBytes") return CreateCacheSizeEditor(requirement);
        if (requirement.Key == "MediaTools.SplitBehavior") return CreateMediaSplitBehaviorEditor(requirement);

        var panel = CreateFieldPanel(requirement);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var supportsBrowse = requirement.Key is "MediaTools.FfmpegPath" or "MediaTools.FfprobePath" or
            "General.ProjectsRoot" or "General.LogDirectory";
        var supportsAutoDetect = requirement.Key is "MediaTools.FfmpegPath" or "MediaTools.FfprobePath";
        if (supportsBrowse) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (supportsAutoDetect) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var textBox = new TextBox
        {
            Text = CurrentValue(requirement),
            Tag = requirement,
            ToolTip = requirement.Placeholder,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        textBox.TextChanged += (_, _) => _pendingValues[requirement.Key] = textBox.Text;
        textBox.LostKeyboardFocus += async (_, _) => await _commitVisibleAsync().ConfigureAwait(true);
        row.Children.Add(textBox);
        _visibleEditors[requirement.Key] = textBox;

        if (supportsBrowse)
        {
            var browse = new Button
            {
                Content = "Browse…",
                Tag = requirement,
                Margin = new Thickness(7, 0, 0, 0)
            };
            browse.Click += _browseSetting;
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);
        }

        if (supportsAutoDetect)
        {
            var executableName = requirement.Key == "MediaTools.FfmpegPath" ? "FFmpeg" : "ffprobe";
            var detect = new Button
            {
                Content = "Auto-detect",
                Tag = requirement,
                Margin = new Thickness(7, 0, 0, 0),
                ToolTip = $"Auto-detect {executableName} from PATH."
            };
            detect.Click += _autoDetectMediaTool;
            Grid.SetColumn(detect, 2);
            row.Children.Add(detect);
        }

        panel.Children.Add(row);
        return panel;
    }

    public async Task<FrameworkElement> CreateSecretEditorAsync(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        var configured = await _secrets.IsConfiguredAsync(requirement).ConfigureAwait(true);
        var row = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var update = new Button { Content = configured ? "Update" : "Configure", Tag = requirement };
        update.Click += _updateSecret;
        buttons.Children.Add(update);
        var remove = new Button
        {
            Content = "Remove",
            Tag = requirement,
            IsEnabled = configured,
            Style = Resource<Style>("DangerButtonStyle")
        };
        remove.Click += _removeSecret;
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

    private FrameworkElement CreateCacheSizeEditor(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        panel.ToolTip = requirement.Description;
        var row = new Grid { ToolTip = requirement.Description };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        var bytes = long.TryParse(CurrentValue(requirement), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : MediaToolConfiguration.DefaultCacheSizeBytes;
        var useGigabytes = bytes >= Gigabyte && bytes % Gigabyte == 0;
        var value = useGigabytes ? bytes / (decimal)Gigabyte : bytes / (decimal)Megabyte;
        var valueEditor = new TextBox
        {
            Text = value.ToString("0.##", CultureInfo.CurrentCulture),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = requirement.Description
        };
        var unitEditor = new ComboBox
        {
            Margin = new Thickness(7, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Choose whether the cache limit is measured in megabytes or gigabytes."
        };
        var megabytes = new ComboBoxItem { Content = "Megabytes (MB)", Tag = Megabyte };
        var gigabytes = new ComboBoxItem { Content = "Gigabytes (GB)", Tag = Gigabyte };
        unitEditor.Items.Add(megabytes);
        unitEditor.Items.Add(gigabytes);
        unitEditor.SelectedItem = useGigabytes ? gigabytes : megabytes;
        var previousUnitFactor = useGigabytes ? Gigabyte : Megabyte;

        void UpdatePendingValue()
        {
            var factor = (unitEditor.SelectedItem as ComboBoxItem)?.Tag is long selectedFactor
                ? selectedFactor
                : Megabyte;
            if (!decimal.TryParse(valueEditor.Text, NumberStyles.Number, CultureInfo.CurrentCulture,
                    out var selectedValue) || selectedValue <= 0)
            {
                _pendingValues[requirement.Key] = "0";
                return;
            }

            try
            {
                _pendingValues[requirement.Key] = decimal.ToInt64(decimal.Round(
                        selectedValue * factor,
                        0,
                        MidpointRounding.AwayFromZero))
                    .ToString(CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                _pendingValues[requirement.Key] = long.MaxValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        valueEditor.TextChanged += (_, _) => UpdatePendingValue();
        valueEditor.LostKeyboardFocus += async (_, _) => await _commitVisibleAsync().ConfigureAwait(true);
        unitEditor.SelectionChanged += async (_, _) =>
        {
            var newUnitFactor = (unitEditor.SelectedItem as ComboBoxItem)?.Tag is long selectedFactor
                ? selectedFactor
                : Megabyte;
            if (newUnitFactor != previousUnitFactor &&
                decimal.TryParse(valueEditor.Text, NumberStyles.Number, CultureInfo.CurrentCulture,
                    out var previousValue))
            {
                var convertedValue = previousValue * previousUnitFactor / newUnitFactor;
                valueEditor.Text = convertedValue.ToString("0.##", CultureInfo.CurrentCulture);
            }
            previousUnitFactor = newUnitFactor;
            UpdatePendingValue();
            await _commitVisibleAsync().ConfigureAwait(true);
        };
        row.Children.Add(valueEditor);
        Grid.SetColumn(unitEditor, 1);
        row.Children.Add(unitEditor);
        panel.Children.Add(row);
        return panel;
    }

    private FrameworkElement CreateUndoSendEditor(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var seconds = int.TryParse(CurrentValue(requirement), out var parsed) ? Math.Clamp(parsed, 0, 30) : 0;
        var valueText = new TextBlock
        {
            MinWidth = 110,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Style = Resource<Style>("ApplicationTextBlockStyle")
        };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 30,
            Value = seconds,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = requirement
        };
        void UpdateValue()
        {
            var selectedSeconds = (int)slider.Value;
            _pendingValues[requirement.Key] = selectedSeconds.ToString(CultureInfo.InvariantCulture);
            valueText.Text = selectedSeconds == 0 ? "Send Immediately" : $"{selectedSeconds} seconds";
        }
        slider.ValueChanged += (_, _) => UpdateValue();
        slider.LostMouseCapture += async (_, _) => await _commitVisibleAsync().ConfigureAwait(true);
        slider.LostKeyboardFocus += async (_, _) => await _commitVisibleAsync().ConfigureAwait(true);
        UpdateValue();
        row.Children.Add(slider);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        panel.Children.Add(row);
        return panel;
    }

    private FrameworkElement CreateBooleanEditor(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        panel.ToolTip = requirement.Description;
        var isEnabled = bool.TryParse(CurrentValue(requirement), out var parsed) && parsed;
        var group = new StackPanel { Orientation = Orientation.Horizontal };
        var usesYesNo = requirement.Key == "MediaTools.PersistModifiedMediaOnDisk";
        var enabled = CreateBooleanChoice(usesYesNo ? "Yes" : "Enabled", requirement, true, isEnabled);
        var disabled = CreateBooleanChoice(usesYesNo ? "No" : "Disabled", requirement, false, !isEnabled);
        group.Children.Add(enabled);
        group.Children.Add(disabled);
        panel.Children.Add(group);
        return panel;
    }

    private FrameworkElement CreateCommandLoggingCheckbox(ConfigurationRequirement requirement)
    {
        var isChecked = bool.TryParse(CurrentValue(requirement), out var parsed) && parsed;
        var checkbox = new CheckBox
        {
            Content = requirement.DisplayName,
            ToolTip = requirement.Description,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 0, 17)
        };
        async Task UpdateAsync()
        {
            if (_isRendering()) return;
            _pendingValues[requirement.Key] = (checkbox.IsChecked == true).ToString().ToLowerInvariant();
            await _commitVisibleAsync().ConfigureAwait(true);
        }
        checkbox.Checked += async (_, _) => await UpdateAsync().ConfigureAwait(true);
        checkbox.Unchecked += async (_, _) => await UpdateAsync().ConfigureAwait(true);
        return checkbox;
    }

    private RadioButton CreateBooleanChoice(
        string label,
        ConfigurationRequirement requirement,
        bool value,
        bool isChecked)
    {
        var choice = new RadioButton
        {
            Content = label,
            IsChecked = isChecked,
            GroupName = requirement.Key,
            Style = Resource<Style>("SettingsBooleanChoiceStyle")
        };
        choice.Checked += async (_, _) =>
        {
            if (_isRendering()) return;
            _pendingValues[requirement.Key] = value.ToString().ToLowerInvariant();
            await _commitVisibleAsync().ConfigureAwait(true);
        };
        return choice;
    }

    private FrameworkElement CreateMediaSplitBehaviorEditor(ConfigurationRequirement requirement)
    {
        var panel = CreateFieldPanel(requirement);
        panel.ToolTip = requirement.Description;
        var behavior = Enum.TryParse<MediaSplitBehavior>(CurrentValue(requirement), ignoreCase: true, out var parsed)
            ? parsed
            : MediaSplitBehavior.BeforeSelectedFrame;
        var group = new StackPanel { Orientation = Orientation.Horizontal };
        group.Children.Add(CreateSplitChoice(
            "Split before selected frame",
            "The selected frame becomes the first frame of the second clip.",
            requirement,
            MediaSplitBehavior.BeforeSelectedFrame,
            behavior));
        group.Children.Add(CreateSplitChoice(
            "Split after selected frame",
            "The selected frame becomes the last frame of the first clip.",
            requirement,
            MediaSplitBehavior.AfterSelectedFrame,
            behavior));
        panel.Children.Add(group);
        return panel;
    }

    private RadioButton CreateSplitChoice(
        string label,
        string tooltip,
        ConfigurationRequirement requirement,
        MediaSplitBehavior value,
        MediaSplitBehavior current)
    {
        var choice = new RadioButton
        {
            Content = label,
            IsChecked = value == current,
            GroupName = requirement.Key,
            Style = Resource<Style>("SettingsBooleanChoiceStyle"),
            ToolTip = tooltip
        };
        choice.Checked += async (_, _) =>
        {
            if (_isRendering()) return;
            _pendingValues[requirement.Key] = value.ToString();
            await _commitVisibleAsync().ConfigureAwait(true);
        };
        return choice;
    }

    private StackPanel CreateFieldPanel(ConfigurationRequirement requirement)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 17) };
        panel.Children.Add(new TextBlock
        {
            Text = requirement.Required ? $"{requirement.DisplayName} (required)" : requirement.DisplayName,
            Style = Resource<Style>("ApplicationTextBlockStyle"),
            Margin = new Thickness(0, 0, 0, 3)
        });
        panel.Children.Add(new TextBlock
        {
            Text = requirement.Secret
                ? $"{requirement.Description} {_secretStore.DisplayName} key: " +
                  _secretStore.GetDisplayKey(requirement.CredentialManagerKey!)
                : requirement.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Resource<System.Windows.Media.Brush>("MutedTextBrush"),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 5)
        });
        return panel;
    }

    private string CurrentValue(ConfigurationRequirement requirement) =>
        _pendingValues.TryGetValue(requirement.Key, out var pending)
            ? pending
            : ApplicationSettingsAccessor.Get(_editor.Settings, requirement.Key);

    private T Resource<T>(string key) where T : class => (T)_findResource(key);
}
