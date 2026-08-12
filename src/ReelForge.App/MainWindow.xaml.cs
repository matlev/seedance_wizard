using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App;

public partial class MainWindow : Window, IDisposable
{
    private readonly ObservableCollection<ProjectAsset> _assets = [];
    private readonly ObservableCollection<GenerationRecord> _generations = [];
    private readonly ObservableCollection<GenerationReferenceChoice> _referenceChoices = [];
    private IReadOnlyList<GenerationProviderChoice> _providerChoices = [];
    private readonly ProjectWorkspace _workspace;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private GenerationWorkflow _generationWorkflow = null!;
    private readonly ISecretStore _secretStore;
    private readonly IApplicationDiagnosticLog _diagnosticLog;
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
    private CancellationTokenSource? _monitoringCancellation;
    private bool _suppressDraftAutosave;
    private bool _suppressPromptSynchronization;
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
        var projectStore = new PortableProjectStore();
        var assetImporter = new AssetImportService(_mediaInspector);
        _workspace = new ProjectWorkspace(projectStore, assetImporter);
        _secretStore = new WindowsCredentialStore();
        _diagnosticLog = new FileApplicationDiagnosticLog();
        _r2HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _temporaryAssetHost = new CloudflareR2TemporaryAssetHost(
            _applicationSettingsStore,
            _secretStore,
            new CloudflareR2ClientFactory(_r2HttpClient));
        _generationProvider = new FakeVideoGenerationProvider();
        RefreshProviderRuntime(preferredProviderId: null);

        AssetsList.ItemsSource = _assets;
        GenerationsList.ItemsSource = _generations;
        ReferenceAssetsGrid.ItemsSource = _referenceChoices;
        ReferenceRoleColumn.ItemsSource = Enum.GetValues<GenerationReferenceRole>().Cast<GenerationReferenceRole?>().Prepend(null);

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _positionTimer.Tick += (_, _) => UpdatePlaybackPosition();
        _positionTimer.Start();

        _draftAutosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _draftAutosaveTimer.Tick += DraftAutosaveTimer_Tick;

        MediaToolsText.Text = _mediaTools.Summary;
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
        _monitoringCancellation?.Cancel();
        _monitoringCancellation?.Dispose();
        foreach (var client in _providerHttpClients) client.Dispose();
        _r2HttpClient.Dispose();
        _downloadHttpClient.Dispose();
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
        _generationWorkflow = new GenerationWorkflow(
            _workspace,
            new PhysicalAssetMaterializer(),
            new HttpGeneratedOutputIngestionService(_downloadHttpClient, _mediaInspector),
            new ProviderAssetPreparationRouter(preparationServices));

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

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var activeDraft = _workspace.Project is null ? null : CaptureDraftFromUi();
        var window = new SettingsWindow(
            _applicationSettingsStore,
            _applicationSettings.Clone(),
            _secretStore,
            _mediaToolDiscovery,
            _temporaryAssetHost)
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
            : "Review and submit paid generation…";
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
        if (AssetsList.SelectedItem is not ProjectAsset asset)
        {
            return;
        }

        var selectedProjectId = _workspace.Project?.Id;
        GenerationsList.SelectedItem = null;

        await RunUiActionAsync(
            $"Inspecting {asset.FileName}…",
            async () =>
            {
                if (asset.MediaType is MediaType.Video or MediaType.Audio &&
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
                StatusText.Text = $"Selected {asset.FileName}.";
            });
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

            var draftSummary = CaptureDraftFromUi();
            var confirmation = MessageBox.Show(
                this,
                $"Review the prompt settings before submitting to {_generationProvider.Capabilities.DisplayName}.\n\n" +
                $"Model: {_generationProvider.Capabilities.ModelVersion}\n" +
                $"Mode: {draftSummary.Mode}\nDuration: {draftSummary.DurationSeconds}s\n" +
                $"Resolution: {draftSummary.Resolution}\nReferences: {draftSummary.References.Count}\n\n" +
                "Proceed with these settings?",
                "Confirm prompt submission",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
            {
                GenerationStatusText.Text = "Submission cancelled. No API request was made.";
                return;
            }

            authorization = GenerationSubmissionAuthorization.FromInteractiveUserConfirmation(
                _generationProvider.Capabilities.ProviderId,
                userConfirmedPotentialCharges: true);
        }

        await RunGenerationWorkflowAsync(CaptureDraftFromUi(), authorization);
    }

