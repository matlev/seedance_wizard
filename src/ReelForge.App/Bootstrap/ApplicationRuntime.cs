using System.Net.Http;
using ReelForge.App.Views.Generation;
using ReelForge.Application;
using ReelForge.Infrastructure;
using ReelForge.Platform.Windows;

namespace ReelForge.App.Bootstrap;

internal sealed record GenerationProviderRuntime(
    IReadOnlyList<GenerationProviderChoice> Choices,
    IVideoGenerationProvider SelectedProvider,
    IProviderAssetPreparationService? PreparationService,
    GenerationWorkflow Workflow);

internal sealed class ApplicationRuntime : IDisposable
{
    private readonly List<HttpClient> _providerHttpClients = [];
    private readonly HttpClient _r2HttpClient;
    private readonly HttpClient _downloadHttpClient;
    private IReadOnlyList<GenerationProviderChoice> _providerChoices = [];
    private bool _disposed;

    private ApplicationRuntime()
    {
        Paths = new WindowsApplicationPathProvider().GetPaths();
        MediaToolDiscovery = new MediaToolDiscovery();
        ApplicationSettingsStore = new JsonApplicationSettingsStore(Paths.LocalSettingsFilePath);
        RecentProjectTracker = new RecentProjectTracker(ApplicationSettingsStore);
        Settings = LoadSettings();
        MediaTools = MediaToolDiscovery.Discover(Settings.MediaTools.FfmpegPath, Settings.MediaTools.FfprobePath);

        DiagnosticLog = new FileApplicationDiagnosticLog(Settings.General.LogDirectory);
        ProcessRunner = new ExternalProcessRunner(
            DiagnosticLog,
            Settings.MediaTools.LogFfmpegCommands,
            Settings.MediaTools.LogFfprobeCommands);
        MediaInspector = new FfprobeMediaInspectionService(MediaTools.FfprobePath, ProcessRunner);
        ExactFrameService = new ExactVideoFrameService(
            MediaTools.FfmpegPath,
            MediaTools.FfprobePath,
            ProcessRunner,
            Paths.MediaCacheDirectory,
            maximumCacheBytes: Settings.MediaTools.CacheSizeBytes);
        MediaMaterializer = new RecipeMediaMaterializer(
            MediaTools.FfmpegPath,
            ProcessRunner,
            ExactFrameService,
            Paths.MediaCacheDirectory,
            mediaInspector: MediaInspector,
            persistModifiedMediaOnDisk: Settings.MediaTools.PersistModifiedMediaOnDisk);
        AudioExtractionEngine = new FfmpegAudioExtractionEngine(MediaTools.FfmpegPath, ProcessRunner);

        ProjectStore = new PortableProjectStore();
        ProjectSaveCoordinator = new ProjectSaveCoordinator();
        ProjectCloneService = new ProjectCloneService(
            ProjectStore,
            new PortableProjectCloneFileSystem(),
            ProjectSaveCoordinator);
        AssetImporter = new AssetImportService(MediaInspector);
        Workspace = new ProjectWorkspace(ProjectStore, AssetImporter, ProjectStore, ProjectSaveCoordinator);
        ProjectRelocationService = new ProjectRelocationService(
            Workspace,
            ProjectStore,
            new PortableProjectRelocationFileSystem(),
            ProjectSaveCoordinator);
        ProjectDegradationAnalyzer = new ProjectDegradationAnalyzer();
        ProjectCleanupService = new ProjectCleanupService(ProjectDegradationAnalyzer);
        AssetTransferService = new ProjectAssetTransferService(ProjectStore, AssetImporter, Workspace);
        ContentHashService = new Sha256ContentHashService();
        RenderedAssetPromotionService = new RenderedAssetPromotionService(
            Workspace,
            MediaMaterializer,
            ContentHashService,
            MediaInspector);
        AudioExtractionService = new AudioExtractionService(
            Workspace,
            MediaMaterializer,
            AudioExtractionEngine,
            ContentHashService,
            MediaInspector);
        ProjectAssetDependencyAnalyzer = new ProjectAssetDependencyAnalyzer();
        PhysicalAssetRelinkStager = new PhysicalAssetRelinkStager();
        PhysicalAssetRelinkService = new PhysicalAssetRelinkService(
            Workspace,
            ContentHashService,
            PhysicalAssetRelinkStager,
            ProjectAssetDependencyAnalyzer);
        DeletedPhysicalAssetRestorationService = new DeletedPhysicalAssetRestorationService(
            Workspace,
            ContentHashService,
            PhysicalAssetRelinkService,
            PhysicalAssetRelinkStager,
            ProjectAssetDependencyAnalyzer);
        PhysicalAssetRemovalService = new PhysicalAssetRemovalService();
        ProjectAssetTransferWorkflow = new ProjectAssetTransferWorkflow(
            Workspace,
            AssetTransferService,
            ProjectAssetDependencyAnalyzer,
            PhysicalAssetRemovalService);
        MaterializedProjectMediaTransferService = new MaterializedProjectMediaTransferService(
            Workspace,
            MediaMaterializer,
            AssetTransferService);

        SecretStore = new WindowsCredentialStore();
        _r2HttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _downloadHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        OutputIngestion = new HttpGeneratedOutputIngestionService(_downloadHttpClient, MediaInspector);
        JobFinalizer = new GenerationJobFinalizer(Workspace, ProjectStore, OutputIngestion);
        TemporaryAssetHost = new CloudflareR2TemporaryAssetHost(
            ApplicationSettingsStore,
            SecretStore,
            new CloudflareR2ClientFactory(_r2HttpClient));

        JobCoordinator = new GenerationJobCoordinator(
            new JsonGenerationJobStore(Paths.ActiveGenerationJobsFilePath),
            ResolveAsyncProvider,
            JobFinalizer);
    }

