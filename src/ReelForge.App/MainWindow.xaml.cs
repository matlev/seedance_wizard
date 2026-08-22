using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ReelForge.App.Bootstrap;
using ReelForge.App.Views.Dialogs;
using ReelForge.App.Views.Editing;
using ReelForge.App.Views.Generation;
using ReelForge.App.Views.Inspector;
using ReelForge.App.Views.Jobs;
using ReelForge.App.Views.MediaPreparation;
using ReelForge.App.Views.MediaPreview;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.App.Views.Projects;
using ReelForge.App.Views.Settings;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly ObservableCollection<ProjectMediaListItem> _assets = [];
    private readonly ObservableCollection<GenerationReferenceChoice> _referenceChoices = [];
    private readonly ObservableCollection<CompositionSegmentListItem> _compositionSegments = [];
    private readonly ObservableCollection<CompositionAudioClipListItem> _compositionAudioClips = [];
    private readonly ApplicationRuntime _runtime;
    private readonly Dictionary<Guid, CancellationTokenSource> _pendingSubmissionDelays = [];
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private readonly SemaphoreSlim _frameNavigationGate = new(1, 1);
    private IReadOnlyList<GenerationProviderChoice> _providerChoices = [];
    private readonly ProjectWorkspace _workspace;
    private readonly PortableProjectStore _projectStore;
    private readonly ProjectMediaOperationsCoordinator _projectMediaOperationsCoordinator;
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
    private readonly ProjectLifecycleDialogs _projectDialogs;
    private readonly ITemporaryAssetHost _temporaryAssetHost;
    private ApplicationSettings _applicationSettings;
    private MediaToolAvailability _mediaTools;
    private readonly DispatcherTimer _draftAutosaveTimer;
    private readonly FramePreparationCoordinator _framePreparationCoordinator;
    private readonly GenerationJobCoordinator _jobCoordinator;
    private bool _suppressDraftAutosave;
    private bool _suppressProjectMediaSelection;
    private ProjectWorkspaceKind _activeWorkspace = ProjectWorkspaceKind.Generate;
    private bool _restoringProjectUiState;
    private CancellationTokenSource? _compositionRenderCancellation;
    private readonly CompositionAuditionController _compositionAuditionController;
    private Guid? _activeCompositionPreviewRevisionId;
    private double? _pendingCompositionTimelineSeekSeconds;
    private long _compositionTimelineSeekGeneration;
    private long? _activeCompositionTimelineSeekGeneration;
    private long? _activeMediaPreviewScrubGeneration;
    private double _activeCompositionTimelineSeekSeconds;
    private Guid? _selectedCompositionSegmentId;
    private Guid? _selectedCompositionAudioClipId;
    private bool _disposed;
    private bool _isMediaImportInProgress;

    public MainWindow()
    {
        InitializeComponent();

        _runtime = ApplicationRuntime.Create();
        _mediaToolDiscovery = _runtime.MediaToolDiscovery;
        _applicationSettingsStore = _runtime.ApplicationSettingsStore;
        _recentProjectTracker = _runtime.RecentProjectTracker;
        _projectDialogs = new ProjectLifecycleDialogs(this, _runtime.Paths);
        _applicationSettings = _runtime.Settings;
        _mediaTools = _runtime.MediaTools;
        _mediaInspector = _runtime.MediaInspector;
        _exactFrameService = _runtime.ExactFrameService;
        _mediaMaterializer = _runtime.MediaMaterializer;
        _audioExtractionEngine = _runtime.AudioExtractionEngine;
        _projectStore = _runtime.ProjectStore;
        _workspace = _runtime.Workspace;
        _projectMediaOperationsCoordinator = new ProjectMediaOperationsCoordinator(
            _workspace,
            _runtime.RenderedAssetPromotionService,
            _runtime.AudioExtractionService,
            _runtime.ProjectAssetDependencyAnalyzer,
            _runtime.PhysicalAssetRemovalService,
            _runtime.ProjectAssetTransferWorkflow);
        _framePreparationCoordinator = new FramePreparationCoordinator(
            _workspace,
            _exactFrameService,
            MediaPreparationPanelControl,
            MediaPreviewPanelControl,
            _frameNavigationGate);
        _framePreparationCoordinator.StatusChanged += FramePreparation_StatusChanged;
        _framePreparationCoordinator.SavedFramesProjected += FramePreparation_SavedFramesProjected;
        _compositionAuditionController = new CompositionAuditionController(
            _workspace,
            _mediaMaterializer,
            MediaPreviewPanelControl);
        _compositionAuditionController.PositionChanged += CompositionAudition_PositionChanged;
        _secretStore = _runtime.SecretStore;
        _diagnosticLog = _runtime.DiagnosticLog;
        _temporaryAssetHost = _runtime.TemporaryAssetHost;
        _generationProvider = new FakeVideoGenerationProvider();
        RefreshProviderRuntime(preferredProviderId: null);
        _jobCoordinator = _runtime.JobCoordinator;
        _runtime.JobFinalizer.Finalized += JobFinalizer_Finalized;
        JobsPanelControl.Initialize(_jobCoordinator);
        JobsChromeControl.Initialize(_jobCoordinator);

        ProjectMediaPanelControl.SetItemsSource(_assets);
        GenerationPanelControl.SetReferences(_referenceChoices);
        _draftAutosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _draftAutosaveTimer.Tick += DraftAutosaveTimer_Tick;

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
        _runtime.JobFinalizer.Finalized -= JobFinalizer_Finalized;
        JobsPanelControl.Dispose();
        JobsChromeControl.Dispose();
        _framePreparationCoordinator.StatusChanged -= FramePreparation_StatusChanged;
        _framePreparationCoordinator.SavedFramesProjected -= FramePreparation_SavedFramesProjected;
        _framePreparationCoordinator.Dispose();
        _compositionRenderCancellation?.Cancel();
        _compositionAuditionController.PositionChanged -= CompositionAudition_PositionChanged;
        _compositionAuditionController.Dispose();
        CompositionTimelineControl.Dispose();
        MediaPreviewPanelControl.Dispose();
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
            JobsPanelControl.Refresh();
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
            InspectorPanelControl.Text = $"Automatic project reopen failed\n\n{exception}";
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
            GenerationPanelControl.SetProviders(providerRuntime.Choices, selected);
            GenerationPanelControl.ConfigureProvider(_generationProvider);
        }
        finally
        {
            _suppressDraftAutosave = suppressAutosave;
        }
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
        if (JobsPanelControl.IsOpen)
        {
            await JobsPanelControl.HideJobsAsync();
            JobsChromeControl.SetJobsOpen(false);
        }
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
        GenerationHistoryPanelControl.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        GenerationPanelSplitter.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        GenerationHistoryRow.MinHeight = isGenerate ? 80 : 0;
        GenerationHistoryRow.Height = isGenerate ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        GenerationSplitterRow.Height = isGenerate ? new GridLength(5) : new GridLength(0);
        ProjectMediaRow.Height = isGenerate ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star);
        GenerateTab.Visibility = isGenerate ? Visibility.Visible : Visibility.Collapsed;
        EditToolsTab.Visibility = isGenerate ? Visibility.Collapsed : Visibility.Visible;
        if (isGenerate && RightPanelTabs.SelectedItem == EditToolsTab) RightPanelTabs.SelectedItem = GenerateTab;
        if (!isGenerate) RightPanelTabs.SelectedItem = EditToolsTab;
        ExpandedPromptEditorControl.CloseEditor(notify: false);
        RefreshEditWorkspaceState();
        if (!isGenerate && _workspace.Project?.WorkingCompositionAssetId is { } compositionId)
        {
            var item = _assets.FirstOrDefault(candidate => candidate.Asset?.Id == compositionId);
            if (item is not null && ProjectMediaPanelControl.SelectedItem != item)
                ProjectMediaPanelControl.SelectedItem = item;
        }
    }

    private async void JobsChromeControl_OpenRequested(object? sender, EventArgs e)
    {
        if (JobsPanelControl.IsOpen)
        {
            await JobsPanelControl.HideJobsAsync();
            JobsChromeControl.SetJobsOpen(false);
            return;
        }

        JobsPanelControl.ShowJobs();
        JobsChromeControl.SetJobsOpen(true);
    }

    private void JobsPanelControl_Closed(object? sender, EventArgs e) =>
        JobsChromeControl.SetJobsOpen(false);

    private void JobsPanelControl_ErrorOccurred(object? sender, GenerationJobsPanelErrorEventArgs e) =>
        StatusText.Text = e.Message;

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
                ProjectMediaPanelControl.SelectedItem = _assets.FirstOrDefault(item =>
                    kind == "asset" ? item.Asset?.Id == mediaId : item.Anchor?.Id == mediaId);
        }
        finally
        {
            _restoringProjectUiState = false;
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
        _applicationSettings = await _runtime.ReloadAndApplySettingsAsync();
        _mediaTools = _runtime.MediaTools;
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

    private void GenerationPanel_ProviderChanged(object? sender, GenerationProviderChangedEventArgs e)
    {
        _generationProvider = e.Choice.Provider;
        _suppressDraftAutosave = true;
        try
        {
            GenerationPanelControl.ConfigureProvider(_generationProvider);
        }
        finally
        {
            _suppressDraftAutosave = false;
        }

        ScheduleDraftAutosave();
    }

    private void GenerationPanel_DraftChanged(object? sender, EventArgs e)
    {
        if (ExpandedPromptEditorControl.IsOpen)
            ExpandedPromptEditorControl.UpdatePrompt(GenerationPanelControl.Prompt);
        ScheduleDraftAutosave();
    }

    private void ExpandedPromptEditor_PromptChanged(object? sender, PromptTextChangedEventArgs e) =>
        GenerationPanelControl.Prompt = e.Prompt;

    private void GenerationPanel_ExpandPromptRequested(object? sender, EventArgs e) =>
        ExpandedPromptEditorControl.Open(GenerationPanelControl.Prompt);

    private void ExpandedPromptEditor_Closed(object? sender, EventArgs e) =>
        GenerationPanelControl.FocusPromptAtEnd();

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
            GenerationPanelControl.Status = "Draft autosaved.";
        }
        catch (Exception exception)
        {
            GenerationPanelControl.Status = $"Draft autosave failed: {exception.Message}";
        }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        var selection = _projectDialogs.SelectNewProject(_applicationSettings);
        if (selection is null) return;

        await RunUiActionAsync(
            "Creating project…",
            async () =>
            {
                await _workspace.CreateAsync(selection.ProjectDirectory, selection.ProjectName);
                RefreshProjectUi();
                await RememberCurrentProjectAsync();
            });
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var projectFilePath = _projectDialogs.SelectProjectToOpen(_applicationSettings);
        if (projectFilePath is null) return;

        await RunUiActionAsync(
            "Opening project…",
            async () =>
            {
                await _workspace.OpenAsync(projectFilePath);
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

    private async void ImportAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen()) return;
        var fileNames = _projectDialogs.SelectMediaToImport();
        if (fileNames.Count > 0) await ImportMediaFilesAsync(fileNames);
    }

    private void MainWindow_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragData.Format)) return;
        UpdateMediaDropFeedback(e);
    }

    private void MainWindow_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragData.Format)) return;
        UpdateMediaDropFeedback(e);
    }

    private void MainWindow_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragData.Format)) return;
        HideMediaDropOverlay();
        e.Handled = true;
    }

    private async void MainWindow_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(ProjectMediaDragData.Format)) return;
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
                    var imported = await _projectMediaOperationsCoordinator.ImportAsync(filePaths);
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

    private async void ProjectMediaPanel_SelectedItemChanged(
        object? sender,
        ProjectMediaSelectionChangedEventArgs e)
    {
        if (_suppressProjectMediaSelection) return;
        if (e.SelectedItem is not { } item)
        {
            return;
        }

        var selectedProjectId = _workspace.Project?.Id;
        GenerationHistoryPanelControl.ClearSelection();
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
            InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
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
                    InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
                    ShowAssetPreview(asset);
                    MediaPreparationPanelControl.SetWorkspaceStatus("Source media is missing");
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
                InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
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
        MediaPreparationPanelControl.ConfigureSelection(asset.EffectiveDisplayName, canPrepare);
        StartEditButton.IsEnabled = _workspace.Project?.WorkingCompositionAssetId is null &&
                                    asset.MediaType == MediaType.Video &&
                                    (asset.StorageKind == AssetStorageKind.Physical ||
                                     asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
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
            CompositionTimelineControl.Clear();
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
        UpdateCompositionTimelineControl();
        UpdateCompositionActionState();
        if (_compositionAuditionController.RecipeRevisionId is { } draftRevisionId && draftRevisionId != revision.Id &&
            ProjectMediaPanelControl.SelectedItem is ProjectMediaListItem selectedItem &&
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
            !CompositionTimelineControl.TryGetSegmentSpan(segmentId, out var span))
            return;
        var playbackSeconds = GetCurrentTimelinePlaybackSeconds();
        var offset = playbackSeconds - span.StartSeconds;
        var boundaryEdge = _applicationSettings.MediaTools.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame
            ? AnchorBoundaryEdge.AfterFrame
            : AnchorBoundaryEdge.BeforeFrame;
        MediaPreviewPanelControl.Pause();
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

        MediaPreviewPanelControl.Pause();
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

    private async void EditToolsPanel_SegmentAudioChanged(object? sender, BooleanValueEventArgs e)
    {
        if (GetSelectedCompositionSegment() is not { } selected) return;
        var audioEnabled = e.Value;
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

    private async void EditToolsPanel_AudioClipMutedChanged(object? sender, BooleanValueEventArgs e)
    {
        if (GetSelectedCompositionAudioClip() is not { } selected) return;
        var isMuted = e.Value;
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

    private async void EditToolsPanel_AudioClipGainCommitted(object? sender, DoubleValueEventArgs e)
    {
        if (GetSelectedCompositionAudioClip() is not { } selected) return;
        var gainDecibels = e.Value;
        if (Math.Abs(selected.GainDecibels - gainDecibels) < 0.000_001) return;

        await RunUiActionAsync("Updating composition audio gain…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetAudioClipMixAsync(selected.AudioClipId, selected.IsMuted, gainDecibels);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Set {selected.DisplayName} gain to {EditToolsPanel.FormatGainDecibels(gainDecibels)}. Preview the composition to rebuild it.";
        });
    }

    private async void EditToolsPanel_AudioClipPanCommitted(object? sender, DoubleValueEventArgs e)
    {
        if (GetSelectedCompositionAudioClip() is not { } selected) return;
        var pan = e.Value;
        if (Math.Abs(selected.Pan - pan) < 0.000_001) return;

        await RunUiActionAsync("Updating composition audio pan…", async () =>
        {
            await new WorkingCompositionService(_workspace).SetAudioClipPanAsync(selected.AudioClipId, pan);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Set {selected.DisplayName} pan to {EditToolsPanel.FormatAudioPan(pan)}. Preview the composition to rebuild it.";
        });
    }

    private async void EditToolsPanel_AudioClipFadesCommitted(object? sender, AudioFadesEventArgs e)
    {
        if (GetSelectedCompositionAudioClip() is not { } selected) return;
        var fadeIn = e.FadeIn;
        var fadeOut = e.FadeOut;
        if (selected.FadeIn == fadeIn && selected.FadeOut == fadeOut) return;

        await RunUiActionAsync("Updating composition audio fades…", async () =>
        {
            await new WorkingCompositionService(_workspace)
                .SetAudioClipFadesAsync(selected.AudioClipId, fadeIn, fadeOut);
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = selected.AudioClipId;
            RefreshEditWorkspaceState();
            StatusText.Text =
                $"Set {selected.DisplayName} fades to {EditToolsPanel.FormatFadeDuration(fadeIn.TotalSeconds)} in / " +
                $"{EditToolsPanel.FormatFadeDuration(fadeOut.TotalSeconds)} out. Preview the composition to rebuild it.";
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
                MediaPreviewPanelControl.HidePlaceholder();
                MediaPreviewPanelControl.OpenLeasedVideo(lease, requiresWarmup: true);
                lease = null;
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
                var path = await _projectMediaOperationsCoordinator.ExportVirtualVideoAsync(
                    composition,
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
        var previewWasHitTestVisible = MediaPreviewPanelControl.IsHitTestVisible;
        var timelineWasHitTestVisible = CompositionTimelineControl.IsHitTestVisible;
        IDisposable? auditionQuiescence = null;
        try
        {
            MediaPreviewPanelControl.IsHitTestVisible = false;
            CompositionTimelineControl.IsHitTestVisible = false;
            auditionQuiescence = await _compositionAuditionController.PauseAndQuiesceAsync(cancellation.Token);
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
            auditionQuiescence?.Dispose();
            MediaPreviewPanelControl.IsHitTestVisible = previewWasHitTestVisible;
            CompositionTimelineControl.IsHitTestVisible = timelineWasHitTestVisible;
            if (ReferenceEquals(_compositionRenderCancellation, cancellation))
                _compositionRenderCancellation = null;
            CompositionRenderIndicator.Visibility = Visibility.Collapsed;
            CancelCompositionRenderButton.Visibility = Visibility.Collapsed;
            UpdateCompositionActionState();
        }
    }

    private bool CanDetachCompositionSegmentAudio(Guid segmentId)
    {
        if (_workspace.Project?.WorkingCompositionAssetId is null)
        {
            return false;
        }

        var (_, _, recipe) = new WorkingCompositionService(_workspace).GetCurrent();
        var segment = recipe.Segments.SingleOrDefault(candidate => candidate.Id == segmentId);
        if (segment is null)
        {
            return false;
        }

        var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId);
        var encoding = source?.Encoding ?? source?.Virtual?.ExpectedMediaProperties;
        if (encoding is not null && encoding.Audio is null)
        {
            return false;
        }

        return !recipe.AudioClips.Any(clip =>
            _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == clip.Source.AssetId)?.Provenance is
            { Operation: "detach-segment-audio" } provenance &&
            provenance.Parameters.GetValueOrDefault("compositionSegmentId") == segmentId.ToString("D"));
    }

    private string GetCompositionSplitActionLabel() =>
        _applicationSettings.MediaTools.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame
            ? "Split after playhead frame"
            : "Split before playhead frame";

    private double GetCurrentTimelinePlaybackSeconds() =>
        _compositionAuditionController.GetCurrentTimelinePosition(MediaPreviewPanelControl.PositionSeconds);

    private bool IsWorkingCompositionSelected() =>
        _workspace.Project?.WorkingCompositionAssetId is { } compositionId &&
        ProjectMediaPanelControl.SelectedItem is ProjectMediaListItem { Asset: { } selected } &&
        selected.Id == compositionId;

    private void SelectWorkingCompositionInProjectMedia()
    {
        if (_workspace.Project?.WorkingCompositionAssetId is not { } compositionId)
        {
            return;
        }

        var item = _assets.FirstOrDefault(candidate => candidate.Asset?.Id == compositionId);
        if (item is not null && ProjectMediaPanelControl.SelectedItem != item)
        {
            ProjectMediaPanelControl.SelectedItem = item;
        }
    }

    private void SeekCompositionTimeline(double seconds)
    {
        if (!MediaPreviewPanelControl.HasVideoSource)
        {
            return;
        }

        if (_compositionAuditionController.IsActive)
        {
            _compositionAuditionController.QueueSeek(seconds);
        }
        else
        {
            SeekPreview(seconds);
        }

        MediaPreviewPanelControl.ClearEndedState();
        MediaPreviewPanelControl.SetPosition(seconds);
        UpdateCompositionTimelinePlayback(seconds);
    }

    private async Task CompleteCompositionTimelineScrubAsync(bool resumePlayback)
    {
        if (_compositionAuditionController.IsActive)
        {
            await _compositionAuditionController.CommitQueuedSeekAsync(resumePlayback);
            return;
        }

        if (resumePlayback)
        {
            MediaPreviewPanelControl.Play();
        }
        else
        {
            MediaPreviewPanelControl.Pause();
        }
    }

    private void CancelCompositionTimelineScrub()
    {
        _compositionAuditionController.CancelQueuedSeek();
        MediaPreviewPanelControl.Pause();
    }

    private void UpdateCompositionTimelinePlayback(double playbackSeconds)
    {
        CompositionTimelineControl.UpdatePlayback(
            playbackSeconds,
            MediaPreviewPanelControl.IsPlaying,
            _activeCompositionPreviewRevisionId is not null || _compositionAuditionController.IsActive,
            MediaPreviewPanelControl.HasVideoSource &&
            !MediaPreviewPanelControl.IsPriming &&
            MediaPreviewPanelControl.IsPlaybackEnabled);
    }

    private static string FormatTimelineTimePrecise(double seconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Round(
            Math.Max(0, seconds) * 1000,
            MidpointRounding.AwayFromZero));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private void UpdateCompositionActionState()
    {
        var index = _selectedCompositionSegmentId is { } selectedId
            ? _compositionSegments.ToList().FindIndex(item => item.SegmentId == selectedId)
            : -1;
        var selectedSegment = index >= 0 ? _compositionSegments[index] : null;
        PreviewCompositionButton.IsEnabled = _compositionSegments.Count > 0 && _compositionRenderCancellation is null;
        ExportCompositionButton.IsEnabled = _compositionSegments.Count > 0 && _compositionRenderCancellation is null;
        var selectedAudio = GetSelectedCompositionAudioClip();
        var videoState = selectedSegment is null
            ? null
            : new VideoSegmentEditState(
                selectedSegment.DisplayName,
                selectedSegment.DetailText,
                $"{selectedSegment.DurationText} • position {selectedSegment.Index + 1} of {_compositionSegments.Count} on the sequential video track",
                selectedSegment.AudioEnabled);
        var audioState = selectedAudio is null
            ? null
            : new AudioClipEditState(
                selectedAudio.DisplayName,
                $"Starts at {FormatTimelineTimePrecise(selectedAudio.TimelineStart.TotalSeconds)} • {selectedAudio.DurationText}",
                selectedAudio.IsMuted,
                selectedAudio.GainDecibels,
                selectedAudio.Pan,
                selectedAudio.FadeIn,
                selectedAudio.FadeOut,
                GetMaximumAudioFadeSeconds(selectedAudio));
        EditToolsPanelControl.ShowSelection(videoState, audioState);
    }

    private double GetMaximumAudioFadeSeconds(CompositionAudioClipListItem? selectedAudio)
    {
        if (selectedAudio is null) return 0;
        var maximum = selectedAudio.DurationSeconds ?? 30;
        maximum = Math.Min(
            maximum,
            Math.Max(0, CompositionTimelineControl.ProjectedDurationSeconds - selectedAudio.TimelineStart.TotalSeconds));
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

    private async void MediaPreparationPanel_SelectFrameRequested(object? sender, EventArgs e)
    {
        if (GetSelectedAsset() is not { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Video } asset)
            return;
        MediaPreviewPanelControl.EnterPrecisionMode();
        MediaPreparationPanelControl.EnterSelectFrame(asset.EffectiveDisplayName);
        await _framePreparationCoordinator.LoadAsync(asset, _workspace.Project?.Id);
    }

    private async void MediaPreparationPanel_MakeClipRequested(object? sender, EventArgs e)
    {
        if (GetSelectedAsset() is not { StorageKind: AssetStorageKind.Physical, MediaType: MediaType.Video } asset)
            return;
        MediaPreviewPanelControl.EnterPrecisionMode();
        MediaPreparationPanelControl.EnterMakeClip(
            asset.EffectiveDisplayName,
            $"{Path.GetFileNameWithoutExtension(asset.EffectiveDisplayName)} clip");
        await _framePreparationCoordinator.LoadAsync(asset, _workspace.Project?.Id);
    }

    private void MediaPreparationPanel_ExitRequested(object? sender, EventArgs e)
    {
        ResetFrameWorkspace();
        if (GetSelectedAsset() is { } asset) ConfigureMediaPreparationFor(asset);
    }

    private void GenerationHistoryPanel_GenerationSelected(object? sender, GenerationSelectedEventArgs e)
    {
        var generation = e.Generation;
        ProjectMediaPanelControl.SelectedItem = null;
        InspectorPanelControl.Text = InspectorTextFormatter.FormatGeneration(generation);
        StatusText.Text = $"Selected generation {generation.Id}.";
    }

    private async void GenerationPanel_SubmitRequested(object? sender, EventArgs e)
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
                GenerationPanelControl.Status = $"Store a {_generationProvider.Capabilities.DisplayName} API key before live submission.";
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
                GenerationPanelControl.Status = "Submission cancelled.";
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

    private void ProjectMediaPanel_ActionRequested(
        object? sender,
        ProjectMediaActionRequestedEventArgs e)
    {
        var routedEvent = new RoutedEventArgs();
        switch (e.Action)
        {
            case ProjectMediaAction.Rename:
                RenameAsset_Click(this, routedEvent);
                break;
            case ProjectMediaAction.Export:
                ExportSelectedMedia_Click(this, routedEvent);
                break;
            case ProjectMediaAction.ExtractAudio:
                ExtractAudio_Click(this, routedEvent);
                break;
            case ProjectMediaAction.Copy:
                CopyAssetToProject_Click(this, routedEvent);
                break;
            case ProjectMediaAction.Move:
                MoveAssetToProject_Click(this, routedEvent);
                break;
            case ProjectMediaAction.Delete:
                DeleteAsset_Click(this, routedEvent);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e), e.Action, "Unknown Project Media action.");
        }
    }

    private void ProjectMediaPanel_DragCompleted(object? sender, EventArgs e) =>
        CompositionTimelineControl.CancelExternalDrag();

    private void UpdateCompositionTimelineControl()
    {
        var eligibleItems = _workspace.Project?.Assets
            .Where(asset => asset.Id != _workspace.Project.WorkingCompositionAssetId)
            .Where(ProjectMediaDragData.CanAddToComposition)
            .Select(asset => new CompositionTimelineDropDescriptor(
                asset.Id,
                asset.EffectiveDisplayName,
                asset.MediaType == MediaType.Video
                    ? CompositionTimelineDropKind.Video
                    : CompositionTimelineDropKind.Audio))
            .ToArray() ?? [];

        var capabilities = _compositionSegments
            .Select((segment, index) => new
            {
                segment.SegmentId,
                Capability = new CompositionTimelineItemCapabilities(
                    segment.DurationSeconds is > 0,
                    CanDetachCompositionSegmentAudio(segment.SegmentId),
                    index > 0,
                    index < _compositionSegments.Count - 1,
                    _compositionSegments.Count > 1)
            })
            .Concat(_compositionAudioClips.Select(clip => new
            {
                SegmentId = clip.AudioClipId,
                Capability = new CompositionTimelineItemCapabilities(CanRemove: true)
            }))
            .ToDictionary(item => item.SegmentId, item => item.Capability);

        CompositionTimelineControl.UpdateState(new CompositionTimelineState(
            _compositionSegments.ToArray(),
            _compositionAudioClips.ToArray(),
            _selectedCompositionSegmentId,
            _selectedCompositionAudioClipId,
            GetCurrentTimelinePlaybackSeconds(),
            _activeCompositionPreviewRevisionId is not null || _compositionAuditionController.IsActive,
            MediaPreviewPanelControl.IsPlaying,
            MediaPreviewPanelControl.HasVideoSource &&
            !MediaPreviewPanelControl.IsPriming &&
            MediaPreviewPanelControl.IsPlaybackEnabled,
            IsWorkingCompositionSelected(),
            GetCompositionSplitActionLabel(),
            _applicationSettings.MediaTools.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame,
            capabilities,
            eligibleItems));
    }

    private void CompositionTimeline_ActivationRequested(
        object? sender,
        CompositionTimelineActivationEventArgs e)
    {
        _pendingCompositionTimelineSeekSeconds = e.PendingRulerSeekSeconds;
        SelectWorkingCompositionInProjectMedia();
    }

    private void CompositionTimeline_SelectionChanged(
        object? sender,
        CompositionTimelineSelectionChangedEventArgs e)
    {
        _selectedCompositionSegmentId = e.SegmentId;
        _selectedCompositionAudioClipId = e.AudioClipId;
        UpdateCompositionActionState();
        UpdateCompositionTimelineControl();
    }

    private async void CompositionTimeline_SeekRequested(
        object? sender,
        CompositionTimelineSeekEventArgs e)
    {
        switch (e.Phase)
        {
            case CompositionTimelineSeekPhase.Started:
                _activeMediaPreviewScrubGeneration = null;
                BeginCompositionTimelineSeek(e.Seconds);
                MediaPreviewPanelControl.Pause();
                SeekCompositionTimeline(e.Seconds);
                break;
            case CompositionTimelineSeekPhase.Changed:
                EnsureCompositionTimelineSeek(e.Seconds);
                SeekCompositionTimeline(e.Seconds);
                break;
            case CompositionTimelineSeekPhase.Completed:
                var generation = EnsureCompositionTimelineSeek(e.Seconds);
                SeekCompositionTimeline(e.Seconds);
                try
                {
                    await CompleteCompositionTimelineScrubAsync(e.ResumePlayback);
                }
                finally
                {
                    CompleteCompositionTimelineSeek(generation);
                }
                break;
            case CompositionTimelineSeekPhase.Cancelled:
                CancelCompositionTimelineScrub();
                CompleteCompositionTimelineSeek(_activeCompositionTimelineSeekGeneration);
                break;
        }
    }

    private long BeginCompositionTimelineSeek(double seconds)
    {
        var generation = ++_compositionTimelineSeekGeneration;
        _activeCompositionTimelineSeekGeneration = generation;
        _activeCompositionTimelineSeekSeconds = seconds;
        return generation;
    }

    private long EnsureCompositionTimelineSeek(double seconds)
    {
        _activeCompositionTimelineSeekSeconds = seconds;
        return _activeCompositionTimelineSeekGeneration ?? BeginCompositionTimelineSeek(seconds);
    }

    private void CompleteCompositionTimelineSeek(long? generation)
    {
        if (generation is not null && _activeCompositionTimelineSeekGeneration == generation)
            _activeCompositionTimelineSeekGeneration = null;
        if (generation is not null && _activeMediaPreviewScrubGeneration == generation)
            _activeMediaPreviewScrubGeneration = null;
    }

    private void ResetCompositionTimelineSeek()
    {
        _compositionTimelineSeekGeneration++;
        _activeCompositionTimelineSeekGeneration = null;
        _activeMediaPreviewScrubGeneration = null;
    }

    private async void CompositionTimeline_SegmentReorderRequested(
        object? sender,
        CompositionTimelineReorderEventArgs e)
    {
        try
        {
            await RunUiActionAsync("Reordering composition segment…", async () =>
            {
                await new WorkingCompositionService(_workspace).MoveSegmentToIndexAsync(e.SegmentId, e.TargetIndex);
                _selectedCompositionSegmentId = e.SegmentId;
                _selectedCompositionAudioClipId = null;
                RefreshEditWorkspaceState();
                StatusText.Text = "Reordered the Working Composition. Preview it to rebuild the video.";
            });
        }
        finally
        {
            UpdateCompositionTimelineControl();
            CompositionTimelineControl.CompletePendingMutation();
        }
    }

    private async void CompositionTimeline_AudioMoveRequested(
        object? sender,
        CompositionTimelineAudioMoveEventArgs e)
    {
        try
        {
            await RunUiActionAsync("Moving composition audio clip…", async () =>
            {
                await new WorkingCompositionService(_workspace).SetAudioClipTimelineStartAsync(e.AudioClipId, e.TimelineStart);
                _selectedCompositionSegmentId = null;
                _selectedCompositionAudioClipId = e.AudioClipId;
                RefreshEditWorkspaceState();
                StatusText.Text = $"Moved the audio clip to {FormatTimelineTimePrecise(e.TimelineStart.TotalSeconds)}. Preview the composition to rebuild it.";
            });
        }
        finally
        {
            UpdateCompositionTimelineControl();
            CompositionTimelineControl.CompletePendingMutation();
        }
    }

    private async void CompositionTimeline_MediaDropRequested(
        object? sender,
        CompositionTimelineDropEventArgs e)
    {
        var asset = _workspace.Project?.Assets.SingleOrDefault(candidate => candidate.Id == e.AssetId);
        if (asset is null) return;

        if (e.Kind == CompositionTimelineDropKind.Video)
        {
            await RunUiActionAsync($"Inserting {asset.EffectiveDisplayName} into the composition…", async () =>
            {
                var revision = await new WorkingCompositionService(_workspace).AddSegmentAsync(asset.Id, e.InsertionIndex);
                var recipe = AssertCompositionRecipe(revision);
                _selectedCompositionSegmentId = recipe.Segments[Math.Clamp(e.InsertionIndex, 0, recipe.Segments.Count - 1)].Id;
                _selectedCompositionAudioClipId = null;
                RefreshEditWorkspaceState();
                StatusText.Text = $"Inserted {asset.EffectiveDisplayName} into the Working Composition.";
            });
            return;
        }

        await RunUiActionAsync($"Adding {asset.EffectiveDisplayName} to the audio track…", async () =>
        {
            var revision = await new WorkingCompositionService(_workspace)
                .AddAudioClipAsync(asset.Id, TimeSpan.FromSeconds(e.TimelineSeconds));
            _selectedCompositionSegmentId = null;
            _selectedCompositionAudioClipId = AssertCompositionRecipe(revision).AudioClips[^1].Id;
            RefreshEditWorkspaceState();
            StatusText.Text = $"Added {asset.EffectiveDisplayName} at {FormatTimelineTimePrecise(e.TimelineSeconds)}.";
        });
    }

    private async void CompositionTimeline_SplitRequested(object? sender, CompositionTimelineItemEventArgs e) =>
        await SplitCompositionSegmentAsync(e.ItemId);

    private async void CompositionTimeline_ShiftLeftRequested(object? sender, CompositionTimelineItemEventArgs e) =>
        await MoveCompositionSegmentAsync(e.ItemId, -1);

    private async void CompositionTimeline_ShiftRightRequested(object? sender, CompositionTimelineItemEventArgs e) =>
        await MoveCompositionSegmentAsync(e.ItemId, 1);

    private async void CompositionTimeline_DetachAudioRequested(object? sender, CompositionTimelineItemEventArgs e) =>
        await DetachCompositionSegmentAudioAsync(e.ItemId);

    private async void CompositionTimeline_RemoveRequested(object? sender, CompositionTimelineItemEventArgs e) =>
        await RemoveCompositionItemAsync(e.ItemId);

    private async void RenameAsset_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { } asset) return;
        var renameKind = ProjectMediaRenamePolicy.GetKind(asset);
        if (renameKind == ProjectMediaRenameKind.None)
        {
            return;
        }

        string requestedName;
        if (renameKind == ProjectMediaRenameKind.PhysicalFile)
        {
            var dialog = new AssetNameDialog(asset.FileName) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            requestedName = dialog.FileName;
        }
        else
        {
            var dialog = new DisplayNameDialog(asset.EffectiveDisplayName) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            requestedName = dialog.DisplayName;
        }

        await RunUiActionAsync(
            renameKind == ProjectMediaRenameKind.PhysicalFile
                ? $"Renaming {asset.FileName}…"
                : $"Renaming {asset.EffectiveDisplayName}…",
            async () =>
            {
                await _projectMediaOperationsCoordinator.RenameAsync(asset, requestedName);
                RefreshProjectCollections(asset.Id);
                InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
                StatusText.Text = renameKind == ProjectMediaRenameKind.PhysicalFile
                    ? $"Renamed stored media file to {asset.FileName}."
                    : $"Renamed Saved Clip to {asset.EffectiveDisplayName}.";
            });
    }

    private async void ExportSelectedMedia_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectMediaPanelControl.SelectedItem is not ProjectMediaListItem item || _workspace.Project is null) return;
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
                var path = await _projectMediaOperationsCoordinator.ExportSavedFrameAsync(
                    anchor,
                    anchorRevision,
                    dialog.FileName);
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
            var path = await _projectMediaOperationsCoordinator.ExportVirtualVideoAsync(
                asset,
                recipeRevisionId,
                videoDialog.FileName);
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
            var extracted = await _projectMediaOperationsCoordinator.ExtractAudioAsync(
                source,
                recipeRevisionId,
                dialog.FileName);
            RefreshProjectCollections(extracted.Id);
            StatusText.Text = $"Extracted audio as {extracted.FileName}.";
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
                await using var media = await _mediaMaterializer.MaterializeAsync(
                        _workspace.Project,
                        _workspace.Location,
                        new MaterializationRequest(
                            new AnchorMaterializationTarget(anchor.Id, revision.Id),
                            MaterializationPurpose.Preview),
                        CancellationToken.None);
                if (_workspace.Project?.Id != selectedProjectId || ProjectMediaPanelControl.SelectedItem != item) return;
                var thumbnail = LoadBitmap(media.Path);
                item.Thumbnail = thumbnail;
                foreach (var choice in _referenceChoices.Where(choice =>
                             choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                             choice.LogicalObjectId == anchor.Id))
                    choice.UpdateThumbnail(thumbnail);
                ProjectMediaPanelControl.RefreshItems();
                GenerationPanelControl.RefreshReferences();
                ClearMediaPreview();
                MediaPreviewPanelControl.ShowImage(thumbnail);
                InspectorPanelControl.Text = InspectorTextFormatter.FormatSavedFrame(
                    new SavedFrameListItem(anchor, revision, thumbnail, error: null));
                StatusText.Text = $"Selected Saved Frame {item.DisplayName}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_workspace.Project?.Id != selectedProjectId) return;
                ClearMediaPreview();
                MediaPreviewPanelControl.ShowPlaceholder($"Saved Frame preview unavailable\n\n{exception.Message}");
                InspectorPanelControl.Text = InspectorTextFormatter.FormatSavedFrame(
                    new SavedFrameListItem(anchor, revision, thumbnail: null, exception.Message));
                StatusText.Text = $"Could not display {item.DisplayName}.";
            }
        });
    }

    private void GenerationPanel_ReferenceSelected(object? sender, GenerationReferenceSelectedEventArgs e)
    {
        var choice = e.Choice;
        var mediaItem = _assets.FirstOrDefault(item => choice.ObjectKind switch
        {
            GenerationReferenceObjectKind.Asset => item.Asset?.Id == choice.LogicalObjectId,
            GenerationReferenceObjectKind.FrameAnchor => item.Anchor?.Id == choice.LogicalObjectId,
            _ => false
        });
        if (mediaItem is not null) ProjectMediaPanelControl.SelectedItem = mediaItem;
    }

    private async void DeleteAsset_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectMediaPanelControl.SelectedItem is not ProjectMediaListItem selected || _workspace.Project is null) return;
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
                await _projectMediaOperationsCoordinator.DeleteSavedClipAsync(asset.Id);
                ProjectMediaPanelControl.SelectedItem = null;
                ClearMediaPreview();
                RefreshProjectCollections();
                InspectorPanelControl.Reset();
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
        var usage = _projectMediaOperationsCoordinator.AnalyzeDependencies(asset);
        if (usage.IsInUse)
        {
            MessageBox.Show(
                this,
                $"'{asset.EffectiveDisplayName}' cannot be deleted because it is still used by:\n\n• {string.Join("\n• ", usage.DisplayDescriptions)}",
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
        if (ProjectMediaPanelControl.SelectedItem is not ProjectMediaListItem selected ||
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
                var result = await _projectMediaOperationsCoordinator
                    .MovePhysicalAssetToProjectAsync(asset, targetProjectFile);
                if (result.SourceRemoved)
                {
                    ApplyRemovedCurrentProjectAssetUi();
                    StatusText.Text = $"Moved {asset.FileName} to {result.CopyResult.TargetProjectName}.";
                    return;
                }

                StatusText.Text = $"Copied {asset.FileName} to {result.CopyResult.TargetProjectName}; the source remains because project history references it.";
                MessageBox.Show(
                    this,
                    $"'{asset.FileName}' is now available in '{result.CopyResult.TargetProjectName}'.\n\n" +
                    "ReelForge retained the source copy because removing it would break:\n\n" +
                    $"• {string.Join("\n• ", result.DependencyReport.DisplayDescriptions)}",
                    "Asset transferred; source retained",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });
    }

    private async void CopyAssetToProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectMediaPanelControl.SelectedItem is not ProjectMediaListItem selected ||
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
                var result = await _projectMediaOperationsCoordinator
                    .CopyPhysicalAssetToProjectAsync(asset, targetProjectFile);
                StatusText.Text = $"Copied {asset.FileName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.";
            });
    }

    private string? ChooseTransferTargetProject()
    {
        var projectFilePath = _projectDialogs.SelectProjectToOpen(_applicationSettings);
        if (projectFilePath is null) return null;
        if (_workspace.Location is not null &&
            Path.GetFullPath(projectFilePath).Equals(
                Path.GetFullPath(_workspace.Location.ProjectFilePath),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Choose a different destination project.", "Transfer asset", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        return projectFilePath;
    }

    private ProjectAsset? GetSelectedAsset() => ProjectMediaPanelControl.SelectedItem?.Asset;

    private async Task RemoveCurrentProjectAssetAsync(ProjectAsset asset)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
        await _projectMediaOperationsCoordinator.DeletePhysicalAssetAsync(asset.Id);
        ApplyRemovedCurrentProjectAssetUi();
    }

    private void ApplyRemovedCurrentProjectAssetUi()
    {
        ProjectMediaPanelControl.SelectedItem = null;
        InspectorPanelControl.Reset();
        ClearMediaPreview();
        RefreshProjectCollections();
    }

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
            GenerationHistoryPanelControl.SelectGeneration(generation.Id);
            GenerationPanelControl.Status = $"Generation queued locally for {delaySeconds} seconds. Use Cancel Job in Jobs to undo.";
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
            GenerationPanelControl.Status = exception.Message;
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
            GenerationPanelControl.IsSubmissionEnabled = false;
            SetProjectActionsEnabled(false);
        }
        IProgress<GenerationWorkflowProgress>? progress = usesActiveWorkspace
            ? new Progress<GenerationWorkflowProgress>(update => GenerationPanelControl.Status = update.Message)
            : null;
        try
        {
            generation = await workflow.SubmitQueuedAsync(provider, generation, authorization, progress);
            var sourceIsActiveNow = IsProjectOpen(projectLocation.ProjectFilePath);
            if (sourceIsActiveNow)
            {
                MergeGenerationStateIntoActiveProject(generation);
                RefreshProjectCollections();
                GenerationHistoryPanelControl.SelectGeneration(generation.Id);
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
                    GenerationPanelControl.Status = "Generation submitted. Follow its progress in the Jobs tab.";
                StatusText.Text = $"Generation accepted by {provider.Capabilities.DisplayName}.";
            }
            else if (generation.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
            {
                await _jobCoordinator.CompleteUnacceptedSubmissionAsync(generation);
                if (sourceIsActiveNow) GenerationPanelControl.Status = FormatGenerationOutcome(generation);
                StatusText.Text = $"Generation state: {generation.Status}; no provider job is being monitored.";
            }
            else
            {
                if (sourceIsActiveNow) GenerationPanelControl.Status = FormatGenerationOutcome(generation);
                StatusText.Text = $"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.";
            }
        }
        finally
        {
            if (usesActiveWorkspace)
            {
                GenerationPanelControl.IsSubmissionEnabled = true;
                SetProjectActionsEnabled(true);
            }
        }
    }

    private async void JobsPanelControl_CancelRequested(
        object? sender,
        GenerationJobCancelRequestedEventArgs e)
    {
        try
        {
            if (!await _jobCoordinator.CancelPendingAsync(e.GenerationId)) return;
            if (_pendingSubmissionDelays.TryGetValue(e.GenerationId, out var delay)) delay.Cancel();
            RemovePendingSubmissionDelay(e.GenerationId, delay);
            GenerationPanelControl.Status = "Queued generation cancelled.";
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
        GenerationPanelControl.IsSubmissionEnabled = false;
        SetProjectActionsEnabled(false);
        var provider = _generationProvider;
        var projectLocation = _workspace.Location;
        var projectName = _workspace.Project?.Name;
        var progress = new Progress<GenerationWorkflowProgress>(update => GenerationPanelControl.Status = update.Message);

        try
        {
            var generation = await _generationWorkflow.SubmitAsync(
                provider,
                draft,
                authorization,
                progress);
            RefreshProjectCollections();
            GenerationHistoryPanelControl.SelectGeneration(generation.Id);
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
                GenerationPanelControl.Status = "Generation submitted. Follow its progress in the Jobs tab.";
                StatusText.Text = $"Generation accepted by {provider.Capabilities.DisplayName}.";
            }
            else
            {
                GenerationPanelControl.Status = FormatGenerationOutcome(generation);
                StatusText.Text = $"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.";
            }
        }
        catch (GenerationValidationException exception)
        {
            GenerationPanelControl.Status = exception.Message;
        }
        catch (Exception exception)
        {
            ShowError("Generation workflow failed", exception);
        }
        finally
        {
            GenerationPanelControl.IsSubmissionEnabled = true;
            SetProjectActionsEnabled(true);
        }
    }

    private void SetProjectActionsEnabled(bool isEnabled)
    {
        NewProjectButton.IsEnabled = isEnabled;
        OpenProjectButton.IsEnabled = isEnabled;
        ImportAssetsButton.IsEnabled = isEnabled;
        SettingsButton.IsEnabled = isEnabled;
        GenerationPanelControl.IsProviderEnabled = isEnabled;
    }

    private void TryAutoPreviewGeneratedOutput(GenerationRecord generation, bool owningProjectIsOpen)
    {
        if (generation.Status != GenerationStatus.Succeeded ||
            generation.IngestionStatus != OutputIngestionStatus.Succeeded ||
            !GeneratedOutputPreviewPolicy.ShouldAutoPreview(
                owningProjectIsOpen,
                _activeWorkspace,
                MediaPreparationPanelControl.IsPreparing))
            return;
        var outputId = generation.OutputAssetIds.LastOrDefault();
        if (outputId == Guid.Empty) return;
        ProjectMediaPanelControl.SelectedItem = _assets.FirstOrDefault(item => item.Asset?.Id == outputId);
    }

    private async void GenerationPanel_DerivedDraftRequested(object? sender, DerivedDraftRequestedEventArgs e)
    {
        if (GenerationHistoryPanelControl.SelectedGeneration is not { } source)
        {
            GenerationPanelControl.Status = "Select a generation in history before creating a derived draft.";
            return;
        }
        var relationship = e.RelationshipType;

        if (relationship is GenerationRelationshipType.ContinueAfter or GenerationRelationshipType.ContinueBefore)
        {
            await PrepareGenerationBoundaryContinuationAsync(source, relationship);
            return;
        }

        var draft = GenerationWorkflow.CreateDerivedDraft(source, relationship);
        LoadDraftIntoUi(draft);
        await _generationWorkflow.SaveDraftAsync(draft);
        GenerationPanelControl.Status =
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
            GenerationPanelControl.Status = "This generation has no durable video output to continue from.";
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
        var transientRevision = TransientFrameAnchorRevisionFactory.Create(sourceAsset.Id, sourceContentHash, frame);
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
        if (_framePreparationCoordinator.HasCurrentSource(sourceAsset.Id))
            await _framePreparationCoordinator.RefreshSavedFramesAsync();
        RightPanelTabs.SelectedIndex = 1;
        GenerationPanelControl.Status = parentGeneration is null
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

    private async void GenerationPanel_NewRootRequested(object? sender, EventArgs e)
    {
        if (!EnsureProjectOpen()) return;
        var draft = CaptureDraftFromUi();
        draft.ParentGenerationId = null;
        draft.RelationshipType = null;
        LoadDraftIntoUi(draft);
        await _generationWorkflow.SaveDraftAsync(draft);
        GenerationPanelControl.Status = "Started a new root generation draft.";
    }

    private GenerationDraft CaptureDraftFromUi()
    {
        var state = GenerationPanelControl.CaptureState();
        var selectedReferences = GenerationReferenceEditor.Capture(state.Mode, _referenceChoices);
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("generate_audio"))
        {
            parameters["generate_audio"] = state.GenerateAudio.ToString().ToLowerInvariant();
        }
        else if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("generateAudio"))
        {
            parameters["generateAudio"] = state.GenerateAudio.ToString().ToLowerInvariant();
        }
        if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("watermark"))
            parameters["watermark"] = state.Watermark.ToString().ToLowerInvariant();
        if (_generationProvider.Capabilities.ProviderParameters.ContainsKey("output_format"))
            parameters["output_format"] = state.OutputFormat;

        var current = _workspace.Project?.CurrentGenerationDraft;
        return new GenerationDraft
        {
            ProviderId = _generationProvider.Capabilities.ProviderId,
            ModelVersion = _generationProvider.Capabilities.ModelVersion,
            Prompt = state.Prompt,
            Mode = state.Mode,
            DurationSeconds = state.DurationSeconds,
            AspectRatio = state.AspectRatio,
            Resolution = state.Resolution,
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
            if (providerChoice is not null)
            {
                _generationProvider = providerChoice.Provider;
                GenerationPanelControl.SelectProvider(providerChoice);
                GenerationPanelControl.ConfigureProvider(_generationProvider);
            }
            GenerationPanelControl.LoadState(new GenerationPanelFormState(
                draft.Prompt,
                draft.Mode,
                draft.DurationSeconds,
                draft.AspectRatio,
                draft.Resolution,
                ReadDraftBoolean(draft, "generate_audio", "generateAudio", true),
                ReadDraftBoolean(draft, "watermark", null, false),
                draft.ProviderParameters.GetValueOrDefault("output_format", "mp4")));

            GenerationReferenceEditor.ApplyDraft(draft.References, _referenceChoices);
            GenerationPanelControl.RefreshReferences();
            GenerationPanelControl.SetLineage(draft.ParentGenerationId is { } parent
                ? $"{draft.RelationshipType} • parent {parent}"
                : "New root generation");
        }
        finally
        {
            _suppressDraftAutosave = false;
        }
    }

    private void ShowAssetPreview(ProjectAsset asset)
    {
        ClearMediaPreview();
        MediaPreviewPanelControl.HidePlaceholder();

        var absolutePath = _workspace.GetAbsoluteAssetPath(asset);
        if (!File.Exists(absolutePath))
        {
            MediaPreviewPanelControl.ShowPlaceholder(
                $"Missing media file\n{asset.FileName}\n\nMoving a file in Explorer does not add it to another project's .rfp file.");
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
            MediaPreviewPanelControl.ShowImage(bitmap);
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
            var revision = _workspace.Project.RecipeRevisions.Single(candidate => candidate.Id == revisionId);
            ClearMediaPreview();
            var requestedPosition = _pendingCompositionTimelineSeekSeconds ?? 0;
            _pendingCompositionTimelineSeekSeconds = null;
            var result = await _compositionAuditionController.OpenAsync(
                composition,
                revision,
                requestedPosition,
                () => _workspace.Project?.Id == selectedProjectId &&
                      ProjectMediaPanelControl.SelectedItem == selectedItem);
            if (result.IsStale) return;
            StatusText.Text = result.AudioWarning is not null
                ? $"Fast composition audition ready without independent audio: {result.AudioWarning}"
                : result.HasAuditionAudio
                    ? "Fast composition audition ready with independent audio clips. " +
                      "Use Preview composition to verify final mix fidelity and render continuity."
                    : "Fast composition audition ready. Source video and source audio play at cuts; " +
                      "use Preview composition to verify the complete audio mix and final render.";
        });
    }

    private async Task ShowVirtualAssetPreviewAsync(
        ProjectAsset asset,
        ProjectMediaListItem selectedItem,
        Guid? selectedProjectId)
    {
        if (_workspace.Project is null || _workspace.Location is null) return;
        if (asset.Virtual?.Kind == VirtualAssetKind.Composition)
        {
            InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
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
                if (_workspace.Project?.Id != selectedProjectId || ProjectMediaPanelControl.SelectedItem != selectedItem)
                {
                    await lease.DisposeAsync();
                    return;
                }

                InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset, lease.Encoding);
                ClearMediaPreview();
                if (asset.Virtual?.Kind == VirtualAssetKind.Composition)
                    _activeCompositionPreviewRevisionId = asset.Virtual.CurrentRecipeRevisionId;
                MediaPreviewPanelControl.HidePlaceholder();
                MediaPreviewPanelControl.OpenLeasedVideo(
                    lease,
                    requiresWarmup: asset.Virtual?.Kind != VirtualAssetKind.SavedClip);
                lease = null;
                StatusText.Text = $"Selected {kindName} {asset.EffectiveDisplayName}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_workspace.Project?.Id != selectedProjectId) return;
                ClearMediaPreview();
                MediaPreviewPanelControl.ShowPlaceholder($"{kindName} preview unavailable\n\n{exception.Message}");
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
        MediaPreviewPanelControl.OpenVideo(
            absolutePath,
            requiresWarmup,
            playAfterPriming,
            startSeconds,
            forceMuted,
            useExternalTimeline: _compositionAuditionController.IsActive);
    }

    private void CompositionAuditionAudio_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MediaPreviewPanelControl.StopAuditionAudio();
        StatusText.Text = $"Independent audio audition unavailable: {e.ErrorException?.Message ?? "media playback failed"}.";
    }

    private void CompositionAudition_PositionChanged(
        object? sender,
        CompositionAuditionPositionChangedEventArgs e)
    {
        if (_activeCompositionTimelineSeekGeneration is null &&
            !_compositionAuditionController.IsQuiesced)
            UpdateCompositionTimelinePlayback(e.PositionSeconds);
    }

    private void ClearMediaPreview()
    {
        _activeCompositionPreviewRevisionId = null;
        _compositionAuditionController.Reset();
        MediaPreviewPanelControl.Reset();
        CompositionTimelineControl.CancelInteractions();
        ResetCompositionTimelineSeek();
        UpdateCompositionTimelinePlayback(0);
    }

    private void ClearStaleCompositionPreview(ProjectAsset composition, RecipeRevision currentRevision)
    {
        ClearMediaPreview();
        if (ProjectMediaPanelControl.SelectedItem is not ProjectMediaListItem { Asset: { } selected } ||
            selected.Id != composition.Id)
            return;

        var selectedItem = ProjectMediaPanelControl.SelectedItem;
        _ = OpenCompositionDraftPreviewAsync(composition, selectedItem, _workspace.Project?.Id);
    }

    private void MediaPreview_VideoReady(object? sender, MediaPreviewReadyEventArgs e)
    {
        _compositionAuditionController.OnVideoReady(e.ShouldPlay);
        UpdatePlaybackPosition();
    }

    private async void MediaPreview_PlaybackEnded(object? sender, EventArgs e)
    {
        if (_activeCompositionTimelineSeekGeneration is not null ||
            _compositionAuditionController.IsQuiesced)
            return;
        if (_compositionAuditionController.IsActive &&
            await _compositionAuditionController.AdvanceAsync())
            return;
        CompleteVideoPlayback();
    }

    private void Playback_Click(object? sender, EventArgs e)
    {
        if (!MediaPreviewPanelControl.HasVideoSource) return;
        if (MediaPreviewPanelControl.HasEnded || IsAtVideoEnd())
        {
            if (_compositionAuditionController.IsActive)
            {
                MediaPreviewPanelControl.ClearEndedState();
                _ = _compositionAuditionController.ReplayAsync();
                return;
            }
            MediaPreviewPanelControl.ReopenForPlayback();
            return;
        }
        if (MediaPreviewPanelControl.IsPlaying)
        {
            MediaPreviewPanelControl.Pause();
            return;
        }

        if (_compositionAuditionController.IsActive)
            _compositionAuditionController.SynchronizeAudio(play: true);
        MediaPreviewPanelControl.Play();
    }

    private async void PreviousFrame_Click(object? sender, EventArgs e) =>
        await StepPreviewFrameAsync(-1);

    private async void NextFrame_Click(object? sender, EventArgs e) =>
        await StepPreviewFrameAsync(1);

    private async Task StepPreviewFrameAsync(int direction)
    {
        if (direction is not (-1 or 1) || MediaPreviewPanelControl.LocalSourcePath is not { } sourcePath ||
            !await _frameNavigationGate.WaitAsync(0))
            return;
        try
        {
            MediaPreviewPanelControl.Pause();
            MediaPreviewPanelControl.SetFrameNavigationEnabled(false);
            var currentSeconds = MediaPreviewPanelControl.PositionSeconds;
            var frames = await _exactFrameService.IndexWindowAsync(sourcePath, currentSeconds, radiusSeconds: 2);
            var target = direction < 0
                ? frames.Where(frame => frame.TimestampSeconds < currentSeconds - 0.000_000_1)
                    .OrderByDescending(frame => frame.TimestampSeconds)
                    .FirstOrDefault()
                : frames.Where(frame => frame.TimestampSeconds > currentSeconds + 0.000_000_1)
                    .OrderBy(frame => frame.TimestampSeconds)
                    .FirstOrDefault();
            if (target is null) return;

            if (_compositionAuditionController.IsActive)
            {
                var globalSeconds = _compositionAuditionController.MapSourcePositionToTimeline(
                    target.TimestampSeconds);
                await SeekCompositionDraftAsync(globalSeconds, playAfterSeek: false);
            }
            else
            {
                SeekPreview(target.TimestampSeconds);
                MediaPreviewPanelControl.SetPosition(target.TimestampSeconds);
            }
        }
        catch (Exception exception)
        {
            ShowError("Frame navigation failed", exception);
        }
        finally
        {
            MediaPreviewPanelControl.SetFrameNavigationEnabled(MediaPreviewPanelControl.HasNaturalVideo);
            _frameNavigationGate.Release();
        }
    }

    private void CompleteVideoPlayback()
    {
        if (_compositionAuditionController.IsActive)
        {
            _compositionAuditionController.Complete();
        }
        MediaPreviewPanelControl.MarkPlaybackEnded(resetVideoPosition: !_compositionAuditionController.IsActive);
        UpdatePlaybackPosition();
    }

    private bool IsAtVideoEnd() =>
        MediaPreviewPanelControl.IsAtVideoEnd(TimeSpan.FromMilliseconds(10));

    private void MediaPreview_ScrubStarted(object? sender, MediaPreviewPositionEventArgs e)
    {
        if (!_compositionAuditionController.IsActive) return;
        var generation = BeginCompositionTimelineSeek(e.PositionSeconds);
        _activeMediaPreviewScrubGeneration = generation;
        _compositionAuditionController.CancelQueuedSeek();
    }

    private void MediaPreview_ScrubPositionChanged(object? sender, MediaPreviewPositionEventArgs e)
    {
        if (_compositionAuditionController.IsActive)
        {
            if (_activeMediaPreviewScrubGeneration is not { } generation ||
                _activeCompositionTimelineSeekGeneration != generation)
                return;
            _activeCompositionTimelineSeekSeconds = e.PositionSeconds;
            _compositionAuditionController.QueueSeek(e.PositionSeconds);
            UpdateCompositionTimelinePlayback(e.PositionSeconds);
            return;
        }
        SeekPreview(e.PositionSeconds);
    }

    private async void MediaPreview_ScrubCompleted(object? sender, MediaPreviewScrubCompletedEventArgs e)
    {
        if (_compositionAuditionController.IsActive)
        {
            if (_activeMediaPreviewScrubGeneration is not { } generation ||
                _activeCompositionTimelineSeekGeneration != generation)
            {
                _compositionAuditionController.CancelQueuedSeek();
                return;
            }
            _activeCompositionTimelineSeekSeconds = e.PositionSeconds;
            try
            {
                await _compositionAuditionController.CommitSeekAsync(e.PositionSeconds, e.ResumePlayback);
            }
            finally
            {
                CompleteCompositionTimelineSeek(generation);
            }
        }
        else
            SeekPreview(e.PositionSeconds);
        if (e.ResumePlayback && !_compositionAuditionController.IsActive)
        {
            MediaPreviewPanelControl.Play();
        }
        else if (!_compositionAuditionController.IsActive)
        {
            MediaPreviewPanelControl.Pause();
        }
        _framePreparationCoordinator.ScheduleContactFrameRefresh(e.PositionSeconds);
    }

    private void MediaPreview_ScrubCancelled(object? sender, EventArgs e)
    {
        if (_activeMediaPreviewScrubGeneration is not { } generation) return;
        _compositionAuditionController.CancelQueuedSeek();
        CompleteCompositionTimelineSeek(generation);
    }

    private void SeekPreview(double seconds)
    {
        if (!MediaPreviewPanelControl.HasVideoSource) return;
        if (_compositionAuditionController.IsActive)
        {
            _ = SeekCompositionDraftAsync(seconds, playAfterSeek: false);
            return;
        }
        MediaPreviewPanelControl.SeekVideo(seconds);
        MediaPreviewPanelControl.ShowPosition(
            MediaPreviewPanelControl.MediaPosition,
            MediaPreviewPanelControl.MediaDuration);
    }

    private async Task SeekCompositionDraftAsync(double seconds, bool playAfterSeek)
    {
        await _compositionAuditionController.SeekAsync(seconds, playAfterSeek);
    }

    private void UpdatePlaybackPosition()
    {
        if (!MediaPreviewPanelControl.HasVideoSource)
        {
            MediaPreviewPanelControl.ShowPosition(TimeSpan.Zero, TimeSpan.Zero);
            return;
        }

        var mediaPosition = MediaPreviewPanelControl.MediaPosition;
        var mediaDuration = MediaPreviewPanelControl.MediaDuration;

        if (_compositionAuditionController.IsQuiesced)
        {
            MediaPreviewPanelControl.ShowTimelinePosition(_compositionAuditionController.PositionSeconds);
            return;
        }

        if (_compositionAuditionController.IsActive &&
            _activeCompositionTimelineSeekGeneration is not null)
        {
            MediaPreviewPanelControl.ShowTimelinePosition(_activeCompositionTimelineSeekSeconds);
            return;
        }

        if (MediaPreviewPanelControl.IsPlaying && _compositionAuditionController.IsActive)
        {
            if (_compositionAuditionController.HasReachedActiveSegmentEnd(mediaPosition.TotalSeconds))
            {
                _ = _compositionAuditionController.AdvanceAsync();
                return;
            }
        }

        if (MediaPreviewPanelControl.IsPlaying && IsAtVideoEnd())
        {
            if (_compositionAuditionController.IsActive)
                _ = _compositionAuditionController.AdvanceAsync();
            else
                CompleteVideoPlayback();
            return;
        }

        if (_compositionAuditionController.IsActive)
        {
            var currentSeconds = _compositionAuditionController.UpdateFromSourcePosition(
                mediaPosition.TotalSeconds);
            if (!MediaPreviewPanelControl.IsScrubbing) MediaPreviewPanelControl.SetPosition(currentSeconds);
            MediaPreviewPanelControl.ShowTimelinePosition(currentSeconds);
            return;
        }

        if (!MediaPreviewPanelControl.IsScrubbing) MediaPreviewPanelControl.SetPosition(mediaPosition.TotalSeconds);
        MediaPreviewPanelControl.ShowPosition(mediaPosition, mediaDuration);
        UpdateCompositionTimelinePlayback(mediaPosition.TotalSeconds);
    }

    private void MediaPreview_PositionTick(object? sender, EventArgs e) => UpdatePlaybackPosition();

    private void ResetFrameWorkspace()
    {
        _framePreparationCoordinator.Reset();
        StartEditButton.IsEnabled = false;
        UpdateCompositionActionState();
    }

    private void FramePreparation_StatusChanged(object? sender, FramePreparationStatusEventArgs e) =>
        StatusText.Text = e.Message;

    private void FramePreparation_SavedFramesProjected(object? sender, SavedFramesProjectedEventArgs e)
    {
        foreach (var item in e.Items)
        {
            foreach (var choice in _referenceChoices.Where(choice =>
                         choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                         choice.LogicalObjectId == item.Anchor.Id))
                choice.UpdateThumbnail(item.Thumbnail);
            var mediaItem = _assets.FirstOrDefault(candidate => candidate.Anchor?.Id == item.Anchor.Id);
            if (mediaItem is not null) mediaItem.Thumbnail = item.Thumbnail;
        }
        GenerationPanelControl.RefreshReferences();
        ProjectMediaPanelControl.RefreshItems();
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

    private async void MediaPreparationPanel_SaveFrameRequested(object? sender, EventArgs e)
    {
        if (_workspace.Project is null || !_framePreparationCoordinator.TryGetSelectedFrame(out var selection))
        {
            StatusText.Text = "Select a frame in the precision strip before saving it.";
            return;
        }

        await RunUiActionAsync("Saving exact frame position…", async () =>
        {
            var saved = await _framePreparationCoordinator.SaveSelectedFrameAsync();
            if (saved is null) return;
            RefreshProjectCollections();
            await _framePreparationCoordinator.RefreshSavedFramesAsync();
            _framePreparationCoordinator.SelectSavedFrameRevision(saved.Revision.Id);
            StatusText.Text = $"Saved exact frame at {FormatFrameTimestamp(saved.Revision.TimestampSeconds)}.";
        });
    }

    private void MediaPreparationPanel_ClipStartRequested(object? sender, EventArgs e)
    {
        if (!_framePreparationCoordinator.SetSelectedClipBoundary(isStart: true))
            StatusText.Text = "Select an exact frame before setting this clip boundary.";
    }

    private void MediaPreparationPanel_ClipEndRequested(object? sender, EventArgs e)
    {
        if (!_framePreparationCoordinator.SetSelectedClipBoundary(isStart: false))
            StatusText.Text = "Select an exact frame before setting this clip boundary.";
    }

    private async void MediaPreparationPanel_SaveClipRequested(object? sender, EventArgs e)
    {
        if (_framePreparationCoordinator.CurrentSourceAssetId is not { } sourceAssetId) return;
        if (!MediaPreparationPanelControl.TryCaptureClipDraft(out var draft))
        {
            StatusText.Text = "Enter a name for the Saved Clip.";
            MediaPreparationPanelControl.FocusClipName();
            return;
        }

        await RunUiActionAsync("Saving non-destructive clip…", async () =>
        {
            var clip = await _framePreparationCoordinator.CreateSavedClipAsync(draft);
            RefreshProjectCollections(clip.Id);
            StatusText.Text = $"Saved Clip '{clip.EffectiveDisplayName}' created without copying source media.";
        });
    }

    private void MediaPreparationPanel_SavedFrameSelected(object? sender, SavedFrameSelectionEventArgs e)
    {
        if (e.Item is { } item)
            InspectorPanelControl.Text = InspectorTextFormatter.FormatSavedFrame(item);
    }

    private async void MediaPreparationPanel_SavedFrameUpdateRequested(
        object? sender,
        SavedFrameUpdateRequestedEventArgs e)
    {
        if (_workspace.Project is null) return;
        var item = e.Item;
        var updated = await _framePreparationCoordinator.UpdateSavedFrameAsync(item, e.Label, e.Notes);
        var sourceName = _workspace.Project.Assets
            .SingleOrDefault(asset => asset.Id == updated.Revision.SourceAssetId)?.EffectiveDisplayName;
        foreach (var choice in _referenceChoices.Where(choice =>
                     choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                     choice.LogicalObjectId == updated.Anchor.Id))
            choice.UpdateAnchor(updated.Anchor, updated.Revision, sourceName);
        GenerationPanelControl.RefreshReferences();
        InspectorPanelControl.Text = InspectorTextFormatter.FormatSavedFrame(item);
        StatusText.Text = "Saved Frame details updated.";
    }

    private void MediaPreparationPanel_SavedFrameJumpRequested(object? sender, SavedFrameSelectionEventArgs e)
    {
        if (e.Item is not { } item) return;
        _framePreparationCoordinator.JumpToSavedFrame(item);
        StatusText.Text = $"Jumped to {FormatFrameTimestamp(item.Revision.TimestampSeconds)}.";
    }

    private async void MediaPreparationPanel_SavedFrameRemoveRequested(object? sender, SavedFrameSelectionEventArgs e)
    {
        if (_workspace.Project is null || e.Item is not { } item) return;
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
        var disposition = await _framePreparationCoordinator.RemoveSavedFrameAsync(anchor.Id);
        RefreshProjectCollections();
        if (MediaPreparationPanelControl.IsPreparing && _framePreparationCoordinator.CurrentSourceAssetId is not null)
            await _framePreparationCoordinator.RefreshSavedFramesAsync();
        StatusText.Text = disposition == AnchorRemovalDisposition.Archived
            ? "The referenced Saved Frame was archived; existing history still resolves it."
            : "Saved Frame removed.";
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
        ExpandedPromptEditorControl.CloseEditor(notify: false);
        ProjectMediaPanelControl.SelectedItem = null;
        GenerationHistoryPanelControl.ClearSelection();
        _referenceChoices.Clear();
        ResetFrameWorkspace();
        StartEditButton.IsEnabled = false;
        RefreshEditWorkspaceState();

        InspectorPanelControl.Reset();
        GenerationPanelControl.Prompt = string.Empty;
        GenerationPanelControl.Status = string.Empty;
        GenerationPanelControl.SetLineage("New root generation");
        ClearMediaPreview();
    }

    private void RefreshProjectCollections(Guid? selectedAssetId = null)
    {
        if (_workspace.Project is null) return;
        var hasExplicitSelection = selectedAssetId.HasValue;
        selectedAssetId ??= ProjectMediaPanelControl.SelectedItem?.Asset?.Id;
        var selectedAnchorId = ProjectMediaPanelControl.SelectedItem?.Anchor?.Id;
        var existingChoices = _referenceChoices.ToList();
        _assets.Clear();
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

        GenerationHistoryPanelControl.SetGenerations(
            _workspace.Project.Generations.OrderByDescending(item => item.RequestedAt));

        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} media items";
        RefreshEditWorkspaceState();
        var selection = selectedAssetId is { } id
            ? _assets.FirstOrDefault(item => item.Asset?.Id == id)
            : selectedAnchorId is { } anchorId
                ? _assets.FirstOrDefault(item => item.Anchor?.Id == anchorId)
                : null;
        var preserveActiveOperation = !hasExplicitSelection && MediaPreparationPanelControl.IsPreparing;
        if (preserveActiveOperation) _suppressProjectMediaSelection = true;
        try
        {
            ProjectMediaPanelControl.SelectedItem = selection;
        }
        finally
        {
            _suppressProjectMediaSelection = false;
        }
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
        InspectorPanelControl.Text = $"{title}\n\n{exception}";
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

}
