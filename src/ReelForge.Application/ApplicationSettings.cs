using System.Text.Json;

namespace ReelForge.Application;

public sealed class ApplicationSettings
{
    public GeneralApplicationSettings General { get; set; } = new();
    public MediaToolConfiguration MediaTools { get; set; } = new();
    public TemporaryAssetHostingSettings TemporaryAssetHosting { get; set; } = new();
    public VideoGenerationProviderSettings VideoGenerationProviders { get; set; } = new();

    public ApplicationSettings Clone() =>
        JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(this)) ?? new ApplicationSettings();
}

public sealed class GeneralApplicationSettings
{
    public string ProjectsRoot { get; set; } = string.Empty;
    public string LastProjectFilePath { get; set; } = string.Empty;
    public int UndoSendSeconds { get; set; }
    public string LogDirectory { get; set; } = ApplicationStoragePaths.GetDefaultLogDirectory();
    public Dictionary<string, ProjectUserInterfaceState> ProjectStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum ProjectWorkspaceKind { Generate, Edit }

public static class GeneratedOutputPreviewPolicy
{
    public static bool ShouldAutoPreview(
        bool owningProjectIsOpen,
        ProjectWorkspaceKind workspace,
        bool isMediaPreparationActive) =>
        owningProjectIsOpen &&
        workspace == ProjectWorkspaceKind.Generate &&
        !isMediaPreparationActive;
}

public sealed class ProjectUserInterfaceState
{
    public ProjectWorkspaceKind Workspace { get; set; } = ProjectWorkspaceKind.Generate;
    public string? SelectedMediaKind { get; set; }
    public Guid? SelectedMediaId { get; set; }
}

public static class ApplicationStoragePaths
{
    public static string GetDefaultLogDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "ReelForge", "Logs");
    }

    public static string ResolveDirectory(string? configuredPath, string defaultPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}

public sealed class RecentProjectTracker
{
    private readonly IApplicationSettingsStore _settingsStore;

    public RecentProjectTracker(IApplicationSettingsStore settingsStore) => _settingsStore = settingsStore;