    private async Task RunGenerationWorkflowAsync(
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization)
    {
        GenerateButton.IsEnabled = false;
        ResumeMonitoringButton.IsEnabled = false;
        StopMonitoringButton.IsEnabled = false;
        _monitoringCancellation?.Dispose();
        _monitoringCancellation = new CancellationTokenSource();
        var progress = new Progress<GenerationWorkflowProgress>(update =>
        {
            GenerationStatusText.Text = $"{update.Message}\nRemote: {update.RemoteStatus} • Ingestion: {update.IngestionStatus}";
            if (update.Message.StartsWith("Remote job:", StringComparison.Ordinal))
                StopMonitoringButton.IsEnabled = true;
        });

        try
        {
            var generation = await _generationWorkflow.RunAsync(
                _generationProvider,
                draft,
                authorization,
                progress: progress,
                cancellationToken: _monitoringCancellation.Token);
            RefreshProjectCollections();
            GenerationsList.SelectedItem = _generations.FirstOrDefault(item => item.Id == generation.Id);
            GenerationStatusText.Text = FormatGenerationOutcome(generation);
            StatusText.Text = generation.IngestionStatus == OutputIngestionStatus.Succeeded
                ? "Generated output added as durable project media."
                : $"Generation state: {generation.Status}; ingestion: {generation.IngestionStatus}.";
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
            ResumeMonitoringButton.IsEnabled = true;
            StopMonitoringButton.IsEnabled = false;
        }
    }

    private void StopMonitoring_Click(object sender, RoutedEventArgs e)
    {
        StopMonitoringButton.IsEnabled = false;
        _monitoringCancellation?.Cancel();
        GenerationStatusText.Text = "Stopping local monitoring. No remote cancellation request will be sent.";
    }

    private async void ResumeMonitoring_Click(object sender, RoutedEventArgs e)
    {
        if (GenerationsList.SelectedItem is not GenerationRecord generation)
        {
            GenerationStatusText.Text = "Select a generation with a remote job ID first.";
            return;
        }
        if (_providerChoices.FirstOrDefault(choice =>
                choice.Provider.Capabilities.ProviderId == generation.RequestSnapshot.ProviderId)?.Provider
            is not IAsyncVideoGenerationProvider provider)
        {
            GenerationStatusText.Text = "The selected generation does not have a resumable asynchronous provider.";
            return;
        }

        _monitoringCancellation?.Dispose();
        _monitoringCancellation = new CancellationTokenSource();
        GenerateButton.IsEnabled = false;
        ResumeMonitoringButton.IsEnabled = false;
        StopMonitoringButton.IsEnabled = true;
        var progress = new Progress<GenerationWorkflowProgress>(update =>
            GenerationStatusText.Text = $"{update.Message}\nRemote: {update.RemoteStatus} • Ingestion: {update.IngestionStatus}");
        try
        {
            await _generationWorkflow.ResumeMonitoringAsync(
                provider,
                generation,
                progress: progress,
                cancellationToken: _monitoringCancellation.Token);
            RefreshProjectCollections();
            GenerationStatusText.Text = FormatGenerationOutcome(generation);
        }
        catch (Exception exception)
        {
            ShowError("Monitoring failed", exception);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
            ResumeMonitoringButton.IsEnabled = true;
            StopMonitoringButton.IsEnabled = false;
        }
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

        var draft = GenerationWorkflow.CreateDerivedDraft(source, relationship);
        LoadDraftIntoUi(draft);
        await _generationWorkflow.SaveDraftAsync(draft);
        GenerationStatusText.Text =
            $"Drafted {relationship} from generation {source.Id}. Review it, then use the submission button.";
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
                    ObjectKind = GenerationReferenceObjectKind.Asset,
                    LogicalObjectId = choice.Asset.Id,
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

            foreach (var choice in _referenceChoices)
            {
                var reference = draft.References.FirstOrDefault(item =>
                    item.ObjectKind == GenerationReferenceObjectKind.Asset && item.LogicalObjectId == choice.Asset.Id);
                choice.IsSelected = reference is not null;
                choice.Role = reference?.Role;
                choice.Order = reference?.Order ?? choice.Order;
                choice.Label = reference?.Label;
                choice.Notes = reference?.Notes;
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

        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            PreviewPlaceholder.Text = "Virtual asset preview will be materialized on demand in a later Milestone 2 phase.";
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        var absolutePath = _workspace.GetAbsoluteAssetPath(asset);
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
    }

    private void ClearMediaPreview()
    {
        VideoPreview.Stop();
        VideoPreview.Source = null;
        VideoPreview.Visibility = Visibility.Collapsed;
        ImagePreview.Source = null;
        ImagePreview.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Text = "Select a video or image asset to preview";
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PositionSlider.Maximum = 1;
        PositionSlider.Value = 0;
        TimeText.Text = "00:00 / 00:00";
    }

    private void VideoPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = VideoPreview.NaturalDuration.TimeSpan.TotalSeconds;
        }

