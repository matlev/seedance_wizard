using System.IO;
using ReelForge.App.Views.MediaPreview;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Editing;

public sealed class CompositionAuditionController : IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly RecipeMediaMaterializer _materializer;
    private readonly MediaPreviewPanel _preview;
    private CompositionAuditionSession? _session;
    private readonly SemaphoreSlim _segmentOpenGate = new(1, 1);
    private CancellationTokenSource? _queuedSeekCancellation;
    private double? _queuedSeekSeconds;
    private int _openVersion;
    private bool _advancing;
    private bool _disposed;

    public CompositionAuditionController(
        ProjectWorkspace workspace,
        RecipeMediaMaterializer materializer,
        MediaPreviewPanel preview)
    {
        _workspace = workspace;
        _materializer = materializer;
        _preview = preview;
    }

    public bool IsActive => _session is not null;
    public Guid? RecipeRevisionId => _session?.RecipeRevisionId;
    public double PositionSeconds => _session?.PositionSeconds ?? 0;
    public double DurationSeconds => _session?.Plan.DurationSeconds ?? 0;

    public event EventHandler<CompositionAuditionPositionChangedEventArgs>? PositionChanged;

    public async Task<CompositionAuditionOpenResult> OpenAsync(
        ProjectAsset composition,
        RecipeRevision revision,
        double requestedPosition,
        Func<bool> isStillCurrent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(isStillCurrent);
        if (_workspace.Project is null || _workspace.Location is null)
            return CompositionAuditionOpenResult.Stale;

        var recipe = revision.Recipe as CompositionRecipe
            ?? throw new InvalidDataException("The Working Composition recipe is invalid.");
        var plan = CompositionAuditionPlan.Create(_workspace.Project, recipe);
        MaterializedMediaLease? auditionAudio = null;
        string? auditionAudioWarning = null;
        try
        {
            try
            {
                auditionAudio = await _materializer.MaterializeCompositionAuditionAudioAsync(
                    _workspace.Project,
                    _workspace.Location,
                    composition.Id,
                    revision.Id,
                    plan.DurationSeconds,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                auditionAudioWarning = exception.Message;
            }

            if (!isStillCurrent()) return CompositionAuditionOpenResult.Stale;

            _session = new CompositionAuditionSession(revision.Id, plan, requestedPosition);
            _preview.SetTimelineRange(0, plan.DurationSeconds);
            if (auditionAudio is not null)
            {
                _preview.OpenLeasedAuditionAudio(auditionAudio, _session.PositionSeconds);
                auditionAudio = null;
            }
            await OpenSegmentAsync(
                _session.ActiveSegmentIndex,
                _session.PositionSeconds,
                playAfterOpen: false,
                cancellationToken);
            return new CompositionAuditionOpenResult(
                IsStale: false,
                auditionAudioWarning,
                _preview.HasAuditionAudioLease);
        }
        finally
        {
            if (auditionAudio is not null) await auditionAudio.DisposeAsync();
        }
    }

    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        if (_advancing || _session is null) return false;
        if (!_session.Plan.TryGetNextSegmentIndex(_session.ActiveSegmentIndex, out var nextIndex))
            return false;
        _advancing = true;
        try
        {
            var next = _session.Plan.Segments[nextIndex];
            await OpenSegmentAsync(
                nextIndex,
                next.TimelineStartSeconds,
                playAfterOpen: true,
                cancellationToken);
            return true;
        }
        finally
        {
            _advancing = false;
        }
    }

    public Task ReplayAsync(CancellationToken cancellationToken = default) =>
        _session is null
            ? Task.CompletedTask
            : OpenSegmentAsync(0, 0, playAfterOpen: true, cancellationToken);

    public void QueueSeek(double globalSeconds, TimeSpan? delay = null)
    {
        if (_disposed || _session is null) return;
        CancelQueuedSeek();
        _queuedSeekSeconds = _session.Plan.ClampGlobalPosition(globalSeconds);
        _queuedSeekCancellation = new CancellationTokenSource();
        _ = RunQueuedSeekAsync(
            _queuedSeekSeconds.Value,
            delay ?? TimeSpan.FromMilliseconds(120),
            _queuedSeekCancellation.Token);
    }

    public async Task CommitQueuedSeekAsync(bool playAfterSeek)
    {
        if (_session is null) return;
        var target = _queuedSeekSeconds ?? _session.PositionSeconds;
        CancelQueuedSeek();
        await SeekCoreAsync(target, playAfterSeek, CancellationToken.None);
    }

    public async Task CommitSeekAsync(double globalSeconds, bool playAfterSeek)
    {
        CancelQueuedSeek();
        await SeekCoreAsync(globalSeconds, playAfterSeek, CancellationToken.None);
    }

    public void CancelQueuedSeek()
    {
        _queuedSeekCancellation?.Cancel();
        _queuedSeekCancellation?.Dispose();
        _queuedSeekCancellation = null;
        _queuedSeekSeconds = null;
    }

    public async Task SeekAsync(
        double globalSeconds,
        bool playAfterSeek,
        CancellationToken cancellationToken = default)
    {
        CancelQueuedSeek();
        await SeekCoreAsync(globalSeconds, playAfterSeek, cancellationToken);
    }

    private async Task SeekCoreAsync(
        double globalSeconds,
        bool playAfterSeek,
        CancellationToken cancellationToken)
    {
        if (_session is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var target = _session.Plan.ClampGlobalPosition(globalSeconds);
        _preview.SyncAuditionAudio(target, playAfterSeek);
        var targetIndex = _session.Plan.FindSegmentIndex(target);
        if (targetIndex != _session.ActiveSegmentIndex)
        {
            await OpenSegmentAsync(targetIndex, target, playAfterSeek, cancellationToken);
            return;
        }

        var position = _session.Seek(target);
        cancellationToken.ThrowIfCancellationRequested();
        _preview.SeekVideo(Math.Max(0, position.SourceSeconds));
        _preview.SetPosition(position.GlobalSeconds);
        _preview.ShowTimelinePosition(position.GlobalSeconds);
        RaisePositionChanged(position.GlobalSeconds);
        if (playAfterSeek)
        {
            _preview.Play();
            _preview.SyncAuditionAudio(position.GlobalSeconds, play: true);
        }
    }

    public double MapSourcePositionToTimeline(double sourceSeconds) =>
        _session?.Plan.GetGlobalPosition(_session.ActiveSegmentIndex, sourceSeconds) ?? sourceSeconds;

    public double GetCurrentTimelinePosition(double sourceSeconds) =>
        _session?.Plan.GetGlobalPosition(_session.ActiveSegmentIndex, sourceSeconds) ?? sourceSeconds;

    public bool HasReachedActiveSegmentEnd(double sourceSeconds, double toleranceSeconds = 0.01) =>
        _session is { } session &&
        sourceSeconds >= session.ActiveSegment.SourceStartSeconds +
                         session.ActiveSegment.DurationSeconds - toleranceSeconds;

    public double UpdateFromSourcePosition(double sourceSeconds)
    {
        if (_session is null) return sourceSeconds;
        var position = _session.UpdateFromSourcePosition(sourceSeconds);
        if (_preview.IsPlaying && _preview.IsAuditionAudioReady &&
            Math.Abs(_preview.AuditionAudioPositionSeconds - position.GlobalSeconds) > 0.2)
            _preview.SyncAuditionAudio(position.GlobalSeconds, play: true);
        RaisePositionChanged(position.GlobalSeconds);
        return position.GlobalSeconds;
    }

    public double Complete()
    {
        if (_session is null) return 0;
        var position = _session.Complete();
        _preview.SetPosition(position.GlobalSeconds);
        RaisePositionChanged(position.GlobalSeconds);
        return position.GlobalSeconds;
    }

    public void OnVideoReady(bool shouldPlay)
    {
        if (_session is not null)
            _preview.SyncAuditionAudio(_session.PositionSeconds, shouldPlay);
    }

    public void SynchronizeAudio(bool play)
    {
        if (_session is not null)
            _preview.SyncAuditionAudio(_session.PositionSeconds, play);
    }

    public void Reset()
    {
        if (_disposed) return;
        CancelQueuedSeek();
        _session = null;
        _openVersion++;
        _advancing = false;
        _preview.StopAuditionAudio();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelQueuedSeek();
        _session = null;
        _openVersion++;
        _segmentOpenGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task OpenSegmentAsync(
        int segmentIndex,
        double globalSeconds,
        bool playAfterOpen,
        CancellationToken cancellationToken)
    {
        if (_workspace.Project is null || _workspace.Location is null || _session is null ||
            segmentIndex < 0 || segmentIndex >= _session.Plan.Segments.Count)
            return;
        await _segmentOpenGate.WaitAsync(cancellationToken);
        try
        {
            var session = _session;
            if (session is null || segmentIndex >= session.Plan.Segments.Count) return;
            var openVersion = ++_openVersion;
            var segment = session.Plan.Segments[segmentIndex];
            _preview.PauseAuditionAudio();
            var lease = await _materializer.MaterializeAsync(
                _workspace.Project,
                _workspace.Location,
                new MaterializationRequest(
                    new AssetMaterializationTarget(segment.Source.AssetId, segment.Source.RecipeRevisionId),
                    MaterializationPurpose.Preview),
                cancellationToken);
            if (openVersion != _openVersion || !ReferenceEquals(_session, session) ||
                cancellationToken.IsCancellationRequested)
            {
                await lease.DisposeAsync();
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            var position = session.ActivateSegment(segmentIndex, globalSeconds);
            _preview.HidePlaceholder();
            _preview.OpenLeasedVideo(
                lease,
                requiresWarmup: true,
                playAfterPriming: playAfterOpen,
                startSeconds: position.SourceSeconds,
                forceMuted: !segment.AudioEnabled,
                useExternalTimeline: true);
            _preview.SetPosition(position.GlobalSeconds);
            RaisePositionChanged(position.GlobalSeconds);
        }
        finally
        {
            _segmentOpenGate.Release();
        }
    }

    private async Task RunQueuedSeekAsync(
        double globalSeconds,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await SeekCoreAsync(globalSeconds, playAfterSeek: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // The final committed seek remains authoritative and reports through its caller.
            // A speculative scrub preview must never tear down the active audition session.
        }
    }

    private void RaisePositionChanged(double positionSeconds) =>
        PositionChanged?.Invoke(this, new CompositionAuditionPositionChangedEventArgs(positionSeconds));
}

public sealed record CompositionAuditionOpenResult(
    bool IsStale,
    string? AudioWarning,
    bool HasAuditionAudio)
{
    public static CompositionAuditionOpenResult Stale { get; } = new(true, null, false);
}

public sealed class CompositionAuditionPositionChangedEventArgs(double positionSeconds) : EventArgs
{
    public double PositionSeconds { get; } = positionSeconds;
}
