using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReelForge.Application;

namespace ReelForge.App.Views.MediaPreview;

public partial class MediaPreviewPanel : UserControl, IDisposable
{
    private readonly CompositionAuditionAudioController _auditionAudio;
    private readonly MediaPreviewLeaseOwner _leases = new();
    private readonly DispatcherTimer _positionTimer;
    private bool _isPriming;
    private bool _requiresWarmup;
    private bool _playAfterPriming;
    private bool _hasEnded;
    private bool _isScrubbing;
    private bool _resumeAfterScrub;
    private bool _useExternalTimeline;
    private bool _forcedMuted;
    private bool _userMuted;
    private bool _mutedBeforePrecisionMode;
    private bool _inPrecisionMode;
    private bool _isInitialized;
    private double _pendingStartSeconds;
    private double _volumeBeforeMute = 1;
    private bool _disposed;

    public MediaPreviewPanel()
    {
        InitializeComponent();
        _auditionAudio = new CompositionAuditionAudioController(CompositionAuditionAudio);
        _isInitialized = true;
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => PositionTick?.Invoke(this, EventArgs.Empty);
        Loaded += (_, _) => _positionTimer.Start();
        Unloaded += (_, _) => _positionTimer.Stop();
    }

    public bool IsPlaying { get; private set; }
    public bool IsPriming => _isPriming;
    public bool HasEnded => _hasEnded;
    public bool IsScrubbing => _isScrubbing;
    public bool HasVideoSource => VideoPreview.Source is not null;
    public bool IsPlaybackEnabled => PlaybackButton.IsEnabled;
    public bool HasNaturalVideo => VideoPreview.NaturalVideoWidth > 0 && VideoPreview.NaturalVideoHeight > 0;
    public bool IsAuditionAudioReady => _auditionAudio.IsReady;
    public bool IsVideoMuted => VideoPreview.IsMuted;
    public double PositionSeconds => VideoPreview.Position.TotalSeconds;
    public double MaximumPositionSeconds => PositionSlider.Maximum;
    public double AuditionAudioPositionSeconds => _auditionAudio.Position.TotalSeconds;
    public string? LocalSourcePath => VideoPreview.Source is { IsFile: true } source ? source.LocalPath : null;
    public TimeSpan MediaPosition => VideoPreview.Position;
    public TimeSpan MediaDuration => VideoPreview.NaturalDuration.HasTimeSpan
        ? VideoPreview.NaturalDuration.TimeSpan
        : TimeSpan.Zero;

    public event EventHandler<MediaPreviewReadyEventArgs>? VideoReady;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<ExceptionRoutedEventArgs>? AuditionAudioFailed;
    public event EventHandler? PlaybackRequested;
    public event EventHandler? PreviousFrameRequested;
    public event EventHandler? NextFrameRequested;
    public event EventHandler<MediaPreviewPositionEventArgs>? ScrubPositionChanged;
    public event EventHandler<MediaPreviewScrubCompletedEventArgs>? ScrubCompleted;
    public event EventHandler? PositionTick;

    public void OpenVideo(
        string absolutePath,
        bool requiresWarmup,
        bool playAfterPriming = false,
        double startSeconds = 0,
        bool forceMuted = false,
        bool useExternalTimeline = false)
    {
        CloseVideoSource();
        _leases.ReleaseVideo();
        OpenVideoCore(
            absolutePath,
            requiresWarmup,
            playAfterPriming,
            startSeconds,
            forceMuted,
            useExternalTimeline);
    }

    private void OpenVideoCore(
        string absolutePath,
        bool requiresWarmup,
        bool playAfterPriming,
        double startSeconds,
        bool forceMuted,
        bool useExternalTimeline)
    {
        _forcedMuted = forceMuted;
        _requiresWarmup = requiresWarmup;
        _playAfterPriming = playAfterPriming;
        _pendingStartSeconds = Math.Max(0, startSeconds);
        _useExternalTimeline = useExternalTimeline;
        _hasEnded = false;
        VideoPreview.IsMuted = true;
        VideoPreview.Source = new Uri(absolutePath, UriKind.Absolute);
        VideoPreview.Visibility = Visibility.Visible;
        PlaybackControlsBorder.Visibility = Visibility.Visible;
        PlaybackButton.IsEnabled = false;
        UpdateAudioControls();
        VideoPreview.Play();
    }