    public static ApplicationRuntime Create() => new();

    public ApplicationPaths Paths { get; }
    public IMediaToolDiscovery MediaToolDiscovery { get; }
    public IApplicationSettingsStore ApplicationSettingsStore { get; }
    public RecentProjectTracker RecentProjectTracker { get; }
    public ApplicationSettings Settings { get; private set; }
    public MediaToolAvailability MediaTools { get; private set; }
    public PortableProjectStore ProjectStore { get; }
    public ProjectSaveCoordinator ProjectSaveCoordinator { get; }
    public ProjectCloneService ProjectCloneService { get; }
    public ProjectRelocationService ProjectRelocationService { get; }
    public ProjectDegradationAnalyzer ProjectDegradationAnalyzer { get; }
    public ProjectCleanupService ProjectCleanupService { get; }
    public AssetImportService AssetImporter { get; }
    public ProjectWorkspace Workspace { get; }
    public ProjectAssetTransferService AssetTransferService { get; }
    public IContentHashService ContentHashService { get; }
    public RenderedAssetPromotionService RenderedAssetPromotionService { get; }
    public AudioExtractionService AudioExtractionService { get; }
    public ProjectAssetDependencyAnalyzer ProjectAssetDependencyAnalyzer { get; }
    public PhysicalAssetRelinkStager PhysicalAssetRelinkStager { get; }
    public PhysicalAssetRelinkService PhysicalAssetRelinkService { get; }
    public DeletedPhysicalAssetRestorationService DeletedPhysicalAssetRestorationService { get; }
    public PhysicalAssetRemovalService PhysicalAssetRemovalService { get; }
    public ProjectAssetTransferWorkflow ProjectAssetTransferWorkflow { get; }
    public MaterializedProjectMediaTransferService MaterializedProjectMediaTransferService { get; }
    public FfprobeMediaInspectionService MediaInspector { get; }
    public ExactVideoFrameService ExactFrameService { get; }
    public RecipeMediaMaterializer MediaMaterializer { get; }
    public FfmpegAudioExtractionEngine AudioExtractionEngine { get; }
    public IGeneratedOutputIngestionService OutputIngestion { get; }
    public GenerationJobFinalizer JobFinalizer { get; }
    public ISecretStore SecretStore { get; }
    public FileApplicationDiagnosticLog DiagnosticLog { get; }
    public ExternalProcessRunner ProcessRunner { get; }
    public ITemporaryAssetHost TemporaryAssetHost { get; }
    public GenerationJobCoordinator JobCoordinator { get; }

    public async Task<ApplicationSettings> ReloadAndApplySettingsAsync(CancellationToken cancellationToken = default)
    {
        Settings = await ApplicationSettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        ApplicationSettingsPlatformDefaults.Apply(Settings, Paths);
        MediaTools = MediaToolDiscovery.Discover(Settings.MediaTools.FfmpegPath, Settings.MediaTools.FfprobePath);
        MediaInspector.UpdateExecutablePath(MediaTools.FfprobePath);
        ExactFrameService.UpdateExecutablePaths(MediaTools.FfmpegPath, MediaTools.FfprobePath);
        MediaMaterializer.UpdateExecutablePath(MediaTools.FfmpegPath);
        AudioExtractionEngine.UpdateExecutablePath(MediaTools.FfmpegPath);
        ProcessRunner.UpdateCommandLogging(
            Settings.MediaTools.LogFfmpegCommands,
            Settings.MediaTools.LogFfprobeCommands);
        MediaMaterializer.UpdatePersistencePreference(Settings.MediaTools.PersistModifiedMediaOnDisk);
        ExactFrameService.UpdateMaximumCacheBytes(Settings.MediaTools.CacheSizeBytes);
        await ExactFrameService.TrimCacheAsync(cancellationToken).ConfigureAwait(false);
        return Settings;
    }

