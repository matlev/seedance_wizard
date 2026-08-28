using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

public sealed record GenerationPanelFormState(
    string Prompt,
    GenerationMode Mode,
    int DurationSeconds,
    string AspectRatio,
    string Resolution,
    bool GenerateAudio,
    bool Watermark,
    string OutputFormat);

public sealed class GenerationProviderChangedEventArgs(GenerationProviderChoice choice) : EventArgs
{
    public GenerationProviderChoice Choice { get; } = choice;
}

public sealed class GenerationReferenceSelectedEventArgs(GenerationReferenceChoice choice) : EventArgs
{
    public GenerationReferenceChoice Choice { get; } = choice;
}

public sealed class DerivedDraftRequestedEventArgs(GenerationRelationshipType relationshipType) : EventArgs
{
    public GenerationRelationshipType RelationshipType { get; } = relationshipType;
}

public partial class GenerationPanel : UserControl
{
    private ObservableCollection<GenerationReferenceChoice>? _references;
    private GenerationProviderCapabilities? _capabilities;
    private bool _suppressEvents;

    public GenerationPanel() => InitializeComponent();

    public event EventHandler<GenerationProviderChangedEventArgs>? ProviderChanged;
    public event EventHandler? DraftChanged;
    public event EventHandler? ExpandPromptRequested;
    public event EventHandler<GenerationReferenceSelectedEventArgs>? ReferenceSelected;
    public event EventHandler? NewRootRequested;
    public event EventHandler<DerivedDraftRequestedEventArgs>? DerivedDraftRequested;
    public event EventHandler? SubmitRequested;

    public GenerationProviderChoice? SelectedProviderChoice =>
        ProviderComboBox.SelectedItem as GenerationProviderChoice;

    public string Prompt
    {
        get => PromptTextBox.Text;
        set => PromptTextBox.Text = value;
    }

    public string Status
    {
        get => GenerationStatusText.Text;
        set => GenerationStatusText.Text = value;
    }

    public bool IsSubmissionEnabled
    {
        get => GenerateButton.IsEnabled;
        set => GenerateButton.IsEnabled = value;
    }

    public bool IsProviderEnabled
    {
        get => ProviderComboBox.IsEnabled;
        set => ProviderComboBox.IsEnabled = value;
    }

    public void SetProviders(
        IReadOnlyList<GenerationProviderChoice> choices,
        GenerationProviderChoice selected)
    {
        WithSuppressedEvents(() =>
        {
            ProviderComboBox.ItemsSource = null;
            ProviderComboBox.ItemsSource = choices;
            ProviderComboBox.SelectedItem = selected;
        });
    }

    public void SetReferences(ObservableCollection<GenerationReferenceChoice> references)
    {
        _references = references;
        ReferenceAssetsGrid.ItemsSource = references;
    }