    public void Play()
    {
        if (!HasVideoSource) return;
        VideoPreview.Play();
        SetPlaying(true);
    }

    public void Pause(bool includeAuditionAudio = true)
    {
        VideoPreview.Pause();
        if (includeAuditionAudio) _auditionAudio.Pause();
        SetPlaying(false);
    }

    public void SeekVideo(double seconds)
    {
        if (!HasVideoSource) return;
        VideoPreview.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, PositionSlider.Maximum));
    }

    public void SetPosition(double seconds)
    {
        PositionSlider.Value = Math.Clamp(seconds, PositionSlider.Minimum, PositionSlider.Maximum);
    }

    public void SetTimelineRange(double minimumSeconds, double maximumSeconds)
    {
        PositionSlider.Minimum = minimumSeconds;
        PositionSlider.Maximum = Math.Max(minimumSeconds, maximumSeconds);
        PositionSlider.Value = Math.Clamp(PositionSlider.Value, PositionSlider.Minimum, PositionSlider.Maximum);
    }

    public void ShowPosition(TimeSpan position, TimeSpan duration) =>
        TimeText.Text = $"{FormatTime(position)} / {FormatTime(duration)}";

    public void ShowTimelinePosition(double positionSeconds) =>
        ShowPosition(TimeSpan.FromSeconds(positionSeconds), TimeSpan.FromSeconds(PositionSlider.Maximum));

    public bool IsAtVideoEnd(TimeSpan tolerance)
    {
        var duration = MediaDuration;
        return duration > TimeSpan.Zero && VideoPreview.Position >= duration - tolerance;
    }

    public void MarkPlaybackEnded(bool resetVideoPosition)
    {
        Pause();
        if (resetVideoPosition) VideoPreview.Position = TimeSpan.Zero;
        _hasEnded = true;
        UpdatePositionDisplay();
    }

    public void ClearEndedState() => _hasEnded = false;

    public bool ReopenForPlayback()
    {
        if (LocalSourcePath is not { } path) return false;
        var requiresWarmup = _requiresWarmup;
        var forceMuted = _forcedMuted;
        VideoPreview.Stop();
        VideoPreview.Close();
        VideoPreview.Source = null;
        OpenVideoCore(
            path,
            requiresWarmup,
            playAfterPriming: true,
            startSeconds: 0,
            forceMuted: forceMuted,
            useExternalTimeline: false);
        return true;
    }

    public void SetFrameNavigationEnabled(bool enabled)
    {
        PreviousFrameButton.IsEnabled = enabled;
        NextFrameButton.IsEnabled = enabled;
    }

    private void OpenAuditionAudio(string absolutePath, double startSeconds)
    {
        _auditionAudio.Open(absolutePath, startSeconds, VolumeSlider.Value);
        UpdateAudioControls();
    }

    public void SyncAuditionAudio(double globalSeconds, bool play) =>
        _auditionAudio.Sync(globalSeconds, PositionSlider.Maximum, play, _userMuted);

    public void PauseAuditionAudio() => _auditionAudio.Pause();

    public void StopAuditionAudio()
    {
        _auditionAudio.Stop();
        _leases.ReleaseAuditionAudio();
        HasIndependentAudio = false;
        UpdateAudioControls();
    }

    private void SetIndependentAudioAvailable(bool available)
    {
        HasIndependentAudio = available;
        UpdateAudioControls();
    }

    public bool HasIndependentAudio { get; private set; }

    public bool HasAuditionAudioLease => _leases.HasAuditionAudio;

    public void OpenLeasedVideo(
        MaterializedMediaLease lease,
        bool requiresWarmup,
        bool playAfterPriming = false,
        double startSeconds = 0,
        bool forceMuted = false,
        bool useExternalTimeline = false)
    {
        CloseVideoSource();
        var path = _leases.AdoptVideo(lease);
        OpenVideoCore(path, requiresWarmup, playAfterPriming, startSeconds, forceMuted, useExternalTimeline);
    }

    public void OpenLeasedAuditionAudio(MaterializedMediaLease lease, double startSeconds)
    {
        StopAuditionAudio();
        var path = _leases.AdoptAuditionAudio(lease);
        SetIndependentAudioAvailable(true);
        OpenAuditionAudio(path, startSeconds);
    }

    public void EnterPrecisionMode()
    {
        if (!_inPrecisionMode) _mutedBeforePrecisionMode = VideoPreview.IsMuted;
        _inPrecisionMode = true;
        VideoPreview.IsMuted = true;
        MuteButton.IsEnabled = false;
        MuteButton.Content = "Muted";
        MuteButton.ToolTip = "Precision frame navigation is silent";
        VolumeSlider.IsEnabled = false;
    }

    public void ExitPrecisionMode()
    {
        if (!_inPrecisionMode) return;
        _inPrecisionMode = false;
        VideoPreview.IsMuted = _mutedBeforePrecisionMode || VolumeSlider.Value <= 0;
        UpdateAudioControls();
    }

    public void ShowImage(BitmapSource image)
    {
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        ImagePreview.Source = image;
        ImagePreview.Visibility = Visibility.Visible;
    }

    public void ShowPlaceholder(string text, TextAlignment alignment = TextAlignment.Center)
    {
        PreviewPlaceholder.Text = text;
        PreviewPlaceholder.TextAlignment = alignment;
        PreviewPlaceholder.Visibility = Visibility.Visible;
    }

    public void HidePlaceholder() => PreviewPlaceholder.Visibility = Visibility.Collapsed;

    public void Reset()
    {
        _isPriming = false;
        _playAfterPriming = false;
        _hasEnded = false;
        _isScrubbing = false;
        _resumeAfterScrub = false;
        _forcedMuted = false;
        _useExternalTimeline = false;
        if (Mouse.Captured == PositionSlider) Mouse.Capture(null);
        CloseVideoSource();
        _auditionAudio.Stop();
        _leases.ReleaseAll();
        HasIndependentAudio = false;
        VideoPreview.Visibility = Visibility.Collapsed;
        PlaybackControlsBorder.Visibility = Visibility.Collapsed;
        PlaybackButton.IsEnabled = false;
        SetFrameNavigationEnabled(false);
        SetPlaying(false);
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        ShowPlaceholder("Select a video or image asset to preview");
        SetTimelineRange(0, 1);
        PositionSlider.Value = 0;
        ShowPosition(TimeSpan.Zero, TimeSpan.Zero);
        UpdateAudioControls();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _positionTimer.Stop();
        CloseVideoSource();
        _auditionAudio.Stop();
        _leases.Dispose();
        GC.SuppressFinalize(this);
    }

    public void UpdatePositionDisplay()
    {
        if (!HasVideoSource)
        {
            ShowPosition(TimeSpan.Zero, TimeSpan.Zero);
            return;
        }
        if (!_isScrubbing) SetPosition(PositionSeconds);
        ShowPosition(MediaPosition, MediaDuration);
    }

    private async void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!_useExternalTimeline && VideoPreview.NaturalDuration.HasTimeSpan)
            SetTimelineRange(0, VideoPreview.NaturalDuration.TimeSpan.TotalSeconds);

        var openedSource = VideoPreview.Source;
        _isPriming = true;
        PlaybackButton.IsEnabled = false;
        VideoPreview.IsMuted = true;
        VideoPreview.Position = TimeSpan.FromSeconds(_pendingStartSeconds);
        VideoPreview.Play();
        if (_requiresWarmup) await Task.Delay(100);
        if (VideoPreview.Source != openedSource) return;

        VideoPreview.Pause();
        VideoPreview.Position = TimeSpan.FromSeconds(_pendingStartSeconds);
        VideoPreview.IsMuted = _forcedMuted || _userMuted || VolumeSlider.Value <= 0;
        _isPriming = false;
        PlaybackButton.IsEnabled = true;
        SetFrameNavigationEnabled(HasNaturalVideo);
        var shouldPlay = _playAfterPriming;
        _playAfterPriming = false;
        if (shouldPlay) VideoPreview.Play();
        SetPlaying(shouldPlay);
        UpdatePositionDisplay();
        VideoReady?.Invoke(this, new MediaPreviewReadyEventArgs(shouldPlay));
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (!_isPriming) PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private async void CompositionAuditionAudio_MediaOpened(object sender, RoutedEventArgs e)
    {
        await _auditionAudio.HandleOpenedAsync(_userMuted);
        UpdateAudioControls();
    }

    private void CompositionAuditionAudio_MediaFailed(object sender, ExceptionRoutedEventArgs e) =>
        AuditionAudioFailed?.Invoke(this, e);

    private void PlaybackButton_Click(object sender, RoutedEventArgs e) =>
        PlaybackRequested?.Invoke(this, EventArgs.Empty);

    private void PreviousFrameButton_Click(object sender, RoutedEventArgs e) =>
        PreviousFrameRequested?.Invoke(this, EventArgs.Empty);

    private void NextFrameButton_Click(object sender, RoutedEventArgs e) =>
        NextFrameRequested?.Invoke(this, EventArgs.Empty);

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!HasVideoSource) return;
        _resumeAfterScrub = IsPlaying;
        _isScrubbing = true;
        Pause();
        PositionSlider.CaptureMouse();
        UpdateScrubPosition(e);
        e.Handled = true;
    }

    private void PositionSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScrubbing || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateScrubPosition(e);
        e.Handled = true;
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!HasVideoSource || !_isScrubbing) return;
        UpdateScrubPosition(e);
        var target = PositionSlider.Value;
        var resume = _resumeAfterScrub;
        _isScrubbing = false;
        _resumeAfterScrub = false;
        if (Mouse.Captured == PositionSlider) Mouse.Capture(null);
        ScrubCompleted?.Invoke(this, new MediaPreviewScrubCompletedEventArgs(target, resume));
        e.Handled = true;
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isScrubbing)
            ScrubPositionChanged?.Invoke(this, new MediaPreviewPositionEventArgs(e.NewValue));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        VideoPreview.Volume = e.NewValue;
        _auditionAudio.SetVolume(e.NewValue);
        _userMuted = e.NewValue <= 0;
        VideoPreview.IsMuted = _forcedMuted || _userMuted || _inPrecisionMode;
        _auditionAudio.SetMuted(_userMuted);
        if (e.NewValue > 0) _volumeBeforeMute = e.NewValue;
        UpdateAudioControls();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forcedMuted && !HasIndependentAudio) return;
        if (_userMuted || VolumeSlider.Value <= 0)
        {
            VolumeSlider.Value = _volumeBeforeMute > 0 ? _volumeBeforeMute : 1;
            _userMuted = false;
            VideoPreview.IsMuted = _forcedMuted || _inPrecisionMode;
            _auditionAudio.SetMuted(false);
            UpdateAudioControls();
            return;
        }

        _volumeBeforeMute = VolumeSlider.Value;
        _userMuted = true;
        VideoPreview.IsMuted = true;
        _auditionAudio.SetMuted(true);
        UpdateAudioControls();
    }

    private void UpdateScrubPosition(MouseEventArgs e)
    {
        if (PositionSlider.ActualWidth <= 0) return;
        var pointer = e.GetPosition(PositionSlider);
        var fraction = Math.Clamp(pointer.X / PositionSlider.ActualWidth, 0, 1);
        PositionSlider.Value = PositionSlider.Minimum +
                               fraction * (PositionSlider.Maximum - PositionSlider.Minimum);
    }

    private void UpdateAudioControls()
    {
        if (_inPrecisionMode) return;
        var canAdjustAudio = !_forcedMuted || HasIndependentAudio;
        MuteButton.IsEnabled = canAdjustAudio;
        VolumeSlider.IsEnabled = canAdjustAudio;
        MuteButton.Content = !canAdjustAudio ? "Muted" : _userMuted ? "Unmute" : "Mute";
        MuteButton.ToolTip = !canAdjustAudio
            ? "Source audio is muted for this composition segment"
            : _forcedMuted
                ? "Mute or unmute the independent composition audio"
                : "Mute or unmute preview audio";
    }

    private void SetPlaying(bool isPlaying)
    {
        IsPlaying = isPlaying;
        PlayGlyph.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
        PlaybackButton.ToolTip = isPlaying ? "Pause preview" : "Play preview";
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");

    private void CloseVideoSource()
    {
        VideoPreview.Stop();
        VideoPreview.Close();
        VideoPreview.Source = null;
    }
}

public sealed class MediaPreviewReadyEventArgs(bool shouldPlay) : EventArgs
{
    public bool ShouldPlay { get; } = shouldPlay;
}

public sealed class MediaPreviewPositionEventArgs(double positionSeconds) : EventArgs
{
    public double PositionSeconds { get; } = positionSeconds;
}

public sealed class MediaPreviewScrubCompletedEventArgs(double positionSeconds, bool resumePlayback) : EventArgs
{
    public double PositionSeconds { get; } = positionSeconds;
    public bool ResumePlayback { get; } = resumePlayback;
}
