using ReelForge.App.Views.Editing;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class CompositionRenderCoordinatorTests
{
    [Fact]
    public async Task PreviewAsyncDoesNotStartWhenTheCompositionIsNotSelected()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Host.CanAdopt = false;

        await harness.Coordinator.PreviewAsync();

        Assert.Equal(0, harness.Materializer.CallCount);
        Assert.Empty(harness.Host.RenderStates);
    }

    [Fact]
    public async Task PreviewAsyncTransfersOneLeaseToTheHost()
    {
        var harness = await RenderHarness.CreateAsync();
        var lease = harness.Materializer.CreateLease();
        harness.Materializer.Complete(lease);

        await harness.Coordinator.PreviewAsync();

        Assert.Same(lease, Assert.Single(harness.Host.AdoptedLeases));
        Assert.Equal(0, harness.Materializer.ReleaseCount);
        await lease.DisposeAsync();
        Assert.Equal(1, harness.Materializer.ReleaseCount);
        Assert.True(harness.Host.InteractionSuppressionDisposed);
        Assert.True(harness.Host.AuditionQuiescenceDisposed);
        Assert.Equal((string?)null, harness.Host.RenderStates.Last().Status);
    }

    [Fact]
    public async Task PreviewAsyncRemembersOnlyAnAdoptedBakedPreview()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Materializer.Complete(harness.Materializer.CreateLease());

        await harness.Coordinator.PreviewAsync();

        Assert.Equal(1, harness.Host.RememberedPreviewCount);
        Assert.Single(harness.Host.AdoptedLeases);
    }

    [Fact]
    public async Task PreviewAsyncDoesNotRememberAStaleOrCancelledPreview()
    {
        var harness = await RenderHarness.CreateAsync();
        var preview = harness.Coordinator.PreviewAsync();
        await harness.Materializer.WaitUntilStartedAsync();
        harness.Host.CanAdopt = false;
        harness.Materializer.Complete(harness.Materializer.CreateLease());

        await preview;

        Assert.Equal(0, harness.Host.RememberedPreviewCount);
    }

    [Fact]
    public async Task PreviewAsyncKeepsAnAdoptedPreviewWhenRememberingReportsANonfatalWarning()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Host.RememberWarning = "settings locked";
        harness.Materializer.Complete(harness.Materializer.CreateLease());

        await harness.Coordinator.PreviewAsync();

        Assert.Single(harness.Host.AdoptedLeases);
        Assert.Empty(harness.Host.Errors);
        Assert.Contains("Working Composition preview is ready.", harness.Host.Statuses[^1], StringComparison.Ordinal);
        Assert.Contains("settings locked", harness.Host.Statuses[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewAsyncDoesNotPublishLateSuccessActionsAfterRememberingWhenTargetChanges()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Host.BlockRemember = true;
        harness.Materializer.Complete(harness.Materializer.CreateLease());

        var preview = harness.Coordinator.PreviewAsync();
        await harness.Host.WaitUntilRememberingAsync();
        harness.Host.CurrentTarget = false;
        harness.Host.CompleteRemembering();

        await preview;

        Assert.Equal(["Rendering preview…"], harness.Host.Statuses);
        Assert.Equal(1, harness.Host.RefreshCount);
    }

    [Fact]
    public async Task PreviewAsyncDisposesLateLeaseWhenSelectionBecomesStale()
    {
        var harness = await RenderHarness.CreateAsync();
        var preview = harness.Coordinator.PreviewAsync();
        await harness.Materializer.WaitUntilStartedAsync();
        harness.Host.CanAdopt = false;
        harness.Materializer.Complete(harness.Materializer.CreateLease());

        await preview;

        Assert.Empty(harness.Host.AdoptedLeases);
        Assert.Equal(1, harness.Materializer.ReleaseCount);
        Assert.DoesNotContain(harness.Host.Statuses, message => message.Contains("ready", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task StaleTargetWhileQuiescingDoesNotStartMaterialization()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Host.BlockQuiescence = true;
        var preview = harness.Coordinator.PreviewAsync();
        await harness.Host.WaitUntilQuiescingAsync();
        harness.Host.CurrentTarget = false;
        harness.Host.CompleteQuiescence();

        await preview;

        Assert.Equal(0, harness.Materializer.CallCount);
        Assert.Empty(harness.Host.AdoptedLeases);
        Assert.Empty(harness.Host.Errors);
    }

    [Fact]
    public async Task ExportAsyncRunsWhenAnotherProjectMediaItemIsSelected()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Host.CanAdopt = false;
        harness.Host.ExportPath = "C:\\exports\\composition.mp4";

        await harness.Coordinator.ExportAsync();

        Assert.Equal(1, harness.Operations.ExportCount);
        Assert.Contains(harness.Host.Statuses, message => message.Contains("Exported Working Composition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsyncSuppressesSuccessStatusWhenSelectionChanges()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Host.ExportPath = "C:\\exports\\composition.mp4";
        harness.Operations.BlockExport = true;
        var export = harness.Coordinator.ExportAsync();
        await harness.Operations.WaitUntilStartedAsync();
        harness.Host.SelectionMatches = false;
        harness.Operations.Complete();

        await export;

        Assert.Equal(1, harness.Operations.ExportCount);
        Assert.DoesNotContain(harness.Host.Statuses, message => message.Contains("Exported Working Composition", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelRestoresRenderStateAndPreventsLatePreviewPublication()
    {
        var harness = await RenderHarness.CreateAsync();
        harness.Materializer.CancelWhenRequested = true;
        var preview = harness.Coordinator.PreviewAsync();
        await harness.Materializer.WaitUntilStartedAsync();

        harness.Coordinator.Cancel();
        await preview;

        Assert.False(harness.Coordinator.IsRendering);
        Assert.Empty(harness.Host.AdoptedLeases);
        Assert.True(harness.Host.InteractionSuppressionDisposed);
        Assert.True(harness.Host.AuditionQuiescenceDisposed);
        Assert.Equal((string?)null, harness.Host.RenderStates.Last().Status);
        Assert.Contains(harness.Host.Statuses, message => message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResetForProjectChangeSuppressesLateFailure()
    {
        var harness = await RenderHarness.CreateAsync();
        var preview = harness.Coordinator.PreviewAsync();
        await harness.Materializer.WaitUntilStartedAsync();
        harness.Coordinator.ResetForProjectChange();
        harness.Materializer.Fail(new InvalidOperationException("late failure"));

        await preview;

        Assert.Empty(harness.Host.Errors);
        Assert.False(harness.Coordinator.IsRendering);
        Assert.True(harness.Host.InteractionSuppressionDisposed);
        Assert.True(harness.Host.AuditionQuiescenceDisposed);
    }

    [Fact]
    public async Task CurrentFailureIsReportedButASecondOperationIsBlockedUntilCleanup()
    {
        var harness = await RenderHarness.CreateAsync();
        var preview = harness.Coordinator.PreviewAsync();
        await harness.Materializer.WaitUntilStartedAsync();
        harness.Host.ExportPath = "C:\\exports\\composition.mp4";

        await harness.Coordinator.ExportAsync();
        Assert.Equal(0, harness.Operations.ExportCount);
        Assert.Equal(0, harness.Host.ExportPromptCount);

        harness.Materializer.Fail(new InvalidOperationException("render failed"));
        await preview;

        Assert.Single(harness.Host.Errors);
        Assert.False(harness.Coordinator.IsRendering);
    }

    private sealed class RenderHarness
    {
        private RenderHarness(ProjectWorkspace workspace, ControlledMaterializer materializer, TestOperations operations, TestHost host)
        {
            Materializer = materializer;
            Operations = operations;
            Host = host;
            Coordinator = new CompositionRenderCoordinator(workspace, materializer, operations, host);
        }

        public ControlledMaterializer Materializer { get; }
        public TestOperations Operations { get; }
        public TestHost Host { get; }
        public CompositionRenderCoordinator Coordinator { get; }

        public static async Task<RenderHarness> CreateAsync()
        {
            var project = new VideoProject { Name = "Render test" };
            var location = new ProjectLocation("C:\\projects\\render-test", "C:\\projects\\render-test\\render-test.rfp");
            var workspace = new ProjectWorkspace(new TestStore(project, location), new UnusedImporter());
            await workspace.OpenAsync(location.ProjectFilePath);
            var source = new ProjectAsset
            {
                DisplayName = "source.mp4",
                FileName = "source.mp4",
                MediaType = MediaType.Video,
                StorageKind = AssetStorageKind.Physical,
                DurationSeconds = 2,
                Physical = new PhysicalAssetStorage { RelativePath = "media\\source.mp4" }
            };
            project.AddAsset(source);
            await new WorkingCompositionService(workspace).CreateInitialAsync(source.Id);
            return new RenderHarness(workspace, new ControlledMaterializer(), new TestOperations(), new TestHost());
        }
    }

    private sealed class ControlledMaterializer : IMediaMaterializer
    {
        private readonly TaskCompletionSource<MaterializedMediaLease> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool CancelWhenRequested { get; set; }

        public async Task<MaterializedMediaLease> MaterializeAsync(VideoProject project, ProjectLocation location, MaterializationRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            _started.TrySetResult();
            if (CancelWhenRequested)
                return await Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<MaterializedMediaLease>(_ => throw new OperationCanceledException(cancellationToken), TaskScheduler.Default);
            return await _completion.Task;
        }

        public Task WaitUntilStartedAsync() => _started.Task;
        public MaterializedMediaLease CreateLease() => new("C:\\cache\\composition.mp4", new ContentIdentity(), null, false, () =>
        {
            ReleaseCount++;
            return ValueTask.CompletedTask;
        });
        public void Complete(MaterializedMediaLease lease) => _completion.TrySetResult(lease);
        public void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class TestOperations : ICompositionRenderOperations
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExportCount { get; private set; }
        public bool BlockExport { get; set; }
        public async Task<string> ExportVirtualVideoAsync(ProjectAsset asset, Guid recipeRevisionId, string destinationPath, CancellationToken cancellationToken = default)
        {
            ExportCount++;
            _started.TrySetResult();
            return BlockExport ? await _completion.Task : destinationPath;
        }
        public Task WaitUntilStartedAsync() => _started.Task;
        public void Complete() => _completion.TrySetResult("C:\\exports\\composition.mp4");
    }

    private sealed class TestHost : ICompositionRenderHost
    {
        public bool CanAdopt { get; set; } = true;
        public bool CurrentTarget { get; set; } = true;
        public bool SelectionMatches { get; set; } = true;
        public bool BlockQuiescence { get; set; }
        public string? ExportPath { get; set; }
        public int ExportPromptCount { get; private set; }
        public List<MaterializedMediaLease> AdoptedLeases { get; } = [];
        public List<(string? Status, bool CanCancel)> RenderStates { get; } = [];
        public List<string> Statuses { get; } = [];
        public List<Exception> Errors { get; } = [];
        public bool InteractionSuppressionDisposed { get; private set; }
        public bool AuditionQuiescenceDisposed { get; private set; }
        public int RememberedPreviewCount { get; private set; }
        public string? RememberWarning { get; set; }
        public bool BlockRemember { get; set; }
        public int RefreshCount { get; private set; }

        private TaskCompletionSource? _quiescing;
        private TaskCompletionSource<IDisposable>? _quiescenceCompletion;
        private TaskCompletionSource? _remembering;
        private TaskCompletionSource<string?>? _rememberCompletion;

        public bool IsCurrentCompositionTarget(CompositionRenderTarget target) => CurrentTarget;
        public bool CanAdoptBakedPreview(CompositionRenderTarget target) => CanAdopt && CurrentTarget;
        public object? CaptureProjectMediaSelectionIdentity() => "selection";
        public bool IsSameProjectMediaSelection(object? selectionIdentity) => SelectionMatches;
        public string? PromptExportPath(CompositionRenderTarget target)
        {
            ExportPromptCount++;
            return ExportPath;
        }
        public IDisposable SuppressPreviewInteractions() => new CallbackDisposable(() => InteractionSuppressionDisposed = true);
        public Task<IDisposable> PauseAndQuiescePreviewAsync(CancellationToken cancellationToken)
        {
            if (!BlockQuiescence)
                return Task.FromResult<IDisposable>(new CallbackDisposable(() => AuditionQuiescenceDisposed = true));

            _quiescing ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _quiescenceCompletion ??= new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
            _quiescing.TrySetResult();
            return _quiescenceCompletion.Task;
        }
        public Task WaitUntilQuiescingAsync() => _quiescing?.Task ?? throw new InvalidOperationException("Quiescence is not blocked.");
        public void CompleteQuiescence() => _quiescenceCompletion?.TrySetResult(new CallbackDisposable(() => AuditionQuiescenceDisposed = true));
        public void AdoptBakedPreview(MaterializedMediaLease lease, CompositionRenderTarget target) => AdoptedLeases.Add(lease);
        public Task<string?> RememberBakedCompositionPreviewAsync(CompositionRenderTarget target)
        {
            RememberedPreviewCount++;
            var warning = RememberWarning is null
                ? null
                : $" ReelForge could not remember this composition preview for the next launch: {RememberWarning}";
            if (!BlockRemember) return Task.FromResult(warning);
            _remembering ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _rememberCompletion ??= new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _remembering.TrySetResult();
            return _rememberCompletion.Task;
        }

        public Task WaitUntilRememberingAsync() => _remembering?.Task ?? throw new InvalidOperationException("Remembering is not blocked.");
        public void CompleteRemembering() => _rememberCompletion?.TrySetResult(RememberWarning is null
            ? null
            : $" ReelForge could not remember this composition preview for the next launch: {RememberWarning}");

        public void SetRenderState(string? status, bool canCancel) => RenderStates.Add((status, canCancel));
        public void RefreshCompositionActions() => RefreshCount++;
        public void SetStatus(string status) => Statuses.Add(status);
        public void ShowError(string title, Exception exception) => Errors.Add(exception);
    }

    private sealed class CallbackDisposable(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private sealed class TestStore(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) => Task.FromResult((project, location));
        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.FromResult((project, location));
        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
