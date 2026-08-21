using ReelForge.Core;

namespace ReelForge.Application;

public sealed class GenerationJobCoordinator : IAsyncDisposable
{
    private readonly IGenerationJobStore _store;
    private readonly Func<string, IAsyncVideoGenerationProvider?> _providerResolver;
    private readonly IGenerationJobFinalizer _finalizer;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, TrackedGenerationJob> _jobs = [];
    private readonly Dictionary<Guid, Task> _monitors = [];
    private readonly CancellationTokenSource _shutdown = new();
    private bool _restored;

    public GenerationJobCoordinator(
        IGenerationJobStore store,
        Func<string, IAsyncVideoGenerationProvider?> providerResolver,
        IGenerationJobFinalizer finalizer,
        TimeSpan? pollInterval = null)
    {
        _store = store;
        _providerResolver = providerResolver;
        _finalizer = finalizer;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    public event EventHandler? JobsChanged;
    public event EventHandler<GenerationJobStatusChangedEventArgs>? JobStatusChanged;

    public IReadOnlyList<TrackedGenerationJob> GetSnapshot()
    {
        _gate.Wait();
        try
        {
            return _jobs.Values.OrderBy(job => job.RequestedAt).Select(Clone).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        var interruptedPendingJobs = new List<TrackedGenerationJob>();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_restored) return;
            foreach (var job in await _store.LoadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (job.IsAwaitingSubmission && string.IsNullOrWhiteSpace(job.ProviderJobId))
                {
                    job.Status = GenerationStatus.Cancelled;
                    job.IsAwaitingSubmission = false;
                    job.WasCancelledBeforeSubmission = true;
                    job.UndoSendExpiresAt = null;
                    job.CompletedAt = DateTimeOffset.UtcNow;
                    job.UpdatedAt = DateTimeOffset.UtcNow;
                    job.Message = "Provider status: Cancelled";
                    interruptedPendingJobs.Add(job);
                }
                _jobs[job.GenerationId] = job;
            }
            _restored = true;
            if (interruptedPendingJobs.Count > 0)
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseJobsChanged();
        foreach (var job in interruptedPendingJobs)
            await FinalizeAndRetainAsync(job, cancellationToken).ConfigureAwait(false);
        foreach (var id in GetSnapshot().Where(job =>
                     !string.IsNullOrWhiteSpace(job.ProviderJobId) ||
                     (!job.IsReconciled && job.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled))
                 .Select(job => job.GenerationId))
            StartMonitor(id);
    }

    public async Task TrackPendingAsync(
        GenerationRecord generation,
        ProjectLocation projectLocation,
        string projectName,
        string providerDisplayName,
        DateTimeOffset undoSendExpiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (generation.Status != GenerationStatus.Queued || !string.IsNullOrWhiteSpace(generation.ProviderJobId))
            throw new InvalidOperationException("Only an unsubmitted queued generation can enter the Undo Send window.");
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(undoSendExpiresAt, generation.RequestedAt);

        var job = new TrackedGenerationJob
        {
            GenerationId = generation.Id,
            ProjectFilePath = projectLocation.ProjectFilePath,
            ProjectName = projectName,
            ProviderId = generation.RequestSnapshot.ProviderId,
            ProviderDisplayName = providerDisplayName,
            ModelVersion = generation.RequestSnapshot.ModelVersion,
            RequestedAt = generation.RequestedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            UndoSendExpiresAt = undoSendExpiresAt,
            Status = GenerationStatus.Queued,
            IngestionStatus = generation.IngestionStatus,
            IsAwaitingSubmission = true,
            Message = string.Empty,
            ResponseMetadata = new Dictionary<string, string>(generation.ResponseMetadata, StringComparer.Ordinal)
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _jobs[job.GenerationId] = job;
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaiseJobsChanged();
    }

    public async Task<bool> TryBeginSubmissionAsync(Guid generationId, CancellationToken cancellationToken = default)
    {
        var claimed = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_jobs.TryGetValue(generationId, out var job) && job.IsAwaitingSubmission &&
                job.Status == GenerationStatus.Queued && string.IsNullOrWhiteSpace(job.ProviderJobId))
            {
                job.IsAwaitingSubmission = false;
                job.UndoSendExpiresAt = null;
                job.ProviderSubmittedAt = DateTimeOffset.UtcNow;
                job.UpdatedAt = DateTimeOffset.UtcNow;
                job.Message = "Submitting generation request…";
                claimed = true;
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
        if (claimed) RaiseJobsChanged();
        return claimed;
    }

    public async Task<bool> CancelPendingAsync(Guid generationId, CancellationToken cancellationToken = default)
    {
        TrackedGenerationJob? cancelled = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_jobs.TryGetValue(generationId, out var job) && job.IsAwaitingSubmission &&
                job.Status == GenerationStatus.Queued && string.IsNullOrWhiteSpace(job.ProviderJobId))
            {
                job.Status = GenerationStatus.Cancelled;
                job.IsAwaitingSubmission = false;
                job.WasCancelledBeforeSubmission = true;
                job.UndoSendExpiresAt = null;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.UpdatedAt = DateTimeOffset.UtcNow;
                job.Message = "Provider status: Cancelled";
                cancelled = Clone(job);
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
        if (cancelled is null) return false;
        JobStatusChanged?.Invoke(this, new GenerationJobStatusChangedEventArgs(
            cancelled.GenerationId,
            cancelled.ProjectName,
            GenerationStatus.Queued,
            GenerationStatus.Cancelled));
        RaiseJobsChanged();
        await FinalizeAndRetainAsync(cancelled, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task TrackAsync(
        GenerationRecord generation,
        ProjectLocation projectLocation,
        string projectName,
        string providerDisplayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (string.IsNullOrWhiteSpace(generation.ProviderJobId))
            throw new InvalidOperationException("Only accepted asynchronous jobs can be tracked.");

        var job = new TrackedGenerationJob
        {
            GenerationId = generation.Id,
            ProjectFilePath = projectLocation.ProjectFilePath,
            ProjectName = projectName,
            ProviderId = generation.RequestSnapshot.ProviderId,
            ProviderDisplayName = providerDisplayName,
            ModelVersion = generation.RequestSnapshot.ModelVersion,
            ProviderJobId = generation.ProviderJobId,
            RequestedAt = generation.RequestedAt,
            ProviderSubmittedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = generation.Status,
            IngestionStatus = generation.IngestionStatus,
            Message = "Generation accepted. Monitoring provider status…",
            Error = generation.Error,
            ResponseMetadata = new Dictionary<string, string>(generation.ResponseMetadata, StringComparer.Ordinal)
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _jobs[job.GenerationId] = job;
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseJobsChanged();
        StartMonitor(job.GenerationId);
    }

    public async Task CompleteUnacceptedSubmissionAsync(
        GenerationRecord generation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generation);
        if (!string.IsNullOrWhiteSpace(generation.ProviderJobId) ||
            generation.Status is not (GenerationStatus.Failed or GenerationStatus.Cancelled))
            throw new InvalidOperationException("The generation must be terminal without a provider job ID.");

        TrackedGenerationJob? terminal = null;
        GenerationStatus previousStatus = GenerationStatus.Queued;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_jobs.TryGetValue(generation.Id, out var job)) return;
            previousStatus = job.Status;
            job.Status = generation.Status;
            job.IsAwaitingSubmission = false;
            job.UndoSendExpiresAt = null;
            job.CompletedAt = generation.CompletedAt ?? DateTimeOffset.UtcNow;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.Error = generation.Error;
            job.ResponseMetadata = new Dictionary<string, string>(generation.ResponseMetadata, StringComparer.Ordinal);
            job.Message = generation.Error?.Message ?? $"Provider status: {generation.Status}";
            terminal = Clone(job);
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (terminal is null) return;
        if (previousStatus != terminal.Status)
            JobStatusChanged?.Invoke(this, new GenerationJobStatusChangedEventArgs(
                terminal.GenerationId,
                terminal.ProjectName,
                previousStatus,
                terminal.Status));
        RaiseJobsChanged();
        await FinalizeAndRetainAsync(terminal, cancellationToken).ConfigureAwait(false);
    }

    private void StartMonitor(Guid generationId)
    {
        lock (_monitors)
        {
            if (_monitors.ContainsKey(generationId)) return;
            _monitors[generationId] = Task.Run(() => MonitorAsync(generationId, _shutdown.Token));
        }
    }

    private async Task MonitorAsync(Guid generationId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var current = GetSnapshot().SingleOrDefault(job => job.GenerationId == generationId);
                if (current is null) return;

                if (current.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
                {
                    if (!current.IsReconciled)
                        await FinalizeAndRetainAsync(current, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var provider = _providerResolver(current.ProviderId);
                if (provider is null)
                {
                    await UpdateMessageAsync(generationId, "The provider is disabled or unavailable. Monitoring will retry automatically.", cancellationToken)
                        .ConfigureAwait(false);
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var remote = await provider.GetJobAsync(current.ProviderJobId, cancellationToken).ConfigureAwait(false);
                    await ApplyRemoteStateAsync(generationId, remote, cancellationToken).ConfigureAwait(false);
                    if (remote.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
                    {
                        var terminal = GetSnapshot().SingleOrDefault(job => job.GenerationId == generationId);
                        if (terminal is not null) await FinalizeAndRetainAsync(terminal, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    await UpdateMessageAsync(generationId, $"Status check failed: {exception.Message}. Retrying…", cancellationToken)
                        .ConfigureAwait(false);
                }

                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_monitors) _monitors.Remove(generationId);
        }
    }

    private async Task ApplyRemoteStateAsync(
        Guid generationId,
        ProviderGenerationJob remote,
        CancellationToken cancellationToken)
    {
        GenerationJobStatusChangedEventArgs? statusChange = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_jobs.TryGetValue(generationId, out var job)) return;
            if (job.Status != remote.Status)
            {
                statusChange = new GenerationJobStatusChangedEventArgs(
                    generationId,
                    job.ProjectName,
                    job.Status,
                    remote.Status);
            }
            job.Status = remote.Status;
            if (remote.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
                job.CompletedAt ??= DateTimeOffset.UtcNow;
            job.Error = remote.Error;
            job.Outputs = remote.Outputs.ToList();
            foreach (var pair in remote.ResponseMetadata) job.ResponseMetadata[pair.Key] = pair.Value;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.Message = remote.Status switch
            {
                GenerationStatus.Succeeded => "Generation finished. Adding output to the project…",
                GenerationStatus.Failed => remote.Error?.Message ?? "Generation failed.",
                GenerationStatus.Cancelled => "Generation was cancelled by the provider.",
                _ => $"Provider status: {remote.Status}"
            };
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (statusChange is not null) JobStatusChanged?.Invoke(this, statusChange);
        RaiseJobsChanged();
    }

    private async Task FinalizeAndRetainAsync(TrackedGenerationJob job, CancellationToken cancellationToken)
    {
        try
        {
            await _finalizer.FinalizeAsync(job, cancellationToken).ConfigureAwait(false);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_jobs.TryGetValue(job.GenerationId, out var current)) return;
                current.IsReconciled = true;
                current.CompletedAt ??= DateTimeOffset.UtcNow;
                current.UpdatedAt = DateTimeOffset.UtcNow;
                if (current.Status == GenerationStatus.Succeeded)
                {
                    current.IngestionStatus = OutputIngestionStatus.Succeeded;
                    current.Message = "Generation completed and its output was added to the project.";
                }
                else if (current.Status == GenerationStatus.Failed)
                {
                    current.Message = current.Error?.Message ?? "Generation failed.";
                }
                else
                {
                    current.Message = current.WasCancelledBeforeSubmission
                        ? "Provider status: Cancelled"
                        : "Generation was cancelled by the provider.";
                }
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
            RaiseJobsChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await UpdateMessageAsync(job.GenerationId, $"Project update failed: {exception.Message}. It will retry next time ReelForge starts.", CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Guid>> DismissAsync(
        IEnumerable<Guid> generationIds,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = generationIds.ToHashSet();
        if (requestedIds.Count == 0) return [];
        var removedIds = new List<Guid>();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var id in requestedIds)
            {
                if (_jobs.TryGetValue(id, out var job) && job.IsReconciled &&
                    job.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
                {
                    _jobs.Remove(id);
                    removedIds.Add(id);
                }
            }
            if (removedIds.Count > 0) await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (removedIds.Count > 0) RaiseJobsChanged();
        return removedIds;
    }

    private async Task UpdateMessageAsync(Guid generationId, string message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_jobs.TryGetValue(generationId, out var job)) return;
            job.Message = message;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaiseJobsChanged();
    }

    private Task SaveLockedAsync(CancellationToken cancellationToken) =>
        _store.SaveAsync(_jobs.Values.ToArray(), cancellationToken);

    private void RaiseJobsChanged() => JobsChanged?.Invoke(this, EventArgs.Empty);

    public void Stop() => _shutdown.Cancel();

    private static TrackedGenerationJob Clone(TrackedGenerationJob source) => new()
    {
        GenerationId = source.GenerationId,
        ProjectFilePath = source.ProjectFilePath,
        ProjectName = source.ProjectName,
        ProviderId = source.ProviderId,
        ProviderDisplayName = source.ProviderDisplayName,
        ModelVersion = source.ModelVersion,
        ProviderJobId = source.ProviderJobId,
        RequestedAt = source.RequestedAt,
        ProviderSubmittedAt = source.ProviderSubmittedAt,
        UndoSendExpiresAt = source.UndoSendExpiresAt,
        UpdatedAt = source.UpdatedAt,
        CompletedAt = source.CompletedAt,
        Status = source.Status,
        IngestionStatus = source.IngestionStatus,
        IsReconciled = source.IsReconciled,
        IsAwaitingSubmission = source.IsAwaitingSubmission,
        WasCancelledBeforeSubmission = source.WasCancelledBeforeSubmission,
        Message = source.Message,
        Outputs = source.Outputs.ToList(),
        Error = source.Error,
        ResponseMetadata = new Dictionary<string, string>(source.ResponseMetadata, StringComparer.Ordinal)
    };

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        Task[] monitors;
        lock (_monitors) monitors = _monitors.Values.ToArray();
        try
        {
            await Task.WhenAll(monitors).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _shutdown.Dispose();
        _gate.Dispose();
    }
}
