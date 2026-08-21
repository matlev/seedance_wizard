using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ReelForge.App;

public partial class FrameConfirmationDialog : Window
{
    public FrameConfirmationDialog(
        ImageSource image,
        string heading,
        string sourceName,
        double timestampSeconds,
        long presentationTimestamp,
        int timeBaseNumerator,
        int timeBaseDenominator)
    {
        InitializeComponent();
        FrameImage.Source = image;
        HeadingText.Text = heading;
        DetailsText.Text =
            $"Source: {sourceName}\n" +
            $"Position: {TimeSpan.FromSeconds(Math.Max(0, timestampSeconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)}\n" +
            $"Exact media position: PTS {presentationTimestamp} at time base {timeBaseNumerator}/{timeBaseDenominator}\n\n" +
            "This exact immutable frame revision will be frozen into the generation draft.";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