    public void ConfigureProvider(IVideoGenerationProvider provider)
    {
        var capabilities = provider.Capabilities;
        _capabilities = capabilities;
        WithSuppressedEvents(() =>
        {
            var costText = provider.CostBehavior == GenerationProviderCostBehavior.NoCharge
                ? "No network or billing"
                : "Potentially billable; explicit confirmation required for every submission";
            ProviderText.Text = $"{capabilities.ModelVersion}\n{costText}";
            GenerateButton.Content = provider.CostBehavior == GenerationProviderCostBehavior.NoCharge
                ? "Run fake generation"
                : "Review and submit generation…";

            var supportsWatermark = capabilities.ProviderParameters.ContainsKey("watermark");
            var supportsAudioToggle = capabilities.ProviderParameters.ContainsKey("generate_audio") ||
                                      capabilities.ProviderParameters.ContainsKey("generateAudio");
            var supportsOutputFormat = capabilities.ProviderParameters.ContainsKey("output_format");
            GenerateAudioCheckBox.Visibility = supportsAudioToggle ? Visibility.Visible : Visibility.Collapsed;
            WatermarkCheckBox.Visibility = supportsWatermark ? Visibility.Visible : Visibility.Collapsed;
            WatermarkHelpText.Visibility = supportsWatermark ? Visibility.Visible : Visibility.Collapsed;
            OutputFormatPanel.Visibility = supportsOutputFormat ? Visibility.Visible : Visibility.Collapsed;
            AudioAndWatermarkPanel.Visibility = supportsAudioToggle || supportsWatermark
                ? Visibility.Visible
                : Visibility.Collapsed;
            OutputSettingsHeading.Visibility = supportsAudioToggle || supportsWatermark || supportsOutputFormat
                ? Visibility.Visible
                : Visibility.Collapsed;

            ModeComboBox.ItemsSource = capabilities.Modes;
            ModeComboBox.SelectedItem = capabilities.Modes.Contains(GenerationMode.ReferenceToVideo)
                ? GenerationMode.ReferenceToVideo
                : capabilities.Modes[0];
            DurationSlider.Minimum = capabilities.MinimumDurationSeconds;
            DurationSlider.Maximum = capabilities.MaximumDurationSeconds;
            DurationSlider.Value = Math.Clamp(
                15,
                capabilities.MinimumDurationSeconds,
                capabilities.MaximumDurationSeconds);
            AspectRatioComboBox.ItemsSource = capabilities.AspectRatios;
            AspectRatioComboBox.SelectedItem = capabilities.AspectRatios.Contains("16:9")
                ? "16:9"
                : capabilities.AspectRatios[0];
            ResolutionComboBox.ItemsSource = capabilities.Resolutions;
            ResolutionComboBox.SelectedItem = capabilities.Resolutions.Contains("720p")
                ? "720p"
                : capabilities.Resolutions[0];
            UpdateModePresentation();
        });
    }

    public GenerationPanelFormState CaptureState() => new(
        PromptTextBox.Text,
        ModeComboBox.SelectedItem is GenerationMode mode ? mode : GenerationMode.TextToVideo,
        (int)DurationSlider.Value,
        AspectRatioComboBox.SelectedItem as string ?? "16:9",
        ResolutionComboBox.SelectedItem as string ?? "720p",
        GenerateAudioCheckBox.IsChecked == true,
        WatermarkCheckBox.IsChecked == true,
        (OutputFormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp4");

    public void LoadState(GenerationPanelFormState state)
    {
        if (_capabilities is null) return;
        WithSuppressedEvents(() =>
        {
            PromptTextBox.Text = state.Prompt;
            ModeComboBox.SelectedItem = state.Mode;
            DurationSlider.Value = Math.Clamp(
                state.DurationSeconds,
                _capabilities.MinimumDurationSeconds,
                _capabilities.MaximumDurationSeconds);
            if (_capabilities.AspectRatios.Contains(state.AspectRatio))
                AspectRatioComboBox.SelectedItem = state.AspectRatio;
            if (_capabilities.Resolutions.Contains(state.Resolution))
                ResolutionComboBox.SelectedItem = state.Resolution;
            GenerateAudioCheckBox.IsChecked = state.GenerateAudio;
            WatermarkCheckBox.IsChecked = state.Watermark;
            OutputFormatComboBox.SelectedItem = OutputFormatComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Content?.ToString(),
                    state.OutputFormat,
                    StringComparison.OrdinalIgnoreCase)) ?? OutputFormatComboBox.Items[0];
            UpdateModePresentation();
        });
    }

    public void SelectProvider(GenerationProviderChoice choice) =>
        WithSuppressedEvents(() => ProviderComboBox.SelectedItem = choice);

    public void RefreshReferences() => ReferenceAssetsGrid.Items.Refresh();

    public void SetLineage(string text) => LineageText.Text = text;

