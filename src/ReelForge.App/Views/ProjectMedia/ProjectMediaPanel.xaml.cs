using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

public enum ProjectMediaAction
{
    Rename,
    Relink,
    Export,
    ExtractAudio,
    Copy,
    Move,
    Delete
}

public sealed class ProjectMediaSelectionChangedEventArgs(ProjectMediaListItem? selectedItem) : EventArgs
{
    public ProjectMediaListItem? SelectedItem { get; } = selectedItem;
}

public sealed class ProjectMediaActionRequestedEventArgs(ProjectMediaAction action) : EventArgs
{
    public ProjectMediaAction Action { get; } = action;
}

public partial class ProjectMediaPanel : UserControl
{
    private Point _dragStart;
    private ProjectMediaListItem? _dragItem;
    private readonly Dictionary<string, bool> _groupExpansionStates = new(StringComparer.Ordinal);
    private bool _restoringGroupExpansionState;

    public ProjectMediaPanel() => InitializeComponent();

    public ProjectMediaListItem? SelectedItem
    {
        get => MediaList.SelectedItem as ProjectMediaListItem;
        set => MediaList.SelectedItem = value;
    }

    public event EventHandler<ProjectMediaSelectionChangedEventArgs>? SelectedItemChanged;
    public event EventHandler<ProjectMediaActionRequestedEventArgs>? ActionRequested;
    public event EventHandler? DragCompleted;

    public void SetItemsSource(IList source)
    {
        var view = new ListCollectionView(source);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectMediaListItem.GroupName)));
        MediaList.ItemsSource = view;
    }

    public void RefreshItems() => MediaList.Items.Refresh();

    private void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectedItemChanged?.Invoke(this, new ProjectMediaSelectionChangedEventArgs(SelectedItem));

    private void GroupExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander { Tag: string groupName } expander) return;

        _restoringGroupExpansionState = true;
        try
        {
            expander.IsExpanded = !_groupExpansionStates.TryGetValue(groupName, out var isExpanded) || isExpanded;
        }
        finally
        {
            _restoringGroupExpansionState = false;
        }
    }

    private void GroupExpander_Expanded(object sender, RoutedEventArgs e) =>
        UpdateGroupExpansionState(sender, isExpanded: true);

    private void GroupExpander_Collapsed(object sender, RoutedEventArgs e) =>
        UpdateGroupExpansionState(sender, isExpanded: false);

    private void UpdateGroupExpansionState(object sender, bool isExpanded)
    {
        if (_restoringGroupExpansionState || sender is not Expander { Tag: string groupName }) return;
        _groupExpansionStates[groupName] = isExpanded;
    }

    private void MediaList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(MediaList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item is not null) item.IsSelected = true;
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var asset = SelectedItem?.Asset;
        if (ProjectMediaContextMenuPolicy.UsesMissingAssetMenu(asset))
        {
            RenameItem.Visibility = Visibility.Collapsed;
            RelinkSourceItem.Visibility = Visibility.Visible;
            ExportItem.Visibility = Visibility.Collapsed;
            ExtractAudioItem.Visibility = Visibility.Collapsed;
            FirstMenuSeparator.Visibility = Visibility.Collapsed;
            CopyToProjectItem.Visibility = Visibility.Collapsed;
            MoveToProjectItem.Visibility = Visibility.Collapsed;
            SecondMenuSeparator.Visibility = Visibility.Collapsed;
            DeleteItem.Visibility = Visibility.Visible;
            return;
        }

        DeleteItem.Visibility = Visibility.Visible;
        ExportItem.Visibility = Visibility.Visible;
        FirstMenuSeparator.Visibility = Visibility.Visible;
        SecondMenuSeparator.Visibility = Visibility.Visible;
        var savedFrame = SelectedItem is { Anchor: not null, AnchorRevision: not null };
        var copyableVirtualVideo = asset is
        {
            StorageKind: AssetStorageKind.Virtual,
            MediaType: MediaType.Video,
            Virtual.Kind: VirtualAssetKind.SavedClip or VirtualAssetKind.Composition,
            Virtual.CurrentRecipeRevisionId: not null
        };
        var physicalAsset = asset is { StorageKind: AssetStorageKind.Physical, Physical: not null };
        RelinkSourceItem.Visibility = physicalAsset ? Visibility.Visible : Visibility.Collapsed;
        CopyToProjectItem.Visibility = savedFrame || copyableVirtualVideo || physicalAsset
            ? Visibility.Visible
            : Visibility.Collapsed;
        MoveToProjectItem.Visibility = physicalAsset
            ? Visibility.Visible
            : Visibility.Collapsed;

        var renameKind = ProjectMediaRenamePolicy.GetKind(asset);
        RenameItem.Visibility = renameKind == ProjectMediaRenameKind.None
            ? Visibility.Collapsed
            : Visibility.Visible;
        RenameItem.Header = renameKind == ProjectMediaRenameKind.SavedClip
            ? "Rename Saved Clip…"
            : "Change filename…";

        var isEligibleVideo = asset is { MediaType: MediaType.Video } &&
                              (asset.StorageKind == AssetStorageKind.Physical ||
                               asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        ExtractAudioItem.Visibility = isEligibleVideo ? Visibility.Visible : Visibility.Collapsed;
        if (!isEligibleVideo) return;

        ExtractAudioItem.IsEnabled = MediaAudioCapabilityPolicy.CanAttemptAudioOperation(asset);
        ExtractAudioItem.ToolTip = ExtractAudioItem.IsEnabled
            ? "Create a permanent audio file from this video's sound."
            : "This video has no audio stream to extract.";
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string actionName } ||
            !Enum.TryParse<ProjectMediaAction>(actionName, out var action)) return;
        ActionRequested?.Invoke(this, new ProjectMediaActionRequestedEventArgs(action));
    }

    private void MediaList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(MediaList);
        var container = ItemsControl.ContainerFromElement(
            MediaList,
            e.OriginalSource as DependencyObject) as ListBoxItem;
        _dragItem = container?.DataContext as ProjectMediaListItem;
    }

    private void MediaList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _dragItem?.Asset is not { } asset ||
            !ProjectMediaDragData.CanAddToComposition(asset)) return;
        var position = e.GetPosition(MediaList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragItem = null;
        var data = new DataObject(
            ProjectMediaDragData.Format,
            asset.Id.ToString("D", CultureInfo.InvariantCulture));
        try
        {
            DragDrop.DoDragDrop(MediaList, data, DragDropEffects.Copy);
        }
        finally
        {
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

}
