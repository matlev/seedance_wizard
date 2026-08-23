using ReelForge.App.Views.MediaPreview;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Editing;

/// <summary>
/// Owns the Working Composition preview/export operation lifecycle. The WPF shell
/// supplies presentation and interaction details through <see cref="ICompositionRenderHost"/>.
/// </summary>
internal sealed class CompositionRenderCoordinator : IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly RecipeMediaMaterializer _materializer;
    private readonly ProjectMediaOperationsCoordinator _operations;
    private readonly ICompositionRenderHost _host;
    private CancellationTokenSource? _cancellation;
    private CompositionRenderTarget? _activeTarget;
    private long _operationGeneration;
    private bool _userCancellationRequested;
    private bool _disposed;

    public CompositionRenderCoordinator(
        ProjectWorkspace workspace,
        RecipeMediaMaterializer materializer,
        ProjectMediaOperationsCoordinator operations,
        ICompositionRenderHost host)
    {
        _workspace = workspace;
        _materializer = materializer;
        _operations = operations;
        _host = host;
    }

    public bool IsRendering => _cancellation is not null;

    public Task PreviewAsync()
    {
        if (!TryCaptureTarget(out var target) || !_host.CanAdoptBakedPreview(target)) return Task.CompletedTask;
        return RunAsync(target, "Rendering preview…", "Composition preview render cancelled.", async cancellationToken =>
        {
            MaterializedMediaLease? lease = null;
            try
            {
                lease = await _materializer.MaterializeAsync(
                    target.Project,
                    target.Location,
                    new MaterializationRequest(
                        new AssetMaterializationTarget(target.Composition.Id, target.Revision.Id),
                        MaterializationPurpose.Preview),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!_host.CanAdoptBakedPreview(target)) return;

                _host.AdoptBakedPreview(lease, target);
                lease = null;
                _host.RefreshCompositionActions();
                _host.SetStatus("Working Composition preview is ready.");
            }
            finally
            {
                if (lease is not null) await lease.DisposeAsync();
            }
        });
    }

    public Task ExportAsync()
    {
        if (!TryCaptureTarget(out var target)) return Task.CompletedTask;
        var destinationPath = _host.PromptExportPath(target);
        if (string.IsNullOrWhiteSpace(destinationPath)) return Task.CompletedTask;

        return RunAsync(target, "Exporting composition…", "Composition export cancelled.", async cancellationToken =>
        {
            // The workspace can change while preview playback is being quiesced. The export
            // workflow owns the captured target once invoked, but it must never start for a
            // newly-opened project or a changed composition revision.
            if (!_host.IsCurrentCompositionTarget(target)) return;
            var path = await _operations.ExportVirtualVideoAsync(
                target.Composition,
                target.Revision.Id,
                destinationPath,
                cancellationToken);
            if (CanPublishStatus(_cancellation, target, _activeSelectionIdentity))
                _host.SetStatus($"Exported Working Composition to {path}.");
        });
    }

    public void Cancel()
    {
        if (_cancellation is null || _userCancellationRequested) return;
        _userCancellationRequested = true;
        _host.SetRenderState("Cancelling…", canCancel: false);
        if (_activeTarget is { } target && CanPublishStatus(cancellation: _cancellation, target, _activeSelectionIdentity))
            _host.SetStatus("Cancelling composition render…");
        _cancellation.Cancel();
    }

    /// <summary>
    /// Makes a project switch unable to publish results from the prior project. Cleanup still
    /// owns the active cancellation source, so another render cannot start until it completes.
    /// </summary>
    public void ResetForProjectChange()
    {
        _operationGeneration++;
        _cancellation?.Cancel();
        _host.SetRenderState(null, canCancel: false);
        _host.RefreshCompositionActions();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operationGeneration++;
        _cancellation?.Cancel();
    }

    private bool TryCaptureTarget(out CompositionRenderTarget target)
    {
        if (_workspace.Project is null || _workspace.Location is null)
        {
            target = default!;
            return false;
        }

        var (composition, revision, _) = new WorkingCompositionService(_workspace).GetCurrent();
        target = new CompositionRenderTarget(_workspace.Project, _workspace.Location, composition, revision);
        return true;
    }

    private async Task RunAsync(
        CompositionRenderTarget target,
        string activeStatus,
        string cancelledStatus,
        Func<CancellationToken, Task> action)
    {
        if (_cancellation is not null) return;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _activeTarget = target;
        _userCancellationRequested = false;
        var operationGeneration = ++_operationGeneration;
        _activeSelectionIdentity = _host.CaptureProjectMediaSelectionIdentity();
        _host.SetRenderState(activeStatus, canCancel: true);
        _host.SetStatus(activeStatus);
        IDisposable? interactionSuppression = null;
        IDisposable? auditionQuiescence = null;
        try
        {
            interactionSuppression = _host.SuppressPreviewInteractions();
            auditionQuiescence = await _host.PauseAndQuiescePreviewAsync(cancellation.Token);
            if (!IsCurrentOperation(cancellation, operationGeneration) || !_host.IsCurrentCompositionTarget(target)) return;
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (CanPublishStatus(cancellation, target, _activeSelectionIdentity, operationGeneration))
                _host.SetStatus(cancelledStatus);
        }
        catch (Exception exception)
        {
            if (CanPublishStatus(cancellation, target, _activeSelectionIdentity, operationGeneration))
                _host.ShowError("Composition render failed", exception);
        }
        finally
        {
            auditionQuiescence?.Dispose();
            interactionSuppression?.Dispose();
            var currentOperation = IsCurrentOperation(cancellation, operationGeneration);
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
                _activeTarget = null;
                _activeSelectionIdentity = null;
            }
            if (currentOperation)
                _host.SetRenderState(null, canCancel: false);
            _host.RefreshCompositionActions();
        }
    }

    private bool IsCurrentOperation(CancellationTokenSource cancellation, long operationGeneration) =>
        ReferenceEquals(_cancellation, cancellation) && _operationGeneration == operationGeneration;

    private object? _activeSelectionIdentity;

    private bool CanPublishStatus(
        CancellationTokenSource? cancellation,
        CompositionRenderTarget target,
        object? selectionIdentity,
        long? operationGeneration = null) =>
        cancellation is not null &&
        ReferenceEquals(_cancellation, cancellation) &&
        (operationGeneration is null || _operationGeneration == operationGeneration.Value) &&
        _host.IsCurrentCompositionTarget(target) &&
        _host.IsSameProjectMediaSelection(selectionIdentity);
}

internal sealed record CompositionRenderTarget(
    VideoProject Project,
    ProjectLocation Location,
    ProjectAsset Composition,
    RecipeRevision Revision)
{
    public Guid ProjectId => Project.Id;
}

internal interface ICompositionRenderHost
{
    bool IsCurrentCompositionTarget(CompositionRenderTarget target);
    bool CanAdoptBakedPreview(CompositionRenderTarget target);
    object? CaptureProjectMediaSelectionIdentity();
    bool IsSameProjectMediaSelection(object? selectionIdentity);
    string? PromptExportPath(CompositionRenderTarget target);
    IDisposable SuppressPreviewInteractions();
    Task<IDisposable> PauseAndQuiescePreviewAsync(CancellationToken cancellationToken);
    void AdoptBakedPreview(MaterializedMediaLease lease, CompositionRenderTarget target);
    void SetRenderState(string? status, bool canCancel);
    void RefreshCompositionActions();
    void SetStatus(string status);
    void ShowError(string title, Exception exception);
}