    public void FocusPromptAtEnd()
    {
        PromptTextBox.Focus();
        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
    }

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || SelectedProviderChoice is not { } choice) return;
        ProviderChanged?.Invoke(this, new GenerationProviderChangedEventArgs(choice));
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        UpdateModePresentation();
        OnDraftChanged();
    }

    private void UpdateModePresentation()
    {
        var selectedMode = ModeComboBox.SelectedItem is GenerationMode mode
            ? mode
            : GenerationMode.TextToVideo;
        ReferenceAssetsGrid.IsEnabled = selectedMode is not GenerationMode.TextToVideo;
        ReferenceAssetsHelpText.Text = selectedMode is GenerationMode.TextToVideo
            ? "Text-to-video does not use reference assets. Choose ImageToVideo or ReferenceToVideo to select and describe references."
            : "Select project assets to use as references. Role, order, label, and notes are frozen into history.";
        if (_capabilities is null) return;
        if (selectedMode is GenerationMode.ImageToVideo && _capabilities.AspectRatios.Contains("adaptive"))
        {
            AspectRatioComboBox.SelectedItem = "adaptive";
        }
        else if (selectedMode is GenerationMode.TextToVideo &&
                 string.Equals(AspectRatioComboBox.SelectedItem as string, "adaptive", StringComparison.OrdinalIgnoreCase))
        {
            AspectRatioComboBox.SelectedItem = _capabilities.AspectRatios.Contains("16:9")
                ? "16:9"
                : _capabilities.AspectRatios.FirstOrDefault(ratio =>
                    !string.Equals(ratio, "adaptive", StringComparison.OrdinalIgnoreCase));
        }
    }

    private void DraftControl_Changed(object sender, EventArgs e) => OnDraftChanged();

    private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationText is not null) DurationText.Text = $"{(int)e.NewValue}s";
        OnDraftChanged();
    }

    private void ReferenceAssetsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(OnDraftChanged, DispatcherPriority.Background);

    private void ReferenceChoiceChanged(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(OnDraftChanged, DispatcherPriority.Background);

    private void ReferenceAssetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ReferenceAssetsGrid.SelectedItem is not GenerationReferenceChoice choice) return;
        ReferenceSelected?.Invoke(this, new GenerationReferenceSelectedEventArgs(choice));
    }

    private void DuplicateReferenceOccurrence_Click(object sender, RoutedEventArgs e)
    {
        if (_references is null) return;
        if (ReferenceAssetsGrid.SelectedItem is not GenerationReferenceChoice selected)
        {
            Status = "Select a reference row to add another occurrence.";
            return;
        }
        if (!selected.CanCreateAdditionalOccurrence)
        {
            Status = "A deleted project asset can remain in this draft, but cannot be added again.";
            return;
        }
        var duplicate = selected.Duplicate(_references.Count);
        _references.Add(duplicate);
        ReferenceAssetsGrid.SelectedItem = duplicate;
        ReferenceAssetsGrid.ScrollIntoView(duplicate);
        OnDraftChanged();
        Status = $"Added another occurrence of {duplicate.DisplayName}.";
    }

    private void ExpandPrompt_Click(object sender, RoutedEventArgs e) =>
        ExpandPromptRequested?.Invoke(this, EventArgs.Empty);

    private void NewRoot_Click(object sender, RoutedEventArgs e) =>
        NewRootRequested?.Invoke(this, EventArgs.Empty);

    private void DerivedDraft_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string relationship } ||
            !Enum.TryParse<GenerationRelationshipType>(relationship, out var relationshipType)) return;
        DerivedDraftRequested?.Invoke(this, new DerivedDraftRequestedEventArgs(relationshipType));
    }

    private void Submit_Click(object sender, RoutedEventArgs e) =>
        SubmitRequested?.Invoke(this, EventArgs.Empty);

    private void OnDraftChanged()
    {
        if (!_suppressEvents) DraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void WithSuppressedEvents(Action action)
    {
        var previous = _suppressEvents;
        _suppressEvents = true;
        try
        {
            action();
        }
        finally
        {
            _suppressEvents = previous;
        }
    }
}
