using System.IO;
using ReelForge.App.Bootstrap;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

/// <summary>
/// Owns WPF-facing generation submission orchestration. Durable submission, monitoring,
/// and reconciliation remain in the application-layer workflow and job services.
/// </summary>
internal sealed class GenerationSubmissionCoordinator : IDisposable
{
    private readonly ApplicationRuntime _runtime;
    private readonly ProjectWorkspace _workspace;
    private readonly GenerationWorkspaceCoordinator _generationWorkspace;
    private readonly GenerationJobCoordinator _jobCoordinator;
    private readonly GenerationJobFinalizer _jobFinalizer;
    private readonly ISecretStore _secretStore;
    private readonly IGenerationSubmissionPresentation _presentation;
    private readonly Dictionary<Guid, CancellationTokenSource> _pendingDelays = [];
    private readonly object _pendingDelaysGate = new();
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private bool _disposed;

    public GenerationSubmissionCoordinator(
        ApplicationRuntime runtime,
        ProjectWorkspace workspace,
        GenerationWorkspaceCoordinator generationWorkspace,
        GenerationJobCoordinator jobCoordinator,
        GenerationJobFinalizer jobFinalizer,
        ISecretStore secretStore,
        IGenerationSubmissionPresentation presentation)
    {
        _runtime = runtime;
        _workspace = workspace;
        _generationWorkspace = generationWorkspace;
        _jobCoordinator = jobCoordinator;
        _jobFinalizer = jobFinalizer;
        _secretStore = secretStore;
        _presentation = presentation;
        _jobFinalizer.Finalized += JobFinalizer_Finalized;
    }

    public async Task SubmitAsync(int configuredUndoSendSeconds)
    {
        if (_disposed || _workspace.Project is null || _workspace.Location is null)
        {
            _presentation.ShowProjectRequired();
            return;
        }

        var provider = _generationWorkspace.CurrentProvider;
        var workflow = _generationWorkspace.CurrentWorkflow;
        var preparation = _generationWorkspace.CurrentPreparation;
        var location = _workspace.Location;
        var projectName = _workspace.Project.Name;
        var draft = _generationWorkspace.CaptureDraft();
        var authorization = await AuthorizeAsync(provider, draft).ConfigureAwait(true);
        if (provider.CostBehavior == GenerationProviderCostBehavior.PotentiallyBillable && authorization is null)
            return;

        var context = new SubmissionContext(
            provider,
            preparation,
            location,
            projectName,
            draft,
            authorization);
        var undoSendSeconds = Math.Clamp(configuredUndoSendSeconds, 0, 30);
        if (undoSendSeconds > 0 && provider is IAsyncVideoGenerationProvider)
        {
            await QueueWithUndoSendAsync(workflow, context, undoSendSeconds).ConfigureAwait(true);
            return;
        }

        await SubmitImmediatelyAsync(workflow, context).ConfigureAwait(true);
    }

    public async Task CancelQueuedAsync(Guid generationId)
    {
        try
        {
            var owningJob = _jobCoordinator.GetSnapshot().SingleOrDefault(job => job.GenerationId == generationId);
            var delay = TakePendingDelay(generationId);
            if (delay is null) return;
            delay.Cancel();
            delay.Dispose();
            if (!await _jobCoordinator.CancelPendingAsync(generationId).ConfigureAwait(true)) return;
            if (owningJob is not null)
                BeginForProject(owningJob.ProjectFilePath, () =>
                {
                    _presentation.SetGenerationStatus("Queued generation cancelled.");
                    _presentation.SetStatus("Provider status: Cancelled");
                });
        }
        catch (Exception exception)
        {
            _presentation.ShowError("Queued generation could not be cancelled", exception);
        }
    }

    private async Task<GenerationSubmissionAuthorization?> AuthorizeAsync(
        IVideoGenerationProvider provider,
        GenerationDraft draft)
    {
        if (provider.CostBehavior != GenerationProviderCostBehavior.PotentiallyBillable) return null;
        if (provider is not IApiKeyVideoGenerationProvider apiKeyProvider)
            throw new InvalidOperationException("This paid provider has no configured credential contract.");

        var apiKey = await _secretStore.GetAsync(apiKeyProvider.ApiKeyCredentialKey).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _presentation.SetGenerationStatus(
                $"Store a {provider.Capabilities.DisplayName} API key before live submission.");
            return null;
        }

