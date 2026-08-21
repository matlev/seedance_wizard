using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.MediaPreparation;

internal enum MediaPreparationMode
{
    None,
    SelectFrame,
    MakeClip
}

public partial class MediaPreparationPanel : UserControl
{
    private ClipBoundarySelection _clipStart = ClipBoundarySelection.SourceStart;
    private ClipBoundarySelection _clipEnd = ClipBoundarySelection.SourceEnd;

    public MediaPreparationPanel()
    {
        InitializeComponent();
        ClearSavedFrameEditor();
    }

    private MediaPreparationMode Mode { get; set; }
    public bool IsPreparing => Mode != MediaPreparationMode.None;
    public FrameContactListItem? SelectedContactFrame => ContactFramesList.SelectedItem as FrameContactListItem;
    public SavedFrameListItem? SelectedSavedFrame => SavedFramesList.SelectedItem as SavedFrameListItem;

    public event EventHandler? SelectFrameRequested;
    public event EventHandler? MakeClipRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? FirstFrameRequested;
    public event EventHandler? LastFrameRequested;
    public event EventHandler? SaveFrameRequested;
    public event EventHandler? ClipStartRequested;
    public event EventHandler? ClipEndRequested;
    public event EventHandler? SaveClipRequested;
    public event EventHandler<FrameContactSelectionEventArgs>? ContactFrameSelected;
    public event EventHandler<FrameStepRequestedEventArgs>? FrameStepRequested;
    public event EventHandler<SavedFrameSelectionEventArgs>? SavedFrameSelected;
    public event EventHandler<SavedFrameUpdateRequestedEventArgs>? SavedFrameUpdateRequested;
    public event EventHandler<SavedFrameSelectionEventArgs>? SavedFrameJumpRequested;
    public event EventHandler<SavedFrameSelectionEventArgs>? SavedFrameRemoveRequested;

    public void SetItemsSources(IEnumerable contactFrames, IEnumerable savedFrames)
    {
        ContactFramesList.ItemsSource = contactFrames;
        SavedFramesList.ItemsSource = savedFrames;
    }

    public void ConfigureSelection(string displayName, bool canPrepare)
    {
        SelectFrameButton.IsEnabled = canPrepare;
        MakeClipButton.IsEnabled = canPrepare;
        MediaPreparationSelectionText.Text = canPrepare
            ? displayName
            : "Select a physical video in Project Media";
    }

    public void EnterSelectFrame(string displayName)
    {
        Mode = MediaPreparationMode.SelectFrame;
        ShowPrecisionWorkspace(displayName, makingClip: false);
    }

    public void EnterMakeClip(string displayName, string defaultClipName)
    {
        Mode = MediaPreparationMode.MakeClip;
        _clipStart = ClipBoundarySelection.SourceStart;
        _clipEnd = ClipBoundarySelection.SourceEnd;
        ClipNameTextBox.Text = defaultClipName;
        UpdateClipBoundarySummary();
        ShowPrecisionWorkspace(displayName, makingClip: true);
    }

    public void ResetPresentation()
    {
        Mode = MediaPreparationMode.None;
        PrecisionFramePanel.Visibility = Visibility.Collapsed;
        PrecisionFramePanel.ScrollToTop();
        MediaPreparationHome.Visibility = Visibility.Visible;
        ConfigureSelection("Select a physical video in Project Media", canPrepare: false);
        ContactFramesEmptyText.Text = "Select a video to browse exact decoded frames.";
        ContactFramesEmptyText.Visibility = Visibility.Visible;
        SavedFramesEmptyText.Visibility = Visibility.Visible;
        FrameWorkspaceStatusText.Text = "Select a physical video";
        PrecisionOperationTitle.Text = "SELECT FRAME";
        FrameSelectionActions.Visibility = Visibility.Visible;
        ClipSelectionActions.Visibility = Visibility.Collapsed;
        SavedFramesHeading.Visibility = Visibility.Visible;
        SavedFramesWorkspace.Visibility = Visibility.Visible;
        ClipEditorWorkspace.Visibility = Visibility.Collapsed;
        _clipStart = ClipBoundarySelection.SourceStart;
        _clipEnd = ClipBoundarySelection.SourceEnd;
        ClipNameTextBox.Text = string.Empty;
        UpdateClipBoundarySummary();
        ClearSavedFrameEditor();
    }

