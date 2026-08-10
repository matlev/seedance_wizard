using System.Text.Json;
using SeedanceWizard.Application;

namespace SeedanceWizard.Infrastructure;

public sealed class JsonMediaToolSettingsStore : IMediaToolSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    public JsonMediaToolSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeedanceWizard",
            "settings.json");
    }

    public async Task<MediaToolConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new MediaToolConfiguration();
        }

        await using var stream = File.OpenRead(_settingsPath);
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
