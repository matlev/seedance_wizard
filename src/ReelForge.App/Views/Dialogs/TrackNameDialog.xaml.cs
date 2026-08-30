using System.Windows;
using ReelForge.App.Views.Editing;
using ReelForge.Core;

namespace ReelForge.App.Views.Dialogs;

public partial class TrackNameDialog : Window
{
    public TrackNameDialog(string currentName, CompositionTimelineTrackKind kind)
    {
        InitializeComponent();
        HeadingText.Text = $"RENAME {kind.ToString().ToUpperInvariant()} TRACK";
        TrackNameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            TrackNameTextBox.Focus();
            TrackNameTextBox.SelectAll();
        };
    }

    public string TrackName => TrackNameTextBox.Text.Trim();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = CompositionTrackName.Normalize(TrackNameTextBox.Text);
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            ValidationText.Text = exception.Message;
            ValidationText.Visibility = Visibility.Visible;
        }
    }
}
