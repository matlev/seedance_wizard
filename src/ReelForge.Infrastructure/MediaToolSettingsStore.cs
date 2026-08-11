using System.Text.Json;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class JsonMediaToolSettingsStore : IMediaToolSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;
    private readonly string? _legacySettingsPath;

    public JsonMediaToolSettingsStore(string? settingsPath = null)
    {
        if (settingsPath is not null)
        {
            _settingsPath = settingsPath;
            return;
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(localApplicationData, "ReelForge", "settings.json");
        _legacySettingsPath = Path.Combine(localApplicationData, "SeedanceWizard", "settings.json");
    }

    public async Task<MediaToolConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        var pathToLoad = File.Exists(_settingsPath)
            ? _settingsPath
            : _legacySettingsPath is not null && File.Exists(_legacySettingsPath)
                ? _legacySettingsPath
                : null;
        if (pathToLoad is null)
        {
            return new MediaToolConfiguration();
        }

        await using var stream = File.OpenRead(pathToLoad);
        return await JsonSerializer
            .DeserializeAsync<MediaToolConfiguration>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? new MediaToolConfiguration();
    }

    public async Task SaveAsync(MediaToolConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var directory = Path.GetDirectoryName(_settingsPath)
            ?? throw new InvalidOperationException("The media tool settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer
                .SerializeAsync(stream, configuration, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporaryPath, _settingsPath, overwrite: true);
    }
}