    public async Task RememberAsync(
        ApplicationSettings settings,
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        settings.General.LastProjectFilePath = Path.GetFullPath(projectFilePath);
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public static string? GetExistingProjectFile(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var configuredPath = settings.General.LastProjectFilePath;
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

public sealed class TemporaryAssetHostingSettings
{
    public string Provider { get; set; } = "CloudflareR2";
    public CloudflareR2Settings CloudflareR2 { get; set; } = new();
}

public sealed class CloudflareR2Settings
{
    public string AccountId { get; set; } = string.Empty;
    public string BucketName { get; set; } = "reelforge-temp-assets";
    public string Endpoint { get; set; } = string.Empty;
    public int PresignedUrlLifetimeMinutes { get; set; } = 60;
    public ManagedCredentialMap Credentials { get; set; } = new()
    {
        AccessKeyId = new("cloudflare.r2.access-key-id"),
        SecretAccessKey = new("cloudflare.r2.secret-access-key")
    };
}

public sealed class VideoGenerationProviderSettings
{
    public ProviderApplicationSettings BytePlus { get; set; } = new()
    {
        Enabled = true,
        ApiBaseUrl = "https://ark.ap-southeast.bytepluses.com/api/v3/",
        Credentials = new ManagedCredentialMap { ApiKey = new("byteplus.modelark.api-key") }
    };

    public ProviderApplicationSettings AtlasCloud { get; set; } = new()
    {
        Enabled = true,
        ApiBaseUrl = "https://api.atlascloud.ai/",
        Credentials = new ManagedCredentialMap { ApiKey = new("atlascloud.api-key") }
    };
}

public sealed class ProviderApplicationSettings
{
    public bool Enabled { get; set; }
    public string ApiBaseUrl { get; set; } = string.Empty;
    public ManagedCredentialMap Credentials { get; set; } = new();
}

public sealed class ManagedCredentialMap
{
    public ManagedCredentialDeclaration? ApiKey { get; set; }
    public ManagedCredentialDeclaration? AccessKeyId { get; set; }
    public ManagedCredentialDeclaration? SecretAccessKey { get; set; }
}

public sealed class ManagedCredentialDeclaration
{
    public const string Marker = "<MANAGED BY WINDOWS CREDENTIAL MANAGER>";

    public ManagedCredentialDeclaration() { }

    public ManagedCredentialDeclaration(string credentialManagerKey)
    {
        CredentialManagerKey = credentialManagerKey;
    }

    public string CredentialManagerKey { get; set; } = string.Empty;
    public string Value { get; set; } = Marker;
}

public sealed record ConfigurationRequirement(
    string Key,
    string DisplayName,
    string Description,
    bool Required,
    bool Secret,
    string Placeholder,
    string Section,
    string? CredentialManagerKey = null);

public static class ApplicationConfigurationCatalog
{
    public const string GeneralSection = "General";
    public const string MediaToolsSection = "Media Tools";
    public const string R2Section = "Temporary Asset Hosting / Cloudflare R2";
    public const string BytePlusSection = "Video Generation Providers / BytePlus";
    public const string AtlasCloudSection = "Video Generation Providers / AtlasCloud";

    public static IReadOnlyList<ConfigurationRequirement> Requirements { get; } =
    [
        new("General.ProjectsRoot", "Default projects location", "Parent folder used by the New Project dialog.", false, false,
            @"%USERPROFILE%\Documents\ReelForge\Projects", GeneralSection),
        new("General.UndoSendSeconds", "Undo Send", "Wait before sending a generation request so it can still be cancelled locally (0 to 30 seconds).", false, false,
            "0", GeneralSection),
        new("General.LogDirectory", "Log location", "Folder used for verbose ReelForge diagnostic logs.", false, false,
            ApplicationStoragePaths.GetDefaultLogDirectory(), GeneralSection),
        new("MediaTools.FfmpegPath", "FFmpeg path", "Explicit ffmpeg.exe path. Leave empty to use PATH auto-detection.", false, false,
            @"C:\path\to\ffmpeg.exe", MediaToolsSection),
        new("MediaTools.FfprobePath", "ffprobe path", "Explicit ffprobe.exe path. Leave empty to use PATH auto-detection.", false, false,
            @"C:\path\to\ffprobe.exe", MediaToolsSection),
        new("MediaTools.CacheSizeBytes", "Media cache limit", "Maximum disk space for disposable media derivatives. Lower cache values may make some video-editing actions perform poorly or become impossible.", false, false,
            MediaToolConfiguration.DefaultCacheSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), MediaToolsSection),
        new("MediaTools.PersistModifiedMediaOnDisk", "Persist modified media on disk", "Captured frames, video clippings, and in-progress compositions are normally stored in cache to optimize space usage and are rebuilt when necessary. Persisting this media on disk will save them as normal, permanent files in your project's media folder.", false, false,
            "false", MediaToolsSection),
        new("TemporaryAssetHosting.CloudflareR2.AccountId", "R2 Account ID", "Cloudflare account identifier used by the R2 S3 endpoint.", true, false,
            "32-character Cloudflare account ID", R2Section),
        new("TemporaryAssetHosting.CloudflareR2.BucketName", "R2 bucket name", "Private bucket used for temporary provider references.", true, false,
            "reelforge-temp-assets", R2Section),
        new("TemporaryAssetHosting.CloudflareR2.Endpoint", "R2 S3 endpoint", "HTTPS S3-compatible account endpoint.", true, false,
            "https://<account-id>.r2.cloudflarestorage.com", R2Section),
        new("TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes", "Read URL lifetime (minutes)", "Lifetime for provider-facing presigned GET URLs (1 minute to 7 days).", true, false,
            "60", R2Section),
        new("TemporaryAssetHosting.CloudflareR2.Credentials.AccessKeyId", "R2 Access Key ID", "R2 S3 API token Access Key ID.", true, true,
            "Enter R2 Access Key ID", R2Section, "cloudflare.r2.access-key-id"),
        new("TemporaryAssetHosting.CloudflareR2.Credentials.SecretAccessKey", "R2 Secret Access Key", "R2 S3 API token Secret Access Key.", true, true,
            "Enter R2 Secret Access Key", R2Section, "cloudflare.r2.secret-access-key"),
        new("VideoGenerationProviders.BytePlus.Enabled", "Enabled", "Show BytePlus as an available generation route.", false, false,
            "true", BytePlusSection),
        new("VideoGenerationProviders.BytePlus.ApiBaseUrl", "API base URL", "BytePlus ModelArk API base URL.", true, false,
            "https://ark.ap-southeast.bytepluses.com/api/v3/", BytePlusSection),
        new("VideoGenerationProviders.BytePlus.Credentials.ApiKey", "BytePlus API key", "Bearer credential for ModelArk.", true, true,
            "Enter BytePlus API key", BytePlusSection, "byteplus.modelark.api-key"),
        new("VideoGenerationProviders.AtlasCloud.Enabled", "Enabled", "Show AtlasCloud models as available generation routes.", false, false,
            "true", AtlasCloudSection),
        new("VideoGenerationProviders.AtlasCloud.ApiBaseUrl", "API base URL", "AtlasCloud API base URL.", true, false,
            "https://api.atlascloud.ai/", AtlasCloudSection),
        new("VideoGenerationProviders.AtlasCloud.Credentials.ApiKey", "AtlasCloud API key", "Bearer credential for AtlasCloud.", true, true,
            "Enter AtlasCloud API key", AtlasCloudSection, "atlascloud.api-key")
    ];

    public static IReadOnlyList<string> Sections { get; } =
        Requirements.Select(requirement => requirement.Section).Distinct(StringComparer.Ordinal).ToArray();
}

public static class ApplicationSettingsAccessor
{
    public static string Get(ApplicationSettings settings, string key) => key switch
    {
        "General.ProjectsRoot" => settings.General.ProjectsRoot,
        "General.UndoSendSeconds" => settings.General.UndoSendSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "General.LogDirectory" => settings.General.LogDirectory,
        "MediaTools.FfmpegPath" => settings.MediaTools.FfmpegPath ?? string.Empty,
        "MediaTools.FfprobePath" => settings.MediaTools.FfprobePath ?? string.Empty,
        "MediaTools.CacheSizeBytes" => settings.MediaTools.CacheSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "MediaTools.PersistModifiedMediaOnDisk" => settings.MediaTools.PersistModifiedMediaOnDisk.ToString().ToLowerInvariant(),
        "TemporaryAssetHosting.CloudflareR2.AccountId" => settings.TemporaryAssetHosting.CloudflareR2.AccountId,
        "TemporaryAssetHosting.CloudflareR2.BucketName" => settings.TemporaryAssetHosting.CloudflareR2.BucketName,
        "TemporaryAssetHosting.CloudflareR2.Endpoint" => settings.TemporaryAssetHosting.CloudflareR2.Endpoint,
        "TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes" => settings.TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "VideoGenerationProviders.BytePlus.Enabled" => settings.VideoGenerationProviders.BytePlus.Enabled.ToString().ToLowerInvariant(),
        "VideoGenerationProviders.BytePlus.ApiBaseUrl" => settings.VideoGenerationProviders.BytePlus.ApiBaseUrl,
        "VideoGenerationProviders.AtlasCloud.Enabled" => settings.VideoGenerationProviders.AtlasCloud.Enabled.ToString().ToLowerInvariant(),
        "VideoGenerationProviders.AtlasCloud.ApiBaseUrl" => settings.VideoGenerationProviders.AtlasCloud.ApiBaseUrl,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown non-secret application setting.")
    };

    public static void Set(ApplicationSettings settings, string key, string value)
    {
        value = value.Trim();
        switch (key)
        {
            case "General.ProjectsRoot": settings.General.ProjectsRoot = value; break;
            case "General.UndoSendSeconds":
                if (!int.TryParse(value, out var seconds) || seconds is < 0 or > 30)
                    throw new ArgumentException("Undo Send must be between 0 and 30 seconds.");
                settings.General.UndoSendSeconds = seconds;
                break;
            case "General.LogDirectory":
                if (value.Length == 0) throw new ArgumentException("Log location cannot be empty.");
                settings.General.LogDirectory = ApplicationStoragePaths.ResolveDirectory(
                    value,
                    ApplicationStoragePaths.GetDefaultLogDirectory());
                break;
            case "MediaTools.FfmpegPath": settings.MediaTools.FfmpegPath = EmptyToNull(value); break;
            case "MediaTools.FfprobePath": settings.MediaTools.FfprobePath = EmptyToNull(value); break;
            case "MediaTools.CacheSizeBytes":
                const long minimumCacheBytes = 1024L * 1024;
                const long maximumCacheBytes = 8L * 1024 * 1024 * 1024 * 1024;
                if (!long.TryParse(value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var cacheBytes) ||
                    cacheBytes is < minimumCacheBytes or > maximumCacheBytes)
                    throw new ArgumentException("Media cache limit must be between 1 MB and 8 TB.");
                settings.MediaTools.CacheSizeBytes = cacheBytes;
                break;
            case "MediaTools.PersistModifiedMediaOnDisk":
                settings.MediaTools.PersistModifiedMediaOnDisk = ParseBoolean(value, key);
                break;
            case "TemporaryAssetHosting.CloudflareR2.AccountId": settings.TemporaryAssetHosting.CloudflareR2.AccountId = value; break;
            case "TemporaryAssetHosting.CloudflareR2.BucketName": settings.TemporaryAssetHosting.CloudflareR2.BucketName = value; break;
            case "TemporaryAssetHosting.CloudflareR2.Endpoint": settings.TemporaryAssetHosting.CloudflareR2.Endpoint = value; break;
            case "TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes":
                if (!int.TryParse(value, out var minutes) || minutes is < 1 or > 10080)
                    throw new ArgumentException("Read URL lifetime must be between 1 and 10,080 minutes.");
                settings.TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes = minutes;
                break;
            case "VideoGenerationProviders.BytePlus.Enabled": settings.VideoGenerationProviders.BytePlus.Enabled = ParseBoolean(value, key); break;
            case "VideoGenerationProviders.BytePlus.ApiBaseUrl":
                settings.VideoGenerationProviders.BytePlus.ApiBaseUrl = ValidateHttpsUrl(value, "BytePlus API base URL");
                break;
            case "VideoGenerationProviders.AtlasCloud.Enabled": settings.VideoGenerationProviders.AtlasCloud.Enabled = ParseBoolean(value, key); break;
            case "VideoGenerationProviders.AtlasCloud.ApiBaseUrl":
                settings.VideoGenerationProviders.AtlasCloud.ApiBaseUrl = ValidateHttpsUrl(value, "AtlasCloud API base URL");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown non-secret application setting.");
        }
    }

    private static bool ParseBoolean(string value, string key) =>
        bool.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"'{key}' must be true or false.");

    private static string ValidateHttpsUrl(string value, string displayName) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : throw new ArgumentException($"{displayName} must be an absolute HTTPS URL.");

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}

public sealed record ConfigurationStatus(bool IsConfigured, IReadOnlyList<string> MissingDisplayNames)
{
    public string Summary => IsConfigured ? "Configured" : $"Missing {string.Join(", ", MissingDisplayNames)}";
}

public sealed class ApplicationConfigurationValidator
{
    private readonly ISecretStore _secretStore;

