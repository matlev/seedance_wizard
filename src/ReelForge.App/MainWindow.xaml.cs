using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
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
    private readonly ProjectWorkspace _workspace;
    private readonly PortableProjectStore _projectStore;
    private readonly ProjectMediaOperationsCoordinator _projectMediaOperationsCoordinator;
    private readonly ProjectMediaCommandCoordinator _projectMediaCommandCoordinator;
    private readonly MediaImportCoordinator _mediaImportCoordinator;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private readonly PhysicalAssetSelectionPreparationService _physicalAssetSelectionPreparationService;
    private readonly ExactVideoFrameService _exactFrameService;
    private readonly RecipeMediaMaterializer _mediaMaterializer;
    private readonly FfmpegAudioExtractionEngine _audioExtractionEngine;
    private readonly ISecretStore _secretStore;
    private readonly FileApplicationDiagnosticLog _diagnosticLog;
    private readonly IMediaToolDiscovery _mediaToolDiscovery;
    private readonly IApplicationSettingsStore _applicationSettingsStore;
    private readonly ProjectLifecycleDialogs _projectDialogs;
    private readonly ProjectLifecycleCoordinator _projectLifecycleCoordinator;
    private readonly ITemporaryAssetHost _temporaryAssetHost;
    private ApplicationSettings _applicationSettings;
    private MediaToolAvailability _mediaTools;
    private readonly GenerationWorkspaceCoordinator _generationWorkspace;
    private readonly GenerationContinuationCoordinator _generationContinuation;
    private readonly GenerationSubmissionCoordinator _generationSubmission;
    private readonly CompositionWorkspaceCoordinator _compositionWorkspace;
    private readonly CompositionRenderCoordinator _compositionRenderCoordinator;
    private readonly MediaPreviewCoordinator _mediaPreviewCoordinator;
    private readonly FramePreparationCoordinator _framePreparationCoordinator;
    private readonly GenerationJobCoordinator _jobCoordinator;
    private bool _suppressProjectMediaSelection;
    private CancellationTokenSource? _projectMediaSelectionCancellation;
    private ProjectWorkspaceKind _activeWorkspace = ProjectWorkspaceKind.Generate;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();

        _runtime = ApplicationRuntime.Create();
        _mediaToolDiscovery = _runtime.MediaToolDiscovery;
        _applicationSettingsStore = _runtime.ApplicationSettingsStore;
        _projectDialogs = new ProjectLifecycleDialogs(this, _runtime.Paths);
        _applicationSettings = _runtime.Settings;
        _mediaTools = _runtime.MediaTools;
        _mediaInspector = _runtime.MediaInspector;
        _exactFrameService = _runtime.ExactFrameService;
        _mediaMaterializer = _runtime.MediaMaterializer;
        _audioExtractionEngine = _runtime.AudioExtractionEngine;
        _projectStore = _runtime.ProjectStore;
        _workspace = _runtime.Workspace;
        _projectLifecycleCoordinator = new ProjectLifecycleCoordinator(
            _workspace,
            _runtime.RecentProjectTracker,
            _projectDialogs,
            _applicationSettingsStore,
            () => _applicationSettings,
            new ProjectLifecyclePresentation(this));
        _projectMediaOperationsCoordinator = new ProjectMediaOperationsCoordinator(
            _workspace,
            _runtime.RenderedAssetPromotionService,
            _runtime.AudioExtractionService,
            _runtime.ProjectAssetDependencyAnalyzer,
            _runtime.PhysicalAssetRelinkService,
            _runtime.PhysicalAssetRemovalService,
            _runtime.ProjectAssetTransferWorkflow,
            _runtime.MaterializedProjectMediaTransferService);
        _projectMediaCommandCoordinator = new ProjectMediaCommandCoordinator(
            _projectMediaOperationsCoordinator,
            new ProjectMediaCommandPresentation(this));
        _mediaImportCoordinator = new MediaImportCoordinator(
            _projectMediaOperationsCoordinator,
            new MediaImportPresentation(this));
        _physicalAssetSelectionPreparationService = new PhysicalAssetSelectionPreparationService(_workspace, _mediaInspector);
        _mediaPreviewCoordinator = new MediaPreviewCoordinator(
            _workspace, _mediaMaterializer, _exactFrameService, MediaPreviewPanelControl,
            CompositionTimelineControl, new MediaPreviewCoordinatorHost(this));
        _framePreparationCoordinator = new FramePreparationCoordinator(
            _workspace,
            _exactFrameService,
            _mediaMaterializer,
            MediaPreparationPanelControl,
            MediaPreviewPanelControl,
            _mediaPreviewCoordinator.FrameNavigationGate);
        _framePreparationCoordinator.StatusChanged += FramePreparation_StatusChanged;
        _framePreparationCoordinator.SavedFramesProjected += FramePreparation_SavedFramesProjected;
        _compositionWorkspace = new CompositionWorkspaceCoordinator(
            _workspace,
            CompositionTimelineControl,
            EditToolsPanelControl,
            () => _mediaPreviewCoordinator.CurrentTimelinePosition,
            () => _mediaPreviewCoordinator.IsPreviewActive,
            () => _mediaPreviewCoordinator.IsPlaying,
            () => _mediaPreviewCoordinator.IsPlaybackEnabled,
            IsWorkingCompositionSelected,
            new CompositionWorkspaceHost(this),
            _mediaMaterializer,
            _exactFrameService,
            _audioExtractionEngine,
            _mediaInspector);
        _compositionRenderCoordinator = new CompositionRenderCoordinator(
            _workspace,
            _mediaMaterializer,
            _projectMediaOperationsCoordinator,
            new CompositionRenderPresentation(this));
        _compositionWorkspace.StateChanged += CompositionWorkspace_StateChanged;
        _compositionWorkspace.RecipeMutationCommitted += CompositionWorkspace_RecipeMutationCommitted;
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
            _workspace, _exactFrameService, _mediaMaterializer,
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
        CancelProjectMediaSelectionWork();
        _generationSubmission.Dispose();
        _generationWorkspace.ReferenceSelectionRequested -= GenerationWorkspace_ReferenceSelectionRequested;
        _generationWorkspace.Dispose();
        JobsPanelControl.Dispose();
        JobsChromeControl.Dispose();
        _framePreparationCoordinator.StatusChanged -= FramePreparation_StatusChanged;
        _framePreparationCoordinator.SavedFramesProjected -= FramePreparation_SavedFramesProjected;
        _framePreparationCoordinator.Dispose();
        _compositionRenderCoordinator.Dispose();
        _mediaPreviewCoordinator.Dispose();
        _compositionWorkspace.StateChanged -= CompositionWorkspace_StateChanged;
        _compositionWorkspace.RecipeMutationCommitted -= CompositionWorkspace_RecipeMutationCommitted;
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

        await _projectLifecycleCoordinator.TryReopenLastProjectAsync();
    }

    private void RefreshProviderRuntime(string? preferredProviderId) =>
        _generationWorkspace.RefreshProviders(preferredProviderId);


    private async void WorkspaceMode_Checked(object sender, RoutedEventArgs e)
    {
        if (RightPanelTabs is null) return;
        var beganDuringProjectUiStateRestoration = _projectLifecycleCoordinator.IsRestoringProjectUiState;
        if (JobsPanelControl.IsOpen)
        {
            await JobsPanelControl.HideJobsAsync();
            JobsChromeControl.SetJobsOpen(false);
        }
        _activeWorkspace = EditWorkspaceButton.IsChecked == true
            ? ProjectWorkspaceKind.Edit
            : ProjectWorkspaceKind.Generate;
        ApplyWorkspaceMode();
        if (!beganDuringProjectUiStateRestoration)
            await _projectLifecycleCoordinator.SaveProjectUiStateAsync();
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
        await _projectLifecycleCoordinator.CreateProjectFromDialogAsync();
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        await _projectLifecycleCoordinator.OpenProjectFromDialogAsync();
    }

    private async void ImportAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectOpen()) return;
        if (!_mediaImportCoordinator.CanBeginImport) return;
        var fileNames = _projectDialogs.SelectMediaToImport();
        await _mediaImportCoordinator.ImportAsync(MediaImportInput.FromDialogSelection(fileNames));
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
        var input = MediaImportInput.AnalyzeExternalDrop(droppedFiles);
        var canImport = _mediaImportCoordinator.CanImport(input);

        e.Effects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        HideMediaDropOverlay();

        if (!canImport) return;
        await _mediaImportCoordinator.ImportAsync(input);
    }

    private void UpdateMediaDropFeedback(DragEventArgs e)
    {
        var droppedFiles = GetDroppedFiles(e.Data);
        var input = MediaImportInput.AnalyzeExternalDrop(droppedFiles);
        var canImport = _mediaImportCoordinator.CanImport(input);

        e.Effects = canImport ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        if (!canImport)
        {
            HideMediaDropOverlay();
            return;
        }

        var mediaDescription = input.FilePaths.Count == 1 ? "1 media file" : $"{input.FilePaths.Count} media files";
        var ignoredDescription = input.SkippedCount == 0
            ? string.Empty
            : $" {input.SkippedCount} unsupported {(input.SkippedCount == 1 ? "item" : "items")} will be skipped.";
        ShowMediaDropOverlay($"Drop to add {mediaDescription} to {_workspace.Project!.Name}.{ignoredDescription}");
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
        CancelProjectMediaSelectionWork();
        if (e.SelectedItem is not { } item)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _projectMediaSelectionCancellation = cancellation;
        var selection = ProjectMediaSelectionIdentity.Capture(_workspace, item, cancellation.Token);
        if (selection is null)
        {
            return;
        }

        GenerationHistoryPanelControl.ClearSelection();
        ResetFrameWorkspace();
        if (item.Anchor is not null && item.AnchorRevision is not null)
        {
            UpdateCompositionActionState();
            if (!_projectLifecycleCoordinator.IsRestoringProjectUiState)
                await _projectLifecycleCoordinator.SaveProjectUiStateAsync("anchor", item.Anchor.Id);
            if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
            await ShowSavedFramePreviewAsync(selection);
            return;
        }

        if (item.Asset is not { } asset) return;
        if (!_projectLifecycleCoordinator.IsRestoringProjectUiState)
            await _projectLifecycleCoordinator.SaveProjectUiStateAsync("asset", asset.Id);
        if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;

        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
            ConfigureMediaPreparationFor(asset);
            await ShowVirtualAssetPreviewAsync(asset, selection);
            return;
        }

        await RunProjectMediaSelectionActionAsync(
            selection,
            $"Inspecting {asset.FileName}…",
            async cancellationToken =>
            {
                var resolution = await _physicalAssetSelectionPreparationService.PrepareAsync(
                    asset,
                    selection.Project,
                    selection.Location,
                    _mediaTools.FfprobePath is not null,
                    cancellationToken);
                if (resolution.Kind == PhysicalAssetSelectionPreparationKind.Stale ||
                    !selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem))
                    return;

                if (resolution.Kind is PhysicalAssetSelectionPreparationKind.Missing or
                    PhysicalAssetSelectionPreparationKind.Inaccessible or
                    PhysicalAssetSelectionPreparationKind.Mismatched)
                {
                    InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
                    ShowAssetPreview(asset);
                    var (workspaceStatus, status) = resolution.Kind switch
                    {
                        PhysicalAssetSelectionPreparationKind.Missing =>
                            ("Source media is missing", $"{asset.FileName} is missing from its recorded project location."),
                        PhysicalAssetSelectionPreparationKind.Inaccessible =>
                            ("Source media is inaccessible", $"{asset.FileName} cannot be accessed at its recorded project location."),
                        PhysicalAssetSelectionPreparationKind.Mismatched =>
                            ("Source media does not match", $"{asset.FileName} no longer matches its recorded SHA-256 identity."),
                        _ => throw new InvalidOperationException("Unknown physical asset selection result.")
                    };
                    MediaPreparationPanelControl.SetWorkspaceStatus(workspaceStatus);
                    StatusText.Text = status;
                    return;
                }

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
            Physical.Availability: PhysicalAssetAvailability.Available
        };
        MediaPreparationPanelControl.ConfigureSelection(asset.EffectiveDisplayName, canPrepare);
        StartEditButton.IsEnabled = _workspace.Project?.WorkingCompositionAssetId is null &&
                                    asset.MediaType == MediaType.Video &&
                                    (asset is { StorageKind: AssetStorageKind.Physical, Physical.Availability: PhysicalAssetAvailability.Available } ||
                                     asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        UpdateCompositionActionState();
    }

    private async void StartEdit_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedAsset() is not { } source) return;
        await RunUiActionAsync("Creating Working Composition…", async () =>
        {
            await _compositionWorkspace.CreateInitialCompositionAsync(source.Id);
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
        _mediaPreviewCoordinator.ClearStaleCompositionPreviewIfNeeded(composition, revision);
        var recipe = (CompositionRecipe)revision.Recipe;
        WorkingCompositionSummaryText.Text =
            $"{recipe.Segments.Count} video segment{(recipe.Segments.Count == 1 ? string.Empty : "s")} • " +
            $"{recipe.AudioClips.Count} audio clip{(recipe.AudioClips.Count == 1 ? string.Empty : "s")} • " +
            $"exact, revision-pinned sources • recipe revision {revision.RevisionNumber}";
        _compositionWorkspace.Refresh();
        UpdateCompositionActionState();
        if (_mediaPreviewCoordinator.AuditionRecipeRevisionId is { } draftRevisionId && draftRevisionId != revision.Id &&
            ProjectMediaPanelControl.SelectedItem is ProjectMediaListItem selectedItem &&
            selectedItem.Asset?.Id == composition.Id)
        {
            _mediaPreviewCoordinator.Clear();
            _ = _mediaPreviewCoordinator.OpenCompositionDraftAsync(composition, revision);
        }
    }

    private async void PreviewComposition_Click(object sender, RoutedEventArgs e)
    {
        await _compositionRenderCoordinator.PreviewAsync();
    }

    private async void ExportComposition_Click(object sender, RoutedEventArgs e)
    {
        await _compositionRenderCoordinator.ExportAsync();
    }

    private void CancelCompositionRender_Click(object sender, RoutedEventArgs e)
    {
        _compositionRenderCoordinator.Cancel();
    }

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

    private void UpdateCompositionActionState()
    {
        PreviewCompositionButton.IsEnabled = _compositionWorkspace.HasSegments &&
                                             !_compositionRenderCoordinator.IsRendering &&
                                             IsWorkingCompositionSelected();
        ExportCompositionButton.IsEnabled = _compositionWorkspace.HasSegments && !_compositionRenderCoordinator.IsRendering;
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

    private void CompositionWorkspace_RecipeMutationCommitted(object? sender, EventArgs e)
    {
        var composition = _workspace.Project?.WorkingCompositionAssetId is { } compositionId
            ? _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == compositionId)
            : null;
        if (composition?.Virtual?.CurrentRecipeRevisionId is not { } revisionId)
            return;
        var revision = _workspace.Project!.RecipeRevisions.SingleOrDefault(candidate => candidate.Id == revisionId);
        if (revision is not null)
            _mediaPreviewCoordinator.ClearStaleCompositionPreviewIfNeeded(composition, revision);
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
        await _generationSubmission.SubmitAsync(_applicationSettings.General.UndoSendSeconds);
    }

    private async void ProjectMediaPanel_ActionRequested(
        object? sender,
        ProjectMediaActionRequestedEventArgs e)
    {
        await _projectMediaCommandCoordinator.HandleAsync(e.Action, ProjectMediaPanelControl.SelectedItem);
    }

    private void ProjectMediaPanel_DragCompleted(object? sender, EventArgs e) =>
        CompositionTimelineControl.CancelExternalDrag();


    private async Task ShowSavedFramePreviewAsync(ProjectMediaSelectionIdentity selection)
    {
        if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
        var item = selection.Item;
        if (item.Anchor is not { } anchor || item.AnchorRevision is not { } revision) return;

        await RunProjectMediaSelectionActionAsync(selection, $"Loading {item.DisplayName}…", async cancellationToken =>
        {
            MaterializedMediaLease? media = null;
            try
            {
                media = await _mediaMaterializer.MaterializeAsync(
                    selection.Project,
                    selection.Location,
                    new MaterializationRequest(
                        new AnchorMaterializationTarget(anchor.Id, revision.Id),
                        MaterializationPurpose.Preview),
                    cancellationToken);
                if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
                var thumbnail = LoadBitmap(media.Path);
                item.Thumbnail = thumbnail;
                foreach (var choice in _referenceChoices.Where(choice =>
                             choice.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                             choice.LogicalObjectId == anchor.Id))
                    choice.UpdateThumbnail(thumbnail);
                ProjectMediaPanelControl.RefreshItems();
                GenerationPanelControl.RefreshReferences();
                _mediaPreviewCoordinator.Clear();
                MediaPreviewPanelControl.ShowImage(thumbnail);
                InspectorPanelControl.Text = InspectorTextFormatter.FormatSavedFrame(
                    new SavedFrameListItem(anchor, revision, thumbnail, error: null));
                StatusText.Text = $"Selected Saved Frame {item.DisplayName}.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
                _mediaPreviewCoordinator.Clear();
                MediaPreviewPanelControl.ShowPlaceholder($"Saved Frame preview unavailable\n\n{exception.Message}");
                InspectorPanelControl.Text = InspectorTextFormatter.FormatSavedFrame(
                    new SavedFrameListItem(anchor, revision, thumbnail: null, exception.Message));
                StatusText.Text = $"Could not display {item.DisplayName}.";
            }
            finally
            {
                if (media is not null) await media.DisposeAsync();
            }
        });
    }

    private ProjectAsset? GetSelectedAsset() => ProjectMediaPanelControl.SelectedItem?.Asset;

    private void ClearProjectMediaSelectionAndPreview()
    {
        ProjectMediaPanelControl.SelectedItem = null;
        InspectorPanelControl.Reset();
        _mediaPreviewCoordinator.Clear();
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
        _mediaPreviewCoordinator.Clear();
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

        _mediaPreviewCoordinator.OpenVideo(absolutePath, requiresWarmup: true);
    }

    private async Task ShowVirtualAssetPreviewAsync(
        ProjectAsset asset,
        ProjectMediaSelectionIdentity selection)
    {
        if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
        var selectedItem = selection.Item;
        if (asset.Virtual?.Kind == VirtualAssetKind.Composition)
        {
            InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
            var revision = selection.Project.RecipeRevisions.Single(candidate => candidate.Id == asset.Virtual.CurrentRecipeRevisionId);
            var restoreOutcome = await _mediaPreviewCoordinator.TryOpenRetainedCompositionPreviewAsync(
                    asset,
                    revision,
                    selection);
            if (restoreOutcome is RetainedCompositionPreviewRestoreOutcome.Restored or
                RetainedCompositionPreviewRestoreOutcome.Stale)
            {
                return;
            }
            if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
            if (restoreOutcome == RetainedCompositionPreviewRestoreOutcome.Failed)
                StatusText.Text = "Composition preview could not be restored; opening fast audition instead.";
            await _mediaPreviewCoordinator.OpenCompositionDraftAsync(asset, revision, selection);
            return;
        }
        var kindName = asset.Virtual?.Kind == VirtualAssetKind.Composition
            ? "Working Composition"
            : "Saved Clip";
        await RunProjectMediaSelectionActionAsync(selection, $"Preparing {asset.EffectiveDisplayName}…", async cancellationToken =>
        {
            MaterializedMediaLease? lease = null;
            try
            {
                lease = await _mediaMaterializer.MaterializeAsync(
                    selection.Project,
                    selection.Location,
                        new MaterializationRequest(
                            new AssetMaterializationTarget(asset.Id, asset.Virtual?.CurrentRecipeRevisionId),
                        MaterializationPurpose.Preview),
                    cancellationToken);
                if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem))
                {
                    await lease.DisposeAsync();
                    return;
                }

                InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset, lease.Encoding);
                _mediaPreviewCoordinator.Clear();
                _mediaPreviewCoordinator.OpenLeasedVideo(
                    lease,
                    requiresWarmup: asset.Virtual?.Kind != VirtualAssetKind.SavedClip,
                    compositionRevisionId: asset.Virtual?.Kind == VirtualAssetKind.Composition
                        ? asset.Virtual.CurrentRecipeRevisionId : null);
                lease = null;
                StatusText.Text = $"Selected {kindName} {asset.EffectiveDisplayName}.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
                _mediaPreviewCoordinator.Clear();
                MediaPreviewPanelControl.ShowPlaceholder($"{kindName} preview unavailable\n\n{exception.Message}");
                StatusText.Text = $"Could not prepare {asset.EffectiveDisplayName}.";
            }
            finally
            {
                if (lease is not null) await lease.DisposeAsync();
            }
        });
    }

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
        if (ProjectMediaPanelControl.SelectedItem?.Anchor?.Id == anchor.Id)
            ClearProjectMediaSelectionAndPreview();
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
        _projectLifecycleCoordinator.RestoreProjectUiState();
        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} media items";
        Title = $"{_workspace.Project.Name} — ReelForge";
        StatusText.Text = $"Opened {_workspace.Location!.ProjectFilePath}";
    }

    private void ResetProjectSpecificUi()
    {
        CancelProjectMediaSelectionWork();
        _compositionRenderCoordinator.ResetForProjectChange();
        ProjectMediaPanelControl.SelectedItem = null;
        GenerationHistoryPanelControl.ClearSelection();
        _generationWorkspace.Reset();
        ResetFrameWorkspace();
        StartEditButton.IsEnabled = false;
        RefreshEditWorkspaceState();

        InspectorPanelControl.Reset();
        _mediaPreviewCoordinator.Clear();
        _mediaPreviewCoordinator.ClearRetainedCompositionPreview();
    }

    private void RefreshProjectCollections(Guid? selectedAssetId = null)
    {
        if (_workspace.Project is null) return;
        var hasExplicitSelection = selectedAssetId.HasValue;
        selectedAssetId ??= ProjectMediaPanelControl.SelectedItem?.Asset?.Id;
        var selectedAnchorId = ProjectMediaPanelControl.SelectedItem?.Anchor?.Id;
        var projection = ProjectMediaProjectionBuilder.Build(
            _workspace.Project,
            _workspace.GetAbsoluteAssetPath,
            LoadBitmap,
            _referenceChoices);
        _assets.Clear();
        _referenceChoices.Clear();

        foreach (var item in projection.MediaItems)
            _assets.Add(item);
        foreach (var choice in projection.ReferenceChoices)
            _referenceChoices.Add(choice);

        GenerationHistoryPanelControl.SetGenerations(
            projection.GenerationHistory);

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

    private async Task RunProjectMediaSelectionActionAsync(
        ProjectMediaSelectionIdentity selection,
        string status,
        Func<CancellationToken, Task> action)
    {
        if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
        StatusText.Text = status;
        try
        {
            await action(selection.CancellationToken);
        }
        catch (OperationCanceledException) when (selection.CancellationToken.IsCancellationRequested)
        {
            // A newer Project Media selection superseded this operation.
        }
        catch (Exception exception)
        {
            if (!selection.IsCurrent(_workspace, ProjectMediaPanelControl.SelectedItem)) return;
            ShowError("Operation failed", exception);
        }
    }

    private void CancelProjectMediaSelectionWork()
    {
        var cancellation = Interlocked.Exchange(ref _projectMediaSelectionCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
    }

    private void ShowError(string title, Exception exception)
    {
        StatusText.Text = exception.Message;
        InspectorPanelControl.Text = $"{title}\n\n{exception}";
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private sealed class ProjectLifecyclePresentation(MainWindow window) : IProjectLifecycleCoordinatorHost
    {
        public ProjectWorkspaceKind ActiveWorkspace => window._activeWorkspace;

        public Task RunUiActionAsync(string status, Func<Task> action) => window.RunUiActionAsync(status, action);

        public void RefreshProjectUi() => window.RefreshProjectUi();

        public void ApplyRestoredWorkspaceMode(ProjectWorkspaceKind workspace)
        {
            window._activeWorkspace = workspace;
            window.GenerateWorkspaceButton.IsChecked = workspace == ProjectWorkspaceKind.Generate;
            window.EditWorkspaceButton.IsChecked = workspace == ProjectWorkspaceKind.Edit;
            window.ApplyWorkspaceMode();
        }

        public ProjectMediaListItem? FindProjectMediaItem(string mediaKind, Guid mediaId) =>
            window._assets.FirstOrDefault(item =>
                mediaKind == "asset" ? item.Asset?.Id == mediaId : item.Anchor?.Id == mediaId);

        public void SelectProjectMediaItem(ProjectMediaListItem? item) =>
            window.ProjectMediaPanelControl.SelectedItem = item;

        public void SetStatus(string status) => window.StatusText.Text = status;

        public void AppendStatus(string status) => window.StatusText.Text += status;

        public void SetInspectorText(string text) => window.InspectorPanelControl.Text = text;
    }

    private sealed class ProjectMediaCommandPresentation(MainWindow window) : IProjectMediaCommandHost
    {
        public bool HasOpenProject => window._workspace.Project is not null && window._workspace.Location is not null;

        public Task RunUiActionAsync(string status, Func<Task> action) => window.RunUiActionAsync(status, action);

        public string? PromptPhysicalFileName(string fileName)
        {
            var dialog = new AssetNameDialog(fileName) { Owner = window };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string? PromptRelinkCandidate(ProjectAsset asset)
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Relink source for {asset.FileName}",
                Filter = "Media files|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4a;*.mp3;*.wav;*.aac;*.flac|All files|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                FileName = asset.FileName
            };
            return dialog.ShowDialog(window) == true ? dialog.FileName : null;
        }

        public string? PromptSavedClipDisplayName(string displayName)
        {
            var dialog = new DisplayNameDialog(displayName) { Owner = window };
            return dialog.ShowDialog() == true ? dialog.DisplayName : null;
        }

        public string? PromptExportPath(ProjectMediaExportRequest request)
        {
            if (window._workspace.Location is null) return null;
            var dialog = new SaveFileDialog
            {
                Title = request.Title,
                Filter = request.Filter,
                DefaultExt = request.DefaultExtension,
                AddExtension = true,
                OverwritePrompt = true,
                FileName = request.FileName,
                InitialDirectory = Path.Combine(window._workspace.Location.RootDirectory, "exports")
            };
            return dialog.ShowDialog(window) == true ? dialog.FileName : null;
        }

        public string? PromptAudioExtractionFileName(string suggestedFileName)
        {
            var dialog = new AssetNameDialog(
                suggestedFileName,
                title: "Extract audio",
                heading: "EXTRACT AUDIO",
                description: "Create a permanent .m4a audio file in this project's media folder. The source video remains unchanged.",
                confirmLabel: "Extract")
            {
                Owner = window
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public bool Confirm(string message, string title) =>
            MessageBox.Show(
                window,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) == MessageBoxResult.Yes;

        public void ShowInformation(string message, string title) =>
            MessageBox.Show(window, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public string? ChooseTransferTargetProject()
        {
            var projectFilePath = window._projectDialogs.SelectProjectToOpen(window._applicationSettings);
            if (projectFilePath is null) return null;
            if (window._workspace.Location is not null &&
                Path.GetFullPath(projectFilePath).Equals(
                    Path.GetFullPath(window._workspace.Location.ProjectFilePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                ShowInformation("Choose a different destination project.", "Transfer asset");
                return null;
            }
            return projectFilePath;
        }

        public void SetStatus(string status) => window.StatusText.Text = status;
        public void RefreshProjectMedia(Guid? selectedAssetId = null) => window.RefreshProjectCollections(selectedAssetId);
        public void ClearSelectionAndPreview() => window.ClearProjectMediaSelectionAndPreview();
        public void UpdateAssetInspector(ProjectAsset asset) =>
            window.InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(asset);
        public Task DeleteSavedFrameAsync(FrameAnchor anchor, string displayLabel) =>
            window.ConfirmAndRemoveSavedFrameAsync(anchor, displayLabel);
    }

    private sealed class MediaImportPresentation(MainWindow window) : IMediaImportCoordinatorHost
    {
        public bool HasOpenProject => window._workspace.Project is not null && window._workspace.Location is not null;
        public Task RunUiActionAsync(string status, Func<Task> action) => window.RunUiActionAsync(status, action);
        public void SetProjectActionsEnabled(bool enabled) => window.SetProjectActionsEnabled(enabled);
        public void RefreshProjectMedia() => window.RefreshProjectCollections();
        public void SetStatus(string status) => window.StatusText.Text = status;
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
        public void RefreshProjectMedia(Guid? selectedAssetId = null) =>
            window.RefreshProjectCollections(selectedAssetId ?? window._workspace.Project?.WorkingCompositionAssetId);
        public void PausePreview() => window._mediaPreviewCoordinator.Pause();
        public MediaSplitBehavior SplitBehavior => window._applicationSettings.MediaTools.SplitBehavior;
        public string? PromptDetachAudioFileName(string displayName)
        {
            var stem = MakeSafeFileName(Path.GetFileNameWithoutExtension(displayName));
            var dialog = new AssetNameDialog($"{stem} detached audio.m4a", "Detach segment audio", "DETACH SEGMENT AUDIO",
                "Create a permanent audio file from this exact timeline segment, add it at the same timeline position, and mute the segment's embedded audio to prevent doubled sound.", "Detach") { Owner = window };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }

    private sealed class MediaPreviewCoordinatorHost(MainWindow window) : IPreviewCoordinatorHost
    {
        public Task RunUiActionAsync(string status, Func<Task> action) => window.RunUiActionAsync(status, action);
        public void SetStatus(string status) => window.StatusText.Text = status;
        public void ShowError(string title, Exception exception) => window.ShowError(title, exception);
        public bool IsCompositionSelected(Guid compositionId) =>
            window.ProjectMediaPanelControl.SelectedItem is ProjectMediaListItem { Asset: { Id: var assetId } } && assetId == compositionId;
        public bool IsCompositionSelected(Guid compositionId, ProjectMediaListItem expectedItem) =>
            ReferenceEquals(window.ProjectMediaPanelControl.SelectedItem, expectedItem) &&
            IsCompositionSelected(compositionId);
        public void SelectWorkingComposition() => window.SelectWorkingCompositionInProjectMedia();
        public void ScheduleContactFrameRefresh(double seconds) => window._framePreparationCoordinator.ScheduleContactFrameRefresh(seconds);
        public Task<bool> TryHandlePrecisionFrameStepAsync(int direction) =>
            window._framePreparationCoordinator.TryHandlePreviewFrameStepAsync(direction);
        public void PreviewStateChanged()
        {
            window._compositionWorkspace.UpdateControls();
            window.UpdateCompositionActionState();
        }
        public void UpdateCompositionPreviewInspector(ProjectAsset composition, MediaEncodingMetadata? encoding) =>
            window.InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(composition, encoding);
        public bool HasRememberedBakedCompositionPreview(
            VideoProject project,
            ProjectLocation location,
            Guid compositionAssetId,
            Guid recipeRevisionId) =>
            window._projectLifecycleCoordinator.HasRememberedBakedCompositionPreview(
                project,
                location,
                compositionAssetId,
                recipeRevisionId);
    }

    private sealed class CompositionRenderPresentation(MainWindow window) : ICompositionRenderHost
    {
        public bool IsCurrentCompositionTarget(CompositionRenderTarget target) =>
            ReferenceEquals(window._workspace.Project, target.Project) &&
            ReferenceEquals(window._workspace.Location, target.Location) &&
            window._workspace.Project?.Id == target.ProjectId &&
            window._workspace.Project.WorkingCompositionAssetId == target.Composition.Id &&
            window._workspace.Project.Assets.SingleOrDefault(asset => asset.Id == target.Composition.Id)?.Virtual
                ?.CurrentRecipeRevisionId == target.Revision.Id;

        public bool CanAdoptBakedPreview(CompositionRenderTarget target) =>
            IsCurrentCompositionTarget(target) &&
            window.ProjectMediaPanelControl.SelectedItem is ProjectMediaListItem { Asset.Id: var selectedAssetId } &&
            selectedAssetId == target.Composition.Id;

        public object? CaptureProjectMediaSelectionIdentity() => window.ProjectMediaPanelControl.SelectedItem;

        public bool IsSameProjectMediaSelection(object? selectionIdentity) =>
            ReferenceEquals(window.ProjectMediaPanelControl.SelectedItem, selectionIdentity);

        public string? PromptExportPath(CompositionRenderTarget target)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Working Composition",
                Filter = "MP4 video|*.mp4",
                DefaultExt = ".mp4",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{MakeSafeFileName(target.Project.Name)} composition.mp4",
                InitialDirectory = Path.Combine(target.Location.RootDirectory, "exports")
            };
            return dialog.ShowDialog(window) == true ? dialog.FileName : null;
        }

        public IDisposable SuppressPreviewInteractions() => new InteractionSuppressionLease(
            window.MediaPreviewPanelControl,
            window.CompositionTimelineControl);

        public Task<IDisposable> PauseAndQuiescePreviewAsync(CancellationToken cancellationToken) =>
            window._mediaPreviewCoordinator.PauseAndQuiesceAsync(cancellationToken);

        public void AdoptBakedPreview(MaterializedMediaLease lease, CompositionRenderTarget target)
        {
            window._mediaPreviewCoordinator.Clear();
            window._mediaPreviewCoordinator.OpenBakedCompositionPreview(
                lease,
                target.Project,
                target.Location,
                target.Composition.Id,
                target.Revision.Id);
            window.InspectorPanelControl.Text = InspectorTextFormatter.FormatAsset(target.Composition, lease.Encoding);
        }

        public async Task<string?> RememberBakedCompositionPreviewAsync(CompositionRenderTarget target)
        {
            try
            {
                await window._projectLifecycleCoordinator.RememberBakedCompositionPreviewAsync(
                    target.Project,
                    target.Location,
                    target.Composition.Id,
                    target.Revision.Id);
                return null;
            }
            catch (Exception exception)
            {
                // The preview was already adopted successfully. Settings are convenience state,
                // not part of the render transaction, so preserve that success for this session.
                return $" ReelForge could not remember this composition preview for the next launch: {exception.Message}";
            }
        }

        public void SetRenderState(string? status, bool canCancel)
        {
            var visible = status is not null;
            window.CompositionRenderStatusText.Text = status ?? string.Empty;
            window.CompositionRenderIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            window.CancelCompositionRenderButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            window.CancelCompositionRenderButton.IsEnabled = visible && canCancel;
        }

        public void RefreshCompositionActions() => window.UpdateCompositionActionState();
        public void SetStatus(string status) => window.StatusText.Text = status;
        public void ShowError(string title, Exception exception) => window.ShowError(title, exception);

        private sealed class InteractionSuppressionLease : IDisposable
        {
            private readonly MediaPreviewPanel _mediaPreview;
            private readonly CompositionTimelineControl _compositionTimeline;
            private readonly bool _previewWasHitTestVisible;
            private readonly bool _timelineWasHitTestVisible;
            private bool _disposed;

            public InteractionSuppressionLease(
                MediaPreviewPanel mediaPreview,
                CompositionTimelineControl compositionTimeline)
            {
                _mediaPreview = mediaPreview;
                _compositionTimeline = compositionTimeline;
                _previewWasHitTestVisible = mediaPreview.IsHitTestVisible;
                _timelineWasHitTestVisible = compositionTimeline.IsHitTestVisible;
                _mediaPreview.IsHitTestVisible = false;
                _compositionTimeline.IsHitTestVisible = false;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _mediaPreview.IsHitTestVisible = _previewWasHitTestVisible;
                _compositionTimeline.IsHitTestVisible = _timelineWasHitTestVisible;
            }
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
