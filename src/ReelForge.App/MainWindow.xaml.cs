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
    private readonly ApplicationRuntime _runtime;
    private readonly SemaphoreSlim _frameNavigationGate = new(1, 1);
    private readonly ProjectWorkspace _workspace;
    private readonly PortableProjectStore _projectStore;
    private readonly ProjectMediaOperationsCoordinator _projectMediaOperationsCoordinator;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private readonly PhysicalAssetSelectionPreparationService _physicalAssetSelectionPreparationService;
    private readonly ExactVideoFrameService _exactFrameService;
    private readonly RecipeMediaMaterializer _mediaMaterializer;
    private readonly FfmpegAudioExtractionEngine _audioExtractionEngine;
    private readonly ISecretStore _secretStore;
    private readonly FileApplicationDiagnosticLog _diagnosticLog;
    private readonly IMediaToolDiscovery _mediaToolDiscovery;
    private readonly IApplicationSettingsStore _applicationSettingsStore;
    private readonly RecentProjectTracker _recentProjectTracker;
    private readonly ProjectLifecycleDialogs _projectDialogs;
    private readonly ITemporaryAssetHost _temporaryAssetHost;
    private ApplicationSettings _applicationSettings;
    private MediaToolAvailability _mediaTools;
    private readonly GenerationWorkspaceCoordinator _generationWorkspace;
    private readonly GenerationContinuationCoordinator _generationContinuation;
    private readonly GenerationSubmissionCoordinator _generationSubmission;
    private readonly CompositionWorkspaceCoordinator _compositionWorkspace;
    private readonly FramePreparationCoordinator _framePreparationCoordinator;
    private readonly GenerationJobCoordinator _jobCoordinator;
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
            _runtime.ProjectAssetTransferWorkflow,
            _runtime.MaterializedProjectMediaTransferService);
        _physicalAssetSelectionPreparationService = new PhysicalAssetSelectionPreparationService(_workspace, _mediaInspector);
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
        _compositionWorkspace = new CompositionWorkspaceCoordinator(
            _workspace,
            CompositionTimelineControl,
            EditToolsPanelControl,
            GetCurrentTimelinePlaybackSeconds,
            () => _activeCompositionPreviewRevisionId is not null || _compositionAuditionController.IsActive,
            () => MediaPreviewPanelControl.IsPlaying,
            () => MediaPreviewPanelControl.HasVideoSource && !MediaPreviewPanelControl.IsPriming && MediaPreviewPanelControl.IsPlaybackEnabled,
            IsWorkingCompositionSelected,
            new CompositionWorkspaceHost(this),
            _mediaMaterializer,
            _exactFrameService,
            _audioExtractionEngine,
            _mediaInspector);
        _compositionWorkspace.StateChanged += CompositionWorkspace_StateChanged;
        _secretStore = _runtime.SecretStore;
        _diagnosticLog = _runtime.DiagnosticLog;
        _temporaryAssetHost = _runtime.TemporaryAssetHost;
        _jobCoordinator = _runtime.JobCoordinator;
        JobsPanelControl.Initialize(_jobCoordinator);
        JobsChromeControl.Initialize(_jobCoordinator);

        ProjectMediaPanelControl.SetItemsSource(_assets);
        _generationWorkspace = new GenerationWorkspaceCoordinator(
            _runtime,
            _workspace,
            GenerationPanelControl,
            ExpandedPromptEditorControl,
            _referenceChoices);
        _generationWorkspace.ReferenceSelectionRequested += GenerationWorkspace_ReferenceSelectionRequested;
        _generationContinuation = new GenerationContinuationCoordinator(
            _workspace, _projectStore, _exactFrameService, new PhysicalAssetMaterializer(),
            new GenerationContinuationPresentation(this),
            draft => _generationWorkspace.LoadDraft(draft),
            () => _generationWorkspace.ProviderChoices,
            () => _generationWorkspace.CurrentProvider);
        _generationSubmission = new GenerationSubmissionCoordinator(
            _runtime,
            _workspace,
            _generationWorkspace,
            _jobCoordinator,
            _runtime.JobFinalizer,
            _secretStore,
            new GenerationSubmissionPresentation(this));

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
        _generationSubmission.Dispose();
        _generationWorkspace.ReferenceSelectionRequested -= GenerationWorkspace_ReferenceSelectionRequested;
        _generationWorkspace.Dispose();
        JobsPanelControl.Dispose();
        JobsChromeControl.Dispose();
        _framePreparationCoordinator.StatusChanged -= FramePreparation_StatusChanged;
        _framePreparationCoordinator.SavedFramesProjected -= FramePreparation_SavedFramesProjected;
        _framePreparationCoordinator.Dispose();
        _compositionRenderCancellation?.Cancel();
        _compositionAuditionController.PositionChanged -= CompositionAudition_PositionChanged;
        _compositionAuditionController.Dispose();
        _compositionWorkspace.StateChanged -= CompositionWorkspace_StateChanged;
        _compositionWorkspace.Dispose();
        CompositionTimelineControl.Dispose();
        MediaPreviewPanelControl.Dispose();
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

    private void RefreshProviderRuntime(string? preferredProviderId) =>
        _generationWorkspace.RefreshProviders(preferredProviderId);


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
        var activeDraft = _workspace.Project is null ? null : _generationWorkspace.CaptureDraft();
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
        var selectedProviderId = _generationWorkspace.CurrentProvider.Capabilities.ProviderId;
        _applicationSettings = await _runtime.ReloadAndApplySettingsAsync();
        _mediaTools = _runtime.MediaTools;
        MediaToolsText.Text = _mediaTools.Summary;
        RefreshProviderRuntime(selectedProviderId);
        if (activeDraft is not null && _generationWorkspace.CurrentProvider.Capabilities.ProviderId.Equals(
                activeDraft.ProviderId,
                StringComparison.Ordinal))
        {
            _generationWorkspace.LoadDraft(activeDraft);
        }
        StatusText.Text = "Application settings and provider availability applied.";
    }

    private void GenerationWorkspace_ReferenceSelectionRequested(
        object? sender,
        GenerationReferenceSelectionRequestedEventArgs e)
    {
        ProjectMediaPanelControl.SelectedItem = _assets.FirstOrDefault(item =>
            e.ObjectKind == GenerationReferenceObjectKind.Asset
                ? item.Asset?.Id == e.LogicalObjectId
                : item.Anchor?.Id == e.LogicalObjectId);
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
                var resolution = await _physicalAssetSelectionPreparationService.PrepareAsync(
                    asset,
                    selectedProjectId,
                    _mediaTools.FfprobePath is not null);
                if (resolution.Kind == PhysicalAssetSelectionPreparationKind.Stale)
                    return;

                if (resolution.Kind == PhysicalAssetSelectionPreparationKind.Missing)
                {
                    InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
                    ShowAssetPreview(asset);
                    MediaPreparationPanelControl.SetWorkspaceStatus("Source media is missing");
                    StatusText.Text = $"{asset.FileName} is missing from its recorded project location.";
                    return;
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
            _compositionWorkspace.Clear();
            UpdateCompositionActionState();
            return;
        }
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
        _compositionWorkspace.Refresh();
        UpdateCompositionActionState();
        if (_compositionAuditionController.RecipeRevisionId is { } draftRevisionId && draftRevisionId != revision.Id &&
            ProjectMediaPanelControl.SelectedItem is ProjectMediaListItem selectedItem &&
            selectedItem.Asset?.Id == composition.Id)
        {
            ClearMediaPreview();
            _ = OpenCompositionDraftPreviewAsync(composition, selectedItem, _workspace.Project.Id);
        }
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

    private void UpdateCompositionActionState()
    {
        PreviewCompositionButton.IsEnabled = _compositionWorkspace.HasSegments && _compositionRenderCancellation is null;
        ExportCompositionButton.IsEnabled = _compositionWorkspace.HasSegments && _compositionRenderCancellation is null;
    }

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

    private void CompositionWorkspace_StateChanged(object? sender, EventArgs e) =>
        UpdateCompositionActionState();

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
        await _generationSubmission.SubmitAsync(_applicationSettings.General.UndoSendSeconds);
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

    private void CompositionTimeline_ActivationRequested(
        object? sender,
        CompositionTimelineActivationEventArgs e)
    {
        _pendingCompositionTimelineSeekSeconds = e.PendingRulerSeekSeconds;
        SelectWorkingCompositionInProjectMedia();
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
            // Saved Frames are deliberately copy-only. The context menu hides
            // Move for anchors; this guard protects routed UI invocations.
            return;
        }
        if (selected.Asset is not { } asset) return;
        if (asset.StorageKind != AssetStorageKind.Physical)
        {
            // Cache-backed Project Media is deliberately copy-only. The context
            // menu hides Move for it; this guard protects routed UI invocations.
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
        var targetProjectFile = ChooseTransferTargetProject();
        if (targetProjectFile is null) return;

        if (selected.Anchor is { } anchor && selected.AnchorRevision is { } anchorRevision)
        {
            var fileName = selected.DisplayName;
            await RunUiActionAsync($"Copying {selected.DisplayName}…", async () =>
            {
                var result = await _projectMediaOperationsCoordinator.CopySavedFrameToProjectAsync(
                    anchor, anchorRevision, fileName, targetProjectFile);
                StatusText.Text = $"Copied {selected.DisplayName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.";
            });
            return;
        }

        if (selected.Asset is not { } asset) return;
        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            if (asset.MediaType != MediaType.Video ||
                asset.Virtual?.Kind is not (VirtualAssetKind.SavedClip or VirtualAssetKind.Composition) ||
                asset.Virtual.CurrentRecipeRevisionId is not { } recipeRevisionId) return;
            var fileName = asset.EffectiveDisplayName;
            await RunUiActionAsync($"Copying {asset.EffectiveDisplayName}…", async () =>
            {
                var result = await _projectMediaOperationsCoordinator.CopyVirtualVideoToProjectAsync(
                    asset, recipeRevisionId, fileName, targetProjectFile);
                StatusText.Text = $"Copied {asset.EffectiveDisplayName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.";
            });
            return;
        }
        if (asset.StorageKind != AssetStorageKind.Physical) return;
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

    private async void JobsPanelControl_CancelRequested(
        object? sender,
        GenerationJobCancelRequestedEventArgs e) =>
        await _generationSubmission.CancelQueuedAsync(e.GenerationId);

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
            await _generationContinuation.PrepareAsync(source, relationship);
            return;
        }

        var draft = GenerationWorkflow.CreateDerivedDraft(source, relationship);
        _generationWorkspace.LoadDraft(draft);
        await _generationWorkspace.CurrentWorkflow.SaveDraftAsync(draft);
        GenerationPanelControl.Status =
            $"Drafted {relationship} from generation {source.Id}. Review it, then use the submission button.";
    }

    private async void GenerationPanel_NewRootRequested(object? sender, EventArgs e)
    {
        if (!EnsureProjectOpen()) return;
        var draft = _generationWorkspace.CaptureDraft();
        draft.ParentGenerationId = null;
        draft.RelationshipType = null;
        _generationWorkspace.LoadDraft(draft);
        await _generationWorkspace.CurrentWorkflow.SaveDraftAsync(draft);
        GenerationPanelControl.Status = "Started a new root generation draft.";
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

        ResetProjectSpecificUi();
        RefreshProjectCollections();
        if (_workspace.Project.CurrentGenerationDraft is { } draft)
            _generationWorkspace.LoadDraft(draft);
        RestoreProjectUiState();
        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} media items";
        Title = $"{_workspace.Project.Name} — ReelForge";
        StatusText.Text = $"Opened {_workspace.Location!.ProjectFilePath}";
    }

    private void ResetProjectSpecificUi()
    {
        ProjectMediaPanelControl.SelectedItem = null;
        GenerationHistoryPanelControl.ClearSelection();
        _generationWorkspace.Reset();
        ResetFrameWorkspace();
        StartEditButton.IsEnabled = false;
        RefreshEditWorkspaceState();

        InspectorPanelControl.Reset();
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

    private sealed class GenerationSubmissionPresentation(MainWindow window) : IGenerationSubmissionPresentation
    {
        public void ShowProjectRequired()
        {
            MessageBox.Show(window, "Create or open a project first.", "ReelForge", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public bool ConfirmPotentiallyBillableSubmission(IVideoGenerationProvider provider, GenerationDraft draft) =>
            MessageBox.Show(
                window,
                $"Review the prompt settings before submitting to {provider.Capabilities.DisplayName}.\n\n" +
                $"Model: {provider.Capabilities.ModelVersion}\n" +
                $"Mode: {draft.Mode}\nDuration: {draft.DurationSeconds}s\n" +
                $"Resolution: {draft.Resolution}\nReferences: {draft.References.Count}\n\n" +
                "Proceed with these settings?",
                "Confirm prompt submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;

        public void ShowError(string title, Exception exception) => window.ShowError(title, exception);
        public void SetGenerationStatus(string status) => window.GenerationPanelControl.Status = status;
        public void SetStatus(string status) => window.StatusText.Text = status;
        public void SetSubmissionEnabled(bool enabled) => window.GenerationPanelControl.IsSubmissionEnabled = enabled;
        public void SetProjectActionsEnabled(bool enabled) => window.SetProjectActionsEnabled(enabled);
        public void RefreshProjectCollections() => window.RefreshProjectCollections();
        public void SelectGeneration(Guid generationId) => window.GenerationHistoryPanelControl.SelectGeneration(generationId);

        public void MergeGenerationState(GenerationRecord source)
        {
            var target = window._workspace.Project?.Generations.SingleOrDefault(candidate => candidate.Id == source.Id);
            if (target is null || ReferenceEquals(target, source)) return;
            target.ProviderJobId = source.ProviderJobId;
            target.Status = source.Status;
            target.IngestionStatus = source.IngestionStatus;
            target.CompletedAt = source.CompletedAt;
            target.OutputAssetIds = source.OutputAssetIds.ToList();
            target.ResponseMetadata = new Dictionary<string, string>(source.ResponseMetadata, StringComparer.Ordinal);
            target.Error = source.Error;
        }

        public void TryAutoPreview(GenerationRecord generation) =>
            window.TryAutoPreviewGeneratedOutput(generation, owningProjectIsOpen: true);

        public void BeginInvoke(Action action)
        {
            if (!window._disposed && !window.Dispatcher.HasShutdownStarted)
                _ = window.Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }

    private sealed class CompositionWorkspaceHost(MainWindow window) : ICompositionWorkspaceHost
    {
        public Task RunUiActionAsync(string status, Func<Task> action) => window.RunUiActionAsync(status, action);
        public void SetStatus(string status) => window.StatusText.Text = status;
        public void RefreshProjectMedia() => window.RefreshProjectCollections(window._workspace.Project?.WorkingCompositionAssetId);
        public void PausePreview() => window.MediaPreviewPanelControl.Pause();
        public MediaSplitBehavior SplitBehavior => window._applicationSettings.MediaTools.SplitBehavior;
        public string? PromptDetachAudioFileName(string displayName)
        {
            var stem = MakeSafeFileName(Path.GetFileNameWithoutExtension(displayName));
            var dialog = new AssetNameDialog($"{stem} detached audio.m4a", "Detach segment audio", "DETACH SEGMENT AUDIO",
                "Create a permanent audio file from this exact timeline segment, add it at the same timeline position, and mute the segment's embedded audio to prevent doubled sound.", "Detach") { Owner = window };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }

    private sealed class GenerationContinuationPresentation(MainWindow window) : IGenerationContinuationPresentation
    {
        public ProjectAsset? SelectOutput(IReadOnlyList<ProjectAsset> outputs)
        {
            var dialog = new GenerationOutputSelectionDialog(outputs) { Owner = window };
            return dialog.ShowDialog() == true ? dialog.SelectedOutput : null;
        }

        public bool ConfirmFrame(
            BitmapSource bitmap,
            string heading,
            string sourceName,
            double timestampSeconds,
            long presentationTimestamp,
            int timeBaseNumerator,
            int timeBaseDenominator)
        {
            var dialog = new FrameConfirmationDialog(
                bitmap,
                heading,
                sourceName,
                timestampSeconds,
                presentationTimestamp,
                timeBaseNumerator,
                timeBaseDenominator)
            {
                Owner = window
            };
            return dialog.ShowDialog() == true;
        }

        public BitmapSource LoadBitmap(string path) => MainWindow.LoadBitmap(path);
        public Task RunUiActionAsync(string status, Func<Task> action) => window.RunUiActionAsync(status, action);
        public void RefreshProjectCollections() => window.RefreshProjectCollections();
        public bool HasCurrentFrameSource(Guid assetId) => window._framePreparationCoordinator.HasCurrentSource(assetId);
        public Task RefreshSavedFramesAsync() => window._framePreparationCoordinator.RefreshSavedFramesAsync();
        public void SelectGenerateTab() => window.RightPanelTabs.SelectedIndex = 1;
        public void SetStatus(string status) => window.GenerationPanelControl.Status = status;
    }

}