    public ApplicationConfigurationValidator(ISecretStore secretStore) => _secretStore = secretStore;

    public async Task<ConfigurationStatus> ValidateSectionAsync(
        ApplicationSettings settings,
        string section,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        foreach (var requirement in ApplicationConfigurationCatalog.Requirements.Where(item =>
                     item.Section.Equals(section, StringComparison.Ordinal) && item.Required))
        {
            var exists = requirement.Secret
                ? await _secretStore.ExistsAsync(requirement.CredentialManagerKey!, cancellationToken).ConfigureAwait(false)
                : !string.IsNullOrWhiteSpace(ApplicationSettingsAccessor.Get(settings, requirement.Key));
            if (!exists) missing.Add(requirement.DisplayName);
        }

        if (section == ApplicationConfigurationCatalog.R2Section && missing.Count == 0)
        {
            var endpoint = settings.TemporaryAssetHosting.CloudflareR2.Endpoint;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                missing.Add("valid HTTPS R2 S3 endpoint");
        }

        return new ConfigurationStatus(missing.Count == 0, missing);
    }
}

public sealed class ApplicationSettingsEditor
{
    private readonly IApplicationSettingsStore _store;
    private readonly HashSet<string> _dirtyKeys = new(StringComparer.Ordinal);

    public ApplicationSettingsEditor(IApplicationSettingsStore store, ApplicationSettings settings)
    {
        _store = store;
        Settings = settings;
    }

