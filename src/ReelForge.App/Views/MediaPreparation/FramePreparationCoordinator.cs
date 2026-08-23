using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReelForge.App.Views.MediaPreview;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.MediaPreparation;

public sealed class FramePreparationCoordinator : IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly ExactVideoFrameService _exactFrameService;
    private readonly IMediaMaterializer _mediaMaterializer;
    private readonly MediaPreparationPanel _panel;
    private readonly MediaPreviewPanel _preview;
    private readonly SemaphoreSlim _navigationGate;
    private readonly ObservableCollection<FrameContactListItem> _contactFrames = [];
    private readonly ObservableCollection<SavedFrameListItem> _savedFrames = [];
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<VideoPresentationFrame> _indexedFrames = [];
    private double? _pendingContactTimestamp;
    private Guid? _sourceProjectId;
    private Guid? _sourceAssetId;
    private string? _sourceContentHash;
    private int _pendingKeyboardSteps;
    private bool _isKeyboardNavigationRunning;
    private bool _suppressSelectionPrefetch;
    private bool _disposed;

    public FramePreparationCoordinator(
        ProjectWorkspace workspace,
        ExactVideoFrameService exactFrameService,
        IMediaMaterializer mediaMaterializer,
        MediaPreparationPanel panel,
        MediaPreviewPanel preview,
        SemaphoreSlim navigationGate)
    {
        _workspace = workspace;
        _exactFrameService = exactFrameService;
        _mediaMaterializer = mediaMaterializer;
        _panel = panel;
        _preview = preview;
        _navigationGate = navigationGate;
        _panel.SetItemsSources(_contactFrames, _savedFrames);

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += DebounceTimer_Tick;
        _panel.ContactFrameSelected += ContactFrameSelected;
        _panel.FrameStepRequested += FrameStepRequested;
        _panel.FirstFrameRequested += FirstFrameRequested;
        _panel.LastFrameRequested += LastFrameRequested;
    }

    public event EventHandler<FramePreparationStatusEventArgs>? StatusChanged;
    public event EventHandler<SavedFramesProjectedEventArgs>? SavedFramesProjected;

    public Guid? CurrentSourceAssetId => _sourceAssetId;

    public bool HasCurrentSource(Guid sourceAssetId) => _sourceAssetId == sourceAssetId;

    public bool TryGetSelectedFrame(out FramePreparationSelection selection)
    {
        if (_panel.SelectedContactFrame is not { } selected ||
            _sourceAssetId is not { } sourceAssetId ||
            string.IsNullOrWhiteSpace(_sourceContentHash))
        {
            selection = null!;
            return false;
        }

        selection = new FramePreparationSelection(sourceAssetId, _sourceContentHash, selected.Frame);
        return true;
    }

    public async Task LoadAsync(ProjectAsset asset, Guid? selectedProjectId)
    {
        if (asset.MediaType != MediaType.Video || asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
        {
            _panel.SetWorkspaceStatus("Select a physical video");
            return;
        }

        var path = _workspace.GetAbsoluteAssetPath(asset);
        if (!File.Exists(path))
        {
            _panel.SetWorkspaceStatus("Source media is missing");
            return;
        }

        var cancellation = ReplaceCancellation();
        _sourceProjectId = selectedProjectId;
        _sourceAssetId = asset.Id;
        _sourceContentHash = null;
        _indexedFrames = [];
        _contactFrames.Clear();
        _savedFrames.Clear();
        _panel.SetWorkspaceStatus("Indexing decoded frames…");
        _panel.ShowContactFramesMessage("Reading exact presentation frames…");
        try
        {
            await using var verifiedSource = await _mediaMaterializer.MaterializeAsync(
                _workspace.Project!,
                _workspace.Location!,
                new MaterializationRequest(new AssetMaterializationTarget(asset.Id), MaterializationPurpose.Preview),
                cancellation.Token);
            if (!IsCurrentSession(cancellation, selectedProjectId, asset.Id)) return;
            var contentHash = verifiedSource.ContentIdentity.Sha256
                ?? throw new InvalidDataException("The selected video does not have a verified SHA-256 identity.");
            _sourceContentHash = contentHash;
            var indexedFrames = await _exactFrameService.IndexWindowAsync(
                path,
                Math.Max(0, _preview.PositionSeconds),
                cancellationToken: cancellation.Token);
            if (!IsCurrentSession(cancellation, selectedProjectId, asset.Id, contentHash)) return;
            _indexedFrames = indexedFrames;
            await _workspace.SaveAsync(cancellation.Token);
            if (!IsCurrentSession(cancellation, selectedProjectId, asset.Id, contentHash)) return;

            _panel.SetWorkspaceStatus($"{_indexedFrames.Count:N0} nearby decoded frames");
            _panel.HideContactFramesMessage();
            await RefreshContactFramesAsync(_preview.PositionSeconds, cancellation.Token);
            await RefreshSavedFramesAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!IsCurrentSession(cancellation, selectedProjectId, asset.Id)) return;
            _panel.SetWorkspaceStatus("Frame browser unavailable");
            _panel.ShowContactFramesMessage(exception.Message);
            PublishStatus($"Precision frame browsing is unavailable: {exception.Message}");
        }
    }

    public void Reset()
    {
        var wasPreparing = _panel.IsPreparing;
        if (wasPreparing) _preview.ExitPrecisionMode();
        _panel.ResetPresentation();
        _debounceTimer.Stop();
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _indexedFrames = [];
        _pendingContactTimestamp = null;
        _pendingKeyboardSteps = 0;
        _isKeyboardNavigationRunning = false;
        _sourceProjectId = null;
        _sourceAssetId = null;
        _sourceContentHash = null;
        _contactFrames.Clear();
        _savedFrames.Clear();
    }

    public void ScheduleContactFrameRefresh(double? targetSeconds = null)
    {
        if (_indexedFrames.Count == 0 || _sourceAssetId is null) return;
        _pendingContactTimestamp = targetSeconds ?? _preview.PositionSeconds;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public async Task RefreshSavedFramesAsync(CancellationToken cancellationToken = default)
    {
        if (_workspace.Project is not { } project || _workspace.Location is null ||
            _sourceProjectId != project.Id || _sourceAssetId is not { } sourceAssetId ||
            string.IsNullOrWhiteSpace(_sourceContentHash)) return;
        var contentHash = _sourceContentHash;
        var source = project.Assets.Single(asset => asset.Id == sourceAssetId);
        var sourcePath = _workspace.GetAbsoluteAssetPath(source);
        var selectedAnchorId = _panel.SelectedSavedFrame?.Anchor.Id;
        var results = new List<SavedFrameListItem>();
        foreach (var anchor in project.Anchors.Where(anchor => !anchor.IsArchived))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (anchor.CurrentRevisionId is not { } revisionId) continue;
            var revision = project.AnchorRevisions.SingleOrDefault(candidate => candidate.Id == revisionId);
            if (revision is null || revision.SourceAssetId != sourceAssetId) continue;
            BitmapSource? thumbnail = null;
            string? error = null;
            try
            {
                await using var lease = await _exactFrameService.ExtractAsync(
                    sourcePath,
                    contentHash,
                    revision,
                    MaterializationPurpose.Thumbnail,
                    "saved-frame",
                    cancellationToken);
                thumbnail = LoadBitmap(lease.Path);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                error = exception.Message;
            }
            results.Add(new SavedFrameListItem(anchor, revision, thumbnail, error));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSource(project.Id, sourceAssetId, contentHash)) return;
        _savedFrames.Clear();
        foreach (var item in results.OrderBy(item => item.Revision.PresentationTimestamp))
            _savedFrames.Add(item);
        SavedFramesProjected?.Invoke(this, new SavedFramesProjectedEventArgs(_savedFrames.ToArray()));
        _panel.SetSavedFramesEmpty(_savedFrames.Count == 0);
        _panel.SelectSavedFrame(selectedAnchorId is { } id
            ? _savedFrames.FirstOrDefault(item => item.Anchor.Id == id)
            : null);
    }

    public void SelectSavedFrameRevision(Guid revisionId) =>
        _panel.SelectSavedFrame(_savedFrames.FirstOrDefault(item => item.Revision.Id == revisionId));

    public void JumpToSavedFrame(SavedFrameListItem item)
    {
        SeekPreview(item.Revision.TimestampSeconds);
        ScheduleContactFrameRefresh(item.Revision.TimestampSeconds);
    }

    public async Task<SavedFrameMutation?> SaveSelectedFrameAsync(CancellationToken cancellationToken = default)
    {
        if (!TryGetSelectedFrame(out var selection)) return null;
        var saved = await new SavedFrameService(_workspace)
            .CreateAsync(selection.ExactPosition, cancellationToken);
        return saved;
    }

    public bool SetSelectedClipBoundary(bool isStart)
    {
        if (!TryGetSelectedFrame(out var selection)) return false;
        _panel.SetClipBoundary(selection.ExactPosition, isStart);
        return true;
    }

    public Task<ProjectAsset> CreateSavedClipAsync(
        MediaPreparationClipDraft draft,
        CancellationToken cancellationToken = default)
    {
        if (_sourceAssetId is not { } sourceAssetId)
            throw new InvalidOperationException("Select a physical video first.");
        return new SavedClipService(_workspace).CreateAsync(
            draft.Name,
            sourceAssetId,
            draft.Start,
            draft.End,
            cancellationToken);
    }

    public async Task<SavedFrameMutation> UpdateSavedFrameAsync(
        SavedFrameListItem item,
        string? label,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var updated = await new SavedFrameService(_workspace)
            .UpdateAsync(item.Anchor.Id, label, notes, cancellationToken);
        _panel.RefreshSavedFrames();
        return updated;
    }

    public async Task<AnchorRemovalDisposition> RemoveSavedFrameAsync(
        Guid anchorId,
        CancellationToken cancellationToken = default)
    {
        return await new SavedFrameService(_workspace).RemoveAsync(anchorId, cancellationToken);
    }

    private CancellationTokenSource ReplaceCancellation()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        return _cancellation;
    }

    private bool IsCurrentSession(
        CancellationTokenSource cancellation,
        Guid? projectId,
        Guid sourceAssetId,
        string? contentHash = null) =>
        ReferenceEquals(_cancellation, cancellation) &&
        !cancellation.IsCancellationRequested &&
        IsCurrentSource(projectId, sourceAssetId, contentHash);

    private bool IsCurrentSource(Guid? projectId, Guid sourceAssetId, string? contentHash = null) =>
        _workspace.Project?.Id == projectId &&
        _sourceProjectId == projectId &&
        _sourceAssetId == sourceAssetId &&
        (contentHash is null || string.Equals(_sourceContentHash, contentHash, StringComparison.Ordinal));

    private async void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        if (_indexedFrames.Count == 0 || _sourceAssetId is null) return;
        var cancellation = ReplaceCancellation();
        try
        {
            var target = _pendingContactTimestamp ?? _preview.PositionSeconds;
            _pendingContactTimestamp = null;
            await RunNavigationAsync(async token =>
            {
                await EnsureFrameWindowAsync(target, token);
                await RefreshContactFramesAsync(target, token);
            }, cancellation.Token);
            await RefreshSavedFramesAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishStatus($"Could not refresh precision frames: {exception.Message}");
        }
    }

    private async Task EnsureFrameWindowAsync(double centerSeconds, CancellationToken cancellationToken)
    {
        if (_workspace.Project is null || _sourceAssetId is not { } sourceAssetId) return;
        var source = _workspace.Project.Assets.Single(asset => asset.Id == sourceAssetId);
        var path = _workspace.GetAbsoluteAssetPath(source);
        var duration = source.DurationSeconds ?? source.Encoding?.DurationSeconds ?? _preview.MaximumPositionSeconds;
        if (duration > 0) centerSeconds = Math.Clamp(centerSeconds, 0, duration);
        var window = await _exactFrameService.IndexWindowAsync(
            path,
            Math.Max(0, centerSeconds),
            cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSource(_sourceProjectId, sourceAssetId, _sourceContentHash)) return;
        _indexedFrames = _indexedFrames.Concat(window)
            .GroupBy(frame => (frame.VideoStreamIndex, frame.PresentationTimestamp))
            .Select(group => group.First())
            .OrderBy(frame => frame.PresentationTimestamp)
            .ToArray();
        _panel.SetWorkspaceStatus($"{_indexedFrames.Count:N0} nearby decoded frames");
    }

    private async Task RefreshContactFramesAsync(double centerSeconds, CancellationToken cancellationToken)
    {
        if (_workspace.Project is not { } project || _workspace.Location is null ||
            _sourceProjectId != project.Id || _sourceAssetId is not { } sourceAssetId ||
            string.IsNullOrWhiteSpace(_sourceContentHash) || _indexedFrames.Count == 0) return;
        var contentHash = _sourceContentHash;
        var source = project.Assets.Single(asset => asset.Id == sourceAssetId);
        var path = _workspace.GetAbsoluteAssetPath(source);
        var selectedFrames = ExactFrameContactWindow.Select(_indexedFrames, centerSeconds);
        if (selectedFrames.Count == 0) return;

        _panel.HideContactFramesMessage();
        var center = selectedFrames.MinBy(frame => Math.Abs(frame.TimestampSeconds - centerSeconds))!;
        var existingItems = _contactFrames.ToDictionary(
            item => (item.Frame.VideoStreamIndex, item.Frame.PresentationTimestamp));
        var missingFrames = selectedFrames.Where(frame =>
            !existingItems.ContainsKey((frame.VideoStreamIndex, frame.PresentationTimestamp))).ToArray();
        var createdItems = await Task.WhenAll(missingFrames.Select(frame =>
            CreateContactItemAsync(path, sourceAssetId, contentHash, frame, cancellationToken)));
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrentSource(project.Id, sourceAssetId, contentHash)) return;
        foreach (var item in createdItems)
            existingItems[(item.Frame.VideoStreamIndex, item.Frame.PresentationTimestamp)] = item;
        var desiredItems = selectedFrames.Select(frame =>
            existingItems[(frame.VideoStreamIndex, frame.PresentationTimestamp)]).ToArray();

        _suppressSelectionPrefetch = true;
        try
        {
            for (var index = 0; index < desiredItems.Length; index++)
            {
                var desired = desiredItems[index];
                if (index < _contactFrames.Count && ReferenceEquals(_contactFrames[index], desired)) continue;
                var existingIndex = _contactFrames.IndexOf(desired);
                if (existingIndex >= 0) _contactFrames.Move(existingIndex, index);
                else _contactFrames.Insert(index, desired);
            }
            while (_contactFrames.Count > desiredItems.Length)
                _contactFrames.RemoveAt(_contactFrames.Count - 1);
            _panel.SelectContactFrame(desiredItems.Single(item =>
                item.Frame.VideoStreamIndex == center.VideoStreamIndex &&
                item.Frame.PresentationTimestamp == center.PresentationTimestamp));
        }
        finally
        {
            _suppressSelectionPrefetch = false;
        }
    }

    private async Task<FrameContactListItem> CreateContactItemAsync(
        string sourcePath,
        Guid sourceAssetId,
        string sourceContentHash,
        VideoPresentationFrame frame,
        CancellationToken cancellationToken)
    {
        var revision = TransientFrameAnchorRevisionFactory.Create(sourceAssetId, sourceContentHash, frame);
        await using var lease = await _exactFrameService.ExtractAsync(
            sourcePath,
            sourceContentHash,
            revision,
            MaterializationPurpose.Thumbnail,
            "contact-strip",
            cancellationToken);
        return new FrameContactListItem(frame, LoadBitmap(lease.Path));
    }

    private async void ContactFrameSelected(object? sender, FrameContactSelectionEventArgs e)
    {
        if (e.Item is not { } item || !_preview.HasVideoSource) return;
        SeekPreview(item.Frame.TimestampSeconds);
        if (_suppressSelectionPrefetch) return;
        var cancellationToken = _cancellation?.Token ?? CancellationToken.None;
        try
        {
            await RunNavigationAsync(async token =>
            {
                if (_panel.SelectedContactFrame is not { } latest) return;
                await EnsureAdjacentFramesAvailableAsync(latest.Frame, direction: 0, token);
                await RefreshContactFramesAsync(latest.Frame.TimestampSeconds, token);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishStatus($"Could not navigate exact frames: {exception.Message}");
        }
    }

    private void FrameStepRequested(object? sender, FrameStepRequestedEventArgs e)
    {
        if (_panel.SelectedContactFrame is null) return;
        _pendingKeyboardSteps += e.Steps;
        if (_isKeyboardNavigationRunning) return;
        _isKeyboardNavigationRunning = true;
        _ = ProcessKeyboardNavigationAsync();
    }

    private async Task ProcessKeyboardNavigationAsync()
    {
        try
        {
            while (_pendingKeyboardSteps != 0 && _panel.IsPreparing)
            {
                var steps = _pendingKeyboardSteps;
                _pendingKeyboardSteps = 0;
                var cancellationToken = _cancellation?.Token ?? CancellationToken.None;
                await RunNavigationAsync(async token =>
                {
                    if (_panel.SelectedContactFrame is not { } selected) return;
                    var targetSeconds = EstimateKeyboardTargetSeconds(selected.Frame, steps);
                    var nearestIndex = ExactFrameContactWindow.FindNearestIndex(_indexedFrames, targetSeconds);
                    var localInterval = EstimateFrameIntervalSeconds(selected.Frame);
                    if (nearestIndex < 0 ||
                        Math.Abs(_indexedFrames[nearestIndex].TimestampSeconds - targetSeconds) > localInterval * 0.6)
                    {
                        await EnsureFrameWindowAsync(targetSeconds, token);
                        nearestIndex = ExactFrameContactWindow.FindNearestIndex(_indexedFrames, targetSeconds);
                    }
                    if (nearestIndex < 0) return;
                    var target = _indexedFrames[nearestIndex];
                    await RefreshContactFramesAsync(target.TimestampSeconds, token);
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PublishStatus($"Could not navigate exact frames: {exception.Message}");
        }
        finally
        {
            _isKeyboardNavigationRunning = false;
            if (_pendingKeyboardSteps != 0 && _panel.IsPreparing)
            {
                _isKeyboardNavigationRunning = true;
                _ = ProcessKeyboardNavigationAsync();
            }
        }
    }

    private async Task EnsureAdjacentFramesAvailableAsync(
        VideoPresentationFrame selected,
        int direction,
        CancellationToken cancellationToken)
    {
        var selectedIndex = FindIndexedFrame(selected);
        var needsEarlier = direction < 0 && selectedIndex <= 0;
        var needsLater = direction > 0 && selectedIndex >= _indexedFrames.Count - 1;
        var nearEitherEdge = direction == 0 &&
                             (selectedIndex < 4 || selectedIndex >= _indexedFrames.Count - 4);
        if (!needsEarlier && !needsLater && !nearEitherEdge) return;

        var source = _workspace.Project?.Assets.SingleOrDefault(asset => asset.Id == _sourceAssetId);
        var duration = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ?? _preview.MaximumPositionSeconds;
        var probeCenter = direction switch
        {
            < 0 => selected.TimestampSeconds - 2,
            > 0 => selected.TimestampSeconds + 2,
            _ when selectedIndex < 4 => selected.TimestampSeconds - 2,
            _ => selected.TimestampSeconds + 2
        };
        if (duration > 0) probeCenter = Math.Clamp(probeCenter, 0, duration);
        else probeCenter = Math.Max(0, probeCenter);
        await EnsureFrameWindowAsync(probeCenter, cancellationToken);
    }

    private int FindIndexedFrame(VideoPresentationFrame frame) => _indexedFrames.ToList().FindIndex(candidate =>
        candidate.VideoStreamIndex == frame.VideoStreamIndex &&
        candidate.PresentationTimestamp == frame.PresentationTimestamp);

    private double EstimateKeyboardTargetSeconds(VideoPresentationFrame selected, int steps)
    {
        var source = _workspace.Project?.Assets.SingleOrDefault(asset => asset.Id == _sourceAssetId);
        var duration = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ?? _preview.MaximumPositionSeconds;
        var target = selected.TimestampSeconds + steps * EstimateFrameIntervalSeconds(selected);
        return duration > 0 ? Math.Clamp(target, 0, duration) : Math.Max(0, target);
    }

    private double EstimateFrameIntervalSeconds(VideoPresentationFrame selected)
    {
        var currentIndex = FindIndexedFrame(selected);
        if (currentIndex >= 0 && currentIndex + 1 < _indexedFrames.Count)
        {
            var nextInterval = _indexedFrames[currentIndex + 1].TimestampSeconds - selected.TimestampSeconds;
            if (nextInterval is > 0 and < 1) return nextInterval;
        }
        if (currentIndex > 0)
        {
            var priorInterval = selected.TimestampSeconds - _indexedFrames[currentIndex - 1].TimestampSeconds;
            if (priorInterval is > 0 and < 1) return priorInterval;
        }
        return 1d / 30;
    }

    private async Task RunNavigationAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            await action(cancellationToken);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private async void FirstFrameRequested(object? sender, EventArgs e) =>
        await SelectBoundaryFrameAsync(first: true);

    private async void LastFrameRequested(object? sender, EventArgs e) =>
        await SelectBoundaryFrameAsync(first: false);

    private async Task SelectBoundaryFrameAsync(bool first)
    {
        if (_workspace.Project is not { } project || _sourceAssetId is not { } sourceAssetId)
        {
            PublishStatus("Select a physical video first.");
            return;
        }
        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == sourceAssetId);
        if (asset is null)
        {
            PublishStatus("Select a physical video first.");
            return;
        }
        var target = first ? 0 : Math.Max(0, asset.DurationSeconds ?? _preview.MaximumPositionSeconds);
        var cancellation = ReplaceCancellation();
        try
        {
            await EnsureFrameWindowAsync(target, cancellation.Token);
            var candidates = await _exactFrameService.IndexWindowAsync(
                _workspace.GetAbsoluteAssetPath(asset),
                target,
                first ? 1 : 5,
                cancellation.Token);
            if (candidates.Count == 0)
                throw new InvalidDataException("No decoded presentation frame was found near the boundary.");
            var frame = first ? candidates[0] : candidates[^1];
            await RefreshContactFramesAsync(frame.TimestampSeconds, cancellation.Token);
            PublishStatus(first
                ? "Selected the first decoded presentation frame."
                : "Selected the final decodable presentation frame.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            PublishStatus($"Could not navigate exact frames: {exception.Message}");
        }
    }

    private void SeekPreview(double seconds)
    {
        if (!_preview.HasVideoSource) return;
        _preview.SeekVideo(seconds);
        _preview.ShowPosition(_preview.MediaPosition, _preview.MediaDuration);
    }

    private void PublishStatus(string message) =>
        StatusChanged?.Invoke(this, new FramePreparationStatusEventArgs(message));

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= DebounceTimer_Tick;
        _panel.ContactFrameSelected -= ContactFrameSelected;
        _panel.FrameStepRequested -= FrameStepRequested;
        _panel.FirstFrameRequested -= FirstFrameRequested;
        _panel.LastFrameRequested -= LastFrameRequested;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}

public sealed record FramePreparationSelection(
    Guid SourceAssetId,
    string SourceContentHash,
    VideoPresentationFrame Frame)
{
    public ExactFramePosition ExactPosition => new(
        SourceAssetId,
        SourceContentHash,
        Frame.VideoStreamIndex,
        Frame.PresentationTimestamp,
        Frame.TimeBaseNumerator,
        Frame.TimeBaseDenominator,
        Frame.FrameNumber);
}

public sealed class FramePreparationStatusEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class SavedFramesProjectedEventArgs(IReadOnlyList<SavedFrameListItem> items) : EventArgs
{
    public IReadOnlyList<SavedFrameListItem> Items { get; } = items;
}
