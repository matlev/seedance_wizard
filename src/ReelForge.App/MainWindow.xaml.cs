using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ReelForge.App.Bootstrap;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;
using Line = System.Windows.Shapes.Line;

namespace ReelForge.App;

internal enum MediaPreparationMode { None, SelectFrame, MakeClip }

public partial class MainWindow : Window, IDisposable
{
    private sealed record TimelineStickyContent(
        FrameworkElement Element,
        double ItemLeft,
        double ItemWidth,
        double MinimumTrailingWidth);

    private sealed record CompositionDraftSegment(
        Guid SegmentId,
        AssetRevisionReference Source,
        double TimelineStartSeconds,
        double SourceStartSeconds,
        double DurationSeconds,
        bool AudioEnabled);

    private const string ProjectMediaDragFormat = "ReelForge.ProjectMediaAssetId";
    private readonly ObservableCollection<ProjectMediaListItem> _assets = [];
    private readonly ObservableCollection<GenerationRecord> _generations = [];
    private readonly ObservableCollection<GenerationReferenceChoice> _referenceChoices = [];
    private readonly ObservableCollection<GenerationJobListItem> _jobs = [];
    private readonly ObservableCollection<FrameContactListItem> _contactFrames = [];
    private readonly ObservableCollection<SavedFrameListItem> _savedFrames = [];
    private readonly ObservableCollection<CompositionSegmentListItem> _compositionSegments = [];
    private readonly ObservableCollection<CompositionAudioClipListItem> _compositionAudioClips = [];
    private readonly ApplicationRuntime _runtime;
    private readonly List<TimelineStickyContent> _compositionTimelineStickyContent = [];
    private readonly IReadOnlyList<BitmapSource> _activeJobSpriteFrames;
    private readonly HashSet<Guid> _viewedTerminalJobIds = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _pendingSubmissionDelays = [];
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private readonly SemaphoreSlim _frameNavigationGate = new(1, 1);
    private IReadOnlyList<GenerationProviderChoice> _providerChoices = [];
    private readonly ProjectWorkspace _workspace;
    private readonly PortableProjectStore _projectStore;
    private readonly ProjectAssetTransferService _assetTransferService;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private readonly ExactVideoFrameService _exactFrameService;
    private readonly RecipeMediaMaterializer _mediaMaterializer;
    private readonly FfmpegAudioExtractionEngine _audioExtractionEngine;
    private GenerationWorkflow _generationWorkflow = null!;
    private IProviderAssetPreparationService? _providerPreparation;
    private readonly ISecretStore _secretStore;
    private readonly FileApplicationDiagnosticLog _diagnosticLog;
    private IVideoGenerationProvider _generationProvider;
    private readonly IMediaToolDiscovery _mediaToolDiscovery;
    private readonly IApplicationSettingsStore _applicationSettingsStore;
    private readonly RecentProjectTracker _recentProjectTracker;
    private readonly ITemporaryAssetHost _temporaryAssetHost;
    private ApplicationSettings _applicationSettings;
    private MediaToolAvailability _mediaTools;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _draftAutosaveTimer;
    private readonly DispatcherTimer _jobElapsedTimer;
    private readonly DispatcherTimer _frameBrowserDebounceTimer;
    private readonly DispatcherTimer _compositionTimelineDragAutoScrollTimer;
    private readonly DispatcherTimer _compositionTimelineItemDragAutoScrollTimer;
    private readonly GenerationJobCoordinator _jobCoordinator;
    private bool _suppressDraftAutosave;
    private bool _suppressPromptSynchronization;
    private bool _isVideoPlaying;
    private bool _isVideoPreviewPriming;
    private bool _videoPreviewWasMutedBeforePriming;
    private bool _videoPreviewRequiresWarmup;
    private bool _playVideoAfterPriming;
    private bool _videoPreviewHasEnded;
    private bool _isScrubbing;
    private bool _resumePlaybackAfterScrub;
    private bool _suppressFrameSelectionPrefetch;
    private bool _suppressProjectMediaSelection;
    private int _pendingKeyboardFrameSteps;
    private bool _isKeyboardFrameNavigationRunning;
    private bool _previewWasMutedBeforeMediaPreparation;
    private double _volumeBeforeMute = 1;
    private bool _isJobsPanelOpen;
    private ProjectWorkspaceKind _activeWorkspace = ProjectWorkspaceKind.Generate;
    private MediaPreparationMode _mediaPreparationMode;
    private ClipBoundarySelection _clipStart = ClipBoundarySelection.SourceStart;
    private ClipBoundarySelection _clipEnd = ClipBoundarySelection.SourceEnd;
    private bool _restoringProjectUiState;
    private bool _dismissingViewedJobs;
    private CancellationTokenSource? _frameBrowserCancellation;
    private IReadOnlyList<VideoPresentationFrame> _indexedFrames = [];
    private double? _pendingContactFrameTimestamp;
    private Guid? _frameSourceAssetId;
    private string? _frameSourceContentHash;
    private MaterializedMediaLease? _activePreviewLease;
    private MaterializedMediaLease? _activeCompositionAuditionAudioLease;
    private CancellationTokenSource? _compositionRenderCancellation;
    private CompositionTimelineLayoutResult? _compositionTimelineLayout;
    private Line? _compositionTimelinePlayhead;
    private Guid? _activeCompositionPreviewRevisionId;
    private Guid? _activeCompositionDraftRevisionId;
    private IReadOnlyList<CompositionDraftSegment> _compositionDraftSegments = [];
    private int _activeCompositionDraftSegmentIndex = -1;
    private double _compositionDraftPositionSeconds;
    private double _pendingPreviewStartSeconds;
    private int _compositionDraftOpenVersion;
    private bool _advancingCompositionDraft;
    private double? _pendingCompositionTimelineSeekSeconds;
    private bool _previewAudioForcedMuted;
    private bool _userPreviewMuted;
    private bool _compositionAuditionAudioReady;
    private bool _compositionAuditionAudioPriming;
    private bool _playCompositionAuditionAudioAfterOpen;
    private double _pendingCompositionAuditionAudioPosition;
    private Guid? _selectedCompositionSegmentId;
    private Guid? _selectedCompositionAudioClipId;
    private bool _suppressCompositionAudioControl;
    private bool _suppressCompositionAudioClipControl;
    private Guid? _pendingCompositionSegmentDragId;
    private Guid? _activeCompositionSegmentDragId;
    private Point _compositionSegmentDragStart;
    private double _compositionSegmentDragPointerOffset;
    private double _compositionSegmentDragPointerX;
    private int _compositionSegmentDragOriginalIndex = -1;
    private int _compositionSegmentDragTargetIndex = -1;
    private Guid? _pendingCompositionAudioClipDragId;
    private Guid? _activeCompositionAudioClipDragId;
    private Point _compositionAudioClipDragStart;
    private double _compositionAudioClipDragPointerOffset;
    private double _compositionAudioClipDraftStartSeconds;
    private long _compositionAudioClipOriginalStartTicks;
    private bool _isCompositionTimelineScrubbing;
    private bool _resumePlaybackAfterCompositionTimelineScrub;
    private double _compositionTimelineZoom = 1;
    private int _compositionTimelineZoomRevision;
    private ProjectAsset? _compositionTimelineDragAsset;
    private double _compositionTimelineDragViewportX;
    private double _compositionTimelineDragAutoScrollDelta;
    private double _compositionTimelineItemDragViewportX;
    private double _compositionTimelineItemDragAutoScrollDelta;
    private bool _compositionTimelineRenderScheduled;
    private Point _projectMediaDragStart;
    private ProjectMediaListItem? _projectMediaDragItem;
    private bool _renderingCompositionTimeline;
    private bool _disposed;
    private bool _isMediaImportInProgress;
    private int _activeJobSpriteFrameIndex;