    public ApplicationSettings Settings { get; }
    public bool IsDirty => _dirtyKeys.Count > 0;

    public void Update(string key, string value)
    {
        var before = ApplicationSettingsAccessor.Get(Settings, key);
        if (before.Equals(value.Trim(), StringComparison.Ordinal)) return;
        ApplicationSettingsAccessor.Set(Settings, key, value);
        _dirtyKeys.Add(key);
    }

    public async Task<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDirty) return false;
        await _store.SaveAsync(Settings, cancellationToken).ConfigureAwait(false);
        _dirtyKeys.Clear();
        return true;
    }
}

public sealed class SecretConfigurationService
{
    private readonly ISecretStore _store;

    public SecretConfigurationService(ISecretStore store) => _store = store;

    public Task<bool> IsConfiguredAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default) =>
        _store.ExistsAsync(RequireSecretKey(requirement), cancellationToken);

    public Task ReplaceAsync(ConfigurationRequirement requirement, string newValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newValue)) throw new ArgumentException("Enter the complete new credential.", nameof(newValue));
        return _store.SetAsync(RequireSecretKey(requirement), newValue.Trim(), cancellationToken);
    }

    public Task RemoveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(RequireSecretKey(requirement), cancellationToken);

    private static string RequireSecretKey(ConfigurationRequirement requirement) =>
        requirement.Secret && !string.IsNullOrWhiteSpace(requirement.CredentialManagerKey)
            ? requirement.CredentialManagerKey
            : throw new ArgumentException("The configuration requirement is not a managed secret.", nameof(requirement));
}
