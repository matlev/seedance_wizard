using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ReelForge.App.Views.MediaPreview;

public partial class MediaPreviewPanel : UserControl
{
    public MediaPreviewPanel()
    {
        InitializeComponent();
        AuditionAudio = new CompositionAuditionAudioController(CompositionAuditionAudio);
    }

    internal CompositionAuditionAudioController AuditionAudio { get; }
    internal MediaElement VideoElement => VideoPreview;
    internal Border ControlsElement => PlaybackControlsBorder;
    internal Button PlaybackButtonElement => PlaybackButton;
    internal Button PreviousFrameButtonElement => PreviousFrameButton;
    internal Button NextFrameButtonElement => NextFrameButton;
    internal Slider PositionSliderElement => PositionSlider;
    internal TextBlock TimeTextElement => TimeText;
    internal Button MuteButtonElement => MuteButton;
    internal Slider VolumeSliderElement => VolumeSlider;

    public event EventHandler<RoutedEventArgs>? VideoOpened;
    public event EventHandler<RoutedEventArgs>? VideoEnded;
    public event EventHandler<RoutedEventArgs>? AuditionAudioOpened;
    public event EventHandler<ExceptionRoutedEventArgs>? AuditionAudioFailed;
    public event EventHandler<RoutedEventArgs>? PlaybackRequested;
    public event EventHandler<RoutedEventArgs>? PreviousFrameRequested;
    public event EventHandler<RoutedEventArgs>? NextFrameRequested;
    public event EventHandler<MouseButtonEventArgs>? ScrubStarted;
    public event EventHandler<MouseEventArgs>? ScrubMoved;
    public event EventHandler<MouseButtonEventArgs>? ScrubCompleted;
    public event EventHandler<RoutedPropertyChangedEventArgs<double>>? PositionChanged;
    public event EventHandler<RoutedPropertyChangedEventArgs<double>>? VolumeChanged;
    public event EventHandler<RoutedEventArgs>? MuteRequested;

    public void ShowImage(BitmapSource image)
    {
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        ImagePreview.Source = image;
        ImagePreview.Visibility = Visibility.Visible;
    }

    public void SetPlaybackState(bool isPlaying)
    {
        PlayGlyph.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
        PlaybackButton.ToolTip = isPlaying ? "Pause preview" : "Play preview";
    }

    public void ShowPlaceholder(string text, TextAlignment alignment = TextAlignment.Center)
    {
        PreviewPlaceholder.Text = text;
        PreviewPlaceholder.TextAlignment = alignment;
        PreviewPlaceholder.Visibility = Visibility.Visible;
    }

    public void HidePlaceholder() => PreviewPlaceholder.Visibility = Visibility.Collapsed;

    public void ResetVisuals()
    {
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        ShowPlaceholder("Select a video or image asset to preview");
    }

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e) => VideoOpened?.Invoke(this, e);
    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e) => VideoEnded?.Invoke(this, e);
    private void CompositionAuditionAudio_MediaOpened(object sender, RoutedEventArgs e) => AuditionAudioOpened?.Invoke(this, e);
    private void CompositionAuditionAudio_MediaFailed(object sender, ExceptionRoutedEventArgs e) => AuditionAudioFailed?.Invoke(this, e);
    private void PlaybackButton_Click(object sender, RoutedEventArgs e) => PlaybackRequested?.Invoke(this, e);
    private void PreviousFrameButton_Click(object sender, RoutedEventArgs e) => PreviousFrameRequested?.Invoke(this, e);
    private void NextFrameButton_Click(object sender, RoutedEventArgs e) => NextFrameRequested?.Invoke(this, e);
    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ScrubStarted?.Invoke(this, e);
    private void PositionSlider_PreviewMouseMove(object sender, MouseEventArgs e) => ScrubMoved?.Invoke(this, e);
    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ScrubCompleted?.Invoke(this, e);
    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PositionChanged?.Invoke(this, e);
    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => VolumeChanged?.Invoke(this, e);
    private void MuteButton_Click(object sender, RoutedEventArgs e) => MuteRequested?.Invoke(this, e);
}