    public GenerationProviderRuntime RefreshProviders(string? preferredProviderId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var choices = new List<GenerationProviderChoice>();
        var preparationServices = new Dictionary<string, IProviderAssetPreparationService>(StringComparer.Ordinal);
        var newClients = new List<HttpClient>();
        try
        {
            choices.Add(new GenerationProviderChoice(new FakeVideoGenerationProvider()));

            if (Settings.VideoGenerationProviders.BytePlus.Enabled)
            {
                var client = CreateProviderHttpClient(
                    Settings.VideoGenerationProviders.BytePlus.ApiBaseUrl,
                    "BytePlus");
                newClients.Add(client);
                choices.Add(new GenerationProviderChoice(new BytePlusModelArkSeedance25Provider(
                    client,
                    SecretStore,
                    new ProjectAssetReferenceResolver())));
                preparationServices[BytePlusModelArkSeedance25Provider.ProviderId] =
                    new BytePlusModelArkAssetPreparationService(TemporaryAssetHost);
            }

            if (Settings.VideoGenerationProviders.AtlasCloud.Enabled)
            {
                var client = CreateProviderHttpClient(
                    Settings.VideoGenerationProviders.AtlasCloud.ApiBaseUrl,
                    "AtlasCloud");
                newClients.Add(client);
                choices.Add(new GenerationProviderChoice(new AtlasCloudSeedance25Provider(
                    client,
                    SecretStore,
                    new ProjectAssetReferenceResolver(),
                    DiagnosticLog)));
                choices.Add(new GenerationProviderChoice(new AtlasCloudMiniMaxH3Provider(
                    client,
                    SecretStore,
                    new ProjectAssetReferenceResolver(),
                    DiagnosticLog)));
                var atlasCloudPreparation = new AtlasCloudAssetPreparationService(
                    client,
                    SecretStore,
                    DiagnosticLog);
                preparationServices[AtlasCloudSeedance25Provider.ProviderId] = atlasCloudPreparation;
                preparationServices[AtlasCloudMiniMaxH3Provider.ProviderId] = atlasCloudPreparation;
            }

            var preparation = preparationServices.Count == 0
                ? null
                : new ProviderAssetPreparationRouter(preparationServices);
            var workflow = CreateGenerationWorkflow(Workspace, preparation);
            var selected = choices.FirstOrDefault(choice =>
                               choice.Provider.Capabilities.ProviderId.Equals(
                                   preferredProviderId,
                                   StringComparison.Ordinal))
                           ?? choices[0];

            _providerHttpClients.AddRange(newClients);
            _providerChoices = choices;
            return new GenerationProviderRuntime(choices, selected.Provider, preparation, workflow);
        }
        catch
        {
            foreach (var client in newClients) client.Dispose();
            throw;
        }
    }

    public GenerationWorkflow CreateGenerationWorkflow(
        ProjectWorkspace workspace,
        IProviderAssetPreparationService? preparationService) =>
        new(workspace, MediaMaterializer, OutputIngestion, preparationService);

    public ProjectWorkspace CreateProjectWorkspace() =>
        new(ProjectStore, AssetImporter, ProjectStore, ProjectSaveCoordinator);

    private ApplicationSettings LoadSettings()
    {
        ApplicationSettings settings;
        try
        {
            settings = ApplicationSettingsStore.LoadAsync().GetAwaiter().GetResult();
        }
        catch
        {
            settings = new ApplicationSettings();
        }
        ApplicationSettingsPlatformDefaults.Apply(settings, Paths);
        return settings;
    }

    private static HttpClient CreateProviderHttpClient(string baseUrl, string providerName) => new()
    {
        BaseAddress = RequireHttpsBaseUri(baseUrl, providerName),
        Timeout = TimeSpan.FromMinutes(10)
    };

    private IAsyncVideoGenerationProvider? ResolveAsyncProvider(string providerId) =>
        _providerChoices.FirstOrDefault(choice =>
            choice.Provider.Capabilities.ProviderId.Equals(providerId, StringComparison.Ordinal))?.Provider
        as IAsyncVideoGenerationProvider;

    private static Uri RequireHttpsBaseUri(string value, string providerName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{providerName} API base URL must be an absolute HTTPS URL.");
        return uri;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        JobCoordinator.Stop();
        foreach (var client in _providerHttpClients) client.Dispose();
        _r2HttpClient.Dispose();
        _downloadHttpClient.Dispose();
        MediaMaterializer.Dispose();
        ExactFrameService.Dispose();
        DiagnosticLog.Dispose();
    }
}