        VideoPreview.Pause();
        UpdatePlaybackPosition();
    }

    private void VideoPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        VideoPreview.Position = TimeSpan.Zero;
        VideoPreview.Pause();
        UpdatePlaybackPosition();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (VideoPreview.Source is not null)
        {
            VideoPreview.Play();
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => VideoPreview.Pause();

    private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (VideoPreview.Source is not null)
        {
            VideoPreview.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        }
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

        PositionSlider.Value = current.TotalSeconds;
        TimeText.Text = $"{FormatTime(current)} / {FormatTime(duration)}";
    }

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
        _suppressDraftAutosave = false;

        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} assets";
        Title = $"{_workspace.Project.Name} — ReelForge";
        StatusText.Text = _workspace.Location!.Migration is { } migration
            ? $"Upgraded schema {migration.FromVersion} to {migration.ToVersion}. Backup: {migration.BackupPath}"
            : $"Opened {_workspace.Location.ProjectFilePath}";
    }

    private void ResetProjectSpecificUi()
    {
        ExpandedPromptPanel.Visibility = Visibility.Collapsed;
        AssetsList.SelectedItem = null;
        GenerationsList.SelectedItem = null;
        _referenceChoices.Clear();

        InspectorText.Text = "Select an asset or generation to inspect its details and history.";
        PromptTextBox.Text = string.Empty;
        GenerationStatusText.Text = string.Empty;
        LineageText.Text = "New root generation";
        ClearMediaPreview();
    }

    private void RefreshProjectCollections()
    {
        if (_workspace.Project is null) return;
        var existingChoices = _referenceChoices.ToDictionary(choice => choice.Asset.Id);
        _assets.Clear();
        _generations.Clear();
        _referenceChoices.Clear();

        foreach (var asset in _workspace.Project.Assets)
        {
            _assets.Add(asset);
            if (existingChoices.TryGetValue(asset.Id, out var existing))
            {
                existing.Asset = asset;
                _referenceChoices.Add(existing);
            }
            else
            {
                _referenceChoices.Add(new GenerationReferenceChoice(asset, _referenceChoices.Count));
            }
        }

        foreach (var generation in _workspace.Project.Generations.OrderByDescending(item => item.RequestedAt))
            _generations.Add(generation);

        ProjectTitleText.Text = $"{_workspace.Project.Name}  •  {_assets.Count} assets";
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

public sealed class GenerationReferenceChoice
{
    public GenerationReferenceChoice(ProjectAsset asset, int order)
    {
        Asset = asset;
        Order = order;
    }

    public ProjectAsset Asset { get; set; }
    public bool IsSelected { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
}
