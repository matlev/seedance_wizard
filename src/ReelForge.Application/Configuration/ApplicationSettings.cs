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
    public List<string> RecentProjectFilePaths { get; set; } = [];
    public int UndoSendSeconds { get; set; }
    public string LogDirectory { get; set; } = string.Empty;
    public Dictionary<string, ProjectUserInterfaceState> ProjectStates { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
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
    public const string Marker = "<MANAGED BY SECURE CREDENTIAL STORE>";

    public ManagedCredentialDeclaration() { }

    public ManagedCredentialDeclaration(string credentialManagerKey)
    {
        CredentialManagerKey = credentialManagerKey;
    }

    public string CredentialManagerKey { get; set; } = string.Empty;
    public string Value { get; set; } = Marker;
}
