using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class JsonApplicationSettingsStore : IApplicationSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _defaultsPath;
    public JsonApplicationSettingsStore(
        string? defaultsPath = null,
        string? localSettingsPath = null)
    {
        _defaultsPath = defaultsPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LocalSettingsPath = localSettingsPath ?? Path.Combine(localApplicationData, "ReelForge", "appsettings.local.json");
    }

    public string LocalSettingsPath { get; }

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var merged = JsonSerializer.SerializeToNode(new ApplicationSettings(), SerializerOptions)!.AsObject();
        if (File.Exists(_defaultsPath))
            Merge(merged, await ReadObjectAsync(_defaultsPath, cancellationToken).ConfigureAwait(false));
        if (File.Exists(LocalSettingsPath))
            Merge(merged, await ReadObjectAsync(LocalSettingsPath, cancellationToken).ConfigureAwait(false));

        var settings = merged.Deserialize<ApplicationSettings>(SerializerOptions) ?? new ApplicationSettings();
        Normalize(settings);
        return settings;
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        var directory = Path.GetDirectoryName(LocalSettingsPath)
            ?? throw new InvalidOperationException("The application settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = LocalSettingsPath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, LocalSettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task<JsonObject> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return node as JsonObject ?? throw new InvalidDataException($"'{path}' must contain a JSON object.");
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, sourceValue) in source)
        {
            if (sourceValue is JsonObject sourceObject && target[key] is JsonObject targetObject)
                Merge(targetObject, sourceObject);
            else
                target[key] = sourceValue?.DeepClone();
        }
    }

    private static void Normalize(ApplicationSettings settings)
    {
        settings.General ??= new GeneralApplicationSettings();
        settings.General.ProjectStates = new Dictionary<string, ProjectUserInterfaceState>(
            settings.General.ProjectStates ?? new Dictionary<string, ProjectUserInterfaceState>(),
            StringComparer.OrdinalIgnoreCase);
        settings.MediaTools ??= new MediaToolConfiguration();
        if (settings.MediaTools.CacheSizeBytes <= 0)
            settings.MediaTools.CacheSizeBytes = MediaToolConfiguration.DefaultCacheSizeBytes;
        settings.TemporaryAssetHosting ??= new TemporaryAssetHostingSettings();
        settings.TemporaryAssetHosting.CloudflareR2 ??= new CloudflareR2Settings();
        settings.VideoGenerationProviders ??= new VideoGenerationProviderSettings();
        settings.VideoGenerationProviders.BytePlus ??= new ProviderApplicationSettings();
        settings.VideoGenerationProviders.AtlasCloud ??= new ProviderApplicationSettings();

        var r2Credentials = settings.TemporaryAssetHosting.CloudflareR2.Credentials ??= new ManagedCredentialMap();
        r2Credentials.AccessKeyId = NormalizeCredential(r2Credentials.AccessKeyId, "cloudflare.r2.access-key-id");
        r2Credentials.SecretAccessKey = NormalizeCredential(r2Credentials.SecretAccessKey, "cloudflare.r2.secret-access-key");
        var bytePlusCredentials = settings.VideoGenerationProviders.BytePlus.Credentials ??= new ManagedCredentialMap();
        bytePlusCredentials.ApiKey = NormalizeCredential(bytePlusCredentials.ApiKey, "byteplus.modelark.api-key");
        var atlasCredentials = settings.VideoGenerationProviders.AtlasCloud.Credentials ??= new ManagedCredentialMap();
        atlasCredentials.ApiKey = NormalizeCredential(atlasCredentials.ApiKey, "atlascloud.api-key");
    }

    private static ManagedCredentialDeclaration NormalizeCredential(ManagedCredentialDeclaration? credential, string key)
    {
        credential ??= new ManagedCredentialDeclaration(key);
        credential.CredentialManagerKey = key;
        credential.Value = ManagedCredentialDeclaration.Marker;
        return credential;
    }
}
