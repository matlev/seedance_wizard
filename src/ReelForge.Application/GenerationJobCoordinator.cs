using ReelForge.Core;

namespace ReelForge.Application;

public sealed class TrackedGenerationJob
{
    public Guid GenerationId { get; set; }
    public string ProjectFilePath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string ProviderJobId { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public GenerationStatus Status { get; set; } = GenerationStatus.Queued;
    public OutputIngestionStatus IngestionStatus { get; set; } = OutputIngestionStatus.NotRequired;
    public string Message { get; set; } = "Waiting to check provider status…";
    public List<ProviderGenerationOutput> Outputs { get; set; } = [];
    public GenerationError? Error { get; set; }
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
}

public interface IGenerationJobStore
{
    Task<IReadOnlyList<TrackedGenerationJob>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<TrackedGenerationJob> jobs, CancellationToken cancellationToken = default);
}

public interface IGenerationJobFinalizer
{
    Task FinalizeAsync(TrackedGenerationJob job, CancellationToken cancellationToken = default);
}

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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_restored) return;
            foreach (var job in await _store.LoadAsync(cancellationToken).ConfigureAwait(false))
                _jobs[job.GenerationId] = job;
            _restored = true;
        }
        finally
        {
            _gate.Release();
        }

        RaiseJobsChanged();
        foreach (var id in GetSnapshot().Select(job => job.GenerationId)) StartMonitor(id);
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
                    await FinalizeAndRemoveAsync(current, cancellationToken).ConfigureAwait(false);
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
                        if (terminal is not null) await FinalizeAndRemoveAsync(terminal, cancellationToken).ConfigureAwait(false);
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_jobs.TryGetValue(generationId, out var job)) return;
            job.Status = remote.Status;
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

        RaiseJobsChanged();
    }

    private async Task FinalizeAndRemoveAsync(TrackedGenerationJob job, CancellationToken cancellationToken)
    {
        try
        {
            await _finalizer.FinalizeAsync(job, cancellationToken).ConfigureAwait(false);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _jobs.Remove(job.GenerationId);
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
        UpdatedAt = source.UpdatedAt,
        Status = source.Status,
        IngestionStatus = source.IngestionStatus,
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
