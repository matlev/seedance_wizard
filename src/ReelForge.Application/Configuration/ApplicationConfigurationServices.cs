namespace ReelForge.Application;

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

    public Task<bool> IsConfiguredAsync(
        ConfigurationRequirement requirement,
        CancellationToken cancellationToken = default) =>
        _store.ExistsAsync(RequireSecretKey(requirement), cancellationToken);

    public Task ReplaceAsync(
        ConfigurationRequirement requirement,
        string newValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newValue))
            throw new ArgumentException("Enter the complete new credential.", nameof(newValue));
        return _store.SetAsync(RequireSecretKey(requirement), newValue.Trim(), cancellationToken);
    }

    public Task RemoveAsync(
        ConfigurationRequirement requirement,
        CancellationToken cancellationToken = default) =>
        _store.DeleteAsync(RequireSecretKey(requirement), cancellationToken);

    private static string RequireSecretKey(ConfigurationRequirement requirement) =>
        requirement.Secret && !string.IsNullOrWhiteSpace(requirement.CredentialManagerKey)
            ? requirement.CredentialManagerKey
            : throw new ArgumentException("The configuration requirement is not a managed secret.", nameof(requirement));
}