        if (!_presentation.ConfirmPotentiallyBillableSubmission(provider, draft))
        {
            _presentation.SetGenerationStatus("Submission cancelled.");
            return null;
        }

        return GenerationSubmissionAuthorization.FromInteractiveUserConfirmation(
            provider.Capabilities.ProviderId,
            userConfirmedPotentialCharges: true);
    }

    private async Task QueueWithUndoSendAsync(
        GenerationWorkflow workflow,
        SubmissionContext context,
        int undoSendSeconds)
    {
        _presentation.SetProjectActionsEnabled(false);
        try
        {
            var generation = await workflow.QueueAsync(
                    context.Provider, context.Draft, context.Authorization)
                .ConfigureAwait(true);
            var delaySeconds = Math.Clamp(undoSendSeconds, 1, 30);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            await _jobCoordinator.TrackPendingAsync(
                    generation,
                    context.ProjectLocation,
                    context.ProjectName,
                    context.Provider.Capabilities.DisplayName,
                    expiresAt)
                .ConfigureAwait(true);

            var delayCancellation = new CancellationTokenSource();
            if (!RegisterPendingDelay(generation.Id, delayCancellation))
            {
                delayCancellation.Dispose();
                return;
            }
            if (IsProjectOpen(context.ProjectLocation.ProjectFilePath))
            {
                BeginForProject(context.ProjectLocation.ProjectFilePath, () =>
                {
                    _presentation.RefreshProjectCollections();
                    _presentation.SelectGeneration(generation.Id);
                    _presentation.SetGenerationStatus(
                        $"Generation queued locally for {delaySeconds} seconds. Use Cancel Job in Jobs to undo.");
                    _presentation.SetStatus("Generation has not been sent to the provider yet.");
                });
            }
            _ = SubmitAfterDelayAsync(generation.Id, context, expiresAt, delayCancellation);
        }
        catch (GenerationValidationException exception)
        {
            _presentation.SetGenerationStatus(exception.Message);
        }
        catch (Exception exception)
        {
            _presentation.ShowError("Generation could not be queued", exception);
        }
        finally
        {
            _presentation.SetProjectActionsEnabled(true);
        }
    }

    private async Task SubmitAfterDelayAsync(
        Guid generationId,
        SubmissionContext context,
        DateTimeOffset expiresAt,
        CancellationTokenSource delayCancellation)
    {
        var crossedUndoBoundary = false;
        try
        {
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, delayCancellation.Token).ConfigureAwait(false);
            if (!TryCrossUndoBoundary(generationId, delayCancellation)) return;
            crossedUndoBoundary = true;
            if (!await _jobCoordinator.TryBeginSubmissionAsync(generationId).ConfigureAwait(false)) return;

            await _submissionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                // A delayed request always works against a fresh owning-project workspace.
                // The active workspace may change while the user is still allowed to navigate.
                var isolatedWorkspace = _runtime.CreateProjectWorkspace();
                await isolatedWorkspace.OpenAsync(context.ProjectLocation.ProjectFilePath).ConfigureAwait(false);
                var workflow = _runtime.CreateGenerationWorkflow(isolatedWorkspace, context.ProviderPreparation);
                var generation = isolatedWorkspace.Project?.Generations
                    .SingleOrDefault(item => item.Id == generationId)
                    ?? throw new InvalidOperationException("The locally queued generation no longer exists in its owning project.");
                await SubmitQueuedAsync(workflow, context, generation).ConfigureAwait(false);
            }
            finally
            {
                _submissionGate.Release();
            }
        }
        catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
        {
            // Undo Send deliberately leaves no provider request to resume.
        }
        catch (Exception exception)
        {
            _presentation.BeginInvoke(() => _presentation.ShowError("Queued generation failed", exception));
        }
        finally
        {
            if (crossedUndoBoundary) delayCancellation.Dispose();
        }
    }

    private async Task SubmitQueuedAsync(
        GenerationWorkflow workflow,
        SubmissionContext context,
        GenerationRecord generation)
    {
        IProgress<GenerationWorkflowProgress> progress = new Progress<GenerationWorkflowProgress>(update => BeginForProject(
            context.ProjectLocation.ProjectFilePath,
            () => _presentation.SetGenerationStatus(update.Message)));
        generation = await workflow.SubmitQueuedAsync(
                context.Provider, generation, context.Authorization, progress)
            .ConfigureAwait(false);
        BeginForProject(context.ProjectLocation.ProjectFilePath,
            () => UpdateActiveProjectForSubmission(context.ProjectLocation, generation));

        if (context.Provider is IAsyncVideoGenerationProvider && !string.IsNullOrWhiteSpace(generation.ProviderJobId))
        {
            await _jobCoordinator.TrackAsync(
                    generation,
                    context.ProjectLocation,
                    context.ProjectName,
                    context.Provider.Capabilities.DisplayName)
                .ConfigureAwait(false);
            BeginForProject(context.ProjectLocation.ProjectFilePath, () =>
            {
                _presentation.SetGenerationStatus("Generation submitted. Follow its progress in the Jobs tab.");
                _presentation.SetStatus($"Generation accepted by {context.Provider.Capabilities.DisplayName}.");
            });
        }
        else if (generation.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
        {
            await _jobCoordinator.CompleteUnacceptedSubmissionAsync(generation).ConfigureAwait(false);
            BeginForProject(context.ProjectLocation.ProjectFilePath, () =>
            {
                _presentation.SetGenerationStatus(FormatOutcome(generation));
                _presentation.SetStatus($"Generation state: {generation.Status}; no provider job is being monitored.");
            });
        }
        else
        {
            BeginForProject(context.ProjectLocation.ProjectFilePath, () =>
            {
                _presentation.SetGenerationStatus(FormatOutcome(generation));
                _presentation.SetStatus($"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.");
            });
        }
    }

    private async Task SubmitImmediatelyAsync(GenerationWorkflow workflow, SubmissionContext context)
    {
        _presentation.SetSubmissionEnabled(false);
        _presentation.SetProjectActionsEnabled(false);
        var progress = new Progress<GenerationWorkflowProgress>(update =>
        {
            if (IsProjectOpen(context.ProjectLocation.ProjectFilePath))
                _presentation.SetGenerationStatus(update.Message);
        });
        try
        {
            var generation = await workflow.SubmitAsync(
                    context.Provider, context.Draft, context.Authorization, progress)
                .ConfigureAwait(true);
            if (context.Provider is IAsyncVideoGenerationProvider && !string.IsNullOrWhiteSpace(generation.ProviderJobId))
            {
                await _jobCoordinator.TrackAsync(
                        generation,
                        context.ProjectLocation,
                        context.ProjectName,
                        context.Provider.Capabilities.DisplayName)
                    .ConfigureAwait(true);
            }

            if (!IsProjectOpen(context.ProjectLocation.ProjectFilePath)) return;

            _presentation.RefreshProjectCollections();
            _presentation.SelectGeneration(generation.Id);
            _presentation.TryAutoPreview(generation);
            if (context.Provider is IAsyncVideoGenerationProvider && !string.IsNullOrWhiteSpace(generation.ProviderJobId))
            {
                _presentation.SetGenerationStatus("Generation submitted. Follow its progress in the Jobs tab.");
                _presentation.SetStatus($"Generation accepted by {context.Provider.Capabilities.DisplayName}.");
            }
            else
            {
                _presentation.SetGenerationStatus(FormatOutcome(generation));
                _presentation.SetStatus($"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.");
            }
        }
        catch (GenerationValidationException exception)
        {
            _presentation.SetGenerationStatus(exception.Message);
        }
        catch (Exception exception)
        {
            _presentation.ShowError("Generation workflow failed", exception);
        }
        finally
        {
            _presentation.SetSubmissionEnabled(true);
            _presentation.SetProjectActionsEnabled(true);
        }
    }

    private void JobFinalizer_Finalized(object? sender, GenerationJobFinalizedEventArgs e)
    {
        if (_disposed || !e.ActiveProjectUpdated) return;
        _presentation.BeginInvoke(() =>
        {
            if (_disposed || !IsProjectOpen(e.ProjectFilePath)) return;
            var generation = _workspace.Project?.Generations.SingleOrDefault(candidate => candidate.Id == e.GenerationId);
            if (generation is null) return;
            _presentation.RefreshProjectCollections();
            _presentation.TryAutoPreview(generation);
            _presentation.SetStatus(e.Status == GenerationStatus.Succeeded
                ? "Generated output added as durable project media."
                : $"Generation finished with status {e.Status}.");
        });
    }

    private void UpdateActiveProjectForSubmission(ProjectLocation location, GenerationRecord generation)
    {
        if (_disposed || !IsProjectOpen(location.ProjectFilePath)) return;
        _presentation.MergeGenerationState(generation);
        _presentation.RefreshProjectCollections();
        _presentation.SelectGeneration(generation.Id);
        _presentation.TryAutoPreview(generation);
    }

    private bool IsProjectOpen(string projectFilePath) =>
        _workspace.Location is not null &&
        PathsEqual(_workspace.Location.ProjectFilePath, projectFilePath);

    private void BeginForProject(string projectFilePath, Action action) =>
        _presentation.BeginInvoke(() =>
        {
            if (!_disposed && IsProjectOpen(projectFilePath)) action();
        });

    private bool RegisterPendingDelay(Guid generationId, CancellationTokenSource delay)
    {
        lock (_pendingDelaysGate)
        {
            if (_disposed) return false;
            _pendingDelays.Add(generationId, delay);
            return true;
        }
    }

    private CancellationTokenSource? TakePendingDelay(Guid generationId)
    {
        lock (_pendingDelaysGate)
        {
            if (!_pendingDelays.Remove(generationId, out var delay)) return null;
            return delay;
        }
    }

    private bool TryCrossUndoBoundary(Guid generationId, CancellationTokenSource expected)
    {
        lock (_pendingDelaysGate)
        {
            if (_disposed ||
                !_pendingDelays.TryGetValue(generationId, out var current) ||
                !ReferenceEquals(current, expected)) return false;
            _pendingDelays.Remove(generationId);
            return true;
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string FormatOutcome(GenerationRecord generation)
    {
        var message = $"Remote: {generation.Status} • Ingestion: {generation.IngestionStatus}";
        if (!string.IsNullOrWhiteSpace(generation.ProviderJobId)) message += $"\nJob: {generation.ProviderJobId}";
        if (generation.Error is not null) message += $"\n{generation.Error.Message}";
        if (generation.ResponseMetadata.GetValueOrDefault("localMonitoring") is { } monitoring)
            message += $"\nLocal monitoring: {monitoring}";
        return message;
    }

    public void Dispose()
    {
        CancellationTokenSource[] pending;
        lock (_pendingDelaysGate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _pendingDelays.Values.ToArray();
            _pendingDelays.Clear();
        }
        _jobFinalizer.Finalized -= JobFinalizer_Finalized;
        foreach (var delay in pending) delay.Cancel();
        foreach (var delay in pending) delay.Dispose();
        // Delayed submission tasks can still be leaving the semaphore. It is deliberately
        // not disposed here; the coordinator becomes collectible with its window.
    }

    private sealed record SubmissionContext(
        IVideoGenerationProvider Provider,
        IProviderAssetPreparationService? ProviderPreparation,
        ProjectLocation ProjectLocation,
        string ProjectName,
        GenerationDraft Draft,
        GenerationSubmissionAuthorization? Authorization);
}

internal interface IGenerationSubmissionPresentation
{
    void ShowProjectRequired();
    bool ConfirmPotentiallyBillableSubmission(IVideoGenerationProvider provider, GenerationDraft draft);
    void ShowError(string title, Exception exception);
    void SetGenerationStatus(string status);
    void SetStatus(string status);
    void SetSubmissionEnabled(bool enabled);
    void SetProjectActionsEnabled(bool enabled);
    void RefreshProjectCollections();
    void SelectGeneration(Guid generationId);
    void MergeGenerationState(GenerationRecord generation);
    void TryAutoPreview(GenerationRecord generation);
    void BeginInvoke(Action action);
}
