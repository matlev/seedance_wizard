using System.Windows.Controls;

namespace ReelForge.App.Views.MediaPreview;

internal sealed class CompositionAuditionAudioController(MediaElement player)
{
    private bool _isReady;
    private bool _isPriming;
    private bool _playAfterOpen;
    private double _pendingPosition;

    public bool IsReady => _isReady;
    public bool HasSource => player.Source is not null;
    public TimeSpan Position => player.Position;

    public void Open(string absolutePath, double startSeconds, double volume)
    {
        _isReady = false;
        _isPriming = false;
        _playAfterOpen = false;
        _pendingPosition = Math.Max(0, startSeconds);
        player.Stop();
        player.Close();
        player.Source = null;
        player.IsMuted = true;
        player.Volume = volume;
        player.Source = new Uri(absolutePath, UriKind.Absolute);
        player.Play();
    }

    public async Task HandleOpenedAsync(bool userMuted)
    {
        var openedSource = player.Source;
        _isPriming = true;
        player.IsMuted = true;
        player.Position = TimeSpan.FromSeconds(_pendingPosition);
        player.Play();
        await Task.Delay(50);
        if (player.Source != openedSource) return;

        player.Pause();
        player.Position = TimeSpan.FromSeconds(_pendingPosition);
        player.IsMuted = userMuted;
        _isPriming = false;
        _isReady = true;
        if (_playAfterOpen)
        {
            _playAfterOpen = false;
            player.Play();
        }
    }

    public void Sync(double globalSeconds, double maximumSeconds, bool play, bool userMuted)
    {
        if (player.Source is null) return;
        _pendingPosition = Math.Clamp(globalSeconds, 0, maximumSeconds);
        _playAfterOpen = play;
        if (!_isReady || _isPriming) return;
        player.Position = TimeSpan.FromSeconds(_pendingPosition);
        player.IsMuted = userMuted;
        if (play)
        {
            _playAfterOpen = false;
            player.Play();
        }
        else
        {
            player.Pause();
        }
    }

    public void Pause()
    {
        _playAfterOpen = false;
        if (player.Source is not null) player.Pause();
    }

    public void Stop()
    {
        _isReady = false;
        _isPriming = false;
        _playAfterOpen = false;
        _pendingPosition = 0;
        player.Stop();
        player.Close();
        player.Source = null;
    }

    public void SetVolume(double volume) => player.Volume = volume;

    public void SetMuted(bool muted) => player.IsMuted = muted;
}
