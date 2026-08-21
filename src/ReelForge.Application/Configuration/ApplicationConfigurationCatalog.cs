namespace ReelForge.Application;

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
            "Use the platform default log folder", GeneralSection),
        new("MediaTools.FfmpegPath", "FFmpeg path", "Explicit ffmpeg.exe path. Leave empty to use PATH auto-detection.", false, false,
            @"C:\path\to\ffmpeg.exe", MediaToolsSection),
        new("MediaTools.FfprobePath", "ffprobe path", "Explicit ffprobe.exe path. Leave empty to use PATH auto-detection.", false, false,
            @"C:\path\to\ffprobe.exe", MediaToolsSection),
        new("MediaTools.CacheSizeBytes", "Media cache limit", "Maximum disk space for disposable media derivatives. Lower cache values may make some video-editing actions perform poorly or become impossible.", false, false,
            MediaToolConfiguration.DefaultCacheSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), MediaToolsSection),
        new("MediaTools.PersistModifiedMediaOnDisk", "Persist modified media on disk", "Captured frames, video clippings, and in-progress compositions are normally stored in cache to optimize space usage and are rebuilt when necessary. Persisting this media on disk will save them as normal, permanent files in your project's media folder.", false, false,
            "false", MediaToolsSection),
        new("MediaTools.SplitBehavior", "Media split behavior", "Choose whether the selected frame belongs to the first or second resulting clip when splitting media.", false, false,
            MediaSplitBehavior.BeforeSelectedFrame.ToString(), MediaToolsSection),
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
