using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReelForge.App.Views.Editing;

public sealed record VideoSegmentEditState(
    string DisplayName,
    string SourceDescription,
    string TimingDescription,
    bool AudioEnabled);

public sealed record AudioClipEditState(
    string DisplayName,
    string TimingDescription,
    bool IsMuted,
    double GainDecibels,
    double Pan,
    TimeSpan FadeIn,
    TimeSpan FadeOut,
    double MaximumFadeSeconds);

public sealed class BooleanValueEventArgs(bool value) : EventArgs
{
    public bool Value { get; } = value;
}

public sealed class DoubleValueEventArgs(double value) : EventArgs
{
    public double Value { get; } = value;
}

public sealed class AudioFadesEventArgs(TimeSpan fadeIn, TimeSpan fadeOut) : EventArgs
{
    public TimeSpan FadeIn { get; } = fadeIn;
    public TimeSpan FadeOut { get; } = fadeOut;
}

public partial class EditToolsPanel : UserControl
{
    private bool _updatingSelection;

    public EditToolsPanel()
    {
        InitializeComponent();
        ShowSelection(video: null, audio: null);
    }

    public event EventHandler<BooleanValueEventArgs>? SegmentAudioChanged;
    public event EventHandler<BooleanValueEventArgs>? AudioClipMutedChanged;
    public event EventHandler<DoubleValueEventArgs>? AudioClipGainCommitted;
    public event EventHandler<DoubleValueEventArgs>? AudioClipPanCommitted;
    public event EventHandler<AudioFadesEventArgs>? AudioClipFadesCommitted;

    public void ShowSelection(VideoSegmentEditState? video, AudioClipEditState? audio)
    {
        _updatingSelection = true;
        try
        {
            EmptyState.Visibility = video is null && audio is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            VideoSegmentTools.Visibility = video is null ? Visibility.Collapsed : Visibility.Visible;
            AudioClipTools.Visibility = audio is null ? Visibility.Collapsed : Visibility.Visible;

            if (video is not null)
            {
                VideoSegmentNameText.Text = video.DisplayName;
                VideoSegmentSourceText.Text = video.SourceDescription;
                VideoSegmentTimingText.Text = video.TimingDescription;
            }

            SegmentAudioOnButton.IsChecked = video?.AudioEnabled == true;
            SegmentAudioMutedButton.IsChecked = video is { AudioEnabled: false };

            if (audio is not null)
            {
                AudioClipNameText.Text = audio.DisplayName;
                AudioClipTimingText.Text = audio.TimingDescription;
            }

            AudioClipEnabledButton.IsChecked = audio is { IsMuted: false };
            AudioClipMutedButton.IsChecked = audio?.IsMuted == true;
            AudioClipGainSlider.Value = audio?.GainDecibels ?? 0;
            AudioClipGainSlider.IsEnabled = audio is not null;
            AudioClipGainText.Text = FormatGainDecibels(audio?.GainDecibels ?? 0);
            AudioClipPanSlider.Value = audio?.Pan ?? 0;
            AudioClipPanSlider.IsEnabled = audio is not null;
            AudioClipPanText.Text = FormatAudioPan(audio?.Pan ?? 0);

            var maximumFadeSeconds = audio?.MaximumFadeSeconds ?? 0;
            AudioClipFadeInSlider.Maximum = Math.Max(maximumFadeSeconds, audio?.FadeIn.TotalSeconds ?? 0);
            AudioClipFadeOutSlider.Maximum = Math.Max(maximumFadeSeconds, audio?.FadeOut.TotalSeconds ?? 0);
            AudioClipFadeInSlider.Value = audio?.FadeIn.TotalSeconds ?? 0;
            AudioClipFadeOutSlider.Value = audio?.FadeOut.TotalSeconds ?? 0;
            AudioClipFadeInSlider.IsEnabled = audio is not null && maximumFadeSeconds > 0;
            AudioClipFadeOutSlider.IsEnabled = audio is not null && maximumFadeSeconds > 0;
            AudioClipFadeInText.Text = FormatFadeDuration(audio?.FadeIn.TotalSeconds ?? 0);
            AudioClipFadeOutText.Text = FormatFadeDuration(audio?.FadeOut.TotalSeconds ?? 0);
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void SegmentAudio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingSelection)
            SegmentAudioChanged?.Invoke(this, new BooleanValueEventArgs(SegmentAudioOnButton.IsChecked == true));
    }

    private void AudioClipEnabled_Checked(object sender, RoutedEventArgs e)
    {
        if (!_updatingSelection)
            AudioClipMutedChanged?.Invoke(this, new BooleanValueEventArgs(AudioClipMutedButton.IsChecked == true));
    }

    private void AudioClipGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AudioClipGainText is not null) AudioClipGainText.Text = FormatGainDecibels(e.NewValue);
    }

    private void AudioClipGainSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CommitGain();

    private void AudioClipGainSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (IsCommitKey(e.Key)) CommitGain();
    }

    private void AudioClipPanSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AudioClipPanText is not null) AudioClipPanText.Text = FormatAudioPan(e.NewValue);
    }

    private void AudioClipPanSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CommitPan();

    private void AudioClipPanSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (IsCommitKey(e.Key)) CommitPan();
    }

    private void AudioClipFadeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AudioClipFadeInText is null || AudioClipFadeOutText is null) return;
        AudioClipFadeInText.Text = FormatFadeDuration(AudioClipFadeInSlider.Value);
        AudioClipFadeOutText.Text = FormatFadeDuration(AudioClipFadeOutSlider.Value);
    }

    private void AudioClipFadeSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CommitFades();

    private void AudioClipFadeSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (IsCommitKey(e.Key)) CommitFades();
    }

    private void CommitGain()
    {
        if (!_updatingSelection)
            AudioClipGainCommitted?.Invoke(this, new DoubleValueEventArgs(AudioClipGainSlider.Value));
    }

    private void CommitPan()
    {
        if (!_updatingSelection)
        {
            var pan = Math.Round(AudioClipPanSlider.Value, 2, MidpointRounding.AwayFromZero);
            AudioClipPanCommitted?.Invoke(this, new DoubleValueEventArgs(pan));
        }
    }

    private void CommitFades()
    {
        if (_updatingSelection) return;
        var fadeIn = TimeSpan.FromMilliseconds(Math.Round(
            AudioClipFadeInSlider.Value * 1000,
            MidpointRounding.AwayFromZero));
        var fadeOut = TimeSpan.FromMilliseconds(Math.Round(
            AudioClipFadeOutSlider.Value * 1000,
            MidpointRounding.AwayFromZero));
        AudioClipFadesCommitted?.Invoke(this, new AudioFadesEventArgs(fadeIn, fadeOut));
    }

    private static bool IsCommitKey(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    public static string FormatGainDecibels(double gainDecibels) =>
        $"{(gainDecibels > 0 ? "+" : string.Empty)}{gainDecibels:0} dB";

    public static string FormatAudioPan(double pan)
    {
        if (Math.Abs(pan) < 0.000_001) return "Center";
        return $"{Math.Round(Math.Abs(pan) * 100):0}% {(pan < 0 ? "left" : "right")}";
    }

    public static string FormatFadeDuration(double seconds) =>
        $"{Math.Max(0, seconds):0.###}s";
}
