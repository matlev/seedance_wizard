namespace ReelForge.Application;

public interface ISecretStore
{
    string DisplayName => "secure credential store";
    string GetDisplayKey(string key) => key;
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(await GetAsync(key, cancellationToken).ConfigureAwait(false));
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public interface IApplicationSettingsStore
{
    string LocalSettingsPath { get; }
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
