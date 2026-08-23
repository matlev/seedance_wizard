using System.Windows;
using ReelForge.App.Views.Editing;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.MediaPreview;

/// <summary>
/// Coordinates the shared transport surface with composition audition playback.
/// <para>
/// The preview panel remains responsible for MediaElement presentation and leases;
/// <see cref="CompositionAuditionController"/> remains responsible for switching
/// source segments. This class owns the bridge between those two state machines,
/// including the short-lived seek generations used to ignore stale transport events.
/// </para>
/// </summary>
internal sealed class MediaPreviewCoordinator : IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly RecipeMediaMaterializer _materializer;
    private readonly ExactVideoFrameService _exactFrames;
    private readonly MediaPreviewPanel _preview;
    private readonly CompositionTimelineControl _timeline;
    private readonly IPreviewCoordinatorHost _host;
    private readonly CompositionAuditionController _audition;
    private readonly SemaphoreSlim _frameNavigationGate = new(1, 1);
    private Guid? _activeCompositionPreviewRevisionId;
    private double? _pendingTimelineSeekSeconds;
    private long _timelineSeekGeneration;
    private long? _activeTimelineSeekGeneration;
    private long? _activePreviewScrubGeneration;
    private double _activeTimelineSeekSeconds;
    private bool _disposed;

    public MediaPreviewCoordinator(ProjectWorkspace workspace, RecipeMediaMaterializer materializer,
        ExactVideoFrameService exactFrames, MediaPreviewPanel preview, CompositionTimelineControl timeline,
        IPreviewCoordinatorHost host)
    {
        _workspace = workspace;
        _materializer = materializer;
        _exactFrames = exactFrames;
        _preview = preview;
        _timeline = timeline;
        _host = host;
        _audition = new CompositionAuditionController(workspace, materializer, preview);
        _audition.PositionChanged += Audition_PositionChanged;
        _preview.VideoReady += Preview_VideoReady;
        _preview.PlaybackEnded += Preview_PlaybackEnded;
        _preview.AuditionAudioFailed += Preview_AuditionAudioFailed;
        _preview.PlaybackRequested += Preview_PlaybackRequested;
        _preview.PreviousFrameRequested += Preview_PreviousFrameRequested;
        _preview.NextFrameRequested += Preview_NextFrameRequested;
        _preview.ScrubStarted += Preview_ScrubStarted;
        _preview.ScrubPositionChanged += Preview_ScrubPositionChanged;
        _preview.ScrubCompleted += Preview_ScrubCompleted;
        _preview.ScrubCancelled += Preview_ScrubCancelled;
        _preview.PositionTick += Preview_PositionTick;
        _timeline.ActivationRequested += Timeline_ActivationRequested;
        _timeline.SeekRequested += Timeline_SeekRequested;
    }

    public SemaphoreSlim FrameNavigationGate => _frameNavigationGate;
    public bool IsAuditionActive => _audition.IsActive;
    public Guid? AuditionRecipeRevisionId => _audition.RecipeRevisionId;
    public bool IsPreviewActive => _activeCompositionPreviewRevisionId is not null || _audition.IsActive;
    public bool IsPlaybackEnabled => _preview.HasVideoSource && !_preview.IsPriming && _preview.IsPlaybackEnabled;
    public bool IsPlaying => _preview.IsPlaying;
    public double CurrentTimelinePosition => _audition.GetCurrentTimelinePosition(_preview.PositionSeconds);

    public async Task<IDisposable> PauseAndQuiesceAsync(CancellationToken cancellationToken) =>
        await _audition.PauseAndQuiesceAsync(cancellationToken);

    public void Pause() => _preview.Pause();

    public void Clear()
    {
        _activeCompositionPreviewRevisionId = null;
        _audition.Reset();
        _preview.Reset();
        _timeline.CancelInteractions();
        ResetTimelineSeek();
        UpdateTimelinePlayback(0);
    }

    public void OpenVideo(string path, bool requiresWarmup, bool playAfterPriming = false,
        double startSeconds = 0, bool forceMuted = false) =>
        _preview.OpenVideo(path, requiresWarmup, playAfterPriming, startSeconds, forceMuted,
            useExternalTimeline: _audition.IsActive);

    public void OpenLeasedVideo(MaterializedMediaLease lease, bool requiresWarmup, Guid? compositionRevisionId = null)
    {
        _activeCompositionPreviewRevisionId = compositionRevisionId;
        _preview.HidePlaceholder();
        _preview.OpenLeasedVideo(lease, requiresWarmup);
        _host.PreviewStateChanged();
    }

    public void ClearStaleCompositionPreviewIfNeeded(ProjectAsset composition, RecipeRevision revision)
    {
        if (_activeCompositionPreviewRevisionId is not { } previewRevisionId || previewRevisionId == revision.Id)
            return;
        Clear();
        if (!_host.IsCompositionSelected(composition.Id)) return;
        _ = OpenCompositionDraftAsync(composition, revision, _workspace.Project?.Id);
    }

    public async Task OpenCompositionDraftAsync(ProjectAsset composition, RecipeRevision revision, Guid? projectId)
    {
        await _host.RunUiActionAsync("Preparing fast composition audition…", async () =>
        {
            Clear();
            var requestedPosition = _pendingTimelineSeekSeconds ?? 0;
            _pendingTimelineSeekSeconds = null;
            var result = await _audition.OpenAsync(composition, revision, requestedPosition,
                () => _workspace.Project?.Id == projectId && _host.IsCompositionSelected(composition.Id));
            if (result.IsStale) return;
            _host.SetStatus(result.AudioWarning is not null
                ? $"Fast composition audition ready without independent audio: {result.AudioWarning}"
                : result.HasAuditionAudio
                    ? "Fast composition audition ready with independent audio clips. Use Preview composition to verify final mix fidelity and render continuity."
                    : "Fast composition audition ready. Source video and source audio play at cuts; use Preview composition to verify the complete audio mix and final render.");
            _host.PreviewStateChanged();
        });
    }

    public void UpdateTimelinePlayback(double playbackSeconds) =>
        _timeline.UpdatePlayback(playbackSeconds, _preview.IsPlaying, IsPreviewActive, IsPlaybackEnabled);

    private void Timeline_ActivationRequested(object? sender, CompositionTimelineActivationEventArgs e)
    {
        _pendingTimelineSeekSeconds = e.PendingRulerSeekSeconds;
        _host.SelectWorkingComposition();
    }

    private async void Timeline_SeekRequested(object? sender, CompositionTimelineSeekEventArgs e)
    {
        switch (e.Phase)
        {
            case CompositionTimelineSeekPhase.Started:
                _activePreviewScrubGeneration = null;
                BeginTimelineSeek(e.Seconds);
                _preview.Pause();
                SeekTimeline(e.Seconds);
                break;
            case CompositionTimelineSeekPhase.Changed:
                EnsureTimelineSeek(e.Seconds);
                SeekTimeline(e.Seconds);
                break;
            case CompositionTimelineSeekPhase.Completed:
                var generation = EnsureTimelineSeek(e.Seconds);
                SeekTimeline(e.Seconds);
                try
                {
                    await CompleteTimelineScrubAsync(e.ResumePlayback);
                }
                finally
                {
                    CompleteTimelineSeek(generation);
                }
                break;
            case CompositionTimelineSeekPhase.Cancelled:
                _audition.CancelQueuedSeek();
                _preview.Pause();
                CompleteTimelineSeek(_activeTimelineSeekGeneration);
                break;
        }
    }

    private void SeekTimeline(double seconds)
    {
        if (!_preview.HasVideoSource) return;
        if (_audition.IsActive) _audition.QueueSeek(seconds); else SeekPreview(seconds);
        _preview.ClearEndedState();
        _preview.SetPosition(seconds);
        UpdateTimelinePlayback(seconds);
    }

    private async Task CompleteTimelineScrubAsync(bool resumePlayback)
    {
        if (_audition.IsActive)
        {
            await _audition.CommitQueuedSeekAsync(resumePlayback);
            return;
        }

        if (resumePlayback)
        {
            _preview.Play();
        }
        else
        {
            _preview.Pause();
        }
    }

    private long BeginTimelineSeek(double seconds)
    {
        var generation = ++_timelineSeekGeneration;
        _activeTimelineSeekGeneration = generation;
        _activeTimelineSeekSeconds = seconds;
        return generation;
    }

    private long EnsureTimelineSeek(double seconds)
    {
        _activeTimelineSeekSeconds = seconds;
        return _activeTimelineSeekGeneration ?? BeginTimelineSeek(seconds);
    }

    private void CompleteTimelineSeek(long? generation)
    {
        if (generation is not null && _activeTimelineSeekGeneration == generation)
        {
            _activeTimelineSeekGeneration = null;
        }

        if (generation is not null && _activePreviewScrubGeneration == generation)
        {
            _activePreviewScrubGeneration = null;
        }
    }

    private void ResetTimelineSeek()
    {
        _timelineSeekGeneration++;
        _activeTimelineSeekGeneration = null;
        _activePreviewScrubGeneration = null;
    }

    private void Preview_VideoReady(object? sender, MediaPreviewReadyEventArgs e)
    {
        _audition.OnVideoReady(e.ShouldPlay);
        UpdatePlaybackPosition();
    }

    private async void Preview_PlaybackEnded(object? sender, EventArgs e)
    {
        if (_activeTimelineSeekGeneration is not null || _audition.IsQuiesced)
        {
            return;
        }

        if (_audition.IsActive && await _audition.AdvanceAsync())
        {
            return;
        }
        CompletePlayback();
    }

    private void Preview_AuditionAudioFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _preview.StopAuditionAudio();
        _host.SetStatus($"Independent audio audition unavailable: {e.ErrorException?.Message ?? "media playback failed"}.");
    }

    private void Preview_PlaybackRequested(object? sender, EventArgs e)
    {
        if (!_preview.HasVideoSource) return;
        if (_preview.HasEnded || _preview.IsAtVideoEnd(TimeSpan.FromMilliseconds(10)))
        {
            if (_audition.IsActive)
            {
                _preview.ClearEndedState();
                _ = _audition.ReplayAsync();
                return;
            }
            _preview.ReopenForPlayback();
            return;
        }
        if (_preview.IsPlaying)
        {
            _preview.Pause();
            return;
        }
        if (_audition.IsActive) _audition.SynchronizeAudio(play: true);
        _preview.Play();
    }

    private async void Preview_PreviousFrameRequested(object? sender, EventArgs e) =>
        await StepFrameAsync(-1);

    private async void Preview_NextFrameRequested(object? sender, EventArgs e) =>
        await StepFrameAsync(1);

    private async Task StepFrameAsync(int direction)
    {
        if (direction is not (-1 or 1) ||
            _preview.LocalSourcePath is not { } sourcePath ||
            !await _frameNavigationGate.WaitAsync(0))
        {
            return;
        }
        try
        {
            _preview.Pause();
            _preview.SetFrameNavigationEnabled(false);
            var currentSeconds = _preview.PositionSeconds;
            var frames = await _exactFrames.IndexWindowAsync(sourcePath, currentSeconds, radiusSeconds: 2);
            var target = direction < 0 ? frames.Where(frame => frame.TimestampSeconds < currentSeconds - 0.0000001).OrderByDescending(frame => frame.TimestampSeconds).FirstOrDefault()
                : frames.Where(frame => frame.TimestampSeconds > currentSeconds + 0.0000001).OrderBy(frame => frame.TimestampSeconds).FirstOrDefault();
            if (target is null)
            {
                return;
            }

            if (_audition.IsActive)
            {
                await _audition.SeekAsync(_audition.MapSourcePositionToTimeline(target.TimestampSeconds), false);
            }
            else
            {
                SeekPreview(target.TimestampSeconds);
                _preview.SetPosition(target.TimestampSeconds);
            }
        }
        catch (Exception exception)
        {
            _host.ShowError("Frame navigation failed", exception);
        }
        finally
        {
            _preview.SetFrameNavigationEnabled(_preview.HasNaturalVideo);
            _frameNavigationGate.Release();
        }
    }

    private void Preview_ScrubStarted(object? sender, MediaPreviewPositionEventArgs e)
    {
        if (!_audition.IsActive) return;
        _activePreviewScrubGeneration = BeginTimelineSeek(e.PositionSeconds);
        _audition.CancelQueuedSeek();
    }

    private void Preview_ScrubPositionChanged(object? sender, MediaPreviewPositionEventArgs e)
    {
        if (!_audition.IsActive)
        {
            SeekPreview(e.PositionSeconds);
            return;
        }

        if (_activePreviewScrubGeneration is not { } generation ||
            _activeTimelineSeekGeneration != generation)
        {
            return;
        }
        _activeTimelineSeekSeconds = e.PositionSeconds;
        _audition.QueueSeek(e.PositionSeconds);
        UpdateTimelinePlayback(e.PositionSeconds);
    }

    private async void Preview_ScrubCompleted(object? sender, MediaPreviewScrubCompletedEventArgs e)
    {
        if (_audition.IsActive)
        {
            if (_activePreviewScrubGeneration is not { } generation || _activeTimelineSeekGeneration != generation)
            {
                _audition.CancelQueuedSeek();
                return;
            }
            _activeTimelineSeekSeconds = e.PositionSeconds;
            try
            {
                await _audition.CommitSeekAsync(e.PositionSeconds, e.ResumePlayback);
            }
            finally
            {
                CompleteTimelineSeek(generation);
            }
        }
        else SeekPreview(e.PositionSeconds);
        if (e.ResumePlayback && !_audition.IsActive) _preview.Play();
        else if (!_audition.IsActive) _preview.Pause();
        _host.ScheduleContactFrameRefresh(e.PositionSeconds);
    }

    private void Preview_ScrubCancelled(object? sender, EventArgs e)
    {
        if (_activePreviewScrubGeneration is not { } generation)
        {
            return;
        }
        _audition.CancelQueuedSeek();
        CompleteTimelineSeek(generation);
    }

    private void SeekPreview(double seconds)
    {
        if (!_preview.HasVideoSource)
        {
            return;
        }

        if (_audition.IsActive)
        {
            _ = _audition.SeekAsync(seconds, false);
            return;
        }
        _preview.SeekVideo(seconds);
        _preview.ShowPosition(_preview.MediaPosition, _preview.MediaDuration);
    }

    private void Audition_PositionChanged(object? sender, CompositionAuditionPositionChangedEventArgs e)
    {
        if (_activeTimelineSeekGeneration is null && !_audition.IsQuiesced) UpdateTimelinePlayback(e.PositionSeconds);
    }

    private void UpdatePlaybackPosition()
    {
        if (!_preview.HasVideoSource)
        {
            _preview.ShowPosition(TimeSpan.Zero, TimeSpan.Zero);
            return;
        }

        var mediaPosition = _preview.MediaPosition;
        var mediaDuration = _preview.MediaDuration;
        if (_audition.IsQuiesced)
        {
            _preview.ShowTimelinePosition(_audition.PositionSeconds);
            return;
        }

        if (_audition.IsActive && _activeTimelineSeekGeneration is not null)
        {
            _preview.ShowTimelinePosition(_activeTimelineSeekSeconds);
            return;
        }

        if (_preview.IsPlaying && _audition.IsActive &&
            _audition.HasReachedActiveSegmentEnd(mediaPosition.TotalSeconds))
        {
            _ = _audition.AdvanceAsync();
            return;
        }

        if (_preview.IsPlaying && _preview.IsAtVideoEnd(TimeSpan.FromMilliseconds(10)))
        {
            if (_audition.IsActive)
            {
                _ = _audition.AdvanceAsync();
            }
            else
            {
                CompletePlayback();
            }

            return;
        }
        if (_audition.IsActive)
        {
            var currentSeconds = _audition.UpdateFromSourcePosition(mediaPosition.TotalSeconds);
            if (!_preview.IsScrubbing) _preview.SetPosition(currentSeconds);
            _preview.ShowTimelinePosition(currentSeconds);
            return;
        }
        if (!_preview.IsScrubbing) _preview.SetPosition(mediaPosition.TotalSeconds);
        _preview.ShowPosition(mediaPosition, mediaDuration);
        // Ordinary Project Media playback deliberately does not move composition state.
    }

    private void CompletePlayback()
    {
        if (_audition.IsActive)
        {
            _audition.Complete();
        }
        _preview.MarkPlaybackEnded(resetVideoPosition: !_audition.IsActive);
        UpdatePlaybackPosition();
    }

    private void Preview_PositionTick(object? sender, EventArgs e) => UpdatePlaybackPosition();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audition.PositionChanged -= Audition_PositionChanged;
        _audition.Dispose();
        _preview.VideoReady -= Preview_VideoReady;
        _preview.PlaybackEnded -= Preview_PlaybackEnded;
        _preview.AuditionAudioFailed -= Preview_AuditionAudioFailed;
        _preview.PlaybackRequested -= Preview_PlaybackRequested;
        _preview.PreviousFrameRequested -= Preview_PreviousFrameRequested;
        _preview.NextFrameRequested -= Preview_NextFrameRequested;
        _preview.ScrubStarted -= Preview_ScrubStarted;
        _preview.ScrubPositionChanged -= Preview_ScrubPositionChanged;
        _preview.ScrubCompleted -= Preview_ScrubCompleted;
        _preview.ScrubCancelled -= Preview_ScrubCancelled;
        _preview.PositionTick -= Preview_PositionTick;
        _timeline.ActivationRequested -= Timeline_ActivationRequested;
        _timeline.SeekRequested -= Timeline_SeekRequested;
        // Frame preparation can still be unwinding an asynchronous wait during window disposal.
        // Leaving this private semaphore for GC avoids disposing a synchronization primitive in use.
    }
}

internal interface IPreviewCoordinatorHost
{
    Task RunUiActionAsync(string status, Func<Task> action);
    void SetStatus(string status);
    void ShowError(string title, Exception exception);
    bool IsCompositionSelected(Guid compositionId);
    void SelectWorkingComposition();
    void ScheduleContactFrameRefresh(double seconds);
    void PreviewStateChanged();
}
