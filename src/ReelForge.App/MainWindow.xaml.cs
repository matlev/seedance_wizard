using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App;

internal enum MediaPreparationMode { None, SelectFrame, MakeClip }

public partial class MainWindow : Window, IDisposable, IGenerationJobFinalizer
{
    private readonly ObservableCollection<ProjectMediaListItem> _assets = [];
    private readonly ObservableCollection<GenerationRecord> _generations = [];
    private readonly ObservableCollection<GenerationReferenceChoice> _referenceChoices = [];
    private readonly ObservableCollection<GenerationJobListItem> _jobs = [];
    private readonly ObservableCollection<FrameContactListItem> _contactFrames = [];
    private readonly ObservableCollection<SavedFrameListItem> _savedFrames = [];
    private readonly HashSet<Guid> _viewedTerminalJobIds = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _pendingSubmissionDelays = [];
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private readonly SemaphoreSlim _frameNavigationGate = new(1, 1);
    private IReadOnlyList<GenerationProviderChoice> _providerChoices = [];
    private readonly ProjectWorkspace _workspace;
    private readonly PortableProjectStore _projectStore;
    private readonly AssetImportService _assetImporter;
    private readonly ProjectAssetTransferService _assetTransferService;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private readonly ExactVideoFrameService _exactFrameService;
    private readonly RecipeMediaMaterializer _mediaMaterializer;
    private readonly IGeneratedOutputIngestionService _outputIngestion;
    private GenerationWorkflow _generationWorkflow = null!;
    private IProviderAssetPreparationService? _providerPreparation;
    private readonly ISecretStore _secretStore;
    private readonly FileApplicationDiagnosticLog _diagnosticLog;
    private readonly List<HttpClient> _providerHttpClients = [];
    private readonly HttpClient _r2HttpClient;
    private readonly HttpClient _downloadHttpClient;
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
    private readonly GenerationJobCoordinator _jobCoordinator;
    private bool _suppressDraftAutosave;
    private bool _suppressPromptSynchronization;
    private bool _isVideoPlaying;
    private bool _isVideoPreviewPriming;
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
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        _mediaToolDiscovery = new MediaToolDiscovery();
        _applicationSettingsStore = new JsonApplicationSettingsStore();
        _recentProjectTracker = new RecentProjectTracker(_applicationSettingsStore);
        _applicationSettings = LoadApplicationSettings();
        var configuredTools = _applicationSettings.MediaTools;
        _mediaTools = _mediaToolDiscovery.Discover(configuredTools.FfmpegPath, configuredTools.FfprobePath);
        var processRunner = new ExternalProcessRunner();
        _mediaInspector = new FfprobeMediaInspectionService(_mediaTools.FfprobePath, processRunner);
        var mediaCacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ReelForge",
            "Cache");
        _exactFrameService = new ExactVideoFrameService(
            _mediaTools.FfmpegPath,
            _mediaTools.FfprobePath,
            processRunner,
            mediaCacheRoot,
            maximumCacheBytes: configuredTools.CacheSizeBytes);
        _mediaMaterializer = new RecipeMediaMaterializer(
            _mediaTools.FfmpegPath,
            processRunner,
            _exactFrameService,
            mediaCacheRoot);
        _projectStore = new PortableProjectStore();
        _assetImporter = new AssetImportService(_mediaInspector);
        _workspace = new ProjectWorkspace(_projectStore, _assetImporter);
        _assetTransferService = new ProjectAssetTransferService(_projectStore, _assetImporter);
        _secretStore = new WindowsCredentialStore();
        _diagnosticLog = new FileApplicationDiagnosticLog(_applicationSettings.General.LogDirectory);
        _r2HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _outputIngestion = new HttpGeneratedOutputIngestionService(_downloadHttpClient, _mediaInspector);
        _temporaryAssetHost = new CloudflareR2TemporaryAssetHost(
            _applicationSettingsStore,
            _secretStore,
            new CloudflareR2ClientFactory(_r2HttpClient));
        _generationProvider = new FakeVideoGenerationProvider();
        RefreshProviderRuntime(preferredProviderId: null);
        _jobCoordinator = new GenerationJobCoordinator(
            new JsonGenerationJobStore(),
            ResolveAsyncProvider,
            this);
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
        _jobElapsedTimer.Tick += (_, _) => RefreshJobElapsedTimes();
        _jobElapsedTimer.Start();

        _frameBrowserDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _frameBrowserDebounceTimer.Tick += FrameBrowserDebounceTimer_Tick;

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
        _jobCoordinator.Stop();
        _jobElapsedTimer.Stop();
        _frameBrowserDebounceTimer.Stop();
        _frameBrowserCancellation?.Cancel();
        _frameBrowserCancellation?.Dispose();
        ReleaseActivePreviewLease();
        foreach (var pending in _pendingSubmissionDelays.Values) pending.Cancel();
        foreach (var pending in _pendingSubmissionDelays.Values) pending.Dispose();
        _pendingSubmissionDelays.Clear();
        foreach (var client in _providerHttpClients) client.Dispose();
        _r2HttpClient.Dispose();
        _downloadHttpClient.Dispose();
        _mediaMaterializer.Dispose();
        _exactFrameService.Dispose();
        if (_diagnosticLog is IDisposable disposableDiagnosticLog) disposableDiagnosticLog.Dispose();
        GC.SuppressFinalize(this);
    }

    private ApplicationSettings LoadApplicationSettings()
    {
        try
        {
            return _applicationSettingsStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return new ApplicationSettings();
        }
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

    private static Uri RequireHttpsBaseUri(string value, string providerName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{providerName} API base URL must be an absolute HTTPS URL.");
        return uri;
    }

    private void RefreshProviderRuntime(string? preferredProviderId)
    {
        var choices = new List<GenerationProviderChoice>();
        var preparationServices = new Dictionary<string, IProviderAssetPreparationService>(StringComparer.Ordinal);
        var fakeProvider = new FakeVideoGenerationProvider();
        choices.Add(new GenerationProviderChoice(fakeProvider));

        if (_applicationSettings.VideoGenerationProviders.BytePlus.Enabled)
        {
            var client = CreateProviderHttpClient(
                _applicationSettings.VideoGenerationProviders.BytePlus.ApiBaseUrl,
                "BytePlus");
            choices.Add(new GenerationProviderChoice(new BytePlusModelArkSeedance25Provider(
                client,
                _secretStore,
                new ProjectAssetReferenceResolver())));
            preparationServices[BytePlusModelArkSeedance25Provider.ProviderId] =
                new BytePlusModelArkAssetPreparationService(_temporaryAssetHost);
        }

        if (_applicationSettings.VideoGenerationProviders.AtlasCloud.Enabled)
        {
            var client = CreateProviderHttpClient(
                _applicationSettings.VideoGenerationProviders.AtlasCloud.ApiBaseUrl,
                "AtlasCloud");
            choices.Add(new GenerationProviderChoice(new AtlasCloudSeedance25Provider(
                client,
                _secretStore,
                new ProjectAssetReferenceResolver(),
                _diagnosticLog)));
            choices.Add(new GenerationProviderChoice(new AtlasCloudMiniMaxH3Provider(
                client,
                _secretStore,
                new ProjectAssetReferenceResolver(),
                _diagnosticLog)));
            var atlasCloudPreparation = new AtlasCloudAssetPreparationService(client, _secretStore, _diagnosticLog);
            preparationServices[AtlasCloudSeedance25Provider.ProviderId] = atlasCloudPreparation;
            preparationServices[AtlasCloudMiniMaxH3Provider.ProviderId] = atlasCloudPreparation;
        }

        _providerChoices = choices;
        _providerPreparation = preparationServices.Count == 0
            ? null
            : new ProviderAssetPreparationRouter(preparationServices);
        _generationWorkflow = CreateGenerationWorkflow(_workspace, _providerPreparation);

        var selected = choices.FirstOrDefault(choice =>
                           choice.Provider.Capabilities.ProviderId.Equals(preferredProviderId, StringComparison.Ordinal))
                       ?? choices[0];
        _generationProvider = selected.Provider;
        var suppressAutosave = _suppressDraftAutosave;
        _suppressDraftAutosave = true;
        try
        {
            ProviderComboBox.ItemsSource = null;
            ProviderComboBox.ItemsSource = choices;
            ProviderComboBox.SelectedItem = selected;
            ConfigureGenerationPanel();
        }
        finally
        {
            _suppressDraftAutosave = suppressAutosave;
        }
    }

    private HttpClient CreateProviderHttpClient(string baseUrl, string providerName)
    {
        var client = new HttpClient
        {
            BaseAddress = RequireHttpsBaseUri(baseUrl, providerName),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _providerHttpClients.Add(client);
        return client;
    }

    private GenerationWorkflow CreateGenerationWorkflow(
        ProjectWorkspace workspace,
        IProviderAssetPreparationService? providerPreparation) =>
        new(
            workspace,
            _mediaMaterializer,
            _outputIngestion,
            providerPreparation);

    private IAsyncVideoGenerationProvider? ResolveAsyncProvider(string providerId) =>
        _providerChoices.FirstOrDefault(choice =>
            choice.Provider.Capabilities.ProviderId.Equals(providerId, StringComparison.Ordinal))?.Provider
        as IAsyncVideoGenerationProvider;

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

    public Task FinalizeAsync(TrackedGenerationJob job, CancellationToken cancellationToken = default)
    {
        if (Dispatcher.CheckAccess()) return FinalizeJobOnUiAsync(job, cancellationToken);
        return Dispatcher.InvokeAsync(() => FinalizeJobOnUiAsync(job, cancellationToken)).Task.Unwrap();
    }

    private async Task FinalizeJobOnUiAsync(TrackedGenerationJob job, CancellationToken cancellationToken)
    {
        var activeLocation = _workspace.Location;
        var isActiveProject = activeLocation is not null &&
                              Path.GetFullPath(activeLocation.ProjectFilePath)
                                  .Equals(Path.GetFullPath(job.ProjectFilePath), StringComparison.OrdinalIgnoreCase);

        VideoProject project;
        ProjectLocation location;
        if (isActiveProject)
        {
            project = _workspace.Project
                ?? throw new InvalidOperationException("The active project could not be loaded for job completion.");
            location = activeLocation!;
        }
        else
        {
            (project, location) = await _projectStore.OpenAsync(job.ProjectFilePath, cancellationToken);
        }

        var generation = project.Generations.SingleOrDefault(candidate => candidate.Id == job.GenerationId)
            ?? throw new InvalidOperationException("The generation record no longer exists in its project.");
        generation.Status = job.Status;
        generation.Error = job.Error;
        foreach (var pair in job.ResponseMetadata) generation.ResponseMetadata[pair.Key] = pair.Value;
        generation.ResponseMetadata["localMonitoring"] = "application-job-coordinator";

        if (job.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
        {
            generation.CompletedAt = DateTimeOffset.UtcNow;
            await _projectStore.SaveAsync(project, location, CancellationToken.None);
        }
        else if (job.Status == GenerationStatus.Succeeded &&
                 generation.IngestionStatus != OutputIngestionStatus.Succeeded)
        {
            generation.CompletedAt = DateTimeOffset.UtcNow;
            generation.IngestionStatus = OutputIngestionStatus.Running;
            await _projectStore.SaveAsync(project, location, CancellationToken.None);
            try
            {
                var assets = await _outputIngestion
                    .IngestAsync(location, generation.Id, job.Outputs, cancellationToken);
                foreach (var asset in assets)
                {
                    project.AddAsset(asset);
                    generation.OutputAssetIds.Add(asset.Id);
                }
                generation.IngestionStatus = OutputIngestionStatus.Succeeded;
                generation.Error = null;
                await _projectStore.SaveAsync(project, location, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                generation.IngestionStatus = OutputIngestionStatus.Failed;
                generation.Error = new GenerationError
                {
                    ProviderCode = "local_ingestion_failed",
                    Message = exception.Message,
                    TechnicalDetails = exception.ToString()
                };
                await _projectStore.SaveAsync(project, location, CancellationToken.None);
                throw;
            }
        }

        if (isActiveProject)
        {
            RefreshProjectCollections();
            TryAutoPreviewGeneratedOutput(generation, owningProjectIsOpen: true);
            StatusText.Text = job.Status == GenerationStatus.Succeeded
                ? "Generated output added as durable project media."
                : $"Generation finished with status {job.Status}.";
        }
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
        _applicationSettings = await _applicationSettingsStore.LoadAsync();
        _mediaTools = _mediaToolDiscovery.Discover(
            _applicationSettings.MediaTools.FfmpegPath,
            _applicationSettings.MediaTools.FfprobePath);
        _mediaInspector.UpdateExecutablePath(_mediaTools.FfprobePath);
        _exactFrameService.UpdateExecutablePaths(_mediaTools.FfmpegPath, _mediaTools.FfprobePath);
        _mediaMaterializer.UpdateExecutablePath(_mediaTools.FfmpegPath);
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
        if (!string.IsNullOrWhiteSpace(_applicationSettings.General.ProjectsRoot))
            return Environment.ExpandEnvironmentVariables(_applicationSettings.General.ProjectsRoot);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        return Path.Combine(documents, "ReelForge", "Projects");
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenProjectDialog(GetDefaultProjectsDirectory()) { Owner = this };
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

    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(
            "Saving project…",
            async () =>
            {
                await _workspace.SaveAsync();
                StatusText.Text = "Project saved.";
            });
    }

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

        await RunUiActionAsync(
            $"Importing {dialog.FileNames.Length} asset(s)…",
            async () =>
            {
                var imported = await _workspace.ImportAssetsAsync(dialog.FileNames);
                RefreshProjectCollections();
                StatusText.Text = $"Imported {imported.Count} asset(s).";
            });
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
        if (!hasComposition) return;
        WorkingCompositionNameText.Text = composition!.EffectiveDisplayName;
        var revision = _workspace.Project!.RecipeRevisions.Single(candidate =>
            candidate.Id == composition.Virtual!.CurrentRecipeRevisionId);
        var recipe = (CompositionRecipe)revision.Recipe;
        WorkingCompositionSummaryText.Text =
            $"{recipe.Segments.Count} exact, revision-pinned segment{(recipe.Segments.Count == 1 ? string.Empty : "s")} • recipe revision {revision.RevisionNumber}";
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

    private async Task ShowSavedFramePreviewAsync(ProjectMediaListItem item, Guid? selectedProjectId)
    {
        if (_workspace.Project is null || _workspace.Location is null ||
            item.Anchor is not { } anchor || item.AnchorRevision is not { } revision) return;

        await RunUiActionAsync($"Loading {item.DisplayName}…", async () =>
        {
            try
            {
                await using var media = await new PhysicalAssetMaterializer(exactFrameService: _exactFrameService)
                    .MaterializeAsync(
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
        if (GetSelectedAsset() is not { } asset || _workspace.Project is null || _workspace.Location is null) return;
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
        if (GetSelectedAsset() is not { } asset || _workspace.Project is null || _workspace.Location is null) return;
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
        var dialog = new OpenProjectDialog(GetDefaultProjectsDirectory()) { Owner = this };
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
        CompositionRecipe composition => composition.Segments.Any(segment => segment.Source.AssetId == assetId),
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
                    submissionWorkspace = new ProjectWorkspace(_projectStore, _assetImporter);
                    await submissionWorkspace.OpenAsync(projectLocation.ProjectFilePath);
                    submissionWorkflow = CreateGenerationWorkflow(submissionWorkspace, providerPreparation);
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
        SaveProjectButton.IsEnabled = isEnabled;
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

        VideoPreview.Source = new Uri(absolutePath, UriKind.Absolute);
        VideoPreview.Visibility = Visibility.Visible;
        PlaybackControlsBorder.Visibility = Visibility.Visible;
        PlaybackButton.IsEnabled = false;
    }

    private async Task ShowVirtualAssetPreviewAsync(
        ProjectAsset asset,
        ProjectMediaListItem selectedItem,
        Guid? selectedProjectId)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
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

                ClearMediaPreview();
                _activePreviewLease = lease;
                lease = null;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                VideoPreview.Source = new Uri(_activePreviewLease.Path, UriKind.Absolute);
                VideoPreview.Visibility = Visibility.Visible;
                PlaybackControlsBorder.Visibility = Visibility.Visible;
                PlaybackButton.IsEnabled = false;
                StatusText.Text = $"Selected Saved Clip {asset.EffectiveDisplayName}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_workspace.Project?.Id != selectedProjectId) return;
                ClearMediaPreview();
                PreviewPlaceholder.Text = $"Saved Clip preview unavailable\n\n{exception.Message}";
                PreviewPlaceholder.Visibility = Visibility.Visible;
                StatusText.Text = $"Could not prepare {asset.EffectiveDisplayName}.";
            }
            finally
            {
                if (lease is not null) await lease.DisposeAsync();
            }
        });
    }

    private void ClearMediaPreview()
    {
        _isVideoPreviewPriming = false;
        VideoPreview.Stop();
        VideoPreview.Close();
        SetPlaybackState(false);
        _isScrubbing = false;
        _resumePlaybackAfterScrub = false;
        if (Mouse.Captured == PositionSlider) Mouse.Capture(null);
        VideoPreview.Source = null;
        ReleaseActivePreviewLease();
        VideoPreview.Visibility = Visibility.Collapsed;
        PlaybackControlsBorder.Visibility = Visibility.Collapsed;
        PlaybackButton.IsEnabled = false;
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Text = "Select a video or image asset to preview";
        PreviewPlaceholder.TextAlignment = TextAlignment.Center;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PositionSlider.Maximum = 1;
        PositionSlider.Value = 0;
        TimeText.Text = "00:00 / 00:00";
    }

    private void ReleaseActivePreviewLease()
    {
        var lease = Interlocked.Exchange(ref _activePreviewLease, null);
        if (lease is not null) lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = VideoPreview.NaturalDuration.TimeSpan.TotalSeconds;
        }

        var openedSource = VideoPreview.Source;
        _isVideoPreviewPriming = true;
        PlaybackButton.IsEnabled = false;
        var wasMuted = VideoPreview.IsMuted;
        VideoPreview.IsMuted = true;
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Play();
        await Task.Delay(100);
        if (VideoPreview.Source != openedSource) return;

        VideoPreview.Stop();
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.IsMuted = wasMuted;
        _isVideoPreviewPriming = false;
        PlaybackButton.IsEnabled = true;
        SetPlaybackState(false);
        UpdatePlaybackPosition();
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_isVideoPreviewPriming) return;
        VideoPreview.Stop();
        VideoPreview.Position = TimeSpan.Zero;
        SetPlaybackState(false);
        UpdatePlaybackPosition();
    }

    private void Playback_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Source is null) return;
        if (_isVideoPlaying)
        {
            VideoPreview.Pause();
            SetPlaybackState(false);
            return;
        }

        VideoPreview.Play();
        SetPlaybackState(true);
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (VideoPreview.Source is null) return;
        _resumePlaybackAfterScrub = _isVideoPlaying;
        _isScrubbing = true;
        VideoPreview.Pause();
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

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (VideoPreview.Source is null || !_isScrubbing) return;
        UpdateScrubPosition(e);
        SeekPreview(PositionSlider.Value);
        _isScrubbing = false;
        if (Mouse.Captured == PositionSlider) Mouse.Capture(null);
        if (_resumePlaybackAfterScrub)
        {
            VideoPreview.Play();
            SetPlaybackState(true);
        }
        else
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
        VideoPreview.Position = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, PositionSlider.Maximum));
        TimeText.Text = $"{FormatTime(VideoPreview.Position)} / {FormatTime(VideoPreview.NaturalDuration.HasTimeSpan ? VideoPreview.NaturalDuration.TimeSpan : TimeSpan.Zero)}";
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
        VideoPreview.IsMuted = e.NewValue <= 0;
        MuteButton.Content = VideoPreview.IsMuted ? "Unmute" : "Mute";
        if (e.NewValue > 0) _volumeBeforeMute = e.NewValue;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.IsMuted || VolumeSlider.Value <= 0)
        {
            VolumeSlider.Value = _volumeBeforeMute > 0 ? _volumeBeforeMute : 1;
            VideoPreview.IsMuted = false;
            MuteButton.Content = "Mute";
            return;
        }

        _volumeBeforeMute = VolumeSlider.Value;
        VideoPreview.IsMuted = true;
        MuteButton.Content = "Unmute";
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

        var current = VideoPreview.Position;
        var duration = VideoPreview.NaturalDuration.HasTimeSpan
            ? VideoPreview.NaturalDuration.TimeSpan
            : TimeSpan.Zero;

        if (!_isScrubbing) PositionSlider.Value = current.TotalSeconds;
        TimeText.Text = $"{FormatTime(current)} / {FormatTime(duration)}";
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

    private static string FormatAssetInspector(ProjectAsset asset)
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

        var encoding = asset.Encoding;
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

public sealed class GenerationProviderChoice
{
    public GenerationProviderChoice(IVideoGenerationProvider provider)
    {
        Provider = provider;
    }

    public IVideoGenerationProvider Provider { get; }
    public string DisplayName => Provider.Capabilities.DisplayName;
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
