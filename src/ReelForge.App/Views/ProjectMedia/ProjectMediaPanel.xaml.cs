using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

public enum ProjectMediaAction
{
    Rename,
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

    public ProjectMediaPanel() => InitializeComponent();

    public ProjectMediaListItem? SelectedItem
    {
        get => MediaList.SelectedItem as ProjectMediaListItem;
        set => MediaList.SelectedItem = value;
    }

    public event EventHandler<ProjectMediaSelectionChangedEventArgs>? SelectedItemChanged;
    public event EventHandler<ProjectMediaActionRequestedEventArgs>? ActionRequested;
    public event EventHandler? DragCompleted;

    public void SetItemsSource(IEnumerable source) => MediaList.ItemsSource = source;

    public void RefreshItems() => MediaList.Items.Refresh();

    private void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        SelectedItemChanged?.Invoke(this, new ProjectMediaSelectionChangedEventArgs(SelectedItem));

    private void MediaList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(MediaList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item is not null) item.IsSelected = true;
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var asset = SelectedItem?.Asset;
        var isEligibleVideo = asset is { MediaType: MediaType.Video } &&
                              (asset.StorageKind == AssetStorageKind.Physical ||
                               asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        ExtractAudioItem.Visibility = isEligibleVideo ? Visibility.Visible : Visibility.Collapsed;
        if (!isEligibleVideo) return;

        var knownEncoding = asset!.StorageKind == AssetStorageKind.Physical
            ? asset.Encoding
            : asset.Virtual?.ExpectedMediaProperties;
        ExtractAudioItem.IsEnabled = knownEncoding?.Audio is not null || knownEncoding is null;
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
