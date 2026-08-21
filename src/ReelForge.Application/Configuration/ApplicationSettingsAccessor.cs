namespace ReelForge.Application;

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
        "MediaTools.SplitBehavior" => settings.MediaTools.SplitBehavior.ToString(),
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
                settings.General.LogDirectory = ApplicationPathResolver.ResolveDirectory(value);
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
            case "MediaTools.SplitBehavior":
                if (!Enum.TryParse<MediaSplitBehavior>(value, ignoreCase: true, out var splitBehavior) ||
                    !Enum.IsDefined(splitBehavior))
                    throw new ArgumentException("Media split behavior must be BeforeSelectedFrame or AfterSelectedFrame.");
                settings.MediaTools.SplitBehavior = splitBehavior;
                break;
            case "TemporaryAssetHosting.CloudflareR2.AccountId": settings.TemporaryAssetHosting.CloudflareR2.AccountId = value; break;
            case "TemporaryAssetHosting.CloudflareR2.BucketName": settings.TemporaryAssetHosting.CloudflareR2.BucketName = value; break;
            case "TemporaryAssetHosting.CloudflareR2.Endpoint": settings.TemporaryAssetHosting.CloudflareR2.Endpoint = value; break;
            case "TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes":
                if (!int.TryParse(value, out var minutes) || minutes is < 1 or > 10080)
                    throw new ArgumentException("Read URL lifetime must be between 1 and 10,080 minutes.");
                settings.TemporaryAssetHosting.CloudflareR2.PresignedUrlLifetimeMinutes = minutes;
                break;
            case "VideoGenerationProviders.BytePlus.Enabled":
                settings.VideoGenerationProviders.BytePlus.Enabled = ParseBoolean(value, key);
                break;
            case "VideoGenerationProviders.BytePlus.ApiBaseUrl":
                settings.VideoGenerationProviders.BytePlus.ApiBaseUrl = ValidateHttpsUrl(value, "BytePlus API base URL");
                break;
            case "VideoGenerationProviders.AtlasCloud.Enabled":
                settings.VideoGenerationProviders.AtlasCloud.Enabled = ParseBoolean(value, key);
                break;
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
