using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public async Task LocalSettingsOverrideDefaultsWithoutErasingOtherValues()
    {
        var directory = CreateDirectory();
        try
        {
            var defaultsPath = Path.Combine(directory, "appsettings.json");
            var localPath = Path.Combine(directory, "appsettings.local.json");
            await File.WriteAllTextAsync(defaultsPath, """
                {
                  "temporaryAssetHosting": {
                    "cloudflareR2": {
                      "bucketName": "default-bucket",
                      "presignedUrlLifetimeMinutes": 60
                    }
                  }
                }
                """);
            await File.WriteAllTextAsync(localPath, """
                {
                  "temporaryAssetHosting": {
                    "cloudflareR2": {
                      "accountId": "local-account",
                      "presignedUrlLifetimeMinutes": 90
                    }
                  }
                }
                """);

            var settings = await new JsonApplicationSettingsStore(defaultsPath, localPath).LoadAsync();

            Assert.Equal("default-bucket", settings.TemporaryAssetHosting.CloudflareR2.BucketName);
            Assert.Equal("local-account", settings.TemporaryAssetHosting.CloudflareR2.AccountId);
            Assert.Equal(90, settings.TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SavingSettingsReplacesAnyCredentialValueWithManagedMarker()
    {
        var directory = CreateDirectory();
        try
        {
            var localPath = Path.Combine(directory, "appsettings.local.json");
            var settings = new ApplicationSettings();
            settings.TemporaryAssetHosting.CloudflareR2.Credentials.SecretAccessKey!.Value = "must-not-persist";
            var store = new JsonApplicationSettingsStore(Path.Combine(directory, "missing.json"), localPath);

            await store.SaveAsync(settings);
            var json = await File.ReadAllTextAsync(localPath);

            Assert.DoesNotContain("must-not-persist", json, StringComparison.Ordinal);
            Assert.Contains(ManagedCredentialDeclaration.Marker, json, StringComparison.Ordinal);
            Assert.Contains("cloudflare.r2.secret-access-key", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EditorCommitsDirtyFieldsOnlyAtLifecycleBoundary()
    {
        var store = new CountingSettingsStore();
        var editor = new ApplicationSettingsEditor(store, new ApplicationSettings());

        editor.Update("TemporaryAssetHosting.CloudflareR2.BucketName", "new-bucket");
        Assert.True(editor.IsDirty);
        Assert.Equal(0, store.SaveCount);

        Assert.True(await editor.CommitAsync());
        Assert.Equal(1, store.SaveCount);
        Assert.False(editor.IsDirty);
        Assert.False(await editor.CommitAsync());
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task RecentProjectTrackerPersistsAndResolvesAvailableProject()
    {
        var directory = CreateDirectory();
        try
        {
            var projectFilePath = Path.Combine(directory, "Last Project.rfp");
            await File.WriteAllTextAsync(projectFilePath, "{}");
            var settings = new ApplicationSettings();
            var store = new CountingSettingsStore();
            var tracker = new RecentProjectTracker(store);

            await tracker.RememberAsync(settings, projectFilePath);

            Assert.Equal(1, store.SaveCount);
            Assert.Equal(Path.GetFullPath(projectFilePath), settings.General.LastProjectFilePath);
            Assert.Equal(Path.GetFullPath(projectFilePath), RecentProjectTracker.GetExistingProjectFile(settings));

            File.Delete(projectFilePath);
            Assert.Null(RecentProjectTracker.GetExistingProjectFile(settings));
            Assert.Equal(Path.GetFullPath(projectFilePath), settings.General.LastProjectFilePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PerProjectWorkspaceAndViewerSelectionRoundTripAsMachineLocalState()
    {
        var directory = CreateDirectory();
        try
        {
            var localPath = Path.Combine(directory, "appsettings.local.json");
            var projectId = Guid.NewGuid();
            var selectedMediaId = Guid.NewGuid();
            var settings = new ApplicationSettings();
            settings.General.ProjectStates[projectId.ToString("N")] = new ProjectUserInterfaceState
            {
                Workspace = ProjectWorkspaceKind.Edit,
                SelectedMediaKind = "asset",
                SelectedMediaId = selectedMediaId
            };
            var store = new JsonApplicationSettingsStore(Path.Combine(directory, "missing.json"), localPath);

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            var state = Assert.Single(loaded.General.ProjectStates).Value;
            Assert.Equal(ProjectWorkspaceKind.Edit, state.Workspace);
            Assert.Equal("asset", state.SelectedMediaKind);
            Assert.Equal(selectedMediaId, state.SelectedMediaId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigurationStatusUsesExistenceCheckWithoutLoadingPlaintext()
    {
        var secrets = new ExistenceOnlySecretStore(
            "cloudflare.r2.access-key-id",
            "cloudflare.r2.secret-access-key");
        var settings = ConfiguredR2Settings();

        var status = await new ApplicationConfigurationValidator(secrets)
            .ValidateSectionAsync(settings, ApplicationConfigurationCatalog.R2Section);

        Assert.True(status.IsConfigured);
        Assert.Equal(2, secrets.ExistsCalls);
        Assert.Equal(0, secrets.GetCalls);
    }

    [Fact]
    public async Task SecretReplacementAndRemovalStayBehindSecretStore()
    {
        var store = new InMemorySecretStore();
        var service = new SecretConfigurationService(store);
        var requirement = ApplicationConfigurationCatalog.Requirements.Single(item =>
            item.CredentialManagerKey == "byteplus.modelark.api-key");

        await service.ReplaceAsync(requirement, "replacement-value");
        Assert.True(await service.IsConfiguredAsync(requirement));
        Assert.Equal("replacement-value", await store.GetAsync(requirement.CredentialManagerKey!));

        await service.RemoveAsync(requirement);
        Assert.False(await service.IsConfiguredAsync(requirement));
    }

    [Fact]
    public void ProviderBaseUrlsMustBeAbsoluteHttpsAddresses()
    {
        var settings = new ApplicationSettings();

        var exception = Assert.Throws<ArgumentException>(() => ApplicationSettingsAccessor.Set(
            settings,
            "VideoGenerationProviders.BytePlus.ApiBaseUrl",
            "http://unsafe.example.test/api"));
        ApplicationSettingsAccessor.Set(
            settings,
            "VideoGenerationProviders.BytePlus.ApiBaseUrl",
            "https://safe.example.test/api");

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
        Assert.Equal("https://safe.example.test/api", settings.VideoGenerationProviders.BytePlus.ApiBaseUrl.TrimEnd('/'));
    }

    [Fact]
    public void UndoSendMustBeBetweenZeroAndThirtySeconds()
    {
        var settings = new ApplicationSettings();

        ApplicationSettingsAccessor.Set(settings, "General.UndoSendSeconds", "30");

        Assert.Equal(30, settings.General.UndoSendSeconds);
        Assert.Throws<ArgumentException>(() =>
            ApplicationSettingsAccessor.Set(settings, "General.UndoSendSeconds", "-1"));
        Assert.Throws<ArgumentException>(() =>
            ApplicationSettingsAccessor.Set(settings, "General.UndoSendSeconds", "31"));
    }

    [Fact]
    public void LogDirectoryDefaultsToCurrentDiagnosticLocationAndExpandsEnvironmentVariables()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(FileApplicationDiagnosticLog.GetDefaultLogDirectory(), settings.General.LogDirectory);

        ApplicationSettingsAccessor.Set(settings, "General.LogDirectory", @"%LOCALAPPDATA%\ReelForge\Alternate Logs");

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ReelForge",
                "Alternate Logs"),
            settings.General.LogDirectory);
        Assert.Throws<ArgumentException>(() =>
            ApplicationSettingsAccessor.Set(settings, "General.LogDirectory", " "));
    }

    [Fact]
    public void MediaCacheLimitDefaultsToTenGigabytesAndStoresCanonicalBytes()
    {
        var settings = new ApplicationSettings();

        Assert.Equal(10L * 1024 * 1024 * 1024, settings.MediaTools.CacheSizeBytes);

        ApplicationSettingsAccessor.Set(
            settings,
            "MediaTools.CacheSizeBytes",
            (2L * 1024 * 1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(2L * 1024 * 1024 * 1024, settings.MediaTools.CacheSizeBytes);
        Assert.Throws<ArgumentException>(() =>
            ApplicationSettingsAccessor.Set(settings, "MediaTools.CacheSizeBytes", "1048575"));
    }

    private static ApplicationSettings ConfiguredR2Settings()
    {
        var settings = new ApplicationSettings();
        settings.TemporaryAssetHosting.CloudflareR2.AccountId = "0123456789abcdef0123456789abcdef";
        settings.TemporaryAssetHosting.CloudflareR2.BucketName = "private-bucket";
        settings.TemporaryAssetHosting.CloudflareR2.Endpoint =
            "https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com";
        return settings;
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ReelForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CountingSettingsStore : IApplicationSettingsStore
    {
        public string LocalSettingsPath => "memory";
        public int SaveCount { get; private set; }
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationSettings());
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ExistenceOnlySecretStore(params string[] configured) : ISecretStore
    {
        private readonly HashSet<string> _configured = new(configured, StringComparer.Ordinal);
        public int ExistsCalls { get; private set; }
        public int GetCalls { get; private set; }
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            throw new InvalidOperationException("Plaintext retrieval is forbidden for status checks.");
        }
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            ExistsCalls++;
            return Task.FromResult(_configured.Contains(key));
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.ContainsKey(key));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