    public MainWindow()
    {
        InitializeComponent();
        _activeJobSpriteFrames = LoadActiveJobSpriteFrames();

        _runtime = ApplicationRuntime.Create();
        _mediaToolDiscovery = _runtime.MediaToolDiscovery;
        _applicationSettingsStore = _runtime.ApplicationSettingsStore;
        _recentProjectTracker = _runtime.RecentProjectTracker;
        _applicationSettings = _runtime.Settings;
        _mediaTools = _runtime.MediaTools;
        _mediaInspector = _runtime.MediaInspector;
        _exactFrameService = _runtime.ExactFrameService;
        _mediaMaterializer = _runtime.MediaMaterializer;
        _audioExtractionEngine = _runtime.AudioExtractionEngine;
        _projectStore = _runtime.ProjectStore;
        _workspace = _runtime.Workspace;
        _assetTransferService = _runtime.AssetTransferService;
        _secretStore = _runtime.SecretStore;
        _diagnosticLog = _runtime.DiagnosticLog;
        _temporaryAssetHost = _runtime.TemporaryAssetHost;
        _generationProvider = new FakeVideoGenerationProvider();
        RefreshProviderRuntime(preferredProviderId: null);
        _jobCoordinator = _runtime.JobCoordinator;
        _runtime.JobFinalizer.Finalized += JobFinalizer_Finalized;
        _jobCoordinator.JobsChanged += JobCoordinator_JobsChanged;
        _jobCoordinator.JobStatusChanged += JobCoordinator_JobStatusChanged;

        var projectMediaView = new ListCollectionView(_assets);
        projectMediaView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectMediaListItem.GroupName)));
        AssetsList.ItemsSource = projectMediaView;
        GenerationsList.ItemsSource = _generations;
        ReferenceAssetsGrid.ItemsSource = _referenceChoices;
        JobsList.ItemsSource = _jobs;
        ContactFramesList.ItemsSource = _contactFrames;
        SavedFramesList.ItemsSource = _savedFrames;
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _positionTimer.Start();

        _draftAutosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _draftAutosaveTimer.Tick += DraftAutosaveTimer_Tick;

        _jobElapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _jobElapsedTimer.Tick += (_, _) =>
        {
            RefreshJobElapsedTimes();
            UpdateActiveJobSprite(advanceFrame: true);
        };
        _jobElapsedTimer.Start();

        _frameBrowserDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _frameBrowserDebounceTimer.Tick += FrameBrowserDebounceTimer_Tick;

        _compositionTimelineDragAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _compositionTimelineDragAutoScrollTimer.Tick += CompositionTimelineDragAutoScrollTimer_Tick;

        _compositionTimelineItemDragAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _compositionTimelineItemDragAutoScrollTimer.Tick += CompositionTimelineItemDragAutoScrollTimer_Tick;

        MediaToolsText.Text = _mediaTools.Summary;
        ApplyWorkspaceMode();
        Loaded += MainWindow_Loaded;
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _jobCoordinator.JobsChanged -= JobCoordinator_JobsChanged;
        _jobCoordinator.JobStatusChanged -= JobCoordinator_JobStatusChanged;
        _runtime.JobFinalizer.Finalized -= JobFinalizer_Finalized;
        _jobElapsedTimer.Stop();
        _frameBrowserDebounceTimer.Stop();
        _compositionTimelineDragAutoScrollTimer.Stop();
        _compositionTimelineItemDragAutoScrollTimer.Stop();
        _frameBrowserCancellation?.Cancel();
        _frameBrowserCancellation?.Dispose();
        _compositionRenderCancellation?.Cancel();
        CompositionAuditionAudio.Stop();
        CompositionAuditionAudio.Close();
        ReleaseCompositionAuditionAudioLease();
        ReleaseActivePreviewLease();
        foreach (var pending in _pendingSubmissionDelays.Values) pending.Cancel();
        foreach (var pending in _pendingSubmissionDelays.Values) pending.Dispose();
        _pendingSubmissionDelays.Clear();
        _runtime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            await _jobCoordinator.RestoreAsync();
            RefreshJobsUi();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Active generation jobs could not be restored: {exception.Message}";
        }

        if (string.IsNullOrWhiteSpace(_applicationSettings.General.LastProjectFilePath)) return;

        var projectFilePath = RecentProjectTracker.GetExistingProjectFile(_applicationSettings);
        if (projectFilePath is null)
        {
            StatusText.Text = "The last project is unavailable. Use Open to choose its current location or another project.";
            return;
        }

        StatusText.Text = $"Reopening {projectFilePath}…";
        try
        {
            await _workspace.OpenAsync(projectFilePath);
            RefreshProjectUi();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"The last project could not be reopened: {exception.Message}";
            InspectorText.Text = $"Automatic project reopen failed\n\n{exception}";
        }
    }

    private void RefreshProviderRuntime(string? preferredProviderId)
    {
        var providerRuntime = _runtime.RefreshProviders(preferredProviderId);
        _providerChoices = providerRuntime.Choices;
        _providerPreparation = providerRuntime.PreparationService;
        _generationWorkflow = providerRuntime.Workflow;
        _generationProvider = providerRuntime.SelectedProvider;
        var selected = providerRuntime.Choices.First(choice =>
            ReferenceEquals(choice.Provider, providerRuntime.SelectedProvider));
        var suppressAutosave = _suppressDraftAutosave;
        _suppressDraftAutosave = true;
        try
        {
            ProviderComboBox.ItemsSource = null;
            ProviderComboBox.ItemsSource = providerRuntime.Choices;
            ProviderComboBox.SelectedItem = selected;
            ConfigureGenerationPanel();
        }
        finally
        {
            _suppressDraftAutosave = suppressAutosave;
        }
    }

    private void JobCoordinator_JobsChanged(object? sender, EventArgs e)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            RefreshJobsUi();
            if (!_isJobsPanelOpen && _viewedTerminalJobIds.Count > 0)
                _ = DismissViewedTerminalJobsAsync();
        }, DispatcherPriority.Background);
    }

    private void JobCoordinator_JobStatusChanged(object? sender, GenerationJobStatusChangedEventArgs e)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_isJobsPanelOpen)
            {
                if (IsTerminalStatus(e.CurrentStatus)) _viewedTerminalJobIds.Add(e.GenerationId);
            }
            else if (JobsActivityIndicator is not null)
            {
                JobsActivityIndicator.Visibility = Visibility.Visible;
                JobsActivityIndicator.ToolTip =
                    $"{e.ProjectName}: job status changed from {e.PreviousStatus} to {e.CurrentStatus}.";
            }
        }, DispatcherPriority.Background);
    }

    private void JobFinalizer_Finalized(object? sender, GenerationJobFinalizedEventArgs e)
    {
        if (_disposed || Dispatcher.HasShutdownStarted || !e.ActiveProjectUpdated) return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            var generation = _workspace.Project?.Generations.SingleOrDefault(candidate =>
                candidate.Id == e.GenerationId);
            if (generation is null) return;
            RefreshProjectCollections();
            TryAutoPreviewGeneratedOutput(generation, owningProjectIsOpen: true);
            StatusText.Text = e.Status == GenerationStatus.Succeeded
                ? "Generated output added as durable project media."
                : $"Generation finished with status {e.Status}.";
        }, DispatcherPriority.Background);
    }

    private async void WorkspaceMode_Checked(object sender, RoutedEventArgs e)
    {
        if (RightPanelTabs is null) return;
        if (_isJobsPanelOpen) await SetJobsPanelOpenAsync(false);
        _activeWorkspace = EditWorkspaceButton.IsChecked == true
            ? ProjectWorkspaceKind.Edit
            : ProjectWorkspaceKind.Generate;
        ApplyWorkspaceMode();
        if (!_restoringProjectUiState) await SaveProjectUiStateAsync();
    }

    private void ApplyWorkspaceMode()
    {
        if (GenerateLowerWorkspace is null) return;
        var isGenerate = _activeWorkspace == ProjectWorkspaceKind.Generate;
        GenerateLowerWorkspace.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        EditLowerWorkspace.Visibility = isGenerate ? Visibility.Collapsed : Visibility.Visible;
        GenerationHistoryPanel.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        GenerationPanelSplitter.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        GenerationHistoryRow.MinHeight = isGenerate ? 80 : 0;
        GenerationHistoryRow.Height = isGenerate ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        GenerationSplitterRow.Height = isGenerate ? new GridLength(5) : new GridLength(0);
        ProjectMediaRow.Height = isGenerate ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        GenerateTab.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        EditToolsTab.Visibility = isGenerate ? Visibility.Collapsed : Visibility.Visible;
        if (isGenerate && RightPanelTabs.SelectedItem == EditToolsTab) RightPanelTabs.SelectedItem = GenerateTab;
        if (!isGenerate) RightPanelTabs.SelectedItem = EditToolsTab;
        ExpandedPromptPanel.Visibility = Visibility.Collapsed;
        RefreshEditWorkspaceState();
        if (!isGenerate && _workspace.Project?.WorkingCompositionAssetId is { } compositionId)
        {
            var item = _assets.FirstOrDefault(candidate => candidate.Asset?.Id == compositionId);
            if (item is not null && AssetsList.SelectedItem != item) AssetsList.SelectedItem = item;
        }
    }

    private async void JobsChrome_Click(object sender, RoutedEventArgs e) =>
        await SetJobsPanelOpenAsync(!_isJobsPanelOpen);

    private async void CloseJobs_Click(object sender, RoutedEventArgs e) =>
        await SetJobsPanelOpenAsync(false);

    private async Task SetJobsPanelOpenAsync(bool isOpen)
    {
        if (_isJobsPanelOpen == isOpen) return;
        _isJobsPanelOpen = isOpen;
        JobsPanel.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        if (isOpen)
        {
            JobsActivityIndicator.Visibility = Visibility.Collapsed;
            MarkVisibleTerminalJobsViewed();
        }
        else
        {
            await DismissViewedTerminalJobsAsync();
        }
    }

    private async Task SaveProjectUiStateAsync(string? mediaKind = null, Guid? mediaId = null)
    {
        if (_workspace.Project is null) return;
        var key = _workspace.Project.Id.ToString("N", CultureInfo.InvariantCulture);
        if (!_applicationSettings.General.ProjectStates.TryGetValue(key, out var state))
        {
            state = new ProjectUserInterfaceState();
            _applicationSettings.General.ProjectStates[key] = state;
        }
        state.Workspace = _activeWorkspace;
        if (mediaKind is not null)
        {
            state.SelectedMediaKind = mediaKind;
            state.SelectedMediaId = mediaId;
        }
        await _applicationSettingsStore.SaveAsync(_applicationSettings);
    }

    private void RestoreProjectUiState()
    {
        if (_workspace.Project is null) return;
        var key = _workspace.Project.Id.ToString("N", CultureInfo.InvariantCulture);
        _applicationSettings.General.ProjectStates.TryGetValue(key, out var state);
        _restoringProjectUiState = true;
        try
        {
            _activeWorkspace = state?.Workspace ?? ProjectWorkspaceKind.Generate;
            GenerateWorkspaceButton.IsChecked = _activeWorkspace == ProjectWorkspaceKind.Generate;
            EditWorkspaceButton.IsChecked = _activeWorkspace == ProjectWorkspaceKind.Edit;
            ApplyWorkspaceMode();
            if (state is { SelectedMediaKind: { } kind, SelectedMediaId: { } mediaId })
                AssetsList.SelectedItem = _assets.FirstOrDefault(item =>
                    kind == "asset" ? item.Asset?.Id == mediaId : item.Anchor?.Id == mediaId);
        }
        finally
        {
            _restoringProjectUiState = false;
        }
    }

    private void RefreshJobsUi()
    {
        var snapshot = _jobCoordinator.GetSnapshot();
        var activeIds = snapshot.Select(job => job.GenerationId).ToHashSet();
        for (var index = _jobs.Count - 1; index >= 0; index--)
        {
            if (!activeIds.Contains(_jobs[index].GenerationId)) _jobs.RemoveAt(index);
        }

        foreach (var job in snapshot)
        {
            var existing = _jobs.FirstOrDefault(item => item.GenerationId == job.GenerationId);
            if (existing is null) _jobs.Add(new GenerationJobListItem(job));
            else existing.Update(job);
        }

        JobsEmptyText.Visibility = _jobs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        JobsList.Visibility = _jobs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateActiveJobSprite(advanceFrame: false);
        if (_isJobsPanelOpen) MarkVisibleTerminalJobsViewed();
        RefreshJobElapsedTimes();
    }

    private void MarkVisibleTerminalJobsViewed()
    {
        foreach (var job in _jobCoordinator.GetSnapshot().Where(job => IsTerminalStatus(job.Status)))
            _viewedTerminalJobIds.Add(job.GenerationId);
    }

    private async Task DismissViewedTerminalJobsAsync()
    {
        if (_dismissingViewedJobs || _viewedTerminalJobIds.Count == 0) return;
        _dismissingViewedJobs = true;
        try
        {
            var dismissed = await _jobCoordinator.DismissAsync(_viewedTerminalJobIds.ToArray());
            foreach (var id in dismissed) _viewedTerminalJobIds.Remove(id);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Completed jobs could not be cleared: {exception.Message}";
        }
        finally
        {
            _dismissingViewedJobs = false;
        }
    }

    private static bool IsTerminalStatus(GenerationStatus status) =>
        status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled;

    private void RefreshJobElapsedTimes()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var job in _jobs) job.RefreshElapsed(now);
    }

    private void UpdateActiveJobSprite(bool advanceFrame)
    {
        var hasActiveJob = _jobCoordinator.GetSnapshot().Any(job =>
            job.Status is GenerationStatus.Queued or GenerationStatus.Running);
        if (!hasActiveJob)
        {
            ActiveJobSprite.Visibility = Visibility.Collapsed;
            _activeJobSpriteFrameIndex = 0;
            ActiveJobSprite.Source = _activeJobSpriteFrames[0];
            return;
        }

        if (ActiveJobSprite.Visibility != Visibility.Visible)
        {
            _activeJobSpriteFrameIndex = 0;
            ActiveJobSprite.Source = _activeJobSpriteFrames[0];
            ActiveJobSprite.Visibility = Visibility.Visible;
            return;
        }

        if (!advanceFrame) return;
        _activeJobSpriteFrameIndex = (_activeJobSpriteFrameIndex + 1) % _activeJobSpriteFrames.Count;
        ActiveJobSprite.Source = _activeJobSpriteFrames[_activeJobSpriteFrameIndex];
    }

    private static IReadOnlyList<BitmapSource> LoadActiveJobSpriteFrames()
    {
        var uri = new Uri(
            "pack://application:,,,/ReelForge.App;component/Assets/Sprites/forging_reel_animation.png",
            UriKind.Absolute);
        var resource = System.Windows.Application.GetResourceStream(uri)
            ?? throw new InvalidDataException("The forging-reel sprite resource is missing.");
        using var stream = resource.Stream;
        var decoder = new PngBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var sheet = decoder.Frames[0];
        if (sheet.PixelWidth % 2 != 0 || sheet.PixelWidth / 2 != sheet.PixelHeight)
            throw new InvalidDataException("The forging-reel sprite must contain two equal square frames side by side.");

        var frameWidth = sheet.PixelWidth / 2;
        return Enumerable.Range(0, 2).Select(index =>
        {
            var frame = new CroppedBitmap(
                sheet,
                new Int32Rect(index * frameWidth, 0, frameWidth, sheet.PixelHeight));
            frame.Freeze();
            return (BitmapSource)frame;
        }).ToArray();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var activeDraft = _workspace.Project is null ? null : CaptureDraftFromUi();
        var window = new SettingsWindow(
            _applicationSettingsStore,
            _applicationSettings.Clone(),
            _secretStore,
            _mediaToolDiscovery,
            _temporaryAssetHost,
            _diagnosticLog)
        {
            Owner = this
        };
        window.ShowDialog();
        var selectedProviderId = _generationProvider.Capabilities.ProviderId;
        _applicationSettings = await _runtime.ReloadSettingsAsync();
        _mediaTools = _runtime.MediaTools;
        _mediaInspector.UpdateExecutablePath(_mediaTools.FfprobePath);
        _exactFrameService.UpdateExecutablePaths(_mediaTools.FfmpegPath, _mediaTools.FfprobePath);
        _mediaMaterializer.UpdateExecutablePath(_mediaTools.FfmpegPath);
        _audioExtractionEngine.UpdateExecutablePath(_mediaTools.FfmpegPath);
        _mediaMaterializer.UpdatePersistencePreference(
            _applicationSettings.MediaTools.PersistModifiedMediaOnDisk);
        _exactFrameService.UpdateMaximumCacheBytes(_applicationSettings.MediaTools.CacheSizeBytes);
        await _exactFrameService.TrimCacheAsync();
        MediaToolsText.Text = _mediaTools.Summary;
        RefreshProviderRuntime(selectedProviderId);
        if (activeDraft is not null && _generationProvider.Capabilities.ProviderId.Equals(
                activeDraft.ProviderId,
                StringComparison.Ordinal))
        {
            LoadDraftIntoUi(activeDraft);
        }
        StatusText.Text = "Application settings and provider availability applied.";
    }

    private void ConfigureGenerationPanel()
    {
        var capabilities = _generationProvider.Capabilities;
        ProviderText.Text = $"{capabilities.DisplayName}\n{capabilities.ModelVersion} • no paid API calls";

        var costText = _generationProvider.CostBehavior == GenerationProviderCostBehavior.NoCharge
            ? "No network or billing"
            : "Potentially billable; explicit confirmation required for every submission";
        ProviderText.Text = $"{capabilities.ModelVersion}\n{costText}";
        GenerateButton.Content = _generationProvider.CostBehavior == GenerationProviderCostBehavior.NoCharge
            ? "Run fake generation"
            : "Review and submit generation…";
        var supportsWatermark = capabilities.ProviderParameters.ContainsKey("watermark");
        var supportsAudioToggle = capabilities.ProviderParameters.ContainsKey("generate_audio") ||
                                  capabilities.ProviderParameters.ContainsKey("generateAudio");
        GenerateAudioCheckBox.Visibility = supportsAudioToggle ? Visibility.Visible : Visibility.Collapsed;
        WatermarkCheckBox.Visibility = supportsWatermark ? Visibility.Visible : Visibility.Collapsed;
        WatermarkHelpText.Visibility = supportsWatermark ? Visibility.Visible : Visibility.Collapsed;
        var supportsOutputFormat = capabilities.ProviderParameters.ContainsKey("output_format");
        OutputFormatPanel.Visibility = supportsOutputFormat
            ? Visibility.Visible
            : Visibility.Collapsed;
        AudioAndWatermarkPanel.Visibility = supportsAudioToggle || supportsWatermark
            ? Visibility.Visible
            : Visibility.Collapsed;
        OutputSettingsHeading.Visibility = supportsAudioToggle || supportsWatermark || supportsOutputFormat
            ? Visibility.Visible
            : Visibility.Collapsed;

        ModeComboBox.ItemsSource = capabilities.Modes;
        ModeComboBox.SelectedItem = capabilities.Modes.Contains(GenerationMode.ReferenceToVideo)
            ? GenerationMode.ReferenceToVideo
            : capabilities.Modes[0];

        DurationSlider.Minimum = capabilities.MinimumDurationSeconds;
        DurationSlider.Maximum = capabilities.MaximumDurationSeconds;
        DurationSlider.Value = Math.Clamp(15, capabilities.MinimumDurationSeconds, capabilities.MaximumDurationSeconds);

        AspectRatioComboBox.ItemsSource = capabilities.AspectRatios;
        AspectRatioComboBox.SelectedItem = capabilities.AspectRatios.Contains("16:9")
            ? "16:9"
            : capabilities.AspectRatios[0];

        ResolutionComboBox.ItemsSource = capabilities.Resolutions;
        ResolutionComboBox.SelectedItem = capabilities.Resolutions.Contains("720p")
            ? "720p"
            : capabilities.Resolutions[0];
    }

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProviderComboBox.SelectedItem is not GenerationProviderChoice choice) return;
        _generationProvider = choice.Provider;
        _suppressDraftAutosave = true;
        try
        {
            ConfigureGenerationPanel();
        }
        finally
        {
            _suppressDraftAutosave = false;
        }

        ScheduleDraftAutosave();
    }

    private void GenerationDraftChanged(object sender, EventArgs e) => ScheduleDraftAutosave();

    private void PromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressPromptSynchronization &&
            ExpandedPromptPanel is not null &&
            ExpandedPromptPanel.Visibility == Visibility.Visible)
            SynchronizePromptText(PromptTextBox, ExpandedPromptTextBox);
        ScheduleDraftAutosave();
    }

    private void ExpandedPromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPromptSynchronization) return;
        SynchronizePromptText(ExpandedPromptTextBox, PromptTextBox);
    }

    private void ExpandPrompt_Click(object sender, RoutedEventArgs e)
    {
        SynchronizePromptText(PromptTextBox, ExpandedPromptTextBox);
        ExpandedPromptPanel.Visibility = Visibility.Visible;
        ExpandedPromptTextBox.Focus();
        ExpandedPromptTextBox.CaretIndex = ExpandedPromptTextBox.Text.Length;
    }

    private void CollapsePrompt_Click(object sender, RoutedEventArgs e) => CollapseExpandedPrompt();

    private void ExpandedPromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CollapseExpandedPrompt();
        e.Handled = true;
    }

    private void CollapseExpandedPrompt()
    {
        SynchronizePromptText(ExpandedPromptTextBox, PromptTextBox);
        ExpandedPromptPanel.Visibility = Visibility.Collapsed;
        PromptTextBox.Focus();
        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
    }

    private void SynchronizePromptText(TextBox source, TextBox destination)
    {
        if (string.Equals(source.Text, destination.Text, StringComparison.Ordinal)) return;
        _suppressPromptSynchronization = true;
        try
        {
            destination.Text = source.Text;
        }
        finally
        {
            _suppressPromptSynchronization = false;
        }
    }

    private void GenerationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedMode = ModeComboBox.SelectedItem is GenerationMode mode
            ? mode
            : GenerationMode.TextToVideo;
        ReferenceAssetsGrid.IsEnabled = selectedMode is not GenerationMode.TextToVideo;
        ReferenceAssetsHelpText.Text = selectedMode is GenerationMode.TextToVideo
            ? "Text-to-video does not use reference assets. Choose ImageToVideo or ReferenceToVideo to select and describe references."
            : "Select project assets to use as references. Role, order, label, and notes are frozen into history.";
        if (selectedMode is GenerationMode.ImageToVideo &&
            _generationProvider.Capabilities.AspectRatios.Contains("adaptive"))
        {
            AspectRatioComboBox.SelectedItem = "adaptive";
        }
        else if (selectedMode is GenerationMode.TextToVideo &&
                 string.Equals(AspectRatioComboBox.SelectedItem as string, "adaptive", StringComparison.OrdinalIgnoreCase))
        {
            var concreteRatio = _generationProvider.Capabilities.AspectRatios.Contains("16:9")
                ? "16:9"
                : _generationProvider.Capabilities.AspectRatios.FirstOrDefault(ratio =>
                    !string.Equals(ratio, "adaptive", StringComparison.OrdinalIgnoreCase));
            if (concreteRatio is not null) AspectRatioComboBox.SelectedItem = concreteRatio;
        }
        ScheduleDraftAutosave();
    }

    private void ReferenceAssetsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(ScheduleDraftAutosave, DispatcherPriority.Background);

    private void ReferenceChoiceChanged(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(ScheduleDraftAutosave, DispatcherPriority.Background);

    private void DuplicateReferenceOccurrence_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceAssetsGrid.SelectedItem is not GenerationReferenceChoice selected)
        {
            GenerationStatusText.Text = "Select a reference row to add another occurrence.";
            return;
        }
        var duplicate = selected.Duplicate(_referenceChoices.Count);
        _referenceChoices.Add(duplicate);
        ReferenceAssetsGrid.SelectedItem = duplicate;
        ReferenceAssetsGrid.ScrollIntoView(duplicate);
        ScheduleDraftAutosave();
        GenerationStatusText.Text = $"Added another occurrence of {duplicate.DisplayName}.";
    }

    private void ScheduleDraftAutosave()
    {
        if (_suppressDraftAutosave || _workspace is null || _workspace.Project is null || _draftAutosaveTimer is null) return;
        _draftAutosaveTimer.Stop();
        _draftAutosaveTimer.Start();
    }

    private async void DraftAutosaveTimer_Tick(object? sender, EventArgs e)
    {
        _draftAutosaveTimer.Stop();
        if (_suppressDraftAutosave || _workspace.Project is null) return;
        try
        {
            await _generationWorkflow.SaveDraftAsync(CaptureDraftFromUi());
            GenerationStatusText.Text = "Draft autosaved.";
        }
        catch (Exception exception)
        {
            GenerationStatusText.Text = $"Draft autosave failed: {exception.Message}";
        }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var projectsLocation = GetDefaultProjectsDirectory();
        if (!Directory.Exists(projectsLocation))
        {
            var choice = MessageBox.Show(
                this,
                $"ReelForge's recommended projects folder does not exist yet:\n\n{projectsLocation}\n\nCreate it now?",
                "Create projects folder",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.Yes)
            {
                Directory.CreateDirectory(projectsLocation);
            }
            else
            {
                var locationDialog = new OpenFolderDialog
                {
                    Title = "Choose the folder that will contain ReelForge projects",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Multiselect = false
                };
                if (locationDialog.ShowDialog(this) != true) return;
                projectsLocation = Path.GetFullPath(locationDialog.FolderName);
            }
        }

        var dialog = new NewProjectDialog(projectsLocation) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RunUiActionAsync(
            "Creating project…",
            async () =>
            {
                await _workspace.CreateAsync(dialog.ProjectDirectory, dialog.ProjectName);
                RefreshProjectUi();
                await RememberCurrentProjectAsync();
            });
    }

    private string GetDefaultProjectsDirectory()
    {
        var configured = _applicationSettings.General.ProjectsRoot;
        return ApplicationPathResolver.ResolveDirectory(
            string.IsNullOrWhiteSpace(configured) ? _runtime.Paths.DefaultProjectsDirectory : configured);
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateOpenProjectDialog();
        if (dialog.ShowDialog() != true) return;

        await RunUiActionAsync(
            "Opening project…",
            async () =>
            {
                await _workspace.OpenAsync(dialog.ProjectFilePath);
                RefreshProjectUi();
                await RememberCurrentProjectAsync();
            });
    }

    private async Task RememberCurrentProjectAsync()
    {
        if (_workspace.Location is null) return;

        try
        {
            await _recentProjectTracker.RememberAsync(_applicationSettings, _workspace.Location.ProjectFilePath);
        }
        catch (Exception exception)
        {
            StatusText.Text += $" ReelForge could not remember this project for the next launch: {exception.Message}";
        }
    }

    private OpenProjectDialog CreateOpenProjectDialog() =>
        new(
            GetDefaultProjectsDirectory(),
            RecentProjectTracker.GetExistingRecentProjectFiles(_applicationSettings))
        {
            Owner = this
        };

    private async void ImportAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import image, video, or audio assets",
            Filter = "Supported media|*.bmp;*.gif;*.heic;*.heif;*.jpeg;*.jpg;*.png;*.tif;*.tiff;*.webp;*.avi;*.m4v;*.mkv;*.mov;*.mp4;*.webm;*.wmv;*.aac;*.flac;*.m4a;*.mp3;*.ogg;*.wav;*.wma|All files|*.*",
            CheckFileExists = true,
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ImportMediaFilesAsync(dialog.FileNames);
    }

    private void MainWindow_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragFormat)) return;
        UpdateMediaDropFeedback(e);
    }

    private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragFormat)) return;
        UpdateMediaDropFeedback(e);
    }

    private void MainWindow_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragFormat)) return;
        HideMediaDropOverlay();
        e.Handled = true;
    }

    private async void MainWindow_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragFormat)) return;
        var droppedFiles = GetDroppedFiles(e.Data);
        var supportedFiles = droppedFiles.Where(AssetImportService.IsSupportedMediaFile).ToArray();
        var skippedCount = droppedFiles.Count - supportedFiles.Length;
        var canImport = _workspace.Project is not null && !_isMediaImportInProgress && supportedFiles.Length > 0;

        e.Effects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        HideMediaDropOverlay();

        if (!canImport) return;
        await ImportMediaFilesAsync(supportedFiles, skippedCount);
    }

    private void UpdateMediaDropFeedback(DragEventArgs e)
    {
        var droppedFiles = GetDroppedFiles(e.Data);
        var supportedCount = droppedFiles.Count(AssetImportService.IsSupportedMediaFile);
        var skippedCount = droppedFiles.Count - supportedCount;
        var canImport = _workspace.Project is not null && !_isMediaImportInProgress && supportedCount > 0;

        e.Effects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!canImport)
        {
            HideMediaDropOverlay();
            return;
        }

        var mediaDescription = supportedCount == 1 ? "1 media file" : $"{supportedCount} media files";
        var ignoredDescription = skippedCount == 0
            ? string.Empty
            : $" {skippedCount} unsupported {(skippedCount == 1 ? "item" : "items")} will be skipped.";
        ShowMediaDropOverlay($"Drop to add {mediaDescription} to {_workspace.Project!.Name}.{ignoredDescription}");
    }

    private async Task ImportMediaFilesAsync(IReadOnlyCollection<string> filePaths, int skippedCount = 0)
    {
        if (_workspace.Project is null || filePaths.Count == 0 || _isMediaImportInProgress) return;

        _isMediaImportInProgress = true;
        SetProjectActionsEnabled(false);
        try
        {
            await RunUiActionAsync(
                $"Importing {filePaths.Count} asset(s)…",
                async () =>
                {
                    var imported = await _workspace.ImportAssetsAsync(filePaths);
                    RefreshProjectCollections();
                    StatusText.Text = skippedCount == 0
                        ? $"Imported {imported.Count} asset(s)."
                        : $"Imported {imported.Count} asset(s); skipped {skippedCount} unsupported item(s).";
                });
        }
        finally
        {
            _isMediaImportInProgress = false;
            SetProjectActionsEnabled(true);
        }
    }

    private void ShowMediaDropOverlay(string message)
    {
        MediaDropHintText.Text = message;
        MediaDropOverlay.Visibility = Visibility.Visible;
        ApplicationContent.Effect ??= new BlurEffect { Radius = 4, KernelType = KernelType.Gaussian };
    }

    private void HideMediaDropOverlay()
    {
        if (MediaDropOverlay is null || ApplicationContent is null) return;
        MediaDropOverlay.Visibility = Visibility.Collapsed;
        ApplicationContent.Effect = null;
    }

    private static IReadOnlyList<string> GetDroppedFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];

        return paths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async void AssetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProjectMediaSelection) return;
        if (AssetsList.SelectedItem is not ProjectMediaListItem item)
        {
            return;
        }

        var selectedProjectId = _workspace.Project?.Id;
        GenerationsList.SelectedItem = null;
        ResetFrameWorkspace();
        if (item.Anchor is not null && item.AnchorRevision is not null)
        {
            UpdateCompositionActionState();
            if (!_restoringProjectUiState) await SaveProjectUiStateAsync("anchor", item.Anchor.Id);
            await ShowSavedFramePreviewAsync(item, selectedProjectId);
            return;
        }

        if (item.Asset is not { } asset) return;
        if (!_restoringProjectUiState) await SaveProjectUiStateAsync("asset", asset.Id);

        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            InspectorText.Text = FormatAssetInspector(asset);
            ConfigureMediaPreparationFor(asset);
            await ShowVirtualAssetPreviewAsync(asset, item, selectedProjectId);
            return;
        }

        await RunUiActionAsync(
            $"Inspecting {asset.FileName}…",
            async () =>
            {
                if (asset.StorageKind == AssetStorageKind.Physical && asset.Physical is not null &&
                    !File.Exists(_workspace.GetAbsoluteAssetPath(asset)))
                {
                    asset.Physical.Availability = PhysicalAssetAvailability.Missing;
                    await _workspace.SaveAsync();
                    if (_workspace.Project?.Id != selectedProjectId) return;
                    InspectorText.Text = FormatAssetInspector(asset);
                    ShowAssetPreview(asset);
                    FrameWorkspaceStatusText.Text = "Source media is missing";
                    StatusText.Text = $"{asset.FileName} is missing from its recorded project location.";
                    return;
                }

                if (asset.StorageKind == AssetStorageKind.Physical &&
                    asset.MediaType is MediaType.Video or MediaType.Audio &&
                    asset.Encoding is null &&
                    _mediaTools.FfprobePath is not null)
                {
                    var encoding = await _mediaInspector.InspectAsync(_workspace.GetAbsoluteAssetPath(asset));
                    if (_workspace.Project?.Id != selectedProjectId) return;
                    asset.Encoding = encoding;
                    asset.DurationSeconds = asset.Encoding.DurationSeconds;
                    asset.Width = asset.Encoding.Video?.Width;
                    asset.Height = asset.Encoding.Video?.Height;
                    await _workspace.SaveAsync();
                }

                if (_workspace.Project?.Id != selectedProjectId) return;
                InspectorText.Text = FormatAssetInspector(asset);
                ShowAssetPreview(asset);
                ConfigureMediaPreparationFor(asset);
                StatusText.Text = $"Selected {asset.FileName}.";
            });
    }

    private void ConfigureMediaPreparationFor(ProjectAsset asset)
    {
        var canPrepare = asset is
        {
            StorageKind: AssetStorageKind.Physical,
            MediaType: MediaType.Video,
            Physical.Availability: not PhysicalAssetAvailability.Missing
        };
        SelectFrameButton.IsEnabled = canPrepare;
        MakeClipButton.IsEnabled = canPrepare;
        StartEditButton.IsEnabled = _workspace.Project?.WorkingCompositionAssetId is null &&
                                    asset.MediaType == MediaType.Video &&
                                    (asset.StorageKind == AssetStorageKind.Physical ||
                                     asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        MediaPreparationSelectionText.Text = canPrepare
            ? asset.EffectiveDisplayName
            : "Select a physical video in Project Media";
        UpdateCompositionActionState();
    }

    private async void StartEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { } source) return;
        await RunUiActionAsync("Creating Working Composition…", async () =>
        {
            var composition = await new WorkingCompositionService(_workspace).CreateInitialAsync(source.Id);
            RefreshProjectCollections(composition.Id);
            RefreshEditWorkspaceState();
            StatusText.Text = $"Working Composition started from {source.EffectiveDisplayName}.";
        });
    }

    private void RefreshEditWorkspaceState()
    {
        if (EditEmptyState is null) return;
        var composition = _workspace.Project?.WorkingCompositionAssetId is { } compositionId
            ? _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == compositionId)
            : null;
        var hasComposition = composition is not null;
        EditEmptyState.Visibility = hasComposition ? Visibility.Collapsed : Visibility.Visible;
        WorkingCompositionState.Visibility = hasComposition ? Visibility.Visible : Visibility.Collapsed;
        if (!hasComposition)
        {
            _compositionSegments.Clear();
            _compositionAudioClips.Clear();
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = null;
            ClearCompositionTimeline();
            UpdateCompositionActionState();
            return;
        }
        var selectedSegmentId = _selectedCompositionSegmentId;
        var selectedAudioClipId = _selectedCompositionAudioClipId;
        WorkingCompositionNameText.Text = composition!.EffectiveDisplayName;
        var revision = _workspace.Project!.RecipeRevisions.Single(candidate =>
            candidate.Id == composition.Virtual!.CurrentRecipeRevisionId);
        if (_activeCompositionPreviewRevisionId is { } previewRevisionId && previewRevisionId != revision.Id)
            ClearStaleCompositionPreview(composition, revision);
        var recipe = (CompositionRecipe)revision.Recipe;
        WorkingCompositionSummaryText.Text =
            $"{recipe.Segments.Count} video segment{(recipe.Segments.Count == 1 ? string.Empty : "s")} • " +
            $"{recipe.AudioClips.Count} audio clip{(recipe.AudioClips.Count == 1 ? string.Empty : "s")} • " +
            $"exact, revision-pinned sources • recipe revision {revision.RevisionNumber}";
        _compositionSegments.Clear();
        for (var index = 0; index < recipe.Segments.Count; index++)
        {
            var segment = recipe.Segments[index];
            var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId);
            _compositionSegments.Add(new CompositionSegmentListItem(
                index,
                segment,
                source,
                CompositionSegmentTiming.ResolveDuration(_workspace.Project, segment, source)));
        }
        _selectedCompositionSegmentId = selectedSegmentId is { } id &&
                                        _compositionSegments.Any(item => item.SegmentId == id)
            ? id
            : null;
        _compositionAudioClips.Clear();
        foreach (var audioClip in recipe.AudioClips)
        {
            var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == audioClip.Source.AssetId);
            _compositionAudioClips.Add(new CompositionAudioClipListItem(audioClip, source));
        }
        _selectedCompositionAudioClipId = selectedAudioClipId is { } audioId &&
                                          _compositionAudioClips.Any(item => item.AudioClipId == audioId)
            ? audioId
            : null;
        RenderCompositionTimeline();
        ScheduleCompositionTimelineRender();
        UpdateCompositionActionState();
        if (_activeCompositionDraftRevisionId is { } draftRevisionId && draftRevisionId != revision.Id &&
            AssetsList.SelectedItem is ProjectMediaListItem selectedItem &&
            selectedItem.Asset?.Id == composition.Id)
        {
            ClearMediaPreview();
            _ = OpenCompositionDraftPreviewAsync(composition, selectedItem, _workspace.Project.Id);
        }
    }

    private async Task SplitCompositionSegmentAsync(Guid segmentId)
    {
        var selected = _compositionSegments.SingleOrDefault(item => item.SegmentId == segmentId);
        if (selected is null ||
            _compositionTimelineLayout?.Segments.SingleOrDefault(span => span.SegmentId == segmentId) is not { } span)
            return;
        var playbackSeconds = GetCurrentTimelinePlaybackSeconds();
        var offset = playbackSeconds - span.StartSeconds;
        var boundaryEdge = _applicationSettings.MediaTools.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame
            ? AnchorBoundaryEdge.AfterFrame
            : AnchorBoundaryEdge.BeforeFrame;
        VideoPreview.Pause();
        SetPlaybackState(false);
        await RunUiActionAsync("Splitting composition segment at the exact playhead frame…", async () =>
        {
            var result = await new CompositionSegmentSplitService(
                    _workspace,
                    _mediaMaterializer,
                    _exactFrameService)
                .SplitAsync(selected.SegmentId, TimeSpan.FromSeconds(offset), boundaryEdge);
            _selectedCompositionSegmentId = result.TrailingSegmentId;
            _selectedCompositionAudioClipId = null;
            var leadingName = _workspace.Project!.Assets.Single(asset => asset.Id == result.LeadingClipAssetId)
                .EffectiveDisplayName;
            var trailingName = _workspace.Project.Assets.Single(asset => asset.Id == result.TrailingClipAssetId)
                .EffectiveDisplayName;
            RefreshProjectCollections(_workspace.Project.WorkingCompositionAssetId);
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Split {selected.DisplayName} into Saved Clips '{leadingName}' and '{trailingName}' at " +
                $"source {FormatTimelineTimePrecise(result.SourceTimestampSeconds)} " +
                $"({(boundaryEdge == AnchorBoundaryEdge.BeforeFrame ? "before" : "after")} selected frame).";
        });
    }

    private async Task DetachCompositionSegmentAudioAsync(Guid segmentId)
    {
        var selected = _compositionSegments.SingleOrDefault(item => item.SegmentId == segmentId);
        if (selected is null) return;
        var stem = MakeSafeFileName(Path.GetFileNameWithoutExtension(selected.DisplayName));
        var dialog = new AssetNameDialog(
            $"{stem} detached audio.m4a",
            title: "Detach segment audio",
            heading: "DETACH SEGMENT AUDIO",
            description:
            "Create a permanent audio file from this exact timeline segment, add it at the same timeline position, " +
            "and mute the segment's embedded audio to prevent doubled sound.",
            confirmLabel: "Detach")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        VideoPreview.Pause();
        PauseCompositionAuditionAudio();
        SetPlaybackState(false);
        await RunUiActionAsync("Detaching exact segment audio…", async () =>
        {
            var result = await new CompositionSegmentAudioDetachmentService(
                    _workspace,
                    _mediaMaterializer,
                    _audioExtractionEngine,
                    new Sha256ContentHashService(),
                    _mediaInspector)
                .DetachAsync(segmentId, dialog.FileName);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = result.AudioClipId;
            RefreshProjectCollections(_workspace.Project!.WorkingCompositionAssetId);
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Detached {selected.DisplayName} audio as '{result.AudioAsset.FileName}' at " +
                $"{FormatTimelineTimePrecise(result.TimelineStart.TotalSeconds)}.";
        });
    }

    private async void CompositionSegmentAudio_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressCompositionAudioControl || GetSelectedCompositionSegment() is not { } selected) return;
        var audioEnabled = CompositionSegmentAudioOnButton.IsChecked == true;
        if (selected.AudioEnabled == audioEnabled) return;

        await RunUiActionAsync("Updating composition source audio…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetSegmentAudioEnabledAsync(selected.SegmentId, audioEnabled);
            _selectedCompositionSegmentId = selected.SegmentId;
            _selectedCompositionAudioClipId = null;
            RefreshEditWorkspaceState();
            StatusText.Text = audioEnabled
                ? $"Enabled source audio for {selected.DisplayName}. Preview the composition to rebuild it."
                : $"Muted source audio for {selected.DisplayName}. Preview the composition to rebuild it.";
        });
    }

    private async void CompositionAudioClipEnabled_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressCompositionAudioClipControl || GetSelectedCompositionAudioClip() is not { } selected) return;
        var isMuted = CompositionAudioClipMutedButton.IsChecked == true;
        if (selected.IsMuted == isMuted) return;

        await RunUiActionAsync("Updating composition audio clip…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetAudioClipMixAsync(selected.AudioClipId, isMuted, selected.GainDecibels);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text = isMuted
                ? $"Muted {selected.DisplayName}. Preview the composition to rebuild it."
                : $"Enabled {selected.DisplayName}. Preview the composition to rebuild it.";
        });
    }

    private void CompositionAudioClipGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CompositionAudioClipGainText is null) return;
        CompositionAudioClipGainText.Text = FormatGainDecibels(e.NewValue);
    }

    private async void CompositionAudioClipGainSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        await CommitSelectedCompositionAudioClipGainAsync();

    private async void CompositionAudioClipGainSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            await CommitSelectedCompositionAudioClipGainAsync();
    }

    private async Task CommitSelectedCompositionAudioClipGainAsync()
    {
        if (_suppressCompositionAudioClipControl || GetSelectedCompositionAudioClip() is not { } selected) return;
        var gainDecibels = CompositionAudioClipGainSlider.Value;
        if (Math.Abs(selected.GainDecibels - gainDecibels) < 0.000_001) return;

        await RunUiActionAsync("Updating composition audio gain…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetAudioClipMixAsync(selected.AudioClipId, selected.IsMuted, gainDecibels);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Set {selected.DisplayName} gain to {FormatGainDecibels(gainDecibels)}. Preview the composition to rebuild it.";
        });
    }

    private void CompositionAudioClipPanSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (CompositionAudioClipPanText is null) return;
        CompositionAudioClipPanText.Text = FormatAudioPan(e.NewValue);
    }

    private async void CompositionAudioClipPanSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        await CommitSelectedCompositionAudioClipPanAsync();

    private async void CompositionAudioClipPanSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            await CommitSelectedCompositionAudioClipPanAsync();
    }

    private async Task CommitSelectedCompositionAudioClipPanAsync()
    {
        if (_suppressCompositionAudioClipControl || GetSelectedCompositionAudioClip() is not { } selected) return;
        var pan = Math.Round(CompositionAudioClipPanSlider.Value, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(selected.Pan - pan) < 0.000_001) return;

        await RunUiActionAsync("Updating composition audio pan…", async () =>
        {
            await new WorkingCompositionService(_workspace).SetAudioClipPanAsync(selected.AudioClipId, pan);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Set {selected.DisplayName} pan to {FormatAudioPan(pan)}. Preview the composition to rebuild it.";
        });
    }

    private void CompositionAudioClipFadeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (CompositionAudioClipFadeInText is null || CompositionAudioClipFadeOutText is null) return;
        CompositionAudioClipFadeInText.Text = FormatFadeDuration(CompositionAudioClipFadeInSlider.Value);
        CompositionAudioClipFadeOutText.Text = FormatFadeDuration(CompositionAudioClipFadeOutSlider.Value);
    }

    private async void CompositionAudioClipFadeSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        await CommitSelectedCompositionAudioClipFadesAsync();

    private async void CompositionAudioClipFadeSlider_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            await CommitSelectedCompositionAudioClipFadesAsync();
    }

    private async Task CommitSelectedCompositionAudioClipFadesAsync()
    {
        if (_suppressCompositionAudioClipControl || GetSelectedCompositionAudioClip() is not { } selected) return;
        var fadeIn = TimeSpan.FromMilliseconds(Math.Round(
            CompositionAudioClipFadeInSlider.Value * 1000,
            MidpointRounding.AwayFromZero));
        var fadeOut = TimeSpan.FromMilliseconds(Math.Round(
            CompositionAudioClipFadeOutSlider.Value * 1000,
            MidpointRounding.AwayFromZero));
        if (selected.FadeIn == fadeIn && selected.FadeOut == fadeOut) return;

        await RunUiActionAsync("Updating composition audio fades…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetAudioClipFadesAsync(selected.AudioClipId, fadeIn, fadeOut);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Set {selected.DisplayName} fades to {FormatFadeDuration(fadeIn.TotalSeconds)} in / " +
                $"{FormatFadeDuration(fadeOut.TotalSeconds)} out. Preview the composition to rebuild it.";
        });
    }

    private async Task MoveCompositionSegmentAsync(Guid segmentId, int offset)
    {
        var selected = _compositionSegments.SingleOrDefault(item => item.SegmentId == segmentId);
        if (selected is null) return;
        await RunUiActionAsync("Reordering the Working Composition…", async () =>
        {
            await new WorkingCompositionService(_workspace).MoveSegmentAsync(selected.SegmentId, offset);
            RefreshEditWorkspaceState();
            _selectedCompositionSegmentId = selected.SegmentId;
            _selectedCompositionAudioClipId = null;
            RenderCompositionTimeline();
            StatusText.Text = "Working Composition order updated.";
        });
    }

    private async Task RemoveCompositionItemAsync(Guid itemId)
    {
        var selectedSegment = _compositionSegments.SingleOrDefault(item => item.SegmentId == itemId);
        var selectedAudio = _compositionAudioClips.SingleOrDefault(item => item.AudioClipId == itemId);
        if (selectedSegment is null && selectedAudio is null) return;
        var displayName = selectedSegment?.DisplayName ?? selectedAudio!.DisplayName;
        await RunUiActionAsync($"Removing {displayName} from the composition…", async () =>
        {
            await new WorkingCompositionService(_workspace).RemoveItemAsync(itemId);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = null;
            RefreshEditWorkspaceState();
            StatusText.Text = $"Removed {displayName} from the Working Composition.";
        });
    }

    private async void PreviewComposition_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
        var projectId = _workspace.Project.Id;
        var (composition, revision, _) = new WorkingCompositionService(_workspace).GetCurrent();
        await RunCompositionRenderAsync(
            "Rendering preview…",
            "Composition preview render cancelled.",
            async cancellationToken =>
        {
            MaterializedMediaLease? lease = null;
            try
            {
                lease = await _mediaMaterializer.MaterializeAsync(
                    _workspace.Project,
                    _workspace.Location,
                    new MaterializationRequest(
                        new AssetMaterializationTarget(composition.Id, revision.Id),
                        MaterializationPurpose.Preview),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (_workspace.Project?.Id != projectId)
                {
                    await lease.DisposeAsync();
                    lease = null;
                    return;
                }
                ClearMediaPreview();
                _activeCompositionPreviewRevisionId = revision.Id;
                _activePreviewLease = lease;
                lease = null;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                OpenVideoPreview(_activePreviewLease.Path, requiresWarmup: true);
                UpdateCompositionActionState();
                StatusText.Text = "Working Composition preview is ready.";
            }
            finally
            {
                if (lease is not null) await lease.DisposeAsync();
            }
        });
    }

    private async void ExportComposition_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null) return;
        var (composition, revision, _) = new WorkingCompositionService(_workspace).GetCurrent();
        var dialog = new SaveFileDialog
        {
            Title = "Export Working Composition",
            Filter = "MP4 video|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{MakeSafeFileName(_workspace.Project.Name)} composition.mp4",
            InitialDirectory = Path.Combine(_workspace.Location!.RootDirectory, "exports")
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunCompositionRenderAsync(
            "Exporting composition…",
            "Composition export cancelled.",
            async cancellationToken =>
            {
                var service = CreateRenderedAssetPromotionService();
                var path = await service.ExportAsync(
                    composition.Id,
                    revision.Id,
                    dialog.FileName,
                    cancellationToken);
                StatusText.Text = $"Exported Working Composition to {path}.";
            });
    }

    private void CancelCompositionRender_Click(object sender, RoutedEventArgs e)
    {
        if (_compositionRenderCancellation is null) return;
        CancelCompositionRenderButton.IsEnabled = false;
        CompositionRenderStatusText.Text = "Cancelling…";
        StatusText.Text = "Cancelling composition render…";
        _compositionRenderCancellation.Cancel();
    }

    private async Task RunCompositionRenderAsync(
        string activeStatus,
        string cancelledStatus,
        Func<CancellationToken, Task> action)
    {
        if (_compositionRenderCancellation is not null) return;

        using var cancellation = new CancellationTokenSource();
        _compositionRenderCancellation = cancellation;
        CompositionRenderStatusText.Text = activeStatus;
        CompositionRenderIndicator.Visibility = Visibility.Visible;
        CancelCompositionRenderButton.Visibility = Visibility.Visible;
        CancelCompositionRenderButton.IsEnabled = true;
        PreviewCompositionButton.IsEnabled = false;
        ExportCompositionButton.IsEnabled = false;
        StatusText.Text = activeStatus;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText.Text = cancelledStatus;
        }
        catch (Exception exception)
        {
            ShowError("Composition render failed", exception);
        }
        finally
        {
            if (ReferenceEquals(_compositionRenderCancellation, cancellation))
                _compositionRenderCancellation = null;
            CompositionRenderIndicator.Visibility = Visibility.Collapsed;
            CancelCompositionRenderButton.Visibility = Visibility.Collapsed;
            UpdateCompositionActionState();
        }
    }

    private void CompositionTimelineScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderCompositionTimeline();

    private void CompositionTimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_renderingCompositionTimeline || Math.Abs(e.HorizontalChange) < 0.001) return;
        UpdateCompositionTimelineStickyContent();
    }

    private void CompositionTimelineZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var zoom = Math.Round(Math.Clamp(e.NewValue, 1, 8) * 4) / 4;
        if (CompositionTimelineZoomText is not null)
            CompositionTimelineZoomText.Text = $"{zoom * 100:0}%";
        if (Math.Abs(_compositionTimelineZoom - zoom) < 0.001) return;

        var oldWidth = _compositionTimelineLayout?.ContentWidth ?? 0;
        var scrollViewer = CompositionTimelineScrollViewer;
        var viewportWidth = scrollViewer?.ViewportWidth ?? 0;
        var focusRatio = scrollViewer is not null && oldWidth > 0 && double.IsFinite(viewportWidth)
            ? Math.Clamp(
                (scrollViewer.HorizontalOffset + viewportWidth / 2) / oldWidth,
                0,
                1)
            : 0.5;
        _compositionTimelineZoom = zoom;
        RenderCompositionTimeline();

        var revision = ++_compositionTimelineZoomRevision;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed || revision != _compositionTimelineZoomRevision || scrollViewer is null) return;
            scrollViewer.UpdateLayout();
            var desiredOffset = focusRatio * scrollViewer.ExtentWidth -
                                scrollViewer.ViewportWidth / 2;
            scrollViewer.ScrollToHorizontalOffset(Math.Clamp(
                desiredOffset,
                0,
                Math.Max(0, scrollViewer.ExtentWidth - scrollViewer.ViewportWidth)));
        }, DispatcherPriority.Render);
    }

    private void CompositionTimelineReset_Click(object sender, RoutedEventArgs e)
    {
        CompositionTimelineZoomSlider.Value = 1;
    }

    private void ScheduleCompositionTimelineRender()
    {
        if (_compositionTimelineRenderScheduled || _disposed) return;
        _compositionTimelineRenderScheduled = true;
        _ = Dispatcher.BeginInvoke(() =>
        {
            _compositionTimelineRenderScheduled = false;
            if (_disposed || WorkingCompositionState.Visibility != Visibility.Visible) return;
            CompositionTimelineScrollViewer.UpdateLayout();
            RenderCompositionTimeline();
        }, DispatcherPriority.ContextIdle);
    }

    private double GetCompositionTimelineViewportWidth()
    {
        var width = CompositionTimelineScrollViewer.ActualWidth;
        if (!double.IsFinite(width) || width <= 1)
            width = CompositionTimelineScrollViewer.ViewportWidth;
        return double.IsFinite(width) && width > 1 ? width : 1;
    }

    private void CompositionTimelineSegment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid segmentId }) return;
        _selectedCompositionSegmentId = segmentId;
        _selectedCompositionAudioClipId = null;
        var point = e.GetPosition(CompositionTimelineCanvas);
        var span = _compositionTimelineLayout?.Segments
            .SingleOrDefault(candidate => candidate.SegmentId == segmentId);
        _pendingCompositionSegmentDragId = segmentId;
        _activeCompositionSegmentDragId = null;
        _compositionSegmentDragStart = point;
        _compositionSegmentDragPointerX = point.X;
        _compositionSegmentDragPointerOffset = span is null
            ? 0
            : Math.Clamp(point.X - span.Left, 0, span.Width);
        _compositionSegmentDragOriginalIndex = _compositionSegments
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => pair.item.SegmentId == segmentId)
            .index;
        _compositionSegmentDragTargetIndex = _compositionSegmentDragOriginalIndex;
        CompositionTimelineCanvas.CaptureMouse();
        UpdateCompositionActionState();
        RenderCompositionTimeline();
        e.Handled = true;
    }

    private void CompositionTimelineCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(CompositionTimelineCanvas);
        if (!IsWorkingCompositionSelected())
        {
            _pendingCompositionTimelineSeekSeconds = point.Y is >= 0 and <= 24 &&
                                                     _compositionTimelineLayout is not null
                ? _compositionTimelineLayout.GetTimeAtX(point.X)
                : null;
            SelectWorkingCompositionInProjectMedia();
            if (_pendingCompositionTimelineSeekSeconds is not null) e.Handled = true;
            return;
        }
        if ((_activeCompositionPreviewRevisionId is null && _activeCompositionDraftRevisionId is null) ||
            _compositionTimelineLayout is null ||
            VideoPreview.Source is null ||
            _isVideoPreviewPriming ||
            PlaybackButton.IsEnabled != true)
            return;

        if (point.Y is < 0 or > 24) return;

        _resumePlaybackAfterCompositionTimelineScrub = _isVideoPlaying;
        _isCompositionTimelineScrubbing = true;
        VideoPreview.Pause();
        SetPlaybackState(false);
        CompositionTimelineCanvas.CaptureMouse();
        CompositionTimelineCanvas.Cursor = Cursors.SizeWE;
        SeekCompositionTimeline(point.X);
        e.Handled = true;
    }

    private void CompositionTimelineCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isCompositionTimelineScrubbing)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                SeekCompositionTimeline(e.GetPosition(CompositionTimelineCanvas).X);
                e.Handled = true;
            }
            return;
        }
        if (_pendingCompositionAudioClipDragId is Guid audioClipId)
        {
            UpdateCompositionAudioClipDrag(audioClipId, e);
            return;
        }
        if (_pendingCompositionSegmentDragId is not Guid segmentId) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetCompositionSegmentDrag();
            return;
        }

        var point = e.GetPosition(CompositionTimelineCanvas);
        if (_activeCompositionSegmentDragId is null &&
            Math.Abs(point.X - _compositionSegmentDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _compositionSegmentDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        UpdateCompositionSegmentDragPointer(segmentId, point.X);
        UpdateCompositionTimelineItemDragAutoScroll(point.X);
        CompositionTimelineCanvas.Cursor = Cursors.SizeAll;
        RenderCompositionTimeline();
        e.Handled = true;
    }

    private async void CompositionTimelineCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isCompositionTimelineScrubbing)
        {
            SeekCompositionTimeline(e.GetPosition(CompositionTimelineCanvas).X);
            CompleteCompositionTimelineScrub();
            e.Handled = true;
            return;
        }
        if (_pendingCompositionAudioClipDragId is Guid audioClipId)
        {
            await CompleteCompositionAudioClipDragAsync(audioClipId, e);
            return;
        }
        if (_pendingCompositionSegmentDragId is not Guid segmentId) return;
        var point = e.GetPosition(CompositionTimelineCanvas);
        var targetIndex = _compositionSegmentDragTargetIndex;
        var shouldCommit = _activeCompositionSegmentDragId is not null &&
                           targetIndex >= 0 &&
                           targetIndex != _compositionSegmentDragOriginalIndex &&
                           point.Y >= 0 && point.Y <= CompositionTimelineCanvas.ActualHeight;
        ResetCompositionSegmentDrag(render: false);
        if (Mouse.Captured == CompositionTimelineCanvas) Mouse.Capture(null);
        RenderCompositionTimeline();
        e.Handled = true;
        if (!shouldCommit) return;

        await RunUiActionAsync("Reordering composition segment…", async () =>
        {
            await new WorkingCompositionService(_workspace).MoveSegmentToIndexAsync(segmentId, targetIndex);
            RefreshEditWorkspaceState();
            _selectedCompositionSegmentId = segmentId;
            _selectedCompositionAudioClipId = null;
            RenderCompositionTimeline();
            StatusText.Text = "Reordered the Working Composition. Preview it to rebuild the video.";
        });
    }

    private void CompositionTimelineCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isCompositionTimelineScrubbing)
        {
            CancelCompositionTimelineScrub();
            return;
        }
        if (_pendingCompositionAudioClipDragId is not null)
        {
            ResetCompositionAudioClipDrag();
            return;
        }
        if (_pendingCompositionSegmentDragId is not null) ResetCompositionSegmentDrag();
    }

    private void SeekCompositionTimeline(double x)
    {
        if (_compositionTimelineLayout is null || VideoPreview.Source is null) return;
        var target = _compositionTimelineLayout.GetTimeAtX(x);
        SeekPreview(target);
        _videoPreviewHasEnded = false;
        PositionSlider.Value = target;
        UpdateCompositionTimelinePlayhead(target);
    }

    private void CompleteCompositionTimelineScrub()
    {
        var resumePlayback = _resumePlaybackAfterCompositionTimelineScrub;
        _isCompositionTimelineScrubbing = false;
        _resumePlaybackAfterCompositionTimelineScrub = false;
        CompositionTimelineCanvas.Cursor = null;
        if (Mouse.Captured == CompositionTimelineCanvas) Mouse.Capture(null);
        if (resumePlayback)
        {
            VideoPreview.Play();
            SetPlaybackState(true);
        }
        else
        {
            VideoPreview.Pause();
            SetPlaybackState(false);
        }
    }

    private void CancelCompositionTimelineScrub()
    {
        _isCompositionTimelineScrubbing = false;
        _resumePlaybackAfterCompositionTimelineScrub = false;
        CompositionTimelineCanvas.Cursor = null;
        VideoPreview.Pause();
        SetPlaybackState(false);
    }

    private void ResetCompositionSegmentDrag(bool render = true)
    {
        StopCompositionTimelineItemDragAutoScroll();
        _pendingCompositionSegmentDragId = null;
        _activeCompositionSegmentDragId = null;
        _compositionSegmentDragOriginalIndex = -1;
        _compositionSegmentDragTargetIndex = -1;
        CompositionTimelineCanvas.Cursor = null;
        if (Mouse.Captured == CompositionTimelineCanvas) Mouse.Capture(null);
        if (render) RenderCompositionTimeline();
    }

    private void CompositionTimelineAudioClip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Guid audioClipId }) return;
        if (GetSelectedCompositionAudioClip(audioClipId) is not { } audioClip) return;
        _selectedCompositionSegmentId = null;
        _selectedCompositionAudioClipId = audioClipId;
        var point = e.GetPosition(CompositionTimelineCanvas);
        var startX = _compositionTimelineLayout?.GetPlayheadX(audioClip.TimelineStart.TotalSeconds) ?? point.X;
        _pendingCompositionAudioClipDragId = audioClipId;
        _activeCompositionAudioClipDragId = null;
        _compositionAudioClipDragStart = point;
        _compositionAudioClipDragPointerOffset = Math.Max(0, point.X - startX);
        _compositionAudioClipDraftStartSeconds = audioClip.TimelineStart.TotalSeconds;
        _compositionAudioClipOriginalStartTicks = audioClip.TimelineStart.Ticks;
        CompositionTimelineCanvas.CaptureMouse();
        UpdateCompositionActionState();
        RenderCompositionTimeline();
        e.Handled = true;
    }

    private void UpdateCompositionAudioClipDrag(Guid audioClipId, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetCompositionAudioClipDrag();
            return;
        }

        var point = e.GetPosition(CompositionTimelineCanvas);
        if (_activeCompositionAudioClipDragId is null &&
            Math.Abs(point.X - _compositionAudioClipDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _compositionAudioClipDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_compositionTimelineLayout is null) return;
        UpdateCompositionAudioClipDragPointer(audioClipId, point.X);
        UpdateCompositionTimelineItemDragAutoScroll(point.X);
        CompositionTimelineCanvas.Cursor = Cursors.SizeWE;
        RenderCompositionTimeline();
        e.Handled = true;
    }

    private async Task CompleteCompositionAudioClipDragAsync(Guid audioClipId, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(CompositionTimelineCanvas);
        var draftStart = TimeSpan.FromMilliseconds(Math.Round(
            Math.Max(0, _compositionAudioClipDraftStartSeconds) * 1000,
            MidpointRounding.AwayFromZero));
        var shouldCommit = _activeCompositionAudioClipDragId is not null &&
                           draftStart.Ticks != _compositionAudioClipOriginalStartTicks &&
                           point.Y >= 0 && point.Y <= CompositionTimelineCanvas.ActualHeight;
        ResetCompositionAudioClipDrag(render: false);
        e.Handled = true;
        if (!shouldCommit)
        {
            RenderCompositionTimeline();
            return;
        }

        await RunUiActionAsync("Moving composition audio clip…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetAudioClipTimelineStartAsync(audioClipId, draftStart);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = audioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Moved the audio clip to {FormatTimelineTimePrecise(draftStart.TotalSeconds)}. Preview the composition to rebuild it.";
        });
        RenderCompositionTimeline();
    }

    private void ResetCompositionAudioClipDrag(bool render = true)
    {
        StopCompositionTimelineItemDragAutoScroll();
        _pendingCompositionAudioClipDragId = null;
        _activeCompositionAudioClipDragId = null;
        _compositionAudioClipDragPointerOffset = 0;
        _compositionAudioClipOriginalStartTicks = 0;
        CompositionTimelineCanvas.Cursor = null;
        if (Mouse.Captured == CompositionTimelineCanvas) Mouse.Capture(null);
        if (render) RenderCompositionTimeline();
    }

    private void UpdateCompositionSegmentDragPointer(Guid segmentId, double contentX)
    {
        _activeCompositionSegmentDragId = segmentId;
        _compositionSegmentDragPointerX = contentX;
        var preview = CompositionTimelineLayout.CalculateReorder(
            _compositionSegments
                .Select(item => new CompositionTimelineSegmentInput(item.SegmentId, item.DurationSeconds))
                .ToArray(),
            segmentId,
            contentX,
            GetCompositionTimelineViewportWidth(),
            zoomFactor: _compositionTimelineZoom);
        _compositionSegmentDragTargetIndex = preview.InsertionIndex;
    }

    private void UpdateCompositionAudioClipDragPointer(Guid audioClipId, double contentX)
    {
        if (_compositionTimelineLayout is null) return;
        _activeCompositionAudioClipDragId = audioClipId;
        _compositionAudioClipDraftStartSeconds = _compositionTimelineLayout.GetTimeAtX(
            contentX - _compositionAudioClipDragPointerOffset);
    }

    private void UpdateCompositionTimelineItemDragAutoScroll(double contentX)
    {
        var scrollViewer = CompositionTimelineScrollViewer;
        var viewportWidth = scrollViewer.ViewportWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            StopCompositionTimelineItemDragAutoScroll();
            return;
        }

        var rawViewportX = contentX - scrollViewer.HorizontalOffset;
        _compositionTimelineItemDragViewportX = Math.Clamp(rawViewportX, 0, viewportWidth);
        _compositionTimelineItemDragAutoScrollDelta = CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            rawViewportX,
            viewportWidth);
        if (Math.Abs(_compositionTimelineItemDragAutoScrollDelta) < 0.1)
        {
            _compositionTimelineItemDragAutoScrollTimer.Stop();
            return;
        }
        if (!_compositionTimelineItemDragAutoScrollTimer.IsEnabled)
            _compositionTimelineItemDragAutoScrollTimer.Start();
    }

    private void CompositionTimelineItemDragAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_disposed ||
            (_activeCompositionSegmentDragId is null && _activeCompositionAudioClipDragId is null) ||
            _compositionTimelineLayout is null)
        {
            StopCompositionTimelineItemDragAutoScroll();
            return;
        }

        var scrollViewer = CompositionTimelineScrollViewer;
        var maximumOffset = Math.Max(0, scrollViewer.ExtentWidth - scrollViewer.ViewportWidth);
        var desiredOffset = Math.Clamp(
            scrollViewer.HorizontalOffset + _compositionTimelineItemDragAutoScrollDelta,
            0,
            maximumOffset);
        if (Math.Abs(desiredOffset - scrollViewer.HorizontalOffset) < 0.1)
        {
            _compositionTimelineItemDragAutoScrollTimer.Stop();
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(desiredOffset);
        scrollViewer.UpdateLayout();
        var contentX = scrollViewer.HorizontalOffset + Math.Clamp(
            _compositionTimelineItemDragViewportX,
            0,
            Math.Max(1, scrollViewer.ViewportWidth));
        if (_activeCompositionSegmentDragId is Guid segmentId)
            UpdateCompositionSegmentDragPointer(segmentId, contentX);
        else if (_activeCompositionAudioClipDragId is Guid audioClipId)
            UpdateCompositionAudioClipDragPointer(audioClipId, contentX);
        RenderCompositionTimeline();
    }

    private void StopCompositionTimelineItemDragAutoScroll()
    {
        _compositionTimelineItemDragAutoScrollTimer.Stop();
        _compositionTimelineItemDragAutoScrollDelta = 0;
    }

    private void RenderCompositionTimeline()
    {
        if (_renderingCompositionTimeline || CompositionTimelineCanvas is null ||
            CompositionTimelineScrollViewer is null || CompositionTimelineDurationText is null)
            return;

        _renderingCompositionTimeline = true;
        try
        {
            var viewportWidth = GetCompositionTimelineViewportWidth();
            var timelineItems = _compositionSegments.ToList();
            if (_activeCompositionSegmentDragId is Guid draggedSegmentId &&
                _compositionSegmentDragTargetIndex >= 0)
            {
                var draggedItem = timelineItems.Single(item => item.SegmentId == draggedSegmentId);
                timelineItems.Remove(draggedItem);
                timelineItems.Insert(
                    Math.Clamp(_compositionSegmentDragTargetIndex, 0, timelineItems.Count),
                    draggedItem);
            }
            _compositionTimelineLayout = CompositionTimelineLayout.Calculate(
                timelineItems
                    .Select(item => new CompositionTimelineSegmentInput(item.SegmentId, item.DurationSeconds))
                    .ToArray(),
                viewportWidth,
                zoomFactor: _compositionTimelineZoom);
            _compositionTimelineStickyContent.Clear();
            CompositionTimelineCanvas.Children.Clear();
            CompositionTimelineCanvas.Width = _compositionTimelineLayout.ContentWidth;
            var audioLaneInputs = _compositionAudioClips
                .Select(item =>
                {
                    var isDragging = item.AudioClipId == _activeCompositionAudioClipDragId;
                    var startSeconds = Math.Clamp(
                        isDragging ? _compositionAudioClipDraftStartSeconds : item.TimelineStart.TotalSeconds,
                        0,
                        _compositionTimelineLayout.ProjectedDurationSeconds);
                    return new CompositionTimelineAudioInput(
                        item.AudioClipId,
                        startSeconds,
                        Math.Max(0.25, item.DurationSeconds ?? 1));
                })
                .ToArray();
            var audioLaneLayout = CompositionTimelineLayout.CalculateAudioLanes(audioLaneInputs);
            const double audioLaneTop = 86;
            const double audioLaneHeight = 34;
            const double audioLaneGap = 4;
            CompositionTimelineCanvas.Height = Math.Max(
                124,
                audioLaneTop + audioLaneLayout.LaneCount * (audioLaneHeight + audioLaneGap));
            CompositionTimelineDurationText.Text = _compositionTimelineLayout.Segments.Count == 0
                ? "No segments"
                : _compositionTimelineLayout.HasUnknownDurations
                    ? $"~{FormatTimelineTime(_compositionTimelineLayout.ProjectedDurationSeconds)} total • estimated"
                    : $"{FormatTimelineTime(_compositionTimelineLayout.KnownDurationSeconds)} total";

            DrawCompositionTimelineRuler(_compositionTimelineLayout);
            var selectedId = _selectedCompositionSegmentId;
            for (var index = 0; index < _compositionTimelineLayout.Segments.Count; index++)
            {
                var span = _compositionTimelineLayout.Segments[index];
                var item = timelineItems.Single(candidate => candidate.SegmentId == span.SegmentId);
                var isSelected = item.SegmentId == selectedId;
                var isDragging = item.SegmentId == _activeCompositionSegmentDragId;
                var segmentBorder = new Border
                {
                    Tag = item.SegmentId,
                    Width = Math.Max(1, span.Width - 3),
                    Height = 57,
                    Background = new SolidColorBrush(isSelected
                        ? Color.FromRgb(62, 54, 105)
                        : Color.FromRgb(31, 37, 51)),
                    BorderBrush = isSelected
                        ? FindResource("AccentBrush") as Brush ?? Brushes.MediumPurple
                        : FindResource("BorderBrush") as Brush ?? Brushes.DimGray,
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 5, 7, 4),
                    ClipToBounds = true,
                    Opacity = isDragging ? 0.84 : 1,
                    Cursor = Cursors.SizeAll,
                    ToolTip = $"{index + 1}. {item.DisplayName}\nStarts at {FormatTimelineTime(span.StartSeconds)}\n{item.DurationText} • {item.AudioText}\nClick to select or drag to reorder"
                };
                if (isDragging)
                    segmentBorder.Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 12,
                        ShadowDepth = 3,
                        Opacity = 0.65
                    };
                var text = new StackPanel();
                text.Children.Add(new TextBlock
                {
                    Text = $"{index + 1}. {item.DisplayName}",
                    Foreground = FindResource("TextBrush") as Brush ?? Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                text.Children.Add(new TextBlock
                {
                    Text = $"{item.DurationText} • {item.AudioText}",
                    Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
                    FontSize = 10,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var identityBadge = new Border
                {
                    Child = text,
                    MaxWidth = 320,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 34)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 2, 4, 2)
                };
                var segmentContents = new Grid();
                segmentContents.Children.Add(identityBadge);
                var removeButton = CreateTimelineRemoveButton(item.SegmentId);
                removeButton.IsEnabled = _compositionSegments.Count > 1;
                segmentContents.Children.Add(removeButton);
                segmentBorder.Child = segmentContents;
                segmentBorder.ContextMenu = CreateCompositionSegmentContextMenu(item.SegmentId, index);
                segmentBorder.MouseEnter += (_, _) => removeButton.Visibility = Visibility.Visible;
                segmentBorder.MouseLeave += (_, _) => removeButton.Visibility = Visibility.Collapsed;
                segmentBorder.MouseLeftButtonDown += CompositionTimelineSegment_MouseLeftButtonDown;
                var left = isDragging
                    ? Math.Clamp(
                        _compositionSegmentDragPointerX - _compositionSegmentDragPointerOffset,
                        0,
                        Math.Max(0, _compositionTimelineLayout.ContentWidth - span.Width))
                    : span.Left + 1;
                Canvas.SetLeft(segmentBorder, left);
                Canvas.SetTop(segmentBorder, 25);
                if (isDragging) Panel.SetZIndex(segmentBorder, 20);
                CompositionTimelineCanvas.Children.Add(segmentBorder);
                _compositionTimelineStickyContent.Add(new TimelineStickyContent(
                    identityBadge,
                    left,
                    segmentBorder.Width,
                    64));
            }

            var selectedAudioId = _selectedCompositionAudioClipId;
            foreach (var item in _compositionAudioClips)
            {
                var isDragging = item.AudioClipId == _activeCompositionAudioClipDragId;
                var startSeconds = Math.Clamp(
                    isDragging ? _compositionAudioClipDraftStartSeconds : item.TimelineStart.TotalSeconds,
                    0,
                    _compositionTimelineLayout.ProjectedDurationSeconds);
                var left = _compositionTimelineLayout.GetPlayheadX(startSeconds);
                var endSeconds = Math.Min(
                    _compositionTimelineLayout.ProjectedDurationSeconds,
                    startSeconds + Math.Max(0.25, item.DurationSeconds ?? 1));
                var right = _compositionTimelineLayout.GetPlayheadX(endSeconds);
                var width = Math.Max(56, right - left);
                if (left + width > _compositionTimelineLayout.ContentWidth)
                    width = Math.Max(1, _compositionTimelineLayout.ContentWidth - left);
                var isSelected = item.AudioClipId == selectedAudioId;
                var audioBorder = new Border
                {
                    Tag = item.AudioClipId,
                    Width = width,
                    Height = 34,
                    Background = new SolidColorBrush(isSelected
                        ? Color.FromRgb(42, 91, 74)
                        : Color.FromRgb(28, 65, 55)),
                    BorderBrush = isSelected
                        ? FindResource("AccentBrush") as Brush ?? Brushes.MediumPurple
                        : new SolidColorBrush(Color.FromRgb(55, 136, 107)),
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(7, 3, 6, 3),
                    ClipToBounds = true,
                    Opacity = isDragging ? 0.86 : 1,
                    Cursor = Cursors.SizeWE,
                    ToolTip = $"Audio: {item.DisplayName}\nStarts at {FormatTimelineTimePrecise(startSeconds)}\n{item.DurationText} • {item.MixText}\nClick to select or drag to move"
                };
                if (isDragging)
                    audioBorder.Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 10,
                        ShadowDepth = 3,
                        Opacity = 0.65
                    };
                var audioText = new StackPanel();
                audioText.Children.Add(new TextBlock
                {
                    Text = $"♪ {item.DisplayName}",
                    Foreground = FindResource("TextBrush") as Brush ?? Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                audioText.Children.Add(new TextBlock
                {
                    Text = item.MixText,
                    Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
                    FontSize = 9,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var audioIdentityBadge = new Border
                {
                    Child = audioText,
                    MaxWidth = 300,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(220, 13, 31, 26)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1, 4, 1)
                };
                var audioContents = new Grid();
                audioContents.Children.Add(audioIdentityBadge);
                var removeAudioButton = CreateTimelineRemoveButton(item.AudioClipId);
                audioContents.Children.Add(removeAudioButton);
                audioBorder.Child = audioContents;
                audioBorder.ContextMenu = CreateCompositionAudioContextMenu(item.AudioClipId);
                audioBorder.MouseEnter += (_, _) => removeAudioButton.Visibility = Visibility.Visible;
                audioBorder.MouseLeave += (_, _) => removeAudioButton.Visibility = Visibility.Collapsed;
                audioBorder.MouseLeftButtonDown += CompositionTimelineAudioClip_MouseLeftButtonDown;
                Canvas.SetLeft(audioBorder, left + 1);
                Canvas.SetTop(
                    audioBorder,
                    audioLaneTop + audioLaneLayout.LaneByAudioClipId[item.AudioClipId] *
                    (audioLaneHeight + audioLaneGap));
                CompositionTimelineCanvas.Children.Add(audioBorder);
                _compositionTimelineStickyContent.Add(new TimelineStickyContent(
                    audioIdentityBadge,
                    left + 1,
                    audioBorder.Width,
                    48));
            }

            _compositionTimelinePlayhead = new Line
            {
                Y1 = 16,
                Y2 = CompositionTimelineCanvas.Height - 2,
                Stroke = FindResource("AccentBrush") as Brush ?? Brushes.MediumPurple,
                StrokeThickness = 2,
                IsHitTestVisible = false,
                Visibility = _activeCompositionSegmentDragId is not null ||
                             _activeCompositionAudioClipDragId is not null
                    ? Visibility.Collapsed
                    : Visibility.Visible
            };
            Panel.SetZIndex(_compositionTimelinePlayhead, 10);
            CompositionTimelineCanvas.Children.Add(_compositionTimelinePlayhead);
            UpdateCompositionTimelineStickyContent();
            UpdateCompositionTimelinePlayhead(GetCurrentTimelinePlaybackSeconds());
        }
        finally
        {
            _renderingCompositionTimeline = false;
        }
    }

    private Button CreateTimelineRemoveButton(Guid itemId)
    {
        var button = new Button
        {
            Tag = itemId,
            Content = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 12,
                Foreground = Brushes.White
            },
            Width = 27,
            Height = 25,
            Padding = new Thickness(4),
            Margin = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Style = FindResource("DangerButtonStyle") as Style,
            ToolTip = "Remove from composition"
        };
        button.Click += async (_, args) =>
        {
            args.Handled = true;
            await RemoveCompositionItemAsync(itemId);
        };
        Panel.SetZIndex(button, 30);
        return button;
    }

    private ContextMenu CreateCompositionSegmentContextMenu(Guid segmentId, int index)
    {
        var menu = new ContextMenu();
        var detachAudio = new MenuItem
        {
            Header = "Detach audio…",
            IsEnabled = CanDetachCompositionSegmentAudio(segmentId)
        };
        detachAudio.Click += async (_, _) => await DetachCompositionSegmentAudioAsync(segmentId);
        var split = new MenuItem
        {
            Header = GetCompositionSplitActionLabel(),
            IsEnabled = CanSplitCompositionSegment(segmentId, GetCurrentTimelinePlaybackSeconds())
        };
        split.Click += async (_, _) =>
        {
            _selectedCompositionSegmentId = segmentId;
            _selectedCompositionAudioClipId = null;
            await SplitCompositionSegmentAsync(segmentId);
        };
        var shiftLeft = new MenuItem { Header = "Shift Left", IsEnabled = index > 0 };
        shiftLeft.Click += async (_, _) => await MoveCompositionSegmentAsync(segmentId, -1);
        var shiftRight = new MenuItem
        {
            Header = "Shift Right",
            IsEnabled = index < _compositionSegments.Count - 1
        };
        shiftRight.Click += async (_, _) => await MoveCompositionSegmentAsync(segmentId, 1);
        var remove = new MenuItem
        {
            Header = "Remove",
            Foreground = new SolidColorBrush(Color.FromRgb(145, 24, 47)),
            IsEnabled = _compositionSegments.Count > 1
        };
        remove.Click += async (_, _) => await RemoveCompositionItemAsync(segmentId);
        menu.Opened += (_, _) =>
        {
            var currentIndex = _compositionSegments.ToList().FindIndex(item => item.SegmentId == segmentId);
            detachAudio.IsEnabled = CanDetachCompositionSegmentAudio(segmentId);
            split.Header = GetCompositionSplitActionLabel();
            split.IsEnabled = CanSplitCompositionSegment(segmentId, GetCurrentTimelinePlaybackSeconds());
            shiftLeft.IsEnabled = currentIndex > 0;
            shiftRight.IsEnabled = currentIndex >= 0 && currentIndex < _compositionSegments.Count - 1;
            remove.IsEnabled = currentIndex >= 0 && _compositionSegments.Count > 1;
        };
        menu.Items.Add(detachAudio);
        menu.Items.Add(split);
        menu.Items.Add(new Separator());
        menu.Items.Add(shiftLeft);
        menu.Items.Add(shiftRight);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        return menu;
    }

    private bool CanDetachCompositionSegmentAudio(Guid segmentId)
    {
        if (_workspace.Project is null || _workspace.Project.WorkingCompositionAssetId is null) return false;
        var (_, _, recipe) = new WorkingCompositionService(_workspace).GetCurrent();
        var segment = recipe.Segments.SingleOrDefault(candidate => candidate.Id == segmentId);
        if (segment is null) return false;
        var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId);
        var encoding = source?.Encoding ?? source?.Virtual?.ExpectedMediaProperties;
        if (encoding is not null && encoding.Audio is null) return false;
        return !recipe.AudioClips.Any(clip =>
            _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == clip.Source.AssetId)?.Provenance is
            {
                Operation: "detach-segment-audio"
            } provenance &&
            provenance.Parameters.GetValueOrDefault("compositionSegmentId") == segmentId.ToString("D"));
    }

    private ContextMenu CreateCompositionAudioContextMenu(Guid audioClipId)
    {
        var menu = new ContextMenu();
        var remove = new MenuItem
        {
            Header = "Remove",
            Foreground = new SolidColorBrush(Color.FromRgb(145, 24, 47))
        };
        remove.Click += async (_, _) => await RemoveCompositionItemAsync(audioClipId);
        menu.Items.Add(remove);
        return menu;
    }

    private bool CanSplitCompositionSegment(Guid segmentId, double playbackSeconds)
    {
        var span = _compositionTimelineLayout?.Segments.SingleOrDefault(candidate =>
            candidate.SegmentId == segmentId);
        var segment = _compositionSegments.SingleOrDefault(candidate => candidate.SegmentId == segmentId);
        var splitAfter = _applicationSettings.MediaTools.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame;
        return segment?.DurationSeconds is > 0 &&
               span is not null &&
               (splitAfter
                   ? playbackSeconds >= span.StartSeconds - 0.000_000_1
                   : playbackSeconds > span.StartSeconds + 0.000_000_1) &&
               playbackSeconds < span.StartSeconds + span.DurationSeconds - 0.000_000_1;
    }

    private string GetCompositionSplitActionLabel() =>
        _applicationSettings.MediaTools.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame
            ? "Split after playhead frame"
            : "Split before playhead frame";

    private double GetCurrentTimelinePlaybackSeconds()
    {
        if (_activeCompositionDraftRevisionId is null ||
            _activeCompositionDraftSegmentIndex < 0 ||
            _activeCompositionDraftSegmentIndex >= _compositionDraftSegments.Count)
            return VideoPreview.Position.TotalSeconds;
        var segment = _compositionDraftSegments[_activeCompositionDraftSegmentIndex];
        return Math.Clamp(
            segment.TimelineStartSeconds + VideoPreview.Position.TotalSeconds - segment.SourceStartSeconds,
            segment.TimelineStartSeconds,
            segment.TimelineStartSeconds + segment.DurationSeconds);
    }

    private bool IsWorkingCompositionSelected() =>
        _workspace.Project?.WorkingCompositionAssetId is { } compositionId &&
        AssetsList.SelectedItem is ProjectMediaListItem { Asset: { } selected } &&
        selected.Id == compositionId;

    private void SelectWorkingCompositionInProjectMedia()
    {
        if (_workspace.Project?.WorkingCompositionAssetId is not { } compositionId) return;
        var item = _assets.FirstOrDefault(candidate => candidate.Asset?.Id == compositionId);
        if (item is not null && AssetsList.SelectedItem != item) AssetsList.SelectedItem = item;
    }

    private void DrawCompositionTimelineRuler(CompositionTimelineLayoutResult layout)
    {
        if (layout.Segments.Count == 0) return;
        var tickCount = Math.Clamp((int)(layout.ContentWidth / 140), 2, 80);
        var showMilliseconds = _compositionTimelineZoom > 1.001;
        for (var index = 0; index <= tickCount; index++)
        {
            var x = layout.ContentWidth * index / tickCount;
            var seconds = layout.ProjectedDurationSeconds * index / tickCount;
            CompositionTimelineCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 17,
                Y2 = 23,
                Stroke = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
                StrokeThickness = 1,
                IsHitTestVisible = false
            });
            var label = new TextBlock
            {
                Text = showMilliseconds
                    ? FormatTimelineRulerTimePrecise(seconds)
                    : FormatTimelineTime(seconds),
                Foreground = FindResource("MutedTextBrush") as Brush ?? Brushes.LightGray,
                FontSize = 9,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(
                x - label.DesiredSize.Width / 2,
                1,
                Math.Max(1, layout.ContentWidth - label.DesiredSize.Width - 1)));
            Canvas.SetTop(label, 1);
            CompositionTimelineCanvas.Children.Add(label);
        }
    }

    private void UpdateCompositionTimelineStickyContent()
    {
        if (CompositionTimelineScrollViewer is null) return;
        var viewportLeft = Math.Max(0, CompositionTimelineScrollViewer.HorizontalOffset);
        foreach (var content in _compositionTimelineStickyContent)
        {
            var offset = CompositionTimelineLayout.GetStickyContentOffset(
                content.ItemLeft,
                content.ItemWidth,
                viewportLeft,
                content.MinimumTrailingWidth);
            content.Element.Margin = new Thickness(offset, 0, 0, 0);
        }
    }

    private void UpdateCompositionTimelinePlayhead(double playbackSeconds)
    {
        if (_compositionTimelinePlayhead is null || _compositionTimelineLayout is null ||
            (_activeCompositionPreviewRevisionId is null && _activeCompositionDraftRevisionId is null))
        {
            if (_compositionTimelinePlayhead is not null)
                _compositionTimelinePlayhead.Visibility = Visibility.Collapsed;
            return;
        }
        var x = _compositionTimelineLayout.GetPlayheadX(playbackSeconds);
        _compositionTimelinePlayhead.X1 = x;
        _compositionTimelinePlayhead.X2 = x;
        _compositionTimelinePlayhead.Visibility = Visibility.Visible;
        if (_isVideoPlaying && CompositionTimelineAutoScrollCheckBox?.IsChecked == true &&
            CompositionTimelineScrollViewer is { } scrollViewer &&
            scrollViewer.ViewportWidth > 0)
        {
            var desiredOffset = _compositionTimelineLayout.GetAutoScrollOffset(
                playbackSeconds,
                scrollViewer.HorizontalOffset,
                scrollViewer.ViewportWidth);
            if (Math.Abs(desiredOffset - scrollViewer.HorizontalOffset) > 0.5)
                scrollViewer.ScrollToHorizontalOffset(desiredOffset);
        }
    }

    private void ClearCompositionTimeline()
    {
        if (CompositionTimelineCanvas is null || CompositionTimelineDurationText is null) return;
        CompositionTimelineCanvas.Children.Clear();
        _compositionTimelineStickyContent.Clear();
        CompositionTimelineCanvas.Width = 1;
        CompositionTimelineDurationText.Text = "No segments";
        _compositionTimelineLayout = null;
        _compositionTimelinePlayhead = null;
    }

    private static string FormatTimelineTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatTimelineTimePrecise(double seconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Round(
            Math.Max(0, seconds) * 1000,
            MidpointRounding.AwayFromZero));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatTimelineRulerTimePrecise(double seconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Round(
            Math.Max(0, seconds) * 1000,
            MidpointRounding.AwayFromZero));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatGainDecibels(double gainDecibels) =>
        $"{(gainDecibels > 0 ? "+" : string.Empty)}{gainDecibels:0} dB";

    private static string FormatAudioPan(double pan)
    {
        if (Math.Abs(pan) < 0.000_001) return "Center";
        return $"{Math.Round(Math.Abs(pan) * 100):0}% {(pan < 0 ? "left" : "right")}";
    }

    private static string FormatFadeDuration(double seconds) =>
        $"{Math.Max(0, seconds):0.###}s";

    private void UpdateCompositionActionState()
    {
        var index = _selectedCompositionSegmentId is { } selectedId
            ? _compositionSegments.ToList().FindIndex(item => item.SegmentId == selectedId)
            : -1;
        var selectedSegment = index >= 0 ? _compositionSegments[index] : null;
        PreviewCompositionButton.IsEnabled = _compositionSegments.Count > 0 && _compositionRenderCancellation is null;
        ExportCompositionButton.IsEnabled = _compositionSegments.Count > 0 && _compositionRenderCancellation is null;
        if (EditToolsEmptyState is null) return;
        _suppressCompositionAudioControl = true;
        try
        {
            var selectedAudio = GetSelectedCompositionAudioClip();
            EditToolsEmptyState.Visibility = selectedSegment is null && selectedAudio is null
                ? Visibility.Visible
                : Visibility.Collapsed;
            VideoSegmentEditTools.Visibility = selectedSegment is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            AudioClipEditTools.Visibility = selectedAudio is null
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (selectedSegment is not null)
            {
                EditVideoSegmentNameText.Text = selectedSegment.DisplayName;
                EditVideoSegmentSourceText.Text = selectedSegment.DetailText;
                EditVideoSegmentTimingText.Text =
                    $"{selectedSegment.DurationText} • position {selectedSegment.Index + 1} of {_compositionSegments.Count} on the sequential video track";
            }

            if (selectedAudio is not null)
            {
                EditAudioClipNameText.Text = selectedAudio.DisplayName;
                EditAudioClipTimingText.Text =
                    $"Starts at {FormatTimelineTimePrecise(selectedAudio.TimelineStart.TotalSeconds)} • {selectedAudio.DurationText}";
            }

            CompositionSegmentAudioOnButton.IsChecked = selectedSegment?.AudioEnabled == true;
            CompositionSegmentAudioMutedButton.IsChecked = selectedSegment is { AudioEnabled: false };
        }
        finally
        {
            _suppressCompositionAudioControl = false;
        }

        _suppressCompositionAudioClipControl = true;
        try
        {
            var selectedAudio = GetSelectedCompositionAudioClip();
            CompositionAudioClipEnabledButton.IsChecked = selectedAudio is { IsMuted: false };
            CompositionAudioClipMutedButton.IsChecked = selectedAudio?.IsMuted == true;
            CompositionAudioClipGainSlider.Value = selectedAudio?.GainDecibels ?? 0;
            CompositionAudioClipGainSlider.IsEnabled = selectedAudio is not null;
            CompositionAudioClipGainText.Text = FormatGainDecibels(selectedAudio?.GainDecibels ?? 0);
            CompositionAudioClipPanSlider.Value = selectedAudio?.Pan ?? 0;
            CompositionAudioClipPanSlider.IsEnabled = selectedAudio is not null;
            CompositionAudioClipPanText.Text = FormatAudioPan(selectedAudio?.Pan ?? 0);
            var maxFadeSeconds = GetMaximumAudioFadeSeconds(selectedAudio);
            CompositionAudioClipFadeInSlider.Maximum = Math.Max(
                maxFadeSeconds,
                selectedAudio?.FadeIn.TotalSeconds ?? 0);
            CompositionAudioClipFadeOutSlider.Maximum = Math.Max(
                maxFadeSeconds,
                selectedAudio?.FadeOut.TotalSeconds ?? 0);
            CompositionAudioClipFadeInSlider.Value = selectedAudio?.FadeIn.TotalSeconds ?? 0;
            CompositionAudioClipFadeOutSlider.Value = selectedAudio?.FadeOut.TotalSeconds ?? 0;
            CompositionAudioClipFadeInSlider.IsEnabled = selectedAudio is not null && maxFadeSeconds > 0;
            CompositionAudioClipFadeOutSlider.IsEnabled = selectedAudio is not null && maxFadeSeconds > 0;
            CompositionAudioClipFadeInText.Text = FormatFadeDuration(selectedAudio?.FadeIn.TotalSeconds ?? 0);
            CompositionAudioClipFadeOutText.Text = FormatFadeDuration(selectedAudio?.FadeOut.TotalSeconds ?? 0);
        }
        finally
        {
            _suppressCompositionAudioClipControl = false;
        }
    }

    private double GetMaximumAudioFadeSeconds(CompositionAudioClipListItem? selectedAudio)
    {
        if (selectedAudio is null) return 0;
        var maximum = selectedAudio.DurationSeconds ?? 30;
        if (_compositionTimelineLayout is { } layout)
            maximum = Math.Min(maximum, Math.Max(
                0,
                layout.ProjectedDurationSeconds - selectedAudio.TimelineStart.TotalSeconds));
        return Math.Max(0, maximum);
    }

    private CompositionSegmentListItem? GetSelectedCompositionSegment() =>
        _selectedCompositionSegmentId is { } id
            ? _compositionSegments.FirstOrDefault(item => item.SegmentId == id)
            : null;

    private CompositionAudioClipListItem? GetSelectedCompositionAudioClip() =>
        _selectedCompositionAudioClipId is { } id
            ? _compositionAudioClips.FirstOrDefault(item => item.AudioClipId == id)
            : null;

    private CompositionAudioClipListItem? GetSelectedCompositionAudioClip(Guid audioClipId) =>
        _compositionAudioClips.FirstOrDefault(item => item.AudioClipId == audioClipId);

    private static CompositionRecipe AssertCompositionRecipe(RecipeRevision revision) =>
        revision.Recipe as CompositionRecipe
        ?? throw new InvalidDataException("The Working Composition update did not produce a composition recipe.");

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Working Composition" : safe;
    }

    private async void SelectFrame_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Video } asset)
            return;
        EnterMediaPreparationMode(MediaPreparationMode.SelectFrame, asset);
        await LoadFrameWorkspaceAsync(asset, _workspace.Project?.Id);
    }

    private async void MakeClip_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Video } asset)
            return;
        _clipStart = ClipBoundarySelection.SourceStart;
        _clipEnd = ClipBoundarySelection.SourceEnd;
        ClipNameTextBox.Text = $"{Path.GetFileNameWithoutExtension(asset.EffectiveDisplayName)} clip";
        EnterMediaPreparationMode(MediaPreparationMode.MakeClip, asset);
        UpdateClipBoundarySummary();
        await LoadFrameWorkspaceAsync(asset, _workspace.Project?.Id);
    }

    private void EnterMediaPreparationMode(MediaPreparationMode mode, ProjectAsset asset)
    {
        if (_mediaPreparationMode == MediaPreparationMode.None)
            _previewWasMutedBeforeMediaPreparation = VideoPreview.IsMuted;
        _mediaPreparationMode = mode;
        VideoPreview.IsMuted = true;
        MuteButton.IsEnabled = false;
        MuteButton.Content = "Muted";
        MuteButton.ToolTip = "Precision frame navigation is silent";
        VolumeSlider.IsEnabled = false;
        MediaPreparationHome.Visibility = Visibility.Collapsed;
        PrecisionFramePanel.Visibility = Visibility.Visible;
        var makingClip = mode == MediaPreparationMode.MakeClip;
        PrecisionOperationTitle.Text = makingClip ? "MAKE CLIP" : "SELECT FRAME";
        FrameSelectionActions.Visibility = makingClip ? Visibility.Collapsed : Visibility.Visible;
        ClipSelectionActions.Visibility = makingClip ? Visibility.Visible : Visibility.Collapsed;
        SavedFramesHeading.Visibility = makingClip ? Visibility.Collapsed : Visibility.Visible;
        SavedFramesWorkspace.Visibility = makingClip ? Visibility.Collapsed : Visibility.Visible;
        ClipEditorWorkspace.Visibility = makingClip ? Visibility.Visible : Visibility.Collapsed;
        FrameWorkspaceStatusText.Text = asset.EffectiveDisplayName;
    }

    private void ExitFrameSelection_Click(object sender, RoutedEventArgs e)
    {
        ResetFrameWorkspace();
        PrecisionFramePanel.Visibility = Visibility.Collapsed;
        MediaPreparationHome.Visibility = Visibility.Visible;
        if (GetSelectedAsset() is { } asset) ConfigureMediaPreparationFor(asset);
    }

    private void GenerationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GenerationsList.SelectedItem is not GenerationRecord generation)
        {
            return;
        }

        AssetsList.SelectedItem = null;
        InspectorText.Text = FormatGenerationInspector(generation);
        StatusText.Text = $"Selected generation {generation.Id}.";
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen())
        {
            return;
        }

        GenerationSubmissionAuthorization? authorization = null;
        var draft = CaptureDraftFromUi();
        if (_generationProvider.CostBehavior == GenerationProviderCostBehavior.PotentiallyBillable)
        {
            if (_generationProvider is not IApiKeyVideoGenerationProvider apiKeyProvider)
                throw new InvalidOperationException("This paid provider has no configured credential contract.");
            var apiKey = await _secretStore.GetAsync(apiKeyProvider.ApiKeyCredentialKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                GenerationStatusText.Text = $"Store a {_generationProvider.Capabilities.DisplayName} API key before live submission.";
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                $"Review the prompt settings before submitting to {_generationProvider.Capabilities.DisplayName}.\n\n" +
                $"Model: {_generationProvider.Capabilities.ModelVersion}\n" +
                $"Mode: {draft.Mode}\nDuration: {draft.DurationSeconds}s\n" +
                $"Resolution: {draft.Resolution}\nReferences: {draft.References.Count}\n\n" +
                "Proceed with these settings?",
                "Confirm prompt submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                GenerationStatusText.Text = "Submission cancelled.";
                return;
            }

            authorization = GenerationSubmissionAuthorization.FromInteractiveUserConfirmation(
                _generationProvider.Capabilities.ProviderId,
                userConfirmedPotentialCharges: true);
        }

        var undoSendSeconds = Math.Clamp(_applicationSettings.General.UndoSendSeconds, 0, 30);
        if (undoSendSeconds > 0 &&
            _generationProvider is IAsyncVideoGenerationProvider)
        {
            await QueueGenerationWithUndoSendAsync(draft, authorization, undoSendSeconds);
            return;
        }

        await RunGenerationWorkflowAsync(draft, authorization);
    }

    private void AssetsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(AssetsList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item is not null) item.IsSelected = true;
    }

    private void AssetsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        var extractAudioItem = menu.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Tag, "ExtractAudio"));
        var asset = GetSelectedAsset();
        var isEligibleVideo = asset is { MediaType: MediaType.Video } &&
                              (asset.StorageKind == AssetStorageKind.Physical ||
                               asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        extractAudioItem.Visibility = isEligibleVideo ? Visibility.Visible : Visibility.Collapsed;
        if (!isEligibleVideo) return;

        var knownEncoding = asset!.StorageKind == AssetStorageKind.Physical
            ? asset.Encoding
            : asset.Virtual?.ExpectedMediaProperties;
        extractAudioItem.IsEnabled = knownEncoding?.Audio is not null || knownEncoding is null;
        extractAudioItem.ToolTip = extractAudioItem.IsEnabled
            ? "Create a permanent audio file from this video's sound."
            : "This video has no audio stream to extract.";
    }

    private void AssetsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _projectMediaDragStart = e.GetPosition(AssetsList);
        var container = ItemsControl.ContainerFromElement(
            AssetsList,
            e.OriginalSource as DependencyObject) as ListBoxItem;
        _projectMediaDragItem = container?.DataContext as ProjectMediaListItem;
    }

    private void AssetsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _projectMediaDragItem?.Asset is not { } asset ||
            !CanDragIntoComposition(asset))
            return;
        var position = e.GetPosition(AssetsList);
        if (Math.Abs(position.X - _projectMediaDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _projectMediaDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _projectMediaDragItem = null;
        var data = new DataObject(ProjectMediaDragFormat, asset.Id.ToString("D", CultureInfo.InvariantCulture));
        try
        {
            DragDrop.DoDragDrop(AssetsList, data, DragDropEffects.Copy);
        }
        finally
        {
            HideCompositionTimelineDropFeedback();
        }
    }

    private static bool CanDragIntoComposition(ProjectAsset asset) =>
        asset is { MediaType: MediaType.Audio, StorageKind: AssetStorageKind.Physical } ||
        asset.MediaType == MediaType.Video &&
        (asset.StorageKind == AssetStorageKind.Physical || asset.Virtual?.Kind == VirtualAssetKind.SavedClip);

    private void CompositionTimeline_PreviewDragEnter(object sender, DragEventArgs e) =>
        UpdateCompositionTimelineDropFeedback(e);

    private void CompositionTimeline_PreviewDragOver(object sender, DragEventArgs e) =>
        UpdateCompositionTimelineDropFeedback(e);

    private void CompositionTimeline_PreviewDragLeave(object sender, DragEventArgs e)
    {
        HideCompositionTimelineDropFeedback();
        e.Handled = true;
    }

    private async void CompositionTimeline_PreviewDrop(object sender, DragEventArgs e)
    {
        var asset = ResolveCompositionDragAsset(e.Data);
        var canDrop = asset is not null && _compositionTimelineLayout?.Segments.Count > 0;
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        var x = canDrop ? GetCompositionTimelineDragContentX(e) : 0;
        HideCompositionTimelineDropFeedback();
        if (!canDrop) return;

        if (asset!.MediaType == MediaType.Video)
        {
            var insertionIndex = GetCompositionVideoInsertionIndex(x);
            await RunUiActionAsync($"Inserting {asset.EffectiveDisplayName} into the composition…", async () =>
            {
                var revision = await new WorkingCompositionService(_workspace)
                    .AddSegmentAsync(asset.Id, insertionIndex);
                var recipe = AssertCompositionRecipe(revision);
                var segment = recipe.Segments[Math.Clamp(insertionIndex, 0, recipe.Segments.Count - 1)];
                RefreshEditWorkspaceState();
                _selectedCompositionSegmentId = segment.Id;
                _selectedCompositionAudioClipId = null;
                RenderCompositionTimeline();
                StatusText.Text = $"Inserted {asset.EffectiveDisplayName} into the Working Composition.";
            });
            return;
        }

        var startSeconds = _compositionTimelineLayout!.GetTimeAtX(x);
        await RunUiActionAsync($"Adding {asset.EffectiveDisplayName} to the audio track…", async () =>
        {
            var revision = await new WorkingCompositionService(_workspace)
                .AddAudioClipAsync(asset.Id, TimeSpan.FromSeconds(startSeconds));
            var clip = AssertCompositionRecipe(revision).AudioClips[^1];
            RefreshEditWorkspaceState();
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = clip.Id;
            RenderCompositionTimeline();
            StatusText.Text =
                $"Added {asset.EffectiveDisplayName} at {FormatTimelineTimePrecise(clip.TimelineStart.TotalSeconds)}.";
        });
    }

    private void UpdateCompositionTimelineDropFeedback(DragEventArgs e)
    {
        var asset = ResolveCompositionDragAsset(e.Data);
        var canDrop = asset is not null && _compositionTimelineLayout?.Segments.Count > 0;
        e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!canDrop)
        {
            HideCompositionTimelineDropFeedback();
            return;
        }

        var viewportX = GetCompositionTimelineDragViewportX(e);
        var x = CompositionTimelineScrollViewer.HorizontalOffset + viewportX;
        _compositionTimelineDragAsset = asset;
        _compositionTimelineDragViewportX = viewportX;
        UpdateCompositionTimelineDragAutoScroll(viewportX);
        RenderCompositionTimelineDropFeedback(asset!, x, viewportX);
    }

    private void RenderCompositionTimelineDropFeedback(ProjectAsset asset, double x, double viewportX)
    {
        CompositionTimelineDropHint.Visibility = Visibility.Visible;
        CompositionTimelineDropHint.UpdateLayout();
        var timelineOrigin = CompositionTimelineCanvas.TranslatePoint(
            new Point(0, 0),
            CompositionTimelineDropHint);
        var overlayWidth = Math.Max(1, CompositionTimelineDropHint.ActualWidth);
        var tokenWidth = CompositionTimelineDropToken.Width;
        var tokenLeft = Math.Clamp(viewportX - tokenWidth / 2, 0, Math.Max(0, overlayWidth - tokenWidth));
        var isVideo = asset.MediaType == MediaType.Video;
        var markerX = isVideo
            ? _compositionTimelineLayout!.GetVideoInsertionX(GetCompositionVideoInsertionIndex(x))
            : x;
        const double markerEdgeInset = 3;
        var markerViewportX = Math.Clamp(
            markerX - CompositionTimelineScrollViewer.HorizontalOffset,
            markerEdgeInset,
            Math.Max(markerEdgeInset, overlayWidth - CompositionTimelineDropMarker.Width - markerEdgeInset));

        CompositionTimelineDropHintText.Text = asset.EffectiveDisplayName;
        CompositionTimelineDropMarker.Height = isVideo ? 67 : 42;
        Canvas.SetLeft(CompositionTimelineDropMarker, markerViewportX);
        Canvas.SetTop(CompositionTimelineDropMarker, timelineOrigin.Y + (isVideo ? 20 : 82));
        Canvas.SetLeft(CompositionTimelineDropToken, tokenLeft);
        Canvas.SetTop(CompositionTimelineDropToken, timelineOrigin.Y + (isVideo ? 35 : 85));
    }

    private double GetCompositionTimelineDragViewportX(DragEventArgs e) => Math.Clamp(
        e.GetPosition(CompositionTimelineScrollViewer).X,
        0,
        Math.Max(1, CompositionTimelineScrollViewer.ViewportWidth));

    private double GetCompositionTimelineDragContentX(DragEventArgs e) =>
        CompositionTimelineScrollViewer.HorizontalOffset + GetCompositionTimelineDragViewportX(e);

    private void UpdateCompositionTimelineDragAutoScroll(double viewportX)
    {
        var viewportWidth = CompositionTimelineScrollViewer.ViewportWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            StopCompositionTimelineDragAutoScroll(clearAsset: false);
            return;
        }

        _compositionTimelineDragAutoScrollDelta = CompositionTimelineLayout.GetEdgeAutoScrollDelta(
            viewportX,
            viewportWidth);
        if (Math.Abs(_compositionTimelineDragAutoScrollDelta) < 0.1)
        {
            _compositionTimelineDragAutoScrollTimer.Stop();
            return;
        }
        if (!_compositionTimelineDragAutoScrollTimer.IsEnabled)
            _compositionTimelineDragAutoScrollTimer.Start();
    }

    private void CompositionTimelineDragAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_disposed || _compositionTimelineDragAsset is not { } asset ||
            _compositionTimelineLayout is null ||
            CompositionTimelineDropHint.Visibility != Visibility.Visible)
        {
            StopCompositionTimelineDragAutoScroll();
            return;
        }

        var scrollViewer = CompositionTimelineScrollViewer;
        var maximumOffset = Math.Max(0, scrollViewer.ExtentWidth - scrollViewer.ViewportWidth);
        var desiredOffset = Math.Clamp(
            scrollViewer.HorizontalOffset + _compositionTimelineDragAutoScrollDelta,
            0,
            maximumOffset);
        if (Math.Abs(desiredOffset - scrollViewer.HorizontalOffset) < 0.1)
        {
            _compositionTimelineDragAutoScrollTimer.Stop();
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(desiredOffset);
        scrollViewer.UpdateLayout();
        var viewportX = Math.Clamp(
            _compositionTimelineDragViewportX,
            0,
            Math.Max(1, scrollViewer.ViewportWidth));
        RenderCompositionTimelineDropFeedback(
            asset,
            scrollViewer.HorizontalOffset + viewportX,
            viewportX);
    }

    private void StopCompositionTimelineDragAutoScroll(bool clearAsset = true)
    {
        _compositionTimelineDragAutoScrollTimer.Stop();
        _compositionTimelineDragAutoScrollDelta = 0;
        if (clearAsset) _compositionTimelineDragAsset = null;
    }

    private ProjectAsset? ResolveCompositionDragAsset(IDataObject data)
    {
        if (!data.GetDataPresent(ProjectMediaDragFormat) || _workspace.Project is null) return null;
        var value = data.GetData(ProjectMediaDragFormat)?.ToString();
        if (!Guid.TryParse(value, out var assetId)) return null;
        var asset = _workspace.Project.Assets.SingleOrDefault(candidate => candidate.Id == assetId);
        return asset is not null &&
               asset.Id != _workspace.Project.WorkingCompositionAssetId &&
               CanDragIntoComposition(asset)
            ? asset
            : null;
    }

    private int GetCompositionVideoInsertionIndex(double x)
        => _compositionTimelineLayout?.GetVideoInsertionIndex(x) ?? _compositionSegments.Count;

    private void HideCompositionTimelineDropFeedback()
    {
        StopCompositionTimelineDragAutoScroll();
        if (CompositionTimelineDropHint is not null)
            CompositionTimelineDropHint.Visibility = Visibility.Hidden;
    }

    private async void RenameAsset_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { } asset) return;
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
        {
            MessageBox.Show(this, "Virtual assets do not have a stored media filename.", "Change filename", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AssetNameDialog(asset.FileName) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await RunUiActionAsync(
            $"Renaming {asset.FileName}…",
            async () =>
            {
                await PhysicalAssetFileRenameService.RenameAsync(_workspace, asset, dialog.FileName);
                RefreshProjectCollections(asset.Id);
                InspectorText.Text = FormatAssetInspector(asset);
                StatusText.Text = $"Renamed stored media file to {asset.FileName}.";
            });
    }

    private async void ExportSelectedMedia_Click(object sender, RoutedEventArgs e)
    {
        if (AssetsList.SelectedItem is not ProjectMediaListItem item || _workspace.Project is null) return;
        var service = CreateRenderedAssetPromotionService();
        var exportsDirectory = Path.Combine(_workspace.Location!.RootDirectory, "exports");
        if (item.Anchor is { } anchor && item.AnchorRevision is { } anchorRevision)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Saved Frame",
                Filter = "PNG image|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{MakeSafeFileName(item.DisplayName)}.png",
                InitialDirectory = exportsDirectory
            };
            if (dialog.ShowDialog(this) != true) return;
            await RunUiActionAsync("Exporting Saved Frame…", async () =>
            {
                var path = await service.ExportFrameAsync(anchor.Id, anchorRevision.Id, dialog.FileName);
                StatusText.Text = $"Exported Saved Frame to {path}.";
            });
            return;
        }

        if (item.Asset is not { StorageKind: AssetStorageKind.Virtual, MediaType: MediaType.Video } asset ||
            asset.Virtual?.CurrentRecipeRevisionId is not { } recipeRevisionId)
        {
            MessageBox.Show(this, "Choose a Saved Frame, Saved Clip, or Working Composition to export rendered media.",
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var videoDialog = new SaveFileDialog
        {
            Title = $"Export {asset.EffectiveDisplayName}",
            Filter = "MP4 video|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{MakeSafeFileName(asset.EffectiveDisplayName)}.mp4",
            InitialDirectory = exportsDirectory
        };
        if (videoDialog.ShowDialog(this) != true) return;
        await RunUiActionAsync($"Exporting {asset.EffectiveDisplayName}…", async () =>
        {
            var path = await service.ExportAsync(asset.Id, recipeRevisionId, videoDialog.FileName);
            StatusText.Text = $"Exported {asset.EffectiveDisplayName} to {path}.";
        });
    }

    private async void ExtractAudio_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { MediaType: MediaType.Video } source) return;
        var recipeRevisionId = source.Virtual?.Kind == VirtualAssetKind.SavedClip
            ? source.Virtual.CurrentRecipeRevisionId
            : null;
        if (source.StorageKind == AssetStorageKind.Virtual && recipeRevisionId is null)
        {
            MessageBox.Show(this, "This Saved Clip does not have a committed recipe revision.",
                "Extract audio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stem = MakeSafeFileName(Path.GetFileNameWithoutExtension(source.EffectiveDisplayName));
        var dialog = new AssetNameDialog(
            $"{stem} audio.m4a",
            title: "Extract audio",
            heading: "EXTRACT AUDIO",
            description: "Create a permanent .m4a audio file in this project's media folder. The source video remains unchanged.",
            confirmLabel: "Extract")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        await RunUiActionAsync($"Extracting audio from {source.EffectiveDisplayName}…", async () =>
        {
            var service = new AudioExtractionService(
                _workspace,
                _mediaMaterializer,
                _audioExtractionEngine,
                new Sha256ContentHashService(),
                _mediaInspector);
            var extracted = await service.ExtractAsAssetAsync(
                source.Id,
                recipeRevisionId,
                dialog.FileName);
            RefreshProjectCollections(extracted.Id);
            StatusText.Text = $"Extracted audio as {extracted.FileName}.";
        });
    }

    private RenderedAssetPromotionService CreateRenderedAssetPromotionService() => new(
        _workspace,
        _mediaMaterializer,
        new Sha256ContentHashService(),
        _mediaInspector);

    private async Task ShowSavedFramePreviewAsync(ProjectMediaListItem item, Guid? selectedProjectId)
    {
        if (_workspace.Project is null || _workspace.Location is null ||
            item.Anchor is not { } anchor || item.AnchorRevision is not { } revision) return;

        await RunUiActionAsync($"Loading {item.DisplayName}…", async () =>
        {
            try
            {
                await using var media = await _mediaMaterializer.MaterializeAsync(
                        _workspace.Project,
                        _workspace.Location,
                        new MaterializationRequest(
                            new AnchorMaterializationTarget(anchor.Id, revision.Id),
                            MaterializationPurpose.Preview),
                        CancellationToken.None);
                if (_workspace.Project?.Id != selectedProjectId || AssetsList.SelectedItem != item) return;
                var thumbnail = LoadBitmap(media.Path);
                item.Thumbnail = thumbnail;
                foreach (var choice in _referenceChoices.Where(choice =>
                             choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                             choice.LogicalObjectId == anchor.Id))
                    choice.UpdateThumbnail(thumbnail);
                AssetsList.Items.Refresh();
                ReferenceAssetsGrid.Items.Refresh();
                ClearMediaPreview();
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                ImagePreview.Source = thumbnail;
                ImagePreview.Visibility = Visibility.Visible;
                InspectorText.Text = FormatSavedFrameInspector(
                    new SavedFrameListItem(anchor, revision, thumbnail, error: null));
                StatusText.Text = $"Selected Saved Frame {item.DisplayName}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_workspace.Project?.Id != selectedProjectId) return;
                ClearMediaPreview();
                PreviewPlaceholder.Text = $"Saved Frame preview unavailable\n\n{exception.Message}";
                PreviewPlaceholder.Visibility = Visibility.Visible;
                InspectorText.Text = FormatSavedFrameInspector(
                    new SavedFrameListItem(anchor, revision, thumbnail: null, exception.Message));
                StatusText.Text = $"Could not display {item.DisplayName}.";
            }
        });
    }

    private void ReferenceAssetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceAssetsGrid.SelectedItem is not GenerationReferenceChoice choice) return;
        var mediaItem = _assets.FirstOrDefault(item => choice.ObjectKind switch
        {
            GenerationReferenceObjectKind.Asset => item.Asset?.Id == choice.LogicalObjectId,
            GenerationReferenceObjectKind.FrameAnchor => item.Anchor?.Id == choice.LogicalObjectId,
            _ => false
        });
        if (mediaItem is not null) AssetsList.SelectedItem = mediaItem;
    }

    private async void DeleteAsset_Click(object sender, RoutedEventArgs e)
    {
        if (AssetsList.SelectedItem is not ProjectMediaListItem selected || _workspace.Project is null) return;
        if (selected.Anchor is { } anchor)
        {
            await ConfirmAndRemoveSavedFrameAsync(anchor, selected.DisplayName);
            return;
        }
        if (selected.Asset is not { } asset) return;
        if (asset.Virtual?.Kind == VirtualAssetKind.SavedClip)
        {
            var deleteClip = MessageBox.Show(
                this,
                $"Delete Saved Clip '{asset.EffectiveDisplayName}' from this project?\n\n" +
                "Its non-destructive recipe and private boundaries will be removed. The source video is unchanged.",
                "Delete Saved Clip",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (deleteClip != MessageBoxResult.Yes) return;
            await RunUiActionAsync($"Deleting {asset.EffectiveDisplayName}…", async () =>
            {
                await new SavedClipService(_workspace).DeleteAsync(asset.Id);
                AssetsList.SelectedItem = null;
                ClearMediaPreview();
                RefreshProjectCollections();
                InspectorText.Text = "Select project media or a generation to inspect its details and history.";
                StatusText.Text = $"Deleted Saved Clip '{asset.EffectiveDisplayName}'. The source video was unchanged.";
            });
            return;
        }
        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            MessageBox.Show(
                this,
                "This virtual project item cannot be deleted from Project Media yet.",
                "Delete project media",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        var usage = GetAssetUsage(_workspace.Project, asset);
        if (usage.Count > 0)
        {
            MessageBox.Show(
                this,
                $"'{asset.EffectiveDisplayName}' cannot be deleted because it is still used by:\n\n• {string.Join("\n• ", usage)}",
                "Asset is in use",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Delete '{asset.EffectiveDisplayName}' from this project and remove its stored media file?\n\nThis cannot be undone.",
            "Delete asset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes) return;

        await RemoveCurrentProjectAssetAsync(asset);
        StatusText.Text = $"Deleted {asset.EffectiveDisplayName}.";
    }

    private async void MoveAssetToProject_Click(object sender, RoutedEventArgs e)
    {
        if (AssetsList.SelectedItem is not ProjectMediaListItem selected ||
            _workspace.Project is null || _workspace.Location is null) return;
        if (selected.Anchor is not null)
        {
            MessageBox.Show(
                this,
                "Saved Frames cannot be moved between projects yet. A Saved Frame is an exact position tied to its source video and may also be referenced by generation or editing history.",
                "Move Saved Frame",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (selected.Asset is not { } asset) return;
        var usage = GetAssetUsage(_workspace.Project, asset);
        if (asset.StorageKind != AssetStorageKind.Physical)
        {
            MessageBox.Show(this, "Virtual assets cannot be moved between projects yet.", "Move asset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetProjectFile = ChooseTransferTargetProject();
        if (targetProjectFile is null) return;

        await RunUiActionAsync(
            $"Moving {asset.EffectiveDisplayName}…",
            async () =>
            {
                var result = await _assetTransferService.CopyToProjectAsync(_workspace, asset, targetProjectFile);
                if (usage.Count == 0)
                {
                    await RemoveCurrentProjectAssetAsync(asset);
                    StatusText.Text = $"Moved {asset.FileName} to {result.TargetProjectName}.";
                    return;
                }

                StatusText.Text = $"Copied {asset.FileName} to {result.TargetProjectName}; the source remains because project history references it.";
                MessageBox.Show(
                    this,
                    $"'{asset.FileName}' is now available in '{result.TargetProjectName}'.\n\n" +
                    "ReelForge retained the source copy because removing it would break:\n\n" +
                    $"• {string.Join("\n• ", usage)}",
                    "Asset transferred; source retained",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
    }

    private async void CopyAssetToProject_Click(object sender, RoutedEventArgs e)
    {
        if (AssetsList.SelectedItem is not ProjectMediaListItem selected ||
            _workspace.Project is null || _workspace.Location is null) return;
        if (selected.Anchor is not null)
        {
            MessageBox.Show(
                this,
                "Saved Frames cannot be copied between projects yet. A Saved Frame is an exact position tied to its source video in the current project.",
                "Copy Saved Frame",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (selected.Asset is not { } asset) return;
        if (asset.StorageKind != AssetStorageKind.Physical)
        {
            MessageBox.Show(this, "Virtual assets cannot be copied between projects until recipe materialization is available.", "Copy asset", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetProjectFile = ChooseTransferTargetProject();
        if (targetProjectFile is null) return;
        await RunUiActionAsync(
            $"Copying {asset.FileName}…",
            async () =>
            {
                var result = await _assetTransferService.CopyToProjectAsync(_workspace, asset, targetProjectFile);
                StatusText.Text = $"Copied {asset.FileName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.";
            });
    }

    private string? ChooseTransferTargetProject()
    {
        var dialog = CreateOpenProjectDialog();
        if (dialog.ShowDialog() != true) return null;
        if (_workspace.Location is not null &&
            Path.GetFullPath(dialog.ProjectFilePath).Equals(
                Path.GetFullPath(_workspace.Location.ProjectFilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Choose a different destination project.", "Transfer asset", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        return dialog.ProjectFilePath;
    }

    private ProjectAsset? GetSelectedAsset() => (AssetsList.SelectedItem as ProjectMediaListItem)?.Asset;

    private async Task RemoveCurrentProjectAssetAsync(ProjectAsset asset)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
        var absolutePath = asset.StorageKind == AssetStorageKind.Physical
            ? _workspace.GetAbsoluteAssetPath(asset)
            : null;
        _workspace.Project.Assets.Remove(asset);
        try
        {
            await _workspace.SaveAsync();
        }
        catch
        {
            _workspace.Project.Assets.Add(asset);
            throw;
        }

        if (absolutePath is not null && File.Exists(absolutePath)) File.Delete(absolutePath);
        AssetsList.SelectedItem = null;
        InspectorText.Text = "Select an asset or generation to inspect its details and history.";
        ClearMediaPreview();
        RefreshProjectCollections();
    }

    private static IReadOnlyList<string> GetAssetUsage(VideoProject project, ProjectAsset asset)
    {
        var usage = new List<string>();
        if (project.CurrentGenerationDraft?.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.Asset && reference.LogicalObjectId == asset.Id) == true)
            usage.Add("the current generation draft");
        if (project.Generations.Any(generation => generation.RequestSnapshot.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.Asset && reference.LogicalObjectId == asset.Id)))
            usage.Add("submitted generation references");
        if (project.Generations.Any(generation => generation.OutputAssetIds.Contains(asset.Id)))
            usage.Add("generated-output history");
        if (project.AnchorRevisions.Any(revision => revision.SourceAssetId == asset.Id)) usage.Add("saved frames");
        if (project.Assets.Any(candidate => candidate.Id != asset.Id && candidate.Provenance?.SourceAssetIds.Contains(asset.Id) == true))
            usage.Add("derived-asset history");
        if (project.RecipeRevisions.Any(revision =>
                revision.VirtualAssetId != asset.Id && RecipeReferencesAsset(revision.Recipe, asset.Id)) ||
            project.RecipeDrafts.Any(draft =>
                draft.VirtualAssetId != asset.Id && RecipeReferencesAsset(draft.EditableRecipe, asset.Id)))
            usage.Add("media recipes");
        return usage.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool RecipeReferencesAsset(AssetRecipe recipe, Guid assetId) => recipe switch
    {
        TrimRecipe trim => trim.Source.AssetId == assetId,
        ExtractFrameRecipe frame => frame.Source.AssetId == assetId,
        CompositionRecipe composition => composition.Segments.Any(segment => segment.Source.AssetId == assetId) ||
                                         composition.AudioClips.Any(clip => clip.Source.AssetId == assetId),
        _ => false
    };

    private async Task QueueGenerationWithUndoSendAsync(
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization,
        int undoSendSeconds)
    {
        var provider = _generationProvider;
        var workflow = _generationWorkflow;
        var providerPreparation = _providerPreparation;
        var projectLocation = _workspace.Location;
        var projectName = _workspace.Project?.Name;
        if (projectLocation is null || projectName is null) return;

        SetProjectActionsEnabled(false);
        try
        {
            var generation = await workflow.QueueAsync(provider, draft, authorization);
            var delaySeconds = Math.Clamp(undoSendSeconds, 1, 30);
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
            await _jobCoordinator.TrackPendingAsync(
                generation,
                projectLocation,
                projectName,
                provider.Capabilities.DisplayName,
                expiresAt);

            var delayCancellation = new CancellationTokenSource();
            _pendingSubmissionDelays[generation.Id] = delayCancellation;
            RefreshProjectCollections();
            GenerationsList.SelectedItem = _generations.FirstOrDefault(item => item.Id == generation.Id);
            GenerationStatusText.Text = $"Generation queued locally for {delaySeconds} seconds. Use Cancel Job in Jobs to undo.";
            StatusText.Text = "Generation has not been sent to the provider yet.";
            _ = SubmitAfterUndoSendDelayAsync(
                generation.Id,
                workflow,
                providerPreparation,
                provider,
                projectLocation,
                projectName,
                authorization,
                expiresAt,
                delayCancellation);
        }
        catch (GenerationValidationException exception)
        {
            GenerationStatusText.Text = exception.Message;
        }
        catch (Exception exception)
        {
            ShowError("Generation could not be queued", exception);
        }
        finally
        {
            SetProjectActionsEnabled(true);
        }
    }

    private async Task SubmitAfterUndoSendDelayAsync(
        Guid generationId,
        GenerationWorkflow activeWorkflow,
        IProviderAssetPreparationService? providerPreparation,
        IVideoGenerationProvider provider,
        ProjectLocation projectLocation,
        string projectName,
        GenerationSubmissionAuthorization? authorization,
        DateTimeOffset expiresAt,
        CancellationTokenSource delayCancellation)
    {
        try
        {
            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, delayCancellation.Token);
            if (!await _jobCoordinator.TryBeginSubmissionAsync(generationId)) return;

            RemovePendingSubmissionDelay(generationId, delayCancellation);
            await _submissionGate.WaitAsync();
            try
            {
                var usesActiveWorkspace = IsProjectOpen(projectLocation.ProjectFilePath);
                var submissionWorkspace = _workspace;
                var submissionWorkflow = activeWorkflow;
                if (!usesActiveWorkspace)
                {
                    submissionWorkspace = _runtime.CreateProjectWorkspace();
                    await submissionWorkspace.OpenAsync(projectLocation.ProjectFilePath);
                    submissionWorkflow = _runtime.CreateGenerationWorkflow(submissionWorkspace, providerPreparation);
                }
                var generation = submissionWorkspace.Project?.Generations.SingleOrDefault(item => item.Id == generationId)
                    ?? throw new InvalidOperationException("The locally queued generation no longer exists in its owning project.");
                await RunQueuedGenerationWorkflowAsync(
                    submissionWorkflow,
                    provider,
                    generation,
                    projectLocation,
                    projectName,
                    authorization,
                    usesActiveWorkspace);
            }
            finally
            {
                _submissionGate.Release();
            }
        }
        catch (OperationCanceledException) when (delayCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowError("Queued generation failed", exception);
        }
        finally
        {
            RemovePendingSubmissionDelay(generationId, delayCancellation);
        }
    }

    private async Task RunQueuedGenerationWorkflowAsync(
        GenerationWorkflow workflow,
        IVideoGenerationProvider provider,
        GenerationRecord generation,
        ProjectLocation projectLocation,
        string projectName,
        GenerationSubmissionAuthorization? authorization,
        bool usesActiveWorkspace)
    {
        if (usesActiveWorkspace)
        {
            GenerateButton.IsEnabled = false;
            SetProjectActionsEnabled(false);
        }
        IProgress<GenerationWorkflowProgress>? progress = usesActiveWorkspace
            ? new Progress<GenerationWorkflowProgress>(update => GenerationStatusText.Text = update.Message)
            : null;
        try
        {
            generation = await workflow.SubmitQueuedAsync(provider, generation, authorization, progress);
            var sourceIsActiveNow = IsProjectOpen(projectLocation.ProjectFilePath);
            if (sourceIsActiveNow)
            {
                MergeGenerationStateIntoActiveProject(generation);
                RefreshProjectCollections();
                GenerationsList.SelectedItem = _generations.FirstOrDefault(item => item.Id == generation.Id);
                TryAutoPreviewGeneratedOutput(generation, owningProjectIsOpen: true);
            }

            if (provider is IAsyncVideoGenerationProvider && !string.IsNullOrWhiteSpace(generation.ProviderJobId))
            {
                await _jobCoordinator.TrackAsync(
                    generation,
                    projectLocation,
                    projectName,
                    provider.Capabilities.DisplayName);
                if (sourceIsActiveNow)
                    GenerationStatusText.Text = "Generation submitted. Follow its progress in the Jobs tab.";
                StatusText.Text = $"Generation accepted by {provider.Capabilities.DisplayName}.";
            }
            else if (generation.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
            {
                await _jobCoordinator.CompleteUnacceptedSubmissionAsync(generation);
                if (sourceIsActiveNow) GenerationStatusText.Text = FormatGenerationOutcome(generation);
                StatusText.Text = $"Generation state: {generation.Status}; no provider job is being monitored.";
            }
            else
            {
                if (sourceIsActiveNow) GenerationStatusText.Text = FormatGenerationOutcome(generation);
                StatusText.Text = $"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.";
            }
        }
        finally
        {
            if (usesActiveWorkspace)
            {
                GenerateButton.IsEnabled = true;
                SetProjectActionsEnabled(true);
            }
        }
    }

    private async void CancelPendingJob_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid generationId }) return;
        try
        {
            if (!await _jobCoordinator.CancelPendingAsync(generationId)) return;
            if (_pendingSubmissionDelays.TryGetValue(generationId, out var delay)) delay.Cancel();
            RemovePendingSubmissionDelay(generationId, delay);
            GenerationStatusText.Text = "Queued generation cancelled.";
            StatusText.Text = "Provider status: Cancelled";
        }
        catch (Exception exception)
        {
            ShowError("Queued generation could not be cancelled", exception);
        }
    }

    private void RemovePendingSubmissionDelay(Guid generationId, CancellationTokenSource? expected)
    {
        if (!_pendingSubmissionDelays.TryGetValue(generationId, out var current) ||
            (expected is not null && !ReferenceEquals(current, expected))) return;
        _pendingSubmissionDelays.Remove(generationId);
        current.Dispose();
    }

    private bool IsProjectOpen(string projectFilePath) =>
        _workspace.Location is not null &&
        Path.GetFullPath(_workspace.Location.ProjectFilePath).Equals(
            Path.GetFullPath(projectFilePath),
            StringComparison.OrdinalIgnoreCase);

    private void MergeGenerationStateIntoActiveProject(GenerationRecord source)
    {
        var target = _workspace.Project?.Generations.SingleOrDefault(candidate => candidate.Id == source.Id);
        if (target is null || ReferenceEquals(target, source)) return;
        target.ProviderJobId = source.ProviderJobId;
        target.Status = source.Status;
        target.IngestionStatus = source.IngestionStatus;
        target.CompletedAt = source.CompletedAt;
        target.OutputAssetIds = source.OutputAssetIds.ToList();
        target.ResponseMetadata = new Dictionary<string, string>(source.ResponseMetadata, StringComparer.Ordinal);
        target.Error = source.Error;
    }

    private async Task RunGenerationWorkflowAsync(
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization)
    {
        GenerateButton.IsEnabled = false;
        SetProjectActionsEnabled(false);
        var provider = _generationProvider;
        var projectLocation = _workspace.Location;
        var projectName = _workspace.Project?.Name;
        var progress = new Progress<GenerationWorkflowProgress>(update => GenerationStatusText.Text = update.Message);

        try
        {
            var generation = await _generationWorkflow.SubmitAsync(
                provider,
                draft,
                authorization,
                progress);
            RefreshProjectCollections();
            GenerationsList.SelectedItem = _generations.FirstOrDefault(item => item.Id == generation.Id);
            TryAutoPreviewGeneratedOutput(generation, owningProjectIsOpen: true);

            if (provider is IAsyncVideoGenerationProvider &&
                !string.IsNullOrWhiteSpace(generation.ProviderJobId) &&
                projectLocation is not null &&
                projectName is not null)
            {
                await _jobCoordinator.TrackAsync(
                    generation,
                    projectLocation,
                    projectName,
                    provider.Capabilities.DisplayName);
                GenerationStatusText.Text = "Generation submitted. Follow its progress in the Jobs tab.";
                StatusText.Text = $"Generation accepted by {provider.Capabilities.DisplayName}.";
            }
            else
            {
                GenerationStatusText.Text = FormatGenerationOutcome(generation);
                StatusText.Text = $"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.";
            }
        }
        catch (GenerationValidationException exception)
        {
            GenerationStatusText.Text = exception.Message;
        }
        catch (Exception exception)
        {
            ShowError("Generation workflow failed", exception);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
            SetProjectActionsEnabled(true);
        }
    }

    private void SetProjectActionsEnabled(bool isEnabled)
    {
        NewProjectButton.IsEnabled = isEnabled;
        OpenProjectButton.IsEnabled = isEnabled;
        ImportAssetsButton.IsEnabled = isEnabled;
        SettingsButton.IsEnabled = isEnabled;
        ProviderComboBox.IsEnabled = isEnabled;
    }

    private void TryAutoPreviewGeneratedOutput(GenerationRecord generation, bool owningProjectIsOpen)
    {
        if (generation.Status != GenerationStatus.Succeeded ||
            generation.IngestionStatus != OutputIngestionStatus.Succeeded ||
            !GeneratedOutputPreviewPolicy.ShouldAutoPreview(
                owningProjectIsOpen,
                _activeWorkspace,
                _mediaPreparationMode != MediaPreparationMode.None))
            return;
        var outputId = generation.OutputAssetIds.LastOrDefault();
        if (outputId == Guid.Empty) return;
        AssetsList.SelectedItem = _assets.FirstOrDefault(item => item.Asset?.Id == outputId);
    }

    private async void PrepareDerivedDraft_Click(object sender, RoutedEventArgs e)
    {
        if (GenerationsList.SelectedItem is not GenerationRecord source)
        {
            GenerationStatusText.Text = "Select a generation in history before creating a derived draft.";
            return;
        }
        if (sender is not Button { Tag: string relationshipName } ||
            !Enum.TryParse<GenerationRelationshipType>(relationshipName, out var relationship))
            return;

        if (relationship is GenerationRelationshipType.ContinueAfter or GenerationRelationshipType.ContinueBefore)
        {
            await PrepareGenerationBoundaryContinuationAsync(source, relationship);
            return;
        }

        var draft = GenerationWorkflow.CreateDerivedDraft(source, relationship);
        LoadDraftIntoUi(draft);
        await _generationWorkflow.SaveDraftAsync(draft);
        GenerationStatusText.Text =
            $"Drafted {relationship} from generation {source.Id}. Review it, then use the submission button.";
    }

    private async Task PrepareGenerationBoundaryContinuationAsync(
        GenerationRecord sourceGeneration,
        GenerationRelationshipType relationship)
    {
        if (_workspace.Project is null) return;
        var outputs = sourceGeneration.OutputAssetIds
            .Select(id => _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == id))
            .OfType<ProjectAsset>()
            .Where(asset => asset.MediaType == MediaType.Video && asset.StorageKind == AssetStorageKind.Physical)
            .ToArray();
        if (outputs.Length == 0)
        {
            GenerationStatusText.Text = "This generation has no durable video output to continue from.";
            return;
        }

        ProjectAsset sourceAsset;
        if (outputs.Length == 1)
        {
            sourceAsset = outputs[0];
        }
        else
        {
            var selection = new GenerationOutputSelectionDialog(outputs) { Owner = this };
            if (selection.ShowDialog() != true || selection.SelectedOutput is null) return;
            sourceAsset = selection.SelectedOutput;
        }

        await RunUiActionAsync("Finding the exact continuation boundary…", async () =>
        {
            var (frames, contentHash) = await IndexSourceFramesAsync(sourceAsset, CancellationToken.None);
            var frame = relationship == GenerationRelationshipType.ContinueAfter ? frames[^1] : frames[0];
            await CreateContinuationDraftAsync(sourceAsset, frame, contentHash, relationship, sourceGeneration);
        });
    }

    private async Task<(IReadOnlyList<VideoPresentationFrame> Frames, string ContentHash)> IndexSourceFramesAsync(
        ProjectAsset sourceAsset,
        CancellationToken cancellationToken)
    {
        if (_workspace.Project is null || _workspace.Location is null)
            throw new InvalidOperationException("Open a project first.");
        await using var source = await new PhysicalAssetMaterializer().MaterializeAsync(
            _workspace.Project,
            _workspace.Location,
            new MaterializationRequest(new AssetMaterializationTarget(sourceAsset.Id), MaterializationPurpose.Preview),
            cancellationToken);
        var contentHash = source.ContentIdentity.Sha256
            ?? throw new InvalidDataException("The continuation source has no verified content identity.");
        var frames = await _exactFrameService.IndexAsync(source.Path, cancellationToken);
        await _workspace.SaveAsync(cancellationToken);
        return (frames, contentHash);
    }

    private async Task CreateContinuationDraftAsync(
        ProjectAsset sourceAsset,
        VideoPresentationFrame frame,
        string sourceContentHash,
        GenerationRelationshipType relationship,
        GenerationRecord? parentGeneration)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
        var sourcePath = _workspace.GetAbsoluteAssetPath(sourceAsset);
        var transientRevision = CreateTransientFrameRevision(sourceAsset.Id, sourceContentHash, frame);
        await using var preview = await _exactFrameService.ExtractAsync(
            sourcePath,
            sourceContentHash,
            transientRevision,
            MaterializationPurpose.Preview,
            "continuation-confirmation");
        var heading = relationship == GenerationRelationshipType.ContinueAfter
            ? "Continue after this exact frame?"
            : "Continue before this exact frame?";
        var confirmation = new FrameConfirmationDialog(
            LoadBitmap(preview.Path),
            heading,
            sourceAsset.EffectiveDisplayName,
            frame.TimestampSeconds,
            frame.PresentationTimestamp,
            frame.TimeBaseNumerator,
            frame.TimeBaseDenominator)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true) return;

        var anchor = new FrameAnchor
        {
            DisplayLabel = relationship == GenerationRelationshipType.ContinueAfter
                ? $"Final frame of {sourceAsset.EffectiveDisplayName}"
                : $"First frame of {sourceAsset.EffectiveDisplayName}"
        };
        _workspace.Project.Anchors.Add(anchor);
        var revision = _workspace.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id,
            sourceContentHash,
            frame.VideoStreamIndex,
            frame.PresentationTimestamp,
            frame.TimeBaseNumerator,
            frame.TimeBaseDenominator,
            frame.FrameNumber));

        var draft = parentGeneration is null
            ? CaptureDraftFromUi()
            : GenerationWorkflow.CreateDerivedDraft(parentGeneration, relationship);
        if (parentGeneration is null)
        {
            draft.ParentGenerationId = null;
            draft.RelationshipType = null;
        }
        draft.References =
        [
            new GenerationReferenceDraft
            {
                ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                LogicalObjectId = anchor.Id,
                AnchorRevisionId = revision.Id,
                Role = relationship == GenerationRelationshipType.ContinueAfter
                    ? GenerationReferenceRole.StartFrame
                    : GenerationReferenceRole.EndFrame,
                Order = 0,
                Label = anchor.DisplayLabel
            }
        ];
        RecommendContinuationMode(draft, relationship);
        await _workspace.SaveAsync();
        RefreshProjectCollections();
        LoadDraftIntoUi(draft);
        await _generationWorkflow.SaveDraftAsync(draft);
        if (_frameSourceAssetId == sourceAsset.Id) await RefreshSavedFramesAsync(CancellationToken.None);
        RightPanelTabs.SelectedIndex = 1;
        GenerationStatusText.Text = parentGeneration is null
            ? "Continuation draft created from imported media. No generation parent was invented."
            : $"Drafted {relationship} from generation {parentGeneration.Id}. Review the exact Saved Frame reference before submitting.";
    }

    private void RecommendContinuationMode(GenerationDraft draft, GenerationRelationshipType relationship)
    {
        var provider = _providerChoices.FirstOrDefault(choice =>
                           choice.Provider.Capabilities.ProviderId.Equals(draft.ProviderId, StringComparison.Ordinal))
                       ?.Provider ?? _generationProvider;
        if (relationship == GenerationRelationshipType.ContinueBefore &&
            provider.Capabilities.Modes.Contains(GenerationMode.ReferenceToVideo))
        {
            draft.Mode = GenerationMode.ReferenceToVideo;
            return;
        }
        if (provider.Capabilities.Modes.Contains(GenerationMode.ImageToVideo))
        {
            draft.Mode = GenerationMode.ImageToVideo;
            if (provider.Capabilities.AspectRatios.Contains("adaptive", StringComparer.OrdinalIgnoreCase))
                draft.AspectRatio = provider.Capabilities.AspectRatios.First(ratio =>
                    ratio.Equals("adaptive", StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (provider.Capabilities.Modes.Contains(GenerationMode.ReferenceToVideo))
        {
            draft.Mode = GenerationMode.ReferenceToVideo;
            return;
        }
        throw new InvalidOperationException($"{provider.Capabilities.DisplayName} does not support a continuation-compatible image reference mode.");
    }

    private async void ClearLineage_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen()) return;
        var draft = CaptureDraftFromUi();
        draft.ParentGenerationId = null;
        draft.RelationshipType = null;
        LoadDraftIntoUi(draft);
        await _generationWorkflow.SaveDraftAsync(draft);
        GenerationStatusText.Text = "Started a new root generation draft.";
    }

    private GenerationDraft CaptureDraftFromUi()
    {
        var mode = (GenerationMode)(ModeComboBox.SelectedItem ?? GenerationMode.TextToVideo);
        List<GenerationReferenceDraft> selectedReferences = mode == GenerationMode.TextToVideo
            ? []
            : _referenceChoices
                .Where(choice => choice.IsSelected)
                .OrderBy(choice => choice.Order)
                .Select(choice => new GenerationReferenceDraft
                {
                    ReferenceId = choice.ReferenceId,
                    ObjectKind = choice.ObjectKind,
                    LogicalObjectId = choice.LogicalObjectId,
                    AnchorRevisionId = choice.AnchorRevisionId,
                    Role = choice.Role,
                    Order = choice.Order,
                    Label = NullIfWhiteSpace(choice.Label),
                    Notes = NullIfWhiteSpace(choice.Notes)
                })
                .ToList();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("generate_audio"))
        {
            parameters["generate_audio"] = (GenerateAudioCheckBox.IsChecked == true).ToString().ToLowerInvariant();
        }
        else if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("generateAudio"))
        {
            parameters["generateAudio"] = (GenerateAudioCheckBox.IsChecked == true).ToString().ToLowerInvariant();
        }
        if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("watermark"))
            parameters["watermark"] = (WatermarkCheckBox.IsChecked == true).ToString().ToLowerInvariant();
        if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("output_format"))
            parameters["output_format"] = GetSelectedOutputFormat();

        var current = _workspace.Project?.CurrentGenerationDraft;
        return new GenerationDraft
        {
            ProviderId = _generationProvider.Capabilities.ProviderId,
            ModelVersion = _generationProvider.Capabilities.ModelVersion,
            Prompt = PromptTextBox.Text,
            Mode = mode,
            DurationSeconds = (int)DurationSlider.Value,
            AspectRatio = (string)(AspectRatioComboBox.SelectedItem ?? "16:9"),
            Resolution = (string)(ResolutionComboBox.SelectedItem ?? "720p"),
            References = selectedReferences,
            ProviderParameters = parameters,
            ParentGenerationId = current?.ParentGenerationId,
            RelationshipType = current?.RelationshipType,
            ModifiedAt = DateTimeOffset.UtcNow
        };
    }

    private void LoadDraftIntoUi(GenerationDraft draft)
    {
        _suppressDraftAutosave = true;
        try
        {
            var providerChoice = _providerChoices.FirstOrDefault(choice =>
                choice.Provider.Capabilities.ProviderId == draft.ProviderId);
            if (providerChoice is not null) ProviderComboBox.SelectedItem = providerChoice;
            PromptTextBox.Text = draft.Prompt;
            ModeComboBox.SelectedItem = draft.Mode;
            DurationSlider.Value = Math.Clamp(
                draft.DurationSeconds,
                _generationProvider.Capabilities.MinimumDurationSeconds,
                _generationProvider.Capabilities.MaximumDurationSeconds);
            if (_generationProvider.Capabilities.AspectRatios.Contains(draft.AspectRatio))
                AspectRatioComboBox.SelectedItem = draft.AspectRatio;
            if (_generationProvider.Capabilities.Resolutions.Contains(draft.Resolution))
                ResolutionComboBox.SelectedItem = draft.Resolution;
            GenerateAudioCheckBox.IsChecked = ReadDraftBoolean(draft, "generate_audio", "generateAudio", true);
            WatermarkCheckBox.IsChecked = ReadDraftBoolean(draft, "watermark", null, false);
            SelectOutputFormat(draft.ProviderParameters.GetValueOrDefault("output_format", "mp4"));

            foreach (var group in draft.References.GroupBy(reference =>
                         (reference.ObjectKind, reference.LogicalObjectId)))
            {
                var matching = _referenceChoices.Where(choice =>
                    choice.ObjectKind == group.Key.ObjectKind &&
                    choice.LogicalObjectId == group.Key.LogicalObjectId).ToList();
                if (matching.Count == 0) continue;
                while (matching.Count < group.Count())
                {
                    var duplicate = matching[0].Duplicate(_referenceChoices.Count);
                    duplicate.IsSelected = false;
                    _referenceChoices.Add(duplicate);
                    matching.Add(duplicate);
                }
            }

            foreach (var choice in _referenceChoices)
            {
                choice.IsSelected = false;
                choice.Role = null;
                choice.Label = null;
                choice.Notes = null;
            }
            foreach (var reference in draft.References.OrderBy(item => item.Order))
            {
                var choice = _referenceChoices.FirstOrDefault(item =>
                                 item.ReferenceId == reference.ReferenceId) ??
                             _referenceChoices.FirstOrDefault(item =>
                                 !item.IsSelected && item.ObjectKind == reference.ObjectKind &&
                                 item.LogicalObjectId == reference.LogicalObjectId);
                if (choice is null) continue;
                choice.IsSelected = true;
                choice.ReferenceId = reference.ReferenceId;
                choice.AnchorRevisionId = reference.AnchorRevisionId ?? choice.AnchorRevisionId;
                choice.Role = reference.Role;
                choice.Order = reference.Order ?? choice.Order;
                choice.Label = reference.Label;
                choice.Notes = reference.Notes;
            }
            ReferenceAssetsGrid.Items.Refresh();
            LineageText.Text = draft.ParentGenerationId is { } parent
                ? $"{draft.RelationshipType} • parent {parent}"
                : "New root generation";
        }
        finally
        {
            _suppressDraftAutosave = false;
        }
    }

    private void ShowAssetPreview(ProjectAsset asset)
    {
        ClearMediaPreview();
        PreviewPlaceholder.Visibility = Visibility.Collapsed;

        var absolutePath = _workspace.GetAbsoluteAssetPath(asset);
        if (!File.Exists(absolutePath))
        {
            PreviewPlaceholder.Text = $"Missing media file\n{asset.FileName}\n\nMoving a file in Explorer does not add it to another project's .rfp file.";
            PreviewPlaceholder.TextAlignment = TextAlignment.Center;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        if (asset.MediaType == MediaType.Image)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(absolutePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            ImagePreview.Source = bitmap;
            ImagePreview.Visibility = Visibility.Visible;
            return;
        }

        OpenVideoPreview(absolutePath, requiresWarmup: true);
    }

    private async Task OpenCompositionDraftPreviewAsync(
        ProjectAsset composition,
        ProjectMediaListItem selectedItem,
        Guid? selectedProjectId)
    {
        if (_workspace.Project is null || _workspace.Location is null ||
            composition.Virtual?.CurrentRecipeRevisionId is not { } revisionId)
            return;

        await RunUiActionAsync("Preparing fast composition audition…", async () =>
        {
            MaterializedMediaLease? auditionAudio = null;
            try
            {
                var revision = _workspace.Project.RecipeRevisions.Single(candidate => candidate.Id == revisionId);
                var recipe = revision.Recipe as CompositionRecipe
                    ?? throw new InvalidDataException("The Working Composition recipe is invalid.");
                var draftSegments = new List<CompositionDraftSegment>();
                var timelineStart = 0d;
                foreach (var segment in recipe.Segments)
                {
                    var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId)
                        ?? throw new InvalidDataException("A composition source is missing.");
                    var duration = CompositionSegmentTiming.ResolveDuration(_workspace.Project, segment, source)
                        ?? throw new InvalidDataException($"The duration of '{source.EffectiveDisplayName}' is unknown.");
                    draftSegments.Add(new CompositionDraftSegment(
                        segment.Id,
                        segment.Source,
                        timelineStart,
                        ResolveCompositionBoundarySeconds(_workspace.Project, segment.Start, source, isEnd: false),
                        duration,
                        segment.AudioEnabled));
                    timelineStart += duration;
                }
                if (draftSegments.Count == 0)
                    throw new InvalidDataException("The Working Composition has no video segments.");

                string? auditionAudioWarning = null;
                try
                {
                    auditionAudio = await _mediaMaterializer.MaterializeCompositionAuditionAudioAsync(
                        _workspace.Project,
                        _workspace.Location,
                        composition.Id,
                        revisionId,
                        timelineStart);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    auditionAudioWarning = exception.Message;
                }

                ClearMediaPreview();
                if (_workspace.Project?.Id != selectedProjectId || AssetsList.SelectedItem != selectedItem) return;
                _activeCompositionDraftRevisionId = revisionId;
                _compositionDraftSegments = draftSegments;
                var requestedPosition = Math.Clamp(
                    _pendingCompositionTimelineSeekSeconds ?? 0,
                    0,
                    timelineStart);
                _pendingCompositionTimelineSeekSeconds = null;
                _compositionDraftPositionSeconds = requestedPosition;
                PositionSlider.Minimum = 0;
                PositionSlider.Maximum = timelineStart;
                if (auditionAudio is not null)
                {
                    _activeCompositionAuditionAudioLease = auditionAudio;
                    auditionAudio = null;
                    OpenCompositionAuditionAudio(_activeCompositionAuditionAudioLease.Path, requestedPosition);
                }
                var requestedSegmentIndex = FindCompositionDraftSegmentIndex(requestedPosition);
                await OpenCompositionDraftSegmentAsync(
                    requestedSegmentIndex,
                    requestedPosition,
                    playAfterOpen: false);
                StatusText.Text = auditionAudioWarning is not null
                    ? $"Fast composition audition ready without independent audio: {auditionAudioWarning}"
                    : _activeCompositionAuditionAudioLease is not null
                        ? "Fast composition audition ready with independent audio clips. " +
                          "Use Preview composition to verify final mix fidelity and render continuity."
                        : "Fast composition audition ready. Source video and source audio play at cuts; " +
                          "use Preview composition to verify the complete audio mix and final render.";
            }
            finally
            {
                if (auditionAudio is not null) await auditionAudio.DisposeAsync();
            }
        });
    }

    private async Task OpenCompositionDraftSegmentAsync(
        int segmentIndex,
        double globalSeconds,
        bool playAfterOpen)
    {
        if (_workspace.Project is null || _workspace.Location is null ||
            _activeCompositionDraftRevisionId is null ||
            segmentIndex < 0 || segmentIndex >= _compositionDraftSegments.Count)
            return;
        var openVersion = ++_compositionDraftOpenVersion;
        var segment = _compositionDraftSegments[segmentIndex];
        PauseCompositionAuditionAudio();
        var lease = await _mediaMaterializer.MaterializeAsync(
            _workspace.Project,
            _workspace.Location,
            new MaterializationRequest(
                new AssetMaterializationTarget(segment.Source.AssetId, segment.Source.RecipeRevisionId),
                MaterializationPurpose.Preview));
        if (openVersion != _compositionDraftOpenVersion || _activeCompositionDraftRevisionId is null)
        {
            await lease.DisposeAsync();
            return;
        }

        VideoPreview.Stop();
        VideoPreview.Close();
        VideoPreview.Source = null;
        ReleaseActivePreviewLease();
        _activePreviewLease = lease;
        _activeCompositionDraftSegmentIndex = segmentIndex;
        _compositionDraftPositionSeconds = Math.Clamp(
            globalSeconds,
            segment.TimelineStartSeconds,
            segment.TimelineStartSeconds + segment.DurationSeconds);
        var offset = _compositionDraftPositionSeconds - segment.TimelineStartSeconds;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        OpenVideoPreview(
            lease.Path,
            requiresWarmup: true,
            playAfterPriming: playAfterOpen,
            startSeconds: segment.SourceStartSeconds + offset,
            forceMuted: !segment.AudioEnabled);
        PositionSlider.Value = _compositionDraftPositionSeconds;
        UpdateCompositionTimelinePlayhead(_compositionDraftPositionSeconds);
    }

    private int FindCompositionDraftSegmentIndex(double globalSeconds)
    {
        if (_compositionDraftSegments.Count == 0) return -1;
        for (var index = 0; index < _compositionDraftSegments.Count; index++)
        {
            var segment = _compositionDraftSegments[index];
            if (globalSeconds < segment.TimelineStartSeconds + segment.DurationSeconds - 0.000_000_1)
                return index;
        }
        return _compositionDraftSegments.Count - 1;
    }

    private async Task<bool> AdvanceCompositionDraftSegmentAsync()
    {
        if (_advancingCompositionDraft || _activeCompositionDraftRevisionId is null ||
            _activeCompositionDraftSegmentIndex < 0)
            return false;
        var nextIndex = _activeCompositionDraftSegmentIndex + 1;
        if (nextIndex >= _compositionDraftSegments.Count) return false;
        _advancingCompositionDraft = true;
        try
        {
            var next = _compositionDraftSegments[nextIndex];
            await OpenCompositionDraftSegmentAsync(nextIndex, next.TimelineStartSeconds, playAfterOpen: true);
            return true;
        }
        finally
        {
            _advancingCompositionDraft = false;
        }
    }

    private static double ResolveCompositionBoundarySeconds(
        VideoProject project,
        RecipeBoundary boundary,
        ProjectAsset source,
        bool isEnd)
    {
        var duration = source.DurationSeconds ?? source.Encoding?.DurationSeconds ??
                       source.Virtual?.ExpectedMediaProperties?.DurationSeconds ?? 0;
        return boundary.Kind switch
        {
            RecipeBoundaryKind.SourceStart => 0,
            RecipeBoundaryKind.SourceEnd => duration,
            RecipeBoundaryKind.Timestamp => boundary.TimestampSeconds ?? (isEnd ? duration : 0),
            RecipeBoundaryKind.Anchor when boundary.Anchor is { } reference =>
                project.AnchorRevisions.Single(revision =>
                    revision.Id == reference.AnchorRevisionId && revision.AnchorId == reference.AnchorId)
                    .TimestampSeconds,
            _ => isEnd ? duration : 0
        };
    }

    private async Task ShowVirtualAssetPreviewAsync(
        ProjectAsset asset,
        ProjectMediaListItem selectedItem,
        Guid? selectedProjectId)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
        if (asset.Virtual?.Kind == VirtualAssetKind.Composition)
        {
            InspectorText.Text = FormatAssetInspector(asset);
            await OpenCompositionDraftPreviewAsync(asset, selectedItem, selectedProjectId);
            return;
        }
        var kindName = asset.Virtual?.Kind == VirtualAssetKind.Composition
            ? "Working Composition"
            : "Saved Clip";
        await RunUiActionAsync($"Preparing {asset.EffectiveDisplayName}…", async () =>
        {
            MaterializedMediaLease? lease = null;
            try
            {
                lease = await _mediaMaterializer.MaterializeAsync(
                    _workspace.Project,
                    _workspace.Location,
                    new MaterializationRequest(
                        new AssetMaterializationTarget(asset.Id, asset.Virtual?.CurrentRecipeRevisionId),
                        MaterializationPurpose.Preview));
                if (_workspace.Project?.Id != selectedProjectId || AssetsList.SelectedItem != selectedItem)
                {
                    await lease.DisposeAsync();
                    return;
                }

                InspectorText.Text = FormatAssetInspector(asset, lease.Encoding);
                ClearMediaPreview();
                if (asset.Virtual?.Kind == VirtualAssetKind.Composition)
                    _activeCompositionPreviewRevisionId = asset.Virtual.CurrentRecipeRevisionId;
                _activePreviewLease = lease;
                lease = null;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                OpenVideoPreview(
                    _activePreviewLease.Path,
                    requiresWarmup: asset.Virtual?.Kind != VirtualAssetKind.SavedClip);
                StatusText.Text = $"Selected {kindName} {asset.EffectiveDisplayName}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_workspace.Project?.Id != selectedProjectId) return;
                ClearMediaPreview();
                PreviewPlaceholder.Text = $"{kindName} preview unavailable\n\n{exception.Message}";
                PreviewPlaceholder.Visibility = Visibility.Visible;
                StatusText.Text = $"Could not prepare {asset.EffectiveDisplayName}.";
            }
            finally
            {
                if (lease is not null) await lease.DisposeAsync();
            }
        });
    }

    private void OpenVideoPreview(
        string absolutePath,
        bool requiresWarmup,
        bool playAfterPriming = false,
        double startSeconds = 0,
        bool forceMuted = false)
    {
        _previewAudioForcedMuted = forceMuted;
        _videoPreviewWasMutedBeforePriming = forceMuted || _userPreviewMuted || VolumeSlider.Value <= 0;
        _videoPreviewRequiresWarmup = requiresWarmup;
        _playVideoAfterPriming = playAfterPriming;
        _pendingPreviewStartSeconds = Math.Max(0, startSeconds);
        _videoPreviewHasEnded = false;
        VideoPreview.IsMuted = true;
        VideoPreview.Source = new Uri(absolutePath, UriKind.Absolute);
        VideoPreview.Visibility = Visibility.Visible;
        PlaybackControlsBorder.Visibility = Visibility.Visible;
        PlaybackButton.IsEnabled = false;
        UpdatePreviewAudioControls();
        VideoPreview.Play();
    }

    private void OpenCompositionAuditionAudio(string absolutePath, double startSeconds)
    {
        _compositionAuditionAudioReady = false;
        _compositionAuditionAudioPriming = false;
        _playCompositionAuditionAudioAfterOpen = false;
        _pendingCompositionAuditionAudioPosition = Math.Max(0, startSeconds);
        CompositionAuditionAudio.Stop();
        CompositionAuditionAudio.Close();
        CompositionAuditionAudio.Source = null;
        CompositionAuditionAudio.IsMuted = true;
        CompositionAuditionAudio.Volume = VolumeSlider.Value;
        CompositionAuditionAudio.Source = new Uri(absolutePath, UriKind.Absolute);
        CompositionAuditionAudio.Play();
        UpdatePreviewAudioControls();
    }

    private async void CompositionAuditionAudio_MediaOpened(object sender, RoutedEventArgs e)
    {
        var openedSource = CompositionAuditionAudio.Source;
        _compositionAuditionAudioPriming = true;
        CompositionAuditionAudio.IsMuted = true;
        CompositionAuditionAudio.Position = TimeSpan.FromSeconds(_pendingCompositionAuditionAudioPosition);
        CompositionAuditionAudio.Play();
        await Task.Delay(50);
        if (CompositionAuditionAudio.Source != openedSource) return;

        CompositionAuditionAudio.Pause();
        CompositionAuditionAudio.Position = TimeSpan.FromSeconds(_pendingCompositionAuditionAudioPosition);
        CompositionAuditionAudio.IsMuted = _userPreviewMuted;
        _compositionAuditionAudioPriming = false;
        _compositionAuditionAudioReady = true;
        if (_playCompositionAuditionAudioAfterOpen)
        {
            _playCompositionAuditionAudioAfterOpen = false;
            CompositionAuditionAudio.Play();
        }
        UpdatePreviewAudioControls();
    }

    private void CompositionAuditionAudio_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StopCompositionAuditionAudio();
        StatusText.Text = $"Independent audio audition unavailable: {e.ErrorException?.Message ?? "media playback failed"}.";
    }

    private void SyncCompositionAuditionAudio(double globalSeconds, bool play)
    {
        if (CompositionAuditionAudio.Source is null) return;
        _pendingCompositionAuditionAudioPosition = Math.Clamp(globalSeconds, 0, PositionSlider.Maximum);
        _playCompositionAuditionAudioAfterOpen = play;
        if (!_compositionAuditionAudioReady || _compositionAuditionAudioPriming) return;
        CompositionAuditionAudio.Position = TimeSpan.FromSeconds(_pendingCompositionAuditionAudioPosition);
        CompositionAuditionAudio.IsMuted = _userPreviewMuted;
        if (play)
        {
            _playCompositionAuditionAudioAfterOpen = false;
            CompositionAuditionAudio.Play();
        }
        else
        {
            CompositionAuditionAudio.Pause();
        }
    }

    private void PauseCompositionAuditionAudio()
    {
        _playCompositionAuditionAudioAfterOpen = false;
        if (CompositionAuditionAudio.Source is not null) CompositionAuditionAudio.Pause();
    }

    private void StopCompositionAuditionAudio()
    {
        _compositionAuditionAudioReady = false;
        _compositionAuditionAudioPriming = false;
        _playCompositionAuditionAudioAfterOpen = false;
        _pendingCompositionAuditionAudioPosition = 0;
        CompositionAuditionAudio.Stop();
        CompositionAuditionAudio.Close();
        CompositionAuditionAudio.Source = null;
        ReleaseCompositionAuditionAudioLease();
        UpdatePreviewAudioControls();
    }

    private void UpdatePreviewAudioControls()
    {
        if (MuteButton is null || VolumeSlider is null) return;
        var hasIndependentAudio = _activeCompositionAuditionAudioLease is not null;
        var canAdjustAudio = !_previewAudioForcedMuted || hasIndependentAudio;
        MuteButton.IsEnabled = canAdjustAudio;
        VolumeSlider.IsEnabled = canAdjustAudio;
        MuteButton.Content = !canAdjustAudio
            ? "Muted"
            : _userPreviewMuted ? "Unmute" : "Mute";
        MuteButton.ToolTip = !canAdjustAudio
            ? "Source audio is muted for this composition segment"
            : _previewAudioForcedMuted
                ? "Mute or unmute the independent composition audio"
                : "Mute or unmute preview audio";
    }

    private void ClearMediaPreview()
    {
        _isVideoPreviewPriming = false;
        _playVideoAfterPriming = false;
        _videoPreviewHasEnded = false;
        _activeCompositionPreviewRevisionId = null;
        _activeCompositionDraftRevisionId = null;
        _compositionDraftSegments = [];
        _activeCompositionDraftSegmentIndex = -1;
        _compositionDraftPositionSeconds = 0;
        _compositionDraftOpenVersion++;
        _previewAudioForcedMuted = false;
        StopCompositionAuditionAudio();
        VideoPreview.Stop();
        VideoPreview.Close();
        SetPlaybackState(false);
        _isScrubbing = false;
        _resumePlaybackAfterScrub = false;
        _isCompositionTimelineScrubbing = false;
        _resumePlaybackAfterCompositionTimelineScrub = false;
        if (Mouse.Captured == PositionSlider) Mouse.Capture(null);
        if (Mouse.Captured == CompositionTimelineCanvas) Mouse.Capture(null);
        VideoPreview.Source = null;
        ReleaseActivePreviewLease();
        VideoPreview.Visibility = Visibility.Collapsed;
        PlaybackControlsBorder.Visibility = Visibility.Collapsed;
        PlaybackButton.IsEnabled = false;
        PreviousFrameButton.IsEnabled = false;
        NextFrameButton.IsEnabled = false;
        UpdatePreviewAudioControls();
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Text = "Select a video or image asset to preview";
        PreviewPlaceholder.TextAlignment = TextAlignment.Center;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PositionSlider.Maximum = 1;
        PositionSlider.Value = 0;
        TimeText.Text = "00:00 / 00:00";
        UpdateCompositionTimelinePlayhead(0);
    }

    private void ClearStaleCompositionPreview(ProjectAsset composition, RecipeRevision currentRevision)
    {
        ClearMediaPreview();
        if (AssetsList.SelectedItem is not ProjectMediaListItem { Asset: { } selected } ||
            selected.Id != composition.Id)
            return;

        var selectedItem = (ProjectMediaListItem)AssetsList.SelectedItem;
        _ = OpenCompositionDraftPreviewAsync(composition, selectedItem, _workspace.Project?.Id);
    }

    private void ReleaseActivePreviewLease()
    {
        var lease = Interlocked.Exchange(ref _activePreviewLease, null);
        if (lease is not null) lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void ReleaseCompositionAuditionAudioLease()
    {
        var lease = Interlocked.Exchange(ref _activeCompositionAuditionAudioLease, null);
        if (lease is not null) lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_activeCompositionDraftRevisionId is null && VideoPreview.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = VideoPreview.NaturalDuration.TimeSpan.TotalSeconds;
        }

        var openedSource = VideoPreview.Source;
        _isVideoPreviewPriming = true;
        PlaybackButton.IsEnabled = false;
        VideoPreview.IsMuted = true;
        VideoPreview.Position = TimeSpan.FromSeconds(_pendingPreviewStartSeconds);
        VideoPreview.Play();
        if (_videoPreviewRequiresWarmup) await Task.Delay(100);
        if (VideoPreview.Source != openedSource) return;

        VideoPreview.Pause();
        VideoPreview.Position = TimeSpan.FromSeconds(_pendingPreviewStartSeconds);
        VideoPreview.IsMuted = _videoPreviewWasMutedBeforePriming;
        _isVideoPreviewPriming = false;
        PlaybackButton.IsEnabled = true;
        var hasVideo = VideoPreview.NaturalVideoWidth > 0 && VideoPreview.NaturalVideoHeight > 0;
        PreviousFrameButton.IsEnabled = hasVideo;
        NextFrameButton.IsEnabled = hasVideo;
        var shouldPlay = _playVideoAfterPriming;
        _playVideoAfterPriming = false;
        if (shouldPlay)
        {
            if (_activeCompositionDraftRevisionId is not null)
                SyncCompositionAuditionAudio(_compositionDraftPositionSeconds, play: true);
            VideoPreview.Play();
            SetPlaybackState(true);
        }
        else
        {
            if (_activeCompositionDraftRevisionId is not null)
                SyncCompositionAuditionAudio(_compositionDraftPositionSeconds, play: false);
            SetPlaybackState(false);
        }
        UpdatePlaybackPosition();
    }

    private async void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_isVideoPreviewPriming) return;
        if (_activeCompositionDraftRevisionId is not null &&
            await AdvanceCompositionDraftSegmentAsync())
            return;
        CompleteVideoPlayback();
    }

    private void Playback_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Source is null) return;
        if (_videoPreviewHasEnded || IsAtVideoEnd())
        {
            if (_activeCompositionDraftRevisionId is not null)
            {
                _videoPreviewHasEnded = false;
                _ = OpenCompositionDraftSegmentAsync(0, 0, playAfterOpen: true);
                return;
            }
            ReopenVideoPreviewForPlayback();
            return;
        }
        if (_isVideoPlaying)
        {
            VideoPreview.Pause();
            PauseCompositionAuditionAudio();
            SetPlaybackState(false);
            return;
        }

        if (_activeCompositionDraftRevisionId is not null)
            SyncCompositionAuditionAudio(GetCurrentTimelinePlaybackSeconds(), play: true);
        VideoPreview.Play();
        SetPlaybackState(true);
    }

    private async void PreviousFrame_Click(object sender, RoutedEventArgs e) =>
        await StepPreviewFrameAsync(-1);

    private async void NextFrame_Click(object sender, RoutedEventArgs e) =>
        await StepPreviewFrameAsync(1);

    private async Task StepPreviewFrameAsync(int direction)
    {
        if (direction is not (-1 or 1) || VideoPreview.Source is not { IsFile: true } source ||
            !await _frameNavigationGate.WaitAsync(0))
            return;
        try
        {
            VideoPreview.Pause();
            PauseCompositionAuditionAudio();
            SetPlaybackState(false);
            PreviousFrameButton.IsEnabled = false;
            NextFrameButton.IsEnabled = false;
            var currentSeconds = VideoPreview.Position.TotalSeconds;
            var frames = await _exactFrameService.IndexWindowAsync(source.LocalPath, currentSeconds, radiusSeconds: 2);
            var target = direction < 0
                ? frames.Where(frame => frame.TimestampSeconds < currentSeconds - 0.000_000_1)
                    .OrderByDescending(frame => frame.TimestampSeconds)
                    .FirstOrDefault()
                : frames.Where(frame => frame.TimestampSeconds > currentSeconds + 0.000_000_1)
                    .OrderBy(frame => frame.TimestampSeconds)
                    .FirstOrDefault();
            if (target is null) return;

            if (_activeCompositionDraftRevisionId is not null &&
                _activeCompositionDraftSegmentIndex >= 0 &&
                _activeCompositionDraftSegmentIndex < _compositionDraftSegments.Count)
            {
                var segment = _compositionDraftSegments[_activeCompositionDraftSegmentIndex];
                var globalSeconds = segment.TimelineStartSeconds + target.TimestampSeconds -
                                    segment.SourceStartSeconds;
                await SeekCompositionDraftAsync(globalSeconds, playAfterSeek: false);
            }
            else
            {
                SeekPreview(target.TimestampSeconds);
                PositionSlider.Value = target.TimestampSeconds;
            }
        }
        catch (Exception exception)
        {
            ShowError("Frame navigation failed", exception);
        }
        finally
        {
            var hasVideo = VideoPreview.Source is not null &&
                           VideoPreview.NaturalVideoWidth > 0 && VideoPreview.NaturalVideoHeight > 0;
            PreviousFrameButton.IsEnabled = hasVideo;
            NextFrameButton.IsEnabled = hasVideo;
            _frameNavigationGate.Release();
        }
    }

    private void CompleteVideoPlayback()
    {
        VideoPreview.Pause();
        PauseCompositionAuditionAudio();
        if (_activeCompositionDraftRevisionId is not null)
        {
            _compositionDraftPositionSeconds = PositionSlider.Maximum;
            PositionSlider.Value = _compositionDraftPositionSeconds;
            UpdateCompositionTimelinePlayhead(_compositionDraftPositionSeconds);
        }
        else
        {
            VideoPreview.Position = TimeSpan.Zero;
        }
        _videoPreviewHasEnded = true;
        SetPlaybackState(false);
        UpdatePlaybackPosition();
    }

    private bool IsAtVideoEnd()
    {
        if (!VideoPreview.NaturalDuration.HasTimeSpan) return false;
        var duration = VideoPreview.NaturalDuration.TimeSpan;
        return duration > TimeSpan.Zero && VideoPreview.Position >= duration - TimeSpan.FromMilliseconds(10);
    }

    private void ReopenVideoPreviewForPlayback()
    {
        if (VideoPreview.Source is not { IsFile: true } source) return;
        var path = source.LocalPath;
        var requiresWarmup = _videoPreviewRequiresWarmup;
        VideoPreview.Stop();
        VideoPreview.Close();
        VideoPreview.Source = null;
        OpenVideoPreview(path, requiresWarmup, playAfterPriming: true);
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VideoPreview.Source is null) return;
        _resumePlaybackAfterScrub = _isVideoPlaying;
        _isScrubbing = true;
        VideoPreview.Pause();
        PauseCompositionAuditionAudio();
        SetPlaybackState(false);
        PositionSlider.CaptureMouse();
        UpdateScrubPosition(e);
        e.Handled = true;
    }

    private void PositionSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isScrubbing || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateScrubPosition(e);
        e.Handled = true;
    }

    private async void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (VideoPreview.Source is null || !_isScrubbing) return;
        UpdateScrubPosition(e);
        if (_activeCompositionDraftRevisionId is not null)
            await SeekCompositionDraftAsync(PositionSlider.Value, _resumePlaybackAfterScrub);
        else
            SeekPreview(PositionSlider.Value);
        _isScrubbing = false;
        if (Mouse.Captured == PositionSlider) Mouse.Capture(null);
        if (_resumePlaybackAfterScrub && _activeCompositionDraftRevisionId is null)
        {
            VideoPreview.Play();
            SetPlaybackState(true);
        }
        else if (_activeCompositionDraftRevisionId is null)
        {
            VideoPreview.Pause();
            SetPlaybackState(false);
        }
        _resumePlaybackAfterScrub = false;
        ScheduleContactFrameRefresh(PositionSlider.Value);
        e.Handled = true;
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isScrubbing) SeekPreview(e.NewValue);
    }

    private void SeekPreview(double seconds)
    {
        if (VideoPreview.Source is null) return;
        if (_activeCompositionDraftRevisionId is not null)
        {
            _ = SeekCompositionDraftAsync(seconds, playAfterSeek: false);
            return;
        }
        VideoPreview.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, PositionSlider.Maximum));
        TimeText.Text = $"{FormatTime(VideoPreview.Position)} / {FormatTime(VideoPreview.NaturalDuration.HasTimeSpan ? VideoPreview.NaturalDuration.TimeSpan : TimeSpan.Zero)}";
    }

    private async Task SeekCompositionDraftAsync(double seconds, bool playAfterSeek)
    {
        if (_activeCompositionDraftRevisionId is null || _compositionDraftSegments.Count == 0) return;
        var target = Math.Clamp(seconds, 0, PositionSlider.Maximum);
        SyncCompositionAuditionAudio(target, playAfterSeek);
        var targetIndex = _compositionDraftSegments.Count - 1;
        for (var index = 0; index < _compositionDraftSegments.Count; index++)
        {
            var segment = _compositionDraftSegments[index];
            if (target < segment.TimelineStartSeconds + segment.DurationSeconds - 0.000_000_1)
            {
                targetIndex = index;
                break;
            }
        }
        var targetSegment = _compositionDraftSegments[targetIndex];
        _compositionDraftPositionSeconds = target;
        if (targetIndex != _activeCompositionDraftSegmentIndex)
        {
            await OpenCompositionDraftSegmentAsync(targetIndex, target, playAfterSeek);
            return;
        }

        var localSeconds = targetSegment.SourceStartSeconds + target - targetSegment.TimelineStartSeconds;
        VideoPreview.Position = TimeSpan.FromSeconds(Math.Max(0, localSeconds));
        PositionSlider.Value = target;
        TimeText.Text = $"{FormatTime(TimeSpan.FromSeconds(target))} / " +
                        $"{FormatTime(TimeSpan.FromSeconds(PositionSlider.Maximum))}";
        UpdateCompositionTimelinePlayhead(target);
        if (playAfterSeek)
        {
            VideoPreview.Play();
            SyncCompositionAuditionAudio(target, play: true);
            SetPlaybackState(true);
        }
    }

    private void UpdateScrubPosition(MouseEventArgs e)
    {
        if (PositionSlider.ActualWidth <= 0) return;
        var pointer = e.GetPosition(PositionSlider);
        var fraction = Math.Clamp(pointer.X / PositionSlider.ActualWidth, 0, 1);
        PositionSlider.Value = PositionSlider.Minimum +
                               fraction * (PositionSlider.Maximum - PositionSlider.Minimum);
    }

    private void SetPlaybackState(bool isPlaying)
    {
        _isVideoPlaying = isPlaying;
        if (PlaybackButton is null || PlayGlyph is null || PauseGlyph is null) return;
        PlayGlyph.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseGlyph.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
        PlaybackButton.ToolTip = isPlaying ? "Pause preview" : "Play preview";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VideoPreview is null || MuteButton is null) return;
        VideoPreview.Volume = e.NewValue;
        if (CompositionAuditionAudio is not null) CompositionAuditionAudio.Volume = e.NewValue;
        _userPreviewMuted = e.NewValue <= 0;
        VideoPreview.IsMuted = _previewAudioForcedMuted || _userPreviewMuted;
        if (CompositionAuditionAudio is not null) CompositionAuditionAudio.IsMuted = _userPreviewMuted;
        UpdatePreviewAudioControls();
        if (e.NewValue > 0) _volumeBeforeMute = e.NewValue;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (_previewAudioForcedMuted && _activeCompositionAuditionAudioLease is null) return;
        if (_userPreviewMuted || VolumeSlider.Value <= 0)
        {
            VolumeSlider.Value = _volumeBeforeMute > 0 ? _volumeBeforeMute : 1;
            _userPreviewMuted = false;
            VideoPreview.IsMuted = _previewAudioForcedMuted;
            CompositionAuditionAudio.IsMuted = false;
            UpdatePreviewAudioControls();
            return;
        }

        _volumeBeforeMute = VolumeSlider.Value;
        _userPreviewMuted = true;
        VideoPreview.IsMuted = true;
        CompositionAuditionAudio.IsMuted = true;
        UpdatePreviewAudioControls();
    }

    private void DurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DurationText is not null)
        {
            DurationText.Text = $"{(int)e.NewValue}s";
        }

        ScheduleDraftAutosave();
    }

    private void UpdatePlaybackPosition()
    {
        if (VideoPreview.Source is null)
        {
            TimeText.Text = "00:00 / 00:00";
            return;
        }

        var mediaPosition = VideoPreview.Position;
        var mediaDuration = VideoPreview.NaturalDuration.HasTimeSpan
            ? VideoPreview.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        if (_isVideoPlaying && _activeCompositionDraftRevisionId is not null &&
            _activeCompositionDraftSegmentIndex >= 0 &&
            _activeCompositionDraftSegmentIndex < _compositionDraftSegments.Count)
        {
            var activeSegment = _compositionDraftSegments[_activeCompositionDraftSegmentIndex];
            if (mediaPosition.TotalSeconds >=
                activeSegment.SourceStartSeconds + activeSegment.DurationSeconds - 0.01)
            {
                _ = AdvanceCompositionDraftSegmentAsync();
                return;
            }
        }

        if (_isVideoPlaying && IsAtVideoEnd())
        {
            if (_activeCompositionDraftRevisionId is not null)
                _ = AdvanceCompositionDraftSegmentAsync();
            else
                CompleteVideoPlayback();
            return;
        }

        if (_activeCompositionDraftRevisionId is not null &&
            _activeCompositionDraftSegmentIndex >= 0 &&
            _activeCompositionDraftSegmentIndex < _compositionDraftSegments.Count)
        {
            var segment = _compositionDraftSegments[_activeCompositionDraftSegmentIndex];
            var currentSeconds = Math.Clamp(
                segment.TimelineStartSeconds + mediaPosition.TotalSeconds - segment.SourceStartSeconds,
                segment.TimelineStartSeconds,
                segment.TimelineStartSeconds + segment.DurationSeconds);
            _compositionDraftPositionSeconds = currentSeconds;
            if (_isVideoPlaying && _compositionAuditionAudioReady &&
                Math.Abs(CompositionAuditionAudio.Position.TotalSeconds - currentSeconds) > 0.2)
                SyncCompositionAuditionAudio(currentSeconds, play: true);
            if (!_isScrubbing) PositionSlider.Value = currentSeconds;
            TimeText.Text = $"{FormatTime(TimeSpan.FromSeconds(currentSeconds))} / " +
                            $"{FormatTime(TimeSpan.FromSeconds(PositionSlider.Maximum))}";
            UpdateCompositionTimelinePlayhead(currentSeconds);
            return;
        }

        if (!_isScrubbing) PositionSlider.Value = mediaPosition.TotalSeconds;
        TimeText.Text = $"{FormatTime(mediaPosition)} / {FormatTime(mediaDuration)}";
        UpdateCompositionTimelinePlayhead(mediaPosition.TotalSeconds);
    }

    private async Task LoadFrameWorkspaceAsync(ProjectAsset asset, Guid? selectedProjectId)
    {
        if (asset.MediaType != MediaType.Video || asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
        {
            FrameWorkspaceStatusText.Text = "Select a physical video";
            return;
        }

        var path = _workspace.GetAbsoluteAssetPath(asset);
        if (!File.Exists(path))
        {
            FrameWorkspaceStatusText.Text = "Source media is missing";
            return;
        }

        var cancellation = ReplaceFrameBrowserCancellation();
        _frameSourceAssetId = asset.Id;
        FrameWorkspaceStatusText.Text = "Indexing decoded frames…";
        ContactFramesEmptyText.Text = "Reading exact presentation frames…";
        try
        {
            await using var verifiedSource = await new PhysicalAssetMaterializer().MaterializeAsync(
                _workspace.Project!,
                _workspace.Location!,
                new MaterializationRequest(new AssetMaterializationTarget(asset.Id), MaterializationPurpose.Preview),
                cancellation.Token);
            if (_workspace.Project?.Id != selectedProjectId || _frameSourceAssetId != asset.Id) return;
            _frameSourceContentHash = verifiedSource.ContentIdentity.Sha256
                ?? throw new InvalidDataException("The selected video does not have a verified SHA-256 identity.");
            _indexedFrames = await _exactFrameService.IndexWindowAsync(
                path,
                Math.Max(0, VideoPreview.Position.TotalSeconds),
                cancellationToken: cancellation.Token);
            if (_workspace.Project?.Id != selectedProjectId || _frameSourceAssetId != asset.Id) return;
            await _workspace.SaveAsync(cancellation.Token);

            FrameWorkspaceStatusText.Text = $"{_indexedFrames.Count:N0} nearby decoded frames";
            ContactFramesEmptyText.Visibility = Visibility.Collapsed;
            await RefreshContactFramesAsync(VideoPreview.Position.TotalSeconds, cancellation.Token);
            await RefreshSavedFramesAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (_frameSourceAssetId != asset.Id) return;
            FrameWorkspaceStatusText.Text = "Frame browser unavailable";
            ContactFramesEmptyText.Text = exception.Message;
            ContactFramesEmptyText.Visibility = Visibility.Visible;
            StatusText.Text = $"Precision frame browsing is unavailable: {exception.Message}";
        }
    }

    private CancellationTokenSource ReplaceFrameBrowserCancellation()
    {
        _frameBrowserCancellation?.Cancel();
        _frameBrowserCancellation?.Dispose();
        _frameBrowserCancellation = new CancellationTokenSource();
        return _frameBrowserCancellation;
    }

    private void ResetFrameWorkspace()
    {
        var wasPreparingMedia = _mediaPreparationMode != MediaPreparationMode.None;
        _mediaPreparationMode = MediaPreparationMode.None;
        if (wasPreparingMedia && VideoPreview is not null)
        {
            VideoPreview.IsMuted = _previewWasMutedBeforeMediaPreparation || VolumeSlider.Value <= 0;
            MuteButton.IsEnabled = true;
            MuteButton.Content = VideoPreview.IsMuted ? "Unmute" : "Mute";
            MuteButton.ToolTip = "Mute or unmute preview audio";
            VolumeSlider.IsEnabled = true;
        }
        if (PrecisionFramePanel is not null)
        {
            PrecisionFramePanel.Visibility = Visibility.Collapsed;
            PrecisionFramePanel.ScrollToTop();
        }
        if (MediaPreparationHome is not null) MediaPreparationHome.Visibility = Visibility.Visible;
        _frameBrowserDebounceTimer?.Stop();
        _frameBrowserCancellation?.Cancel();
        _frameBrowserCancellation?.Dispose();
        _frameBrowserCancellation = null;
        _indexedFrames = [];
        _pendingContactFrameTimestamp = null;
        _pendingKeyboardFrameSteps = 0;
        _isKeyboardFrameNavigationRunning = false;
        _frameSourceAssetId = null;
        _frameSourceContentHash = null;
        _contactFrames.Clear();
        _savedFrames.Clear();
        if (ContactFramesEmptyText is null) return;
        SelectFrameButton.IsEnabled = false;
        MakeClipButton.IsEnabled = false;
        StartEditButton.IsEnabled = false;
        UpdateCompositionActionState();
        MediaPreparationSelectionText.Text = "Select a physical video in Project Media";
        ContactFramesEmptyText.Text = "Select a video to browse exact decoded frames.";
        ContactFramesEmptyText.Visibility = Visibility.Visible;
        SavedFramesEmptyText.Visibility = Visibility.Visible;
        FrameWorkspaceStatusText.Text = "Select a physical video";
        if (PrecisionOperationTitle is not null)
        {
            PrecisionOperationTitle.Text = "SELECT FRAME";
            FrameSelectionActions.Visibility = Visibility.Visible;
            ClipSelectionActions.Visibility = Visibility.Collapsed;
            SavedFramesHeading.Visibility = Visibility.Visible;
            SavedFramesWorkspace.Visibility = Visibility.Visible;
            ClipEditorWorkspace.Visibility = Visibility.Collapsed;
        }
        ClearSavedFrameEditor();
    }

    private void ScheduleContactFrameRefresh(double? targetSeconds = null)
    {
        if (_indexedFrames.Count == 0 || _frameSourceAssetId is null) return;
        _pendingContactFrameTimestamp = targetSeconds ?? VideoPreview.Position.TotalSeconds;
        _frameBrowserDebounceTimer.Stop();
        _frameBrowserDebounceTimer.Start();
    }

    private async void FrameBrowserDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _frameBrowserDebounceTimer.Stop();
        if (_indexedFrames.Count == 0 || _frameSourceAssetId is null) return;
        var cancellation = ReplaceFrameBrowserCancellation();
        try
        {
            var target = _pendingContactFrameTimestamp ?? VideoPreview.Position.TotalSeconds;
            _pendingContactFrameTimestamp = null;
            await RunFrameNavigationAsync(async token =>
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
            StatusText.Text = $"Could not refresh precision frames: {exception.Message}";
        }
    }

    private async Task EnsureFrameWindowAsync(double centerSeconds, CancellationToken cancellationToken)
    {
        if (_workspace.Project is null || _frameSourceAssetId is not { } sourceAssetId) return;
        var source = _workspace.Project.Assets.Single(asset => asset.Id == sourceAssetId);
        var path = _workspace.GetAbsoluteAssetPath(source);
        var duration = source.DurationSeconds ?? source.Encoding?.DurationSeconds ?? PositionSlider.Maximum;
        if (duration > 0) centerSeconds = Math.Clamp(centerSeconds, 0, duration);
        var window = await _exactFrameService.IndexWindowAsync(
            path,
            Math.Max(0, centerSeconds),
            cancellationToken: cancellationToken);
        _indexedFrames = _indexedFrames.Concat(window)
            .GroupBy(frame => (frame.VideoStreamIndex, frame.PresentationTimestamp))
            .Select(group => group.First())
            .OrderBy(frame => frame.PresentationTimestamp)
            .ToArray();
        FrameWorkspaceStatusText.Text = $"{_indexedFrames.Count:N0} nearby decoded frames";
    }

    private async Task RefreshContactFramesAsync(double centerSeconds, CancellationToken cancellationToken)
    {
        if (_workspace.Project is null || _workspace.Location is null ||
            _frameSourceAssetId is not { } sourceAssetId ||
            string.IsNullOrWhiteSpace(_frameSourceContentHash) || _indexedFrames.Count == 0) return;
        var source = _workspace.Project.Assets.Single(asset => asset.Id == sourceAssetId);
        var path = _workspace.GetAbsoluteAssetPath(source);
        var selectedFrames = ExactFrameContactWindow.Select(_indexedFrames, centerSeconds);
        if (selectedFrames.Count == 0) return;

        ContactFramesEmptyText.Visibility = Visibility.Collapsed;
        var center = selectedFrames.MinBy(frame => Math.Abs(frame.TimestampSeconds - centerSeconds))!;
        var existingItems = _contactFrames.ToDictionary(
            item => (item.Frame.VideoStreamIndex, item.Frame.PresentationTimestamp));
        var missingFrames = selectedFrames.Where(frame =>
            !existingItems.ContainsKey((frame.VideoStreamIndex, frame.PresentationTimestamp))).ToArray();
        var createdItems = await Task.WhenAll(missingFrames.Select(frame =>
            CreateContactItemAsync(path, sourceAssetId, frame, cancellationToken)));
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var item in createdItems)
            existingItems[(item.Frame.VideoStreamIndex, item.Frame.PresentationTimestamp)] = item;
        var desiredItems = selectedFrames.Select(frame =>
            existingItems[(frame.VideoStreamIndex, frame.PresentationTimestamp)]).ToArray();

        _suppressFrameSelectionPrefetch = true;
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
            while (_contactFrames.Count > desiredItems.Length) _contactFrames.RemoveAt(_contactFrames.Count - 1);
            ContactFramesList.SelectedItem = desiredItems.Single(item =>
                item.Frame.VideoStreamIndex == center.VideoStreamIndex &&
                item.Frame.PresentationTimestamp == center.PresentationTimestamp);
            ContactFramesList.ScrollIntoView(ContactFramesList.SelectedItem);
        }
        finally
        {
            _suppressFrameSelectionPrefetch = false;
        }
    }

    private async Task<FrameContactListItem> CreateContactItemAsync(
        string sourcePath,
        Guid sourceAssetId,
        VideoPresentationFrame frame,
        CancellationToken cancellationToken)
    {
        var revision = CreateTransientFrameRevision(sourceAssetId, _frameSourceContentHash!, frame);
        await using var lease = await _exactFrameService.ExtractAsync(
            sourcePath,
            _frameSourceContentHash!,
            revision,
            MaterializationPurpose.Thumbnail,
            "contact-strip",
            cancellationToken);
        return new FrameContactListItem(frame, LoadBitmap(lease.Path));
    }

    private static FrameAnchorRevision CreateTransientFrameRevision(
        Guid sourceAssetId,
        string sourceContentHash,
        VideoPresentationFrame frame)
    {
        var identityBytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            sourceContentHash,
            frame.VideoStreamIndex,
            frame.PresentationTimestamp,
            frame.TimeBaseNumerator,
            frame.TimeBaseDenominator)));
        return new FrameAnchorRevision
        {
            Id = new Guid(identityBytes.AsSpan(0, 16)),
            AnchorId = Guid.Empty,
            RevisionNumber = 0,
            SourceAssetId = sourceAssetId,
            SourceContentHash = sourceContentHash,
            VideoStreamIndex = frame.VideoStreamIndex,
            PresentationTimestamp = frame.PresentationTimestamp,
            TimeBaseNumerator = frame.TimeBaseNumerator,
            TimeBaseDenominator = frame.TimeBaseDenominator,
            FrameNumber = frame.FrameNumber
        };
    }

    private async Task RefreshSavedFramesAsync(CancellationToken cancellationToken)
    {
        if (_workspace.Project is null || _workspace.Location is null ||
            _frameSourceAssetId is not { } sourceAssetId || string.IsNullOrWhiteSpace(_frameSourceContentHash)) return;
        var project = _workspace.Project;
        var source = project.Assets.Single(asset => asset.Id == sourceAssetId);
        var sourcePath = _workspace.GetAbsoluteAssetPath(source);
        var selectedAnchorId = (SavedFramesList.SelectedItem as SavedFrameListItem)?.Anchor.Id;
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
                    _frameSourceContentHash!,
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
        _savedFrames.Clear();
        foreach (var item in results.OrderBy(item => item.Revision.PresentationTimestamp)) _savedFrames.Add(item);
        foreach (var item in results)
        {
            foreach (var choice in _referenceChoices.Where(choice =>
                         choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                         choice.LogicalObjectId == item.Anchor.Id))
                choice.UpdateThumbnail(item.Thumbnail);
            var mediaItem = _assets.FirstOrDefault(candidate => candidate.Anchor?.Id == item.Anchor.Id);
            if (mediaItem is not null) mediaItem.Thumbnail = item.Thumbnail;
        }
        ReferenceAssetsGrid.Items.Refresh();
        AssetsList.Items.Refresh();
        SavedFramesEmptyText.Visibility = _savedFrames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SavedFramesList.SelectedItem = selectedAnchorId is { } id
            ? _savedFrames.FirstOrDefault(item => item.Anchor.Id == id)
            : null;
    }

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

    private async void ContactFramesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContactFramesList.SelectedItem is not FrameContactListItem item || VideoPreview.Source is null) return;
        SeekPreview(item.Frame.TimestampSeconds);
        if (_suppressFrameSelectionPrefetch) return;
        var cancellationToken = _frameBrowserCancellation?.Token ?? CancellationToken.None;
        try
        {
            await RunFrameNavigationAsync(async token =>
            {
                if (ContactFramesList.SelectedItem is not FrameContactListItem latest) return;
                await EnsureAdjacentFramesAvailableAsync(latest.Frame, direction: 0, token);
                await RefreshContactFramesAsync(latest.Frame.TimestampSeconds, token);
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void ContactFramesList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right)) return;
        e.Handled = true;
        if (ContactFramesList.SelectedItem is not FrameContactListItem) return;
        _pendingKeyboardFrameSteps += e.Key == Key.Left ? -1 : 1;
        if (_isKeyboardFrameNavigationRunning) return;
        _isKeyboardFrameNavigationRunning = true;
        _ = ProcessKeyboardFrameNavigationAsync();
    }

    private async Task ProcessKeyboardFrameNavigationAsync()
    {
        try
        {
            while (_pendingKeyboardFrameSteps != 0 && _mediaPreparationMode != MediaPreparationMode.None)
            {
                var steps = _pendingKeyboardFrameSteps;
                _pendingKeyboardFrameSteps = 0;
                var cancellationToken = _frameBrowserCancellation?.Token ?? CancellationToken.None;
                await RunFrameNavigationAsync(async token =>
                {
                    if (ContactFramesList.SelectedItem is not FrameContactListItem selected) return;
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
            StatusText.Text = $"Could not navigate exact frames: {exception.Message}";
        }
        finally
        {
            _isKeyboardFrameNavigationRunning = false;
            if (_pendingKeyboardFrameSteps != 0 && _mediaPreparationMode != MediaPreparationMode.None)
            {
                _isKeyboardFrameNavigationRunning = true;
                _ = ProcessKeyboardFrameNavigationAsync();
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

        var source = _workspace.Project?.Assets.SingleOrDefault(asset => asset.Id == _frameSourceAssetId);
        var duration = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ?? PositionSlider.Maximum;
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
        var source = _workspace.Project?.Assets.SingleOrDefault(asset => asset.Id == _frameSourceAssetId);
        var duration = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ?? PositionSlider.Maximum;
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

    private async Task RunFrameNavigationAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await _frameNavigationGate.WaitAsync(cancellationToken);
        try
        {
            await action(cancellationToken);
        }
        finally
        {
            _frameNavigationGate.Release();
        }
    }

    private async void SelectFirstFrame_Click(object sender, RoutedEventArgs e) => await SelectBoundaryFrameAsync(first: true);

    private async void SelectLastFrame_Click(object sender, RoutedEventArgs e) => await SelectBoundaryFrameAsync(first: false);

    private async Task SelectBoundaryFrameAsync(bool first)
    {
        if (GetSelectedAsset() is not { } asset)
        {
            StatusText.Text = "Select a physical video first.";
            return;
        }
        var target = first ? 0 : Math.Max(0, asset.DurationSeconds ?? PositionSlider.Maximum);
        var cancellation = ReplaceFrameBrowserCancellation();
        try
        {
            await EnsureFrameWindowAsync(target, cancellation.Token);
            var candidates = await _exactFrameService.IndexWindowAsync(
                _workspace.GetAbsoluteAssetPath(asset),
                target,
                first ? 1 : 5,
                cancellation.Token);
            if (candidates.Count == 0) throw new InvalidDataException("No decoded presentation frame was found near the boundary.");
            var frame = first ? candidates[0] : candidates[^1];
            await RefreshContactFramesAsync(frame.TimestampSeconds, cancellation.Token);
            StatusText.Text = first ? "Selected the first decoded presentation frame." : "Selected the final decodable presentation frame.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private async void SaveSelectedFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null || ContactFramesList.SelectedItem is not FrameContactListItem selected ||
            _frameSourceAssetId is not { } sourceAssetId || string.IsNullOrWhiteSpace(_frameSourceContentHash))
        {
            StatusText.Text = "Select a frame in the precision strip before saving it.";
            return;
        }

        await RunUiActionAsync("Saving exact frame position…", async () =>
        {
            var anchor = new FrameAnchor
            {
                DisplayLabel = $"Saved frame {FormatFrameTimestamp(selected.Frame.TimestampSeconds)}"
            };
            _workspace.Project.Anchors.Add(anchor);
            var revision = _workspace.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
                sourceAssetId,
                _frameSourceContentHash!,
                selected.Frame.VideoStreamIndex,
                selected.Frame.PresentationTimestamp,
                selected.Frame.TimeBaseNumerator,
                selected.Frame.TimeBaseDenominator,
                selected.Frame.FrameNumber));
            await _workspace.SaveAsync();
            RefreshProjectCollections();
            await RefreshSavedFramesAsync(CancellationToken.None);
            SavedFramesList.SelectedItem = _savedFrames.FirstOrDefault(item => item.Revision.Id == revision.Id);
            StatusText.Text = $"Saved exact frame at {FormatFrameTimestamp(revision.TimestampSeconds)}.";
        });
    }

    private void UseSourceStart_Click(object sender, RoutedEventArgs e)
    {
        _clipStart = ClipBoundarySelection.SourceStart;
        UpdateClipBoundarySummary();
    }

    private void UseSourceEnd_Click(object sender, RoutedEventArgs e)
    {
        _clipEnd = ClipBoundarySelection.SourceEnd;
        UpdateClipBoundarySummary();
    }

    private void SetClipStart_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClipFrame(out var position)) return;
        _clipStart = ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.BeforeFrame);
        UpdateClipBoundarySummary();
    }

    private void SetClipEnd_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedClipFrame(out var position)) return;
        _clipEnd = ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.AfterFrame);
        UpdateClipBoundarySummary();
    }

    private bool TryGetSelectedClipFrame(out ExactFramePosition position)
    {
        if (ContactFramesList.SelectedItem is not FrameContactListItem selected ||
            _frameSourceAssetId is not { } sourceAssetId ||
            string.IsNullOrWhiteSpace(_frameSourceContentHash))
        {
            StatusText.Text = "Select an exact frame before setting this clip boundary.";
            position = null!;
            return false;
        }

        position = new ExactFramePosition(
            sourceAssetId,
            _frameSourceContentHash,
            selected.Frame.VideoStreamIndex,
            selected.Frame.PresentationTimestamp,
            selected.Frame.TimeBaseNumerator,
            selected.Frame.TimeBaseDenominator,
            selected.Frame.FrameNumber);
        return true;
    }

    private void UpdateClipBoundarySummary()
    {
        if (ClipBoundarySummaryText is null) return;
        ClipBoundarySummaryText.Text =
            $"Start: {FormatClipBoundary(_clipStart)}\nEnd: {FormatClipBoundary(_clipEnd)}";
    }

    private static string FormatClipBoundary(ClipBoundarySelection boundary) => boundary.Kind switch
    {
        ClipBoundaryKind.SourceStart => "video beginning",
        ClipBoundaryKind.SourceEnd => "video end",
        ClipBoundaryKind.ExactFrame when boundary.ExactPosition is { } position =>
            $"{FormatFrameTimestamp(position.PresentationTimestamp * (double)position.TimeBaseNumerator / position.TimeBaseDenominator)} " +
            $"({boundary.Edge})",
        _ => "not set"
    };

    private async void SaveClip_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPreparationMode != MediaPreparationMode.MakeClip ||
            _frameSourceAssetId is not { } sourceAssetId)
            return;
        if (string.IsNullOrWhiteSpace(ClipNameTextBox.Text))
        {
            StatusText.Text = "Enter a name for the Saved Clip.";
            ClipNameTextBox.Focus();
            return;
        }

        await RunUiActionAsync("Saving non-destructive clip…", async () =>
        {
            var clip = await new SavedClipService(_workspace).CreateAsync(
                ClipNameTextBox.Text,
                sourceAssetId,
                _clipStart,
                _clipEnd);
            RefreshProjectCollections(clip.Id);
            StatusText.Text = $"Saved Clip '{clip.EffectiveDisplayName}' created without copying source media.";
        });
    }

    private void SavedFramesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedFramesList.SelectedItem is not SavedFrameListItem item)
        {
            ClearSavedFrameEditor();
            return;
        }
        SavedFrameLabelTextBox.IsEnabled = true;
        SavedFrameNotesTextBox.IsEnabled = true;
        UpdateSavedFrameButton.IsEnabled = true;
        JumpToSavedFrameButton.IsEnabled = true;
        RemoveSavedFrameButton.IsEnabled = true;
        SavedFrameLabelTextBox.Text = item.Anchor.DisplayLabel ?? string.Empty;
        SavedFrameNotesTextBox.Text = item.Anchor.Notes ?? string.Empty;
        InspectorText.Text = FormatSavedFrameInspector(item);
    }

    private void ClearSavedFrameEditor()
    {
        if (SavedFrameLabelTextBox is null) return;
        SavedFrameLabelTextBox.Text = string.Empty;
        SavedFrameNotesTextBox.Text = string.Empty;
        SavedFrameLabelTextBox.IsEnabled = false;
        SavedFrameNotesTextBox.IsEnabled = false;
        UpdateSavedFrameButton.IsEnabled = false;
        JumpToSavedFrameButton.IsEnabled = false;
        RemoveSavedFrameButton.IsEnabled = false;
    }

    private async void UpdateSavedFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null || SavedFramesList.SelectedItem is not SavedFrameListItem item) return;
        item.Anchor.DisplayLabel = NullIfWhiteSpace(SavedFrameLabelTextBox.Text)
            ?? $"Saved frame {FormatFrameTimestamp(item.Revision.TimestampSeconds)}";
        item.Anchor.Notes = NullIfWhiteSpace(SavedFrameNotesTextBox.Text);
        _workspace.Project.Touch();
        await _workspace.SaveAsync();
        SavedFramesList.Items.Refresh();
        var sourceName = _workspace.Project.Assets
            .SingleOrDefault(asset => asset.Id == item.Revision.SourceAssetId)?.EffectiveDisplayName;
        foreach (var choice in _referenceChoices.Where(choice =>
                     choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                     choice.LogicalObjectId == item.Anchor.Id))
            choice.UpdateAnchor(item.Anchor, item.Revision, sourceName);
        ReferenceAssetsGrid.Items.Refresh();
        InspectorText.Text = FormatSavedFrameInspector(item);
        StatusText.Text = "Saved Frame details updated.";
    }

    private void JumpToSavedFrame_Click(object sender, RoutedEventArgs e)
    {
        if (SavedFramesList.SelectedItem is not SavedFrameListItem item) return;
        SeekPreview(item.Revision.TimestampSeconds);
        ScheduleContactFrameRefresh(item.Revision.TimestampSeconds);
        StatusText.Text = $"Jumped to {FormatFrameTimestamp(item.Revision.TimestampSeconds)}.";
    }

    private async void RemoveSavedFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null || SavedFramesList.SelectedItem is not SavedFrameListItem item) return;
        await ConfirmAndRemoveSavedFrameAsync(item.Anchor, item.DisplayLabel);
    }

    private async Task ConfirmAndRemoveSavedFrameAsync(FrameAnchor anchor, string displayLabel)
    {
        if (_workspace.Project is null) return;
        var result = MessageBox.Show(
            this,
            $"Delete Saved Frame '{displayLabel}' from Project Media?\n\n" +
            "If generation or recipe history references it, ReelForge hides it while retaining the exact historical position.",
            "Remove Saved Frame",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;
        var disposition = _workspace.Project.RemoveOrArchiveAnchor(anchor.Id);
        await _workspace.SaveAsync();
        RefreshProjectCollections();
        if (_mediaPreparationMode != MediaPreparationMode.None && _frameSourceAssetId is not null)
            await RefreshSavedFramesAsync(CancellationToken.None);
        StatusText.Text = disposition == AnchorRemovalDisposition.Archived
            ? "The referenced Saved Frame was archived; existing history still resolves it."
            : "Saved Frame removed.";
    }

    private static string FormatSavedFrameInspector(SavedFrameListItem item)
    {
        var builder = new StringBuilder();
        builder.AppendLine(item.DisplayLabel);
        builder.AppendLine($"Saved Frame: {item.Anchor.Id}");
        builder.AppendLine($"Revision: {item.Revision.RevisionNumber} ({item.Revision.Id})");
        builder.AppendLine($"Position: {FormatFrameTimestamp(item.Revision.TimestampSeconds)}");
        builder.AppendLine($"Stream: {item.Revision.VideoStreamIndex}");
        builder.AppendLine($"Presentation timestamp: {item.Revision.PresentationTimestamp}");
        builder.AppendLine($"Time base: {item.Revision.TimeBaseNumerator}/{item.Revision.TimeBaseDenominator}");
        builder.AppendLine($"Source SHA-256: {item.Revision.SourceContentHash}");
        if (!string.IsNullOrWhiteSpace(item.Anchor.Notes)) builder.AppendLine($"Notes: {item.Anchor.Notes}");
        if (!string.IsNullOrWhiteSpace(item.Error)) builder.AppendLine($"Preview unavailable: {item.Error}");
        return builder.ToString();
    }

    private static string FormatFrameTimestamp(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

    private void RefreshProjectUi()
    {
        if (_workspace.Project is null)
        {
            return;
        }

        _suppressDraftAutosave = true;
        ResetProjectSpecificUi();
        RefreshProjectCollections();
        if (_workspace.Project.CurrentGenerationDraft is { } draft)
            LoadDraftIntoUi(draft);
        RestoreProjectUiState();
        _suppressDraftAutosave = false;

        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} media items";
        Title = $"{_workspace.Project.Name} — ReelForge";
        StatusText.Text = $"Opened {_workspace.Location!.ProjectFilePath}";
    }

    private void ResetProjectSpecificUi()
    {
        ExpandedPromptPanel.Visibility = Visibility.Collapsed;
        AssetsList.SelectedItem = null;
        GenerationsList.SelectedItem = null;
        _referenceChoices.Clear();
        ResetFrameWorkspace();
        PrecisionFramePanel.Visibility = Visibility.Collapsed;
        MediaPreparationHome.Visibility = Visibility.Visible;
        SelectFrameButton.IsEnabled = false;
        MakeClipButton.IsEnabled = false;
        StartEditButton.IsEnabled = false;
        MediaPreparationSelectionText.Text = "Select a video in Project Media";
        RefreshEditWorkspaceState();

        InspectorText.Text = "Select an asset or generation to inspect its details and history.";
        PromptTextBox.Text = string.Empty;
        GenerationStatusText.Text = string.Empty;
        LineageText.Text = "New root generation";
        ClearMediaPreview();
    }

    private void RefreshProjectCollections(Guid? selectedAssetId = null)
    {
        if (_workspace.Project is null) return;
        var hasExplicitSelection = selectedAssetId.HasValue;
        selectedAssetId ??= (AssetsList.SelectedItem as ProjectMediaListItem)?.Asset?.Id;
        var selectedAnchorId = (AssetsList.SelectedItem as ProjectMediaListItem)?.Anchor?.Id;
        var existingChoices = _referenceChoices.ToList();
        _assets.Clear();
        _generations.Clear();
        _referenceChoices.Clear();

        var mediaItems = new List<ProjectMediaListItem>();
        foreach (var asset in _workspace.Project.Assets)
        {
            var mediaItem = new ProjectMediaListItem(asset);
            if (asset is { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Image } &&
                File.Exists(_workspace.GetAbsoluteAssetPath(asset)))
            {
                try
                {
                    mediaItem.Thumbnail = LoadBitmap(_workspace.GetAbsoluteAssetPath(asset));
                }
                catch (Exception exception) when (exception is IOException or NotSupportedException)
                {
                    // The viewer reports unreadable image details when the item is explicitly selected.
                }
            }
            mediaItems.Add(mediaItem);
            var matching = existingChoices.Where(choice =>
                choice.ObjectKind == GenerationReferenceObjectKind.Asset && choice.LogicalObjectId == asset.Id).ToArray();
            if (matching.Length > 0)
            {
                foreach (var existing in matching)
                {
                    existing.UpdateAsset(asset, mediaItem.Thumbnail);
                    _referenceChoices.Add(existing);
                }
            }
            else
            {
                _referenceChoices.Add(new GenerationReferenceChoice(asset, _referenceChoices.Count, mediaItem.Thumbnail));
            }
        }

        foreach (var anchor in _workspace.Project.Anchors.Where(anchor => !anchor.IsArchived))
        {
            if (anchor.CurrentRevisionId is not { } revisionId) continue;
            var revision = _workspace.Project.AnchorRevisions.SingleOrDefault(candidate => candidate.Id == revisionId);
            if (revision is null) continue;
            var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == revision.SourceAssetId);
            mediaItems.Add(new ProjectMediaListItem(anchor, revision));
            var matching = existingChoices.Where(choice =>
                choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor && choice.LogicalObjectId == anchor.Id).ToArray();
            if (matching.Length > 0)
            {
                foreach (var existing in matching)
                {
                    existing.UpdateAnchor(anchor, revision, source?.EffectiveDisplayName);
                    _referenceChoices.Add(existing);
                }
            }
            else
            {
                _referenceChoices.Add(new GenerationReferenceChoice(
                    anchor,
                    revision,
                    source?.EffectiveDisplayName,
                    _referenceChoices.Count));
            }
        }


        foreach (var item in mediaItems.OrderBy(item => item.GroupOrder).ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            _assets.Add(item);

        foreach (var generation in _workspace.Project.Generations.OrderByDescending(item => item.RequestedAt))
            _generations.Add(generation);

        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} media items";
        RefreshEditWorkspaceState();
        var selection = selectedAssetId is { } id
            ? _assets.FirstOrDefault(item => item.Asset?.Id == id)
            : selectedAnchorId is { } anchorId
                ? _assets.FirstOrDefault(item => item.Anchor?.Id == anchorId)
                : null;
        var preserveActiveOperation = !hasExplicitSelection && _mediaPreparationMode != MediaPreparationMode.None;
        if (preserveActiveOperation) _suppressProjectMediaSelection = true;
        try
        {
            AssetsList.SelectedItem = selection;
        }
        finally
        {
            _suppressProjectMediaSelection = false;
        }
    }

    private string GetSelectedOutputFormat() =>
        (OutputFormatComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "mp4";

    private void SelectOutputFormat(string value)
    {
        OutputFormatComboBox.SelectedItem = OutputFormatComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            ?? OutputFormatComboBox.Items[0];
    }

    private static bool ReadDraftBoolean(
        GenerationDraft draft,
        string primaryName,
        string? fallbackName,
        bool defaultValue)
    {
        if (draft.ProviderParameters.TryGetValue(primaryName, out var value) && bool.TryParse(value, out var parsed))
            return parsed;
        if (fallbackName is not null &&
            draft.ProviderParameters.TryGetValue(fallbackName, out value) &&
            bool.TryParse(value, out parsed))
            return parsed;
        return defaultValue;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatGenerationOutcome(GenerationRecord generation)
    {
        var message = $"Remote: {generation.Status} • Ingestion: {generation.IngestionStatus}";
        if (!string.IsNullOrWhiteSpace(generation.ProviderJobId))
            message += $"\nJob: {generation.ProviderJobId}";
        if (generation.Error is not null)
            message += $"\n{generation.Error.Message}";
        if (generation.ResponseMetadata.GetValueOrDefault("localMonitoring") is { } monitoring)
            message += $"\nLocal monitoring: {monitoring}";
        return message;
    }

    private bool EnsureProjectOpen()
    {
        if (_workspace.Project is not null)
        {
            return true;
        }

        MessageBox.Show(this, "Create or open a project first.", "ReelForge", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private async Task RunUiActionAsync(string status, Func<Task> action)
    {
        StatusText.Text = status;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowError("Operation failed", exception);
        }
    }

    private void ShowError(string title, Exception exception)
    {
        StatusText.Text = exception.Message;
        InspectorText.Text = $"{title}\n\n{exception}";
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string FormatAssetInspector(
        ProjectAsset asset,
        MediaEncodingMetadata? realizedEncoding = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine(asset.FileName);
        builder.AppendLine($"ID: {asset.Id}");
        builder.AppendLine($"Type: {asset.MediaType}");
        builder.AppendLine($"Storage: {asset.StorageKind}");
        builder.AppendLine($"Created from: {asset.Origin}");
        builder.AppendLine($"Path: {asset.Physical?.RelativePath ?? "materialized on demand"}");
        if (asset.Physical is { } physical)
        {
            builder.AppendLine($"Availability: {physical.Availability}");
        }
        if (asset.Physical?.ContentIdentity is { } identity)
        {
            builder.AppendLine($"SHA-256: {identity.Sha256 ?? identity.Status.ToString()}");
        }
        builder.AppendLine($"Created: {asset.CreatedAt.LocalDateTime:g}");

        if (asset.DurationSeconds is not null)
        {
            builder.AppendLine($"Duration: {asset.DurationSeconds:0.###} seconds");
        }

        var encoding = realizedEncoding ?? asset.Encoding;
        if (encoding is null)
        {
            builder.AppendLine();
            builder.AppendLine("Encoding metadata unavailable. Install/configure ffprobe, then reselect the asset.");
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("CONTAINER");
        builder.AppendLine($"Format: {encoding.ContainerFormat ?? "—"}");
        builder.AppendLine($"Size: {FormatBytes(encoding.SizeBytes)}");
        builder.AppendLine($"Bit rate: {encoding.BitRate?.ToString("N0", CultureInfo.InvariantCulture) ?? "—"} bps");

        if (encoding.Video is { } video)
        {
            builder.AppendLine();
            builder.AppendLine("VIDEO");
            builder.AppendLine($"Codec: {video.Codec ?? "—"} / {video.CodecProfile ?? "—"}");
            builder.AppendLine($"Dimensions: {video.Width?.ToString(CultureInfo.InvariantCulture) ?? "—"} × {video.Height?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Pixel format: {video.PixelFormat ?? "—"}");
            builder.AppendLine($"Frame rate: {video.FrameRate ?? "—"}");
            builder.AppendLine($"Time base: {video.TimeBase ?? "—"}");
            builder.AppendLine($"Codec level: {video.CodecLevel?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        }

        if (encoding.Audio is { } audio)
        {
            builder.AppendLine();
            builder.AppendLine("AUDIO");
            builder.AppendLine($"Codec: {audio.Codec ?? "—"}");
            builder.AppendLine($"Sample rate: {audio.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "—"} Hz");
            builder.AppendLine($"Channels: {audio.Channels?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
            builder.AppendLine($"Layout: {audio.ChannelLayout ?? "—"}");
        }

        return builder.ToString();
    }

    private static string FormatGenerationInspector(GenerationRecord generation)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Generation {generation.Id}");
        builder.AppendLine($"Status: {generation.Status}");
        builder.AppendLine($"Output ingestion: {generation.IngestionStatus}");
        builder.AppendLine($"Provider: {generation.RequestSnapshot.ProviderId}");
        builder.AppendLine($"Model: {generation.RequestSnapshot.ModelVersion}");
        builder.AppendLine($"Provider job: {generation.ProviderJobId ?? "—"}");
        builder.AppendLine($"Requested: {generation.RequestedAt.LocalDateTime:g}");
        builder.AppendLine($"Completed: {generation.CompletedAt?.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) ?? "—"}");
        builder.AppendLine();
        builder.AppendLine("PROMPT");
        builder.AppendLine(generation.RequestSnapshot.Prompt);
        builder.AppendLine();
        builder.AppendLine("SETTINGS");
        builder.AppendLine($"Mode: {generation.RequestSnapshot.Mode}");
        builder.AppendLine($"Duration: {generation.RequestSnapshot.DurationSeconds}s");
        builder.AppendLine($"Aspect ratio: {generation.RequestSnapshot.AspectRatio}");
        builder.AppendLine($"Resolution: {generation.RequestSnapshot.Resolution}");
        builder.AppendLine($"References: {generation.RequestSnapshot.References.Count}");
        builder.AppendLine($"Lineage: {generation.RelationshipType?.ToString() ?? "root"}");
        builder.AppendLine($"Parent: {generation.ParentGenerationId?.ToString() ?? "—"}");
        builder.AppendLine($"Output assets: {generation.OutputAssetIds.Count}");

        foreach (var reference in generation.RequestSnapshot.References.OrderBy(item => item.Order))
        {
            builder.AppendLine(
                $"  [{reference.Order}] {reference.ObjectKind} {reference.LogicalObjectId} • {reference.Role?.ToString() ?? "general"}" +
                (string.IsNullOrWhiteSpace(reference.Label) ? string.Empty : $" • {reference.Label}"));
            if (generation.ReferenceMaterializations.TryGetValue(reference.ReferenceId, out var receipt))
            {
                builder.AppendLine($"      prepared bytes: {receipt.ProducedContentHash ?? "—"}");
                builder.AppendLine($"      preparation: {receipt.ProviderScope ?? "local"}");
            }
        }

        foreach (var pair in generation.ResponseMetadata)
        {
            builder.AppendLine($"{pair.Key}: {pair.Value}");
        }

        if (generation.Error is not null)
        {
            builder.AppendLine();
            builder.AppendLine("ERROR");
            builder.AppendLine(generation.Error.Message);
            builder.AppendLine(generation.Error.TechnicalDetails);
        }

        return builder.ToString();
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"hh\:mm\:ss") : time.ToString(@"mm\:ss");

    private static string FormatBytes(long? bytes)
    {
        if (bytes is null)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}

public sealed class FrameContactListItem
{
    public FrameContactListItem(VideoPresentationFrame frame, BitmapSource thumbnail)
    {
        Frame = frame;
        Thumbnail = thumbnail;
    }

    public VideoPresentationFrame Frame { get; }
    public BitmapSource Thumbnail { get; }
    public string TimestampText => TimeSpan.FromSeconds(Math.Max(0, Frame.TimestampSeconds))
        .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class SavedFrameListItem
{
    public SavedFrameListItem(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        BitmapSource? thumbnail,
        string? error)
    {
        Anchor = anchor;
        Revision = revision;
        Thumbnail = thumbnail;
        Error = error;
    }

    public FrameAnchor Anchor { get; }
    public FrameAnchorRevision Revision { get; }
    public BitmapSource? Thumbnail { get; }
    public string? Error { get; }
    public string DisplayLabel => Anchor.DisplayLabel ?? "Saved Frame";
    public string TimestampText => TimeSpan.FromSeconds(Math.Max(0, Revision.TimestampSeconds))
        .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class ProjectMediaListItem
{
    public ProjectMediaListItem(ProjectAsset asset) => Asset = asset;

    public ProjectMediaListItem(FrameAnchor anchor, FrameAnchorRevision revision)
    {
        Anchor = anchor;
        AnchorRevision = revision;
    }

    public ProjectAsset? Asset { get; }
    public FrameAnchor? Anchor { get; }
    public FrameAnchorRevision? AnchorRevision { get; }
    public BitmapSource? Thumbnail { get; set; }
    public string DisplayName => Anchor?.DisplayLabel ??
                                 (Asset!.StorageKind == AssetStorageKind.Physical
                                     ? Asset.FileName
                                     : Asset.EffectiveDisplayName);
    public string KindText => Anchor is not null ? "Saved Frame" : Asset!.StorageKind == AssetStorageKind.Virtual
        ? IsSavedClip ? "Saved Clip" : IsComposition ? "Working Composition" : $"Virtual {Asset.MediaType}"
        : Asset.MediaType.ToString();
    public string GroupName => Anchor is not null ? "SAVED FRAMES" : Asset!.StorageKind == AssetStorageKind.Virtual
        ? IsSavedClip ? "SAVED CLIPS" : IsComposition ? "COMPOSITIONS" : "VIRTUAL MEDIA"
        : Asset.MediaType switch
        {
            MediaType.Video => "VIDEOS",
            MediaType.Image => "IMAGES",
            MediaType.Audio => "AUDIO",
            _ => "MEDIA"
        };
    public int GroupOrder => GroupName switch
    {
        "VIDEOS" => 0,
        "IMAGES" => 1,
        "AUDIO" => 2,
        "SAVED FRAMES" => 3,
        "SAVED CLIPS" => 4,
        "COMPOSITIONS" => 5,
        _ => 6
    };
    public string Glyph => Anchor is not null ? "▣" : Asset!.StorageKind == AssetStorageKind.Virtual
        ? IsComposition ? "▤" : "✂"
        : Asset.MediaType switch
        {
            MediaType.Video => "▶",
            MediaType.Image => "▧",
            MediaType.Audio => "♪",
            _ => "•"
        };
    private bool IsSavedClip => Asset?.Virtual?.Kind == VirtualAssetKind.SavedClip;
    private bool IsComposition => Asset?.Virtual?.Kind == VirtualAssetKind.Composition;
}

public sealed class CompositionSegmentListItem
{
    public CompositionSegmentListItem(
        int index,
        CompositionSegment segment,
        ProjectAsset? source,
        double? durationSeconds)
    {
        Index = index;
        SegmentId = segment.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing source";
        var isExactRange = segment.Start.Kind != RecipeBoundaryKind.SourceStart ||
                           segment.End.Kind != RecipeBoundaryKind.SourceEnd;
        DetailText = source is null
            ? $"Source {segment.Source.AssetId:N} is unavailable"
            : source.StorageKind == AssetStorageKind.Virtual
                ? $"Saved Clip • {(isExactRange ? "exact range • " : string.Empty)}pinned recipe " +
                  (segment.Source.RecipeRevisionId?.ToString("N") ?? "missing")
                : $"Physical video • {(isExactRange ? "exact range" : "full source")}";
        AudioText = segment.AudioEnabled ? "Audio on" : "Audio muted";
        AudioEnabled = segment.AudioEnabled;
        DurationSeconds = durationSeconds;
        DurationText = DurationSeconds is > 0
            ? FormatDuration(DurationSeconds.Value)
            : "Duration unknown";
    }

    public int Index { get; }
    public Guid SegmentId { get; }
    public string PositionText => $"{Index + 1}.";
    public string DisplayName { get; }
    public string DetailText { get; }
    public string AudioText { get; }
    public bool AudioEnabled { get; }
    public double? DurationSeconds { get; }
    public string DurationText { get; }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        if (seconds < 10 || Math.Abs(seconds - Math.Round(seconds)) > 0.000_5)
            return time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}

public sealed class CompositionAudioClipListItem
{
    public CompositionAudioClipListItem(CompositionAudioClip clip, ProjectAsset? source)
    {
        AudioClipId = clip.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing audio source";
        TimelineStart = clip.TimelineStart;
        IsMuted = clip.IsMuted;
        GainDecibels = clip.GainDecibels;
        Pan = clip.Pan;
        FadeIn = clip.FadeIn;
        FadeOut = clip.FadeOut;
        MixText = (IsMuted
            ? "Muted"
            : $"Gain {(GainDecibels > 0 ? "+" : string.Empty)}{GainDecibels:0} dB") +
            (Math.Abs(Pan) > 0.000_001
                ? $" • {Math.Round(Math.Abs(Pan) * 100):0}% {(Pan < 0 ? "left" : "right")}"
                : string.Empty) +
            (FadeIn > TimeSpan.Zero || FadeOut > TimeSpan.Zero
                ? $" • Fade {FadeIn.TotalSeconds:0.###}s in / {FadeOut.TotalSeconds:0.###}s out"
                : string.Empty);
        DurationSeconds = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ??
                          source?.Virtual?.ExpectedMediaProperties?.DurationSeconds;
        DurationText = DurationSeconds is > 0
            ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(@"m\:ss", CultureInfo.InvariantCulture)
            : "Duration unknown";
    }

    public Guid AudioClipId { get; }
    public string DisplayName { get; }
    public TimeSpan TimelineStart { get; }
    public bool IsMuted { get; }
    public double GainDecibels { get; }
    public double Pan { get; }
    public TimeSpan FadeIn { get; }
    public TimeSpan FadeOut { get; }
    public string MixText { get; }
    public double? DurationSeconds { get; }
    public string DurationText { get; }
}

public sealed class GenerationReferenceChoice
{
    private static readonly IReadOnlyList<GenerationReferenceRole?> ReferenceRoles =
        Enum.GetValues<GenerationReferenceRole>().Cast<GenerationReferenceRole?>().Prepend(null).ToArray();
    private readonly IReadOnlyList<GenerationReferenceRole?> _availableRoles = ReferenceRoles;

    public GenerationReferenceChoice(ProjectAsset asset, int order, BitmapSource? thumbnail = null)
    {
        UpdateAsset(asset, thumbnail);
        Order = order;
    }

    public GenerationReferenceChoice(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        string? sourceDisplayName,
        int order)
    {
        UpdateAnchor(anchor, revision, sourceDisplayName);
        Order = order;
    }

    public Guid ReferenceId { get; set; } = Guid.NewGuid();
    public GenerationReferenceObjectKind ObjectKind { get; private set; }
    public Guid LogicalObjectId { get; private set; }
    public Guid? AnchorRevisionId { get; set; }
    public string DisplayName { get; private set; } = string.Empty;
    public MediaType MediaType { get; private set; }
    public string MediaTypeText { get; private set; } = string.Empty;
    public string Glyph { get; private set; } = "•";
    public BitmapSource? Thumbnail { get; private set; }
    public bool HasThumbnail => Thumbnail is not null;
    public IReadOnlyList<GenerationReferenceRole?> AvailableRoles => _availableRoles;
    public bool IsSelected { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }

    public void UpdateAsset(ProjectAsset asset, BitmapSource? thumbnail = null)
    {
        ObjectKind = GenerationReferenceObjectKind.Asset;
        LogicalObjectId = asset.Id;
        AnchorRevisionId = null;
        DisplayName = asset.EffectiveDisplayName;
        MediaType = asset.MediaType;
        MediaTypeText = asset.Virtual?.Kind == VirtualAssetKind.SavedClip
            ? "Saved Clip • Video"
            : asset.MediaType.ToString();
        Glyph = asset.Virtual?.Kind == VirtualAssetKind.SavedClip
            ? "✂"
            : asset.MediaType switch
            {
                MediaType.Video => "▶",
                MediaType.Image => "▧",
                MediaType.Audio => "♪",
                _ => "•"
            };
        if (thumbnail is not null) Thumbnail = thumbnail;
    }

    public void UpdateAnchor(FrameAnchor anchor, FrameAnchorRevision revision, string? sourceDisplayName)
    {
        ObjectKind = GenerationReferenceObjectKind.FrameAnchor;
        LogicalObjectId = anchor.Id;
        AnchorRevisionId = revision.Id;
        DisplayName = $"Saved Frame • {anchor.DisplayLabel ?? "Untitled"}" +
                      (string.IsNullOrWhiteSpace(sourceDisplayName) ? string.Empty : $" ({sourceDisplayName})");
        MediaType = MediaType.Image;
        MediaTypeText = "Saved Frame • Image";
        Glyph = "▣";
    }

    public void UpdateThumbnail(BitmapSource? thumbnail) => Thumbnail = thumbnail;

    public GenerationReferenceChoice Duplicate(int order)
    {
        var duplicate = (GenerationReferenceChoice)MemberwiseClone();
        duplicate.ReferenceId = Guid.NewGuid();
        duplicate.Order = order;
        duplicate.IsSelected = true;
        return duplicate;
    }
}

public sealed class GenerationJobListItem : INotifyPropertyChanged
{
    private TrackedGenerationJob _job;
    private string _elapsedText = "0:00";
    private string _undoSendRemainingText = "0s";

    public GenerationJobListItem(TrackedGenerationJob job)
    {
        _job = job;
        RefreshElapsed(DateTimeOffset.UtcNow);
    }

    public Guid GenerationId => _job.GenerationId;
    public string ProjectName => _job.ProjectName;
    public string ProviderAndModel => $"{_job.ProviderDisplayName} • {_job.ModelVersion}";
    public string StatusText => _job.Status.ToString();
    public string Message => _job.Message;
    public string ElapsedText => _elapsedText;
    public string UndoSendRemainingText => _undoSendRemainingText;
    public bool IsUndoSendPending => _job.IsAwaitingSubmission && _job.UndoSendExpiresAt.HasValue;
    public bool WasCancelledBeforeSubmission => _job.WasCancelledBeforeSubmission;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(TrackedGenerationJob job)
    {
        _job = job;
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProviderAndModel));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsUndoSendPending));
        OnPropertyChanged(nameof(WasCancelledBeforeSubmission));
        RefreshElapsed(DateTimeOffset.UtcNow);
    }

    public void RefreshElapsed(DateTimeOffset now)
    {
        if (IsUndoSendPending && _job.UndoSendExpiresAt is { } expiresAt)
        {
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((expiresAt - now).TotalSeconds));
            var remainingText = $"{remainingSeconds}s";
            if (remainingText != _undoSendRemainingText)
            {
                _undoSendRemainingText = remainingText;
                OnPropertyChanged(nameof(UndoSendRemainingText));
            }
            return;
        }

        var elapsedUntil = _job.CompletedAt ?? now;
        var elapsedFrom = _job.ProviderSubmittedAt ?? _job.RequestedAt;
        var elapsed = elapsedUntil > elapsedFrom ? elapsedUntil - elapsedFrom : TimeSpan.Zero;
        var text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        if (text == _elapsedText) return;
        _elapsedText = text;
        OnPropertyChanged(nameof(ElapsedText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