    public void SetWorkspaceStatus(string text) => FrameWorkspaceStatusText.Text = text;

    public void ShowContactFramesMessage(string text)
    {
        ContactFramesEmptyText.Text = text;
        ContactFramesEmptyText.Visibility = Visibility.Visible;
    }

    public void HideContactFramesMessage() => ContactFramesEmptyText.Visibility = Visibility.Collapsed;

    public void SetSavedFramesEmpty(bool isEmpty) =>
        SavedFramesEmptyText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;

    public void SelectContactFrame(FrameContactListItem item)
    {
        ContactFramesList.SelectedItem = item;
        ContactFramesList.ScrollIntoView(item);
    }

    public void SelectSavedFrame(SavedFrameListItem? item) => SavedFramesList.SelectedItem = item;

    public void RefreshSavedFrames() => SavedFramesList.Items.Refresh();

    public void SetClipBoundary(ExactFramePosition position, bool isStart)
    {
        if (isStart)
            _clipStart = ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.BeforeFrame);
        else
            _clipEnd = ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.AfterFrame);
        UpdateClipBoundarySummary();
    }

    public bool TryCaptureClipDraft(out MediaPreparationClipDraft draft)
    {
        var name = ClipNameTextBox.Text.Trim();
        if (Mode != MediaPreparationMode.MakeClip || string.IsNullOrWhiteSpace(name))
        {
            draft = null!;
            return false;
        }
        draft = new MediaPreparationClipDraft(name, _clipStart, _clipEnd);
        return true;
    }

    public void FocusClipName() => ClipNameTextBox.Focus();

    private void ShowPrecisionWorkspace(string displayName, bool makingClip)
    {
        MediaPreparationHome.Visibility = Visibility.Collapsed;
        PrecisionFramePanel.Visibility = Visibility.Visible;
        PrecisionOperationTitle.Text = makingClip ? "MAKE CLIP" : "SELECT FRAME";
        FrameSelectionActions.Visibility = makingClip ? Visibility.Collapsed : Visibility.Visible;
        ClipSelectionActions.Visibility = makingClip ? Visibility.Visible : Visibility.Collapsed;
        SavedFramesHeading.Visibility = makingClip ? Visibility.Collapsed : Visibility.Visible;
        SavedFramesWorkspace.Visibility = makingClip ? Visibility.Collapsed : Visibility.Visible;
        ClipEditorWorkspace.Visibility = makingClip ? Visibility.Visible : Visibility.Collapsed;
        FrameWorkspaceStatusText.Text = displayName;
    }

    private void UpdateClipBoundarySummary() =>
        ClipBoundarySummaryText.Text =
            $"Start: {FormatClipBoundary(_clipStart)}\nEnd: {FormatClipBoundary(_clipEnd)}";

    private static string FormatClipBoundary(ClipBoundarySelection boundary) => boundary.Kind switch
    {
        ClipBoundaryKind.SourceStart => "video beginning",
        ClipBoundaryKind.SourceEnd => "video end",
        ClipBoundaryKind.ExactFrame when boundary.ExactPosition is { } position =>
            $"{FormatFrameTimestamp(position.PresentationTimestamp * (double)position.TimeBaseNumerator / position.TimeBaseDenominator)} " +
            $"({boundary.Edge})",
        _ => "not set"
    };

    private static string FormatFrameTimestamp(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private void ClearSavedFrameEditor()
    {
        SavedFrameLabelTextBox.Text = string.Empty;
        SavedFrameNotesTextBox.Text = string.Empty;
        SavedFrameLabelTextBox.IsEnabled = false;
        SavedFrameNotesTextBox.IsEnabled = false;
        UpdateSavedFrameButton.IsEnabled = false;
        JumpToSavedFrameButton.IsEnabled = false;
        RemoveSavedFrameButton.IsEnabled = false;
    }

    private void SelectFrameButton_Click(object sender, RoutedEventArgs e) =>
        SelectFrameRequested?.Invoke(this, EventArgs.Empty);

    private void MakeClipButton_Click(object sender, RoutedEventArgs e) =>
        MakeClipRequested?.Invoke(this, EventArgs.Empty);

    private void ExitButton_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void FirstFrameButton_Click(object sender, RoutedEventArgs e) => FirstFrameRequested?.Invoke(this, EventArgs.Empty);
    private void LastFrameButton_Click(object sender, RoutedEventArgs e) => LastFrameRequested?.Invoke(this, EventArgs.Empty);
    private void SaveFrameButton_Click(object sender, RoutedEventArgs e) => SaveFrameRequested?.Invoke(this, EventArgs.Empty);
    private void ClipStartButton_Click(object sender, RoutedEventArgs e) => ClipStartRequested?.Invoke(this, EventArgs.Empty);
    private void ClipEndButton_Click(object sender, RoutedEventArgs e) => ClipEndRequested?.Invoke(this, EventArgs.Empty);
    private void SaveClipButton_Click(object sender, RoutedEventArgs e) => SaveClipRequested?.Invoke(this, EventArgs.Empty);

    private void SourceStartButton_Click(object sender, RoutedEventArgs e)
    {
        _clipStart = ClipBoundarySelection.SourceStart;
        UpdateClipBoundarySummary();
    }

    private void SourceEndButton_Click(object sender, RoutedEventArgs e)
    {
        _clipEnd = ClipBoundarySelection.SourceEnd;
        UpdateClipBoundarySummary();
    }

    private void ContactFramesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ContactFrameSelected?.Invoke(this, new FrameContactSelectionEventArgs(SelectedContactFrame));

    private void ContactFramesList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right)) return;
        e.Handled = true;
        if (SelectedContactFrame is null) return;
        FrameStepRequested?.Invoke(this, new FrameStepRequestedEventArgs(e.Key == Key.Left ? -1 : 1));
    }

    private void SavedFramesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = SelectedSavedFrame;
        if (item is null)
        {
            ClearSavedFrameEditor();
        }
        else
        {
            SavedFrameLabelTextBox.IsEnabled = true;
            SavedFrameNotesTextBox.IsEnabled = true;
            UpdateSavedFrameButton.IsEnabled = true;
            JumpToSavedFrameButton.IsEnabled = true;
            RemoveSavedFrameButton.IsEnabled = true;
            SavedFrameLabelTextBox.Text = item.Anchor.DisplayLabel ?? string.Empty;
            SavedFrameNotesTextBox.Text = item.Anchor.Notes ?? string.Empty;
        }
        SavedFrameSelected?.Invoke(this, new SavedFrameSelectionEventArgs(item));
    }

    private void UpdateSavedFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSavedFrame is not { } item) return;
        SavedFrameUpdateRequested?.Invoke(
            this,
            new SavedFrameUpdateRequestedEventArgs(item, SavedFrameLabelTextBox.Text, SavedFrameNotesTextBox.Text));
    }

    private void JumpToSavedFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSavedFrame is { } item)
            SavedFrameJumpRequested?.Invoke(this, new SavedFrameSelectionEventArgs(item));
    }

    private void RemoveSavedFrameButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSavedFrame is { } item)
            SavedFrameRemoveRequested?.Invoke(this, new SavedFrameSelectionEventArgs(item));
    }
}

public sealed record MediaPreparationClipDraft(
    string Name,
    ClipBoundarySelection Start,
    ClipBoundarySelection End);

public sealed class FrameContactSelectionEventArgs(FrameContactListItem? item) : EventArgs
{
    public FrameContactListItem? Item { get; } = item;
}

public sealed class FrameStepRequestedEventArgs(int steps) : EventArgs
{
    public int Steps { get; } = steps;
}

public sealed class SavedFrameSelectionEventArgs(SavedFrameListItem? item) : EventArgs
{
    public SavedFrameListItem? Item { get; } = item;
}

public sealed class SavedFrameUpdateRequestedEventArgs(
    SavedFrameListItem item,
    string label,
    string notes) : EventArgs
{
    public SavedFrameListItem Item { get; } = item;
    public string Label { get; } = label;
    public string Notes { get; } = notes;
}
