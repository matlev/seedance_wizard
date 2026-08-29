using System.Windows;
using ReelForge.App.Views.Editing;

namespace ReelForge.App.Views.Dialogs;

public partial class AudioPlacementTrackDialog : Window
{
    public AudioPlacementTrackDialog(IReadOnlyList<CompositionTimelineTrackRow> tracks)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(tracks);
        TrackPicker.ItemsSource = tracks;
    }

    public Guid? SelectedTrackId { get; private set; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (TrackPicker.SelectedItem is not CompositionTimelineTrackRow track)
            return;
        SelectedTrackId = track.TrackId;
        DialogResult = true;
    }
}
