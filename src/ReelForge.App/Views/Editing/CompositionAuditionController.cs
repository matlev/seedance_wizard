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
    private readonly SemaphoreSlim _auditionOpenGate = new(1, 1);
    private readonly SemaphoreSlim _segmentOpenGate = new(1, 1);
    private readonly LatestOperationSequence _openOperations = new();
    private CancellationTokenSource? _queuedSeekCancellation;
    private double? _queuedSeekSeconds;
    private bool _advancing;
    private bool _quiescing;
    private int _quiescenceGeneration;
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
    public bool IsQuiesced => _quiescing;
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
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || _quiescing || _workspace.Project is null || _workspace.Location is null ||
            !isStillCurrent())
            return CompositionAuditionOpenResult.Stale;

        var recipe = revision.Recipe as CompositionRecipe
            ?? throw new InvalidDataException("The Working Composition recipe is invalid.");
        var plan = CompositionAuditionPlan.Create(_workspace.Project, recipe);
        using var operation = _openOperations.Begin(cancellationToken);
        MaterializedMediaLease? auditionAudio = null;
        CompositionAuditionSession? openingSession = null;
        string? auditionAudioWarning = null;
        var gateAcquired = false;
        var auditionAudioAdopted = false;
        var publicationCommitted = false;
        try
        {
            await _auditionOpenGate.WaitAsync(operation.CancellationToken);
            gateAcquired = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!operation.IsCurrent || !isStillCurrent())
                return CompositionAuditionOpenResult.Stale;

            try
            {
                auditionAudio = await _materializer.MaterializeCompositionAuditionAudioAsync(
                    _workspace.Project,
                    _workspace.Location,
                    composition.Id,
                    revision.Id,
                    plan.DurationSeconds,
                    operation.CancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                auditionAudioWarning = exception.Message;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!operation.IsCurrent || !isStillCurrent())
                return CompositionAuditionOpenResult.Stale;

            openingSession = new CompositionAuditionSession(revision.Id, plan, requestedPosition);
            _session = openingSession;
            _preview.SetTimelineRange(0, plan.DurationSeconds);
            if (auditionAudio is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!operation.IsCurrent || !isStillCurrent())
                    return CompositionAuditionOpenResult.Stale;
                _preview.OpenLeasedAuditionAudio(auditionAudio, openingSession.PositionSeconds);
                auditionAudio = null;
                auditionAudioAdopted = true;
            }
            var opened = await OpenSegmentAsync(
                openingSession.ActiveSegmentIndex,
                openingSession.PositionSeconds,
                playAfterOpen: false,
                operation,
                isStillCurrent);
            if (!opened)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return CompositionAuditionOpenResult.Stale;
            }
            publicationCommitted = true;
            return new CompositionAuditionOpenResult(
                IsStale: false,
                auditionAudioWarning,
                _preview.HasAuditionAudioLease);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !operation.IsCurrent)
        {
            return CompositionAuditionOpenResult.Stale;
        }
        finally
        {
            try
            {
                try
                {
                    if (!publicationCommitted && openingSession is not null &&
                        ReferenceEquals(_session, openingSession))
                    {
                        _session = null;
                        _preview.SetTimelineRange(0, 0);
                        if (auditionAudioAdopted) _preview.StopAuditionAudio();
                    }
                }
                finally
                {
                    if (auditionAudio is not null) await auditionAudio.DisposeAsync();
                }
            }
            finally
            {
                if (gateAcquired) _auditionOpenGate.Release();
            }
        }
    }

    public async Task<bool> AdvanceAsync(CancellationToken cancellationToken = default)
    {
        if (_advancing || _quiescing || _session is null) return false;
        if (!_session.Plan.TryGetNextSegmentIndex(_session.ActiveSegmentIndex, out var nextIndex))
            return false;
        _advancing = true;
        try
        {
            var next = _session.Plan.Segments[nextIndex];
            return await RequestSegmentOpenAsync(
                nextIndex,
                next.TimelineStartSeconds,
                playAfterOpen: true,
                cancellationToken);
        }
        finally
        {
            _advancing = false;
        }
    }

    public async Task ReplayAsync(CancellationToken cancellationToken = default)
    {
        if (_quiescing || _session is null) return;
        await RequestSegmentOpenAsync(0, 0, playAfterOpen: true, cancellationToken);
    }

    public void QueueSeek(double globalSeconds, TimeSpan? delay = null)
    {
        if (_disposed || _quiescing || _session is null) return;
        CancelQueuedSeekDelay();
        _openOperations.Invalidate();
        _queuedSeekSeconds = _session.Plan.ClampGlobalPosition(globalSeconds);
        _queuedSeekCancellation = new CancellationTokenSource();
        _ = RunQueuedSeekAsync(
            _queuedSeekSeconds.Value,
            delay ?? TimeSpan.FromMilliseconds(120),
            _queuedSeekCancellation.Token);
    }

    public async Task CommitQueuedSeekAsync(bool playAfterSeek)
    {
        if (_quiescing || _session is null) return;
        var target = _queuedSeekSeconds ?? _session.PositionSeconds;
        CancelQueuedSeekDelay();
        await SeekCoreAsync(target, playAfterSeek, CancellationToken.None);
    }

    public async Task CommitSeekAsync(double globalSeconds, bool playAfterSeek)
    {
        if (_quiescing) return;
        CancelQueuedSeekDelay();
        await SeekCoreAsync(globalSeconds, playAfterSeek, CancellationToken.None);
    }

    public void CancelQueuedSeek()
    {
        CancelQueuedSeekDelay();
        _openOperations.Invalidate();
    }

    private void CancelQueuedSeekDelay()
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
        if (_quiescing) return;
        CancelQueuedSeekDelay();
        await SeekCoreAsync(globalSeconds, playAfterSeek, cancellationToken);
    }

    private async Task SeekCoreAsync(
        double globalSeconds,
        bool playAfterSeek,
        CancellationToken cancellationToken)
    {
        if (_session is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = _openOperations.Begin(cancellationToken);
        try
        {
            var target = _session.Plan.ClampGlobalPosition(globalSeconds);
            if (!operation.IsCurrent) return;
            _preview.SyncAuditionAudio(target, playAfterSeek);
            var targetIndex = _session.Plan.FindSegmentIndex(target);
            if (targetIndex != _session.ActiveSegmentIndex)
            {
                await OpenSegmentAsync(targetIndex, target, playAfterSeek, operation);
                return;
            }

            if (!operation.IsCurrent) return;
            var position = _session.Seek(target);
            if (!operation.IsCurrent) return;
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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !operation.IsCurrent)
        {
            // A later seek is authoritative.
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

    public async Task<IDisposable> PauseAndQuiesceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_quiescing)
            throw new InvalidOperationException("The composition audition is already quiesced.");
        var generation = ++_quiescenceGeneration;
        _quiescing = true;
        try
        {
            CancelQueuedSeek();
            _advancing = false;
            _preview.PauseAndCancelDeferredPlayback();
            _preview.PauseAuditionAudio();

            await _auditionOpenGate.WaitAsync(cancellationToken);
            _auditionOpenGate.Release();
            await _segmentOpenGate.WaitAsync(cancellationToken);
            _segmentOpenGate.Release();
            return new QuiescenceLease(this, generation);
        }
        catch
        {
            EndQuiescence(generation);
            throw;
        }
    }

    private void EndQuiescence(int generation)
    {
        if (_quiescenceGeneration == generation) _quiescing = false;
    }

    public void Reset()
    {
        if (_disposed) return;
        CancelQueuedSeek();
        _session = null;
        _advancing = false;
        _preview.StopAuditionAudio();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelQueuedSeek();
        _session = null;
        _openOperations.Dispose();
        // An invalidated opener may still unwind through its finally block and release this gate.
        // The controller therefore leaves both small synchronization primitives for GC ownership.
        GC.SuppressFinalize(this);
    }

    private async Task<bool> RequestSegmentOpenAsync(
        int segmentIndex,
        double globalSeconds,
        bool playAfterOpen,
        CancellationToken cancellationToken)
    {
        if (_quiescing) return false;
        using var operation = _openOperations.Begin(cancellationToken);
        try
        {
            return await OpenSegmentAsync(segmentIndex, globalSeconds, playAfterOpen, operation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !operation.IsCurrent)
        {
            return false;
        }
    }

    private async Task<bool> OpenSegmentAsync(
        int segmentIndex,
        double globalSeconds,
        bool playAfterOpen,
        LatestOperationSequence.Operation operation,
        Func<bool>? isStillCurrent = null)
    {
        if (_workspace.Project is null || _workspace.Location is null || _session is null ||
            segmentIndex < 0 || segmentIndex >= _session.Plan.Segments.Count)
            return false;
        await _segmentOpenGate.WaitAsync(operation.CancellationToken);
        try
        {
            var session = _session;
            if (!operation.IsCurrent || isStillCurrent?.Invoke() == false ||
                session is null || segmentIndex >= session.Plan.Segments.Count)
                return false;
            var segment = session.Plan.Segments[segmentIndex];
            _preview.PauseAuditionAudio();
            var lease = await _materializer.MaterializeAsync(
                _workspace.Project,
                _workspace.Location,
                new MaterializationRequest(
                    new AssetMaterializationTarget(segment.Source.AssetId, segment.Source.RecipeRevisionId),
                    MaterializationPurpose.Preview),
                operation.CancellationToken);
            if (!operation.IsCurrent || isStillCurrent?.Invoke() == false ||
                !ReferenceEquals(_session, session))
            {
                await lease.DisposeAsync();
                return false;
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
            return true;
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

    private sealed class QuiescenceLease(
        CompositionAuditionController owner,
        int generation) : IDisposable
    {
        private CompositionAuditionController? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.EndQuiescence(generation);
    }
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
