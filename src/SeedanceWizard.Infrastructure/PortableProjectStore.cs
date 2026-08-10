using System.Text.Json;
using System.Text.Json.Serialization;
using SeedanceWizard.Application;
using SeedanceWizard.Core;

namespace SeedanceWizard.Infrastructure;

public sealed class PortableProjectStore : IProjectStore
{
    public const string ProjectFileName = "project.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var root = Path.GetFullPath(rootDirectory);
        var location = new ProjectLocation(root, Path.Combine(root, ProjectFileName));
        if (File.Exists(location.ProjectFilePath))
        {
            throw new IOException($"A project already exists at '{location.ProjectFilePath}'.");
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets", "images"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "videos"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "audio"));
        Directory.CreateDirectory(Path.Combine(root, "generated"));
        Directory.CreateDirectory(Path.Combine(root, "exports"));
        Directory.CreateDirectory(Path.Combine(root, "cache"));

        var project = new VideoProject { Name = name.Trim() };
        await SaveAsync(project, location, cancellationToken).ConfigureAwait(false);
        return (project, location);
    }

    public async Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);

        var fullPath = Path.GetFullPath(projectFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Project file was not found.", fullPath);
        }

        var json = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaProperty) ||
            !schemaProperty.TryGetInt32(out var schemaVersion))
        {
            throw new InvalidDataException("The project file does not declare a valid schemaVersion.");
        }

        if (schemaVersion > VideoProject.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Project schema {schemaVersion} is newer than this application supports ({VideoProject.CurrentSchemaVersion}).");
        }

        var root = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The project file must have a parent directory.");

        if (schemaVersion == VideoProject.CurrentSchemaVersion)
        {
            var dto = JsonSerializer.Deserialize<ProjectV2Dto>(json, SerializerOptions)
                ?? throw new InvalidDataException("The project file did not contain a valid schema-v2 project.");
            var project = ProjectPersistenceMapper.FromDto(dto);
            RefreshPhysicalAvailability(project, root);
            ProjectInvariantValidator.ThrowIfInvalid(project);
            return (project, new ProjectLocation(root, fullPath));
        }

        if (schemaVersion != 1)
            throw new InvalidDataException($"Project schema {schemaVersion} is not supported.");

        var legacy = JsonSerializer.Deserialize<ProjectV1Dto>(json, SerializerOptions)
            ?? throw new InvalidDataException("The project file did not contain a valid schema-v1 project.");
        var migrated = ProjectPersistenceMapper.Migrate(legacy);
        RefreshPhysicalAvailability(migrated, root);
        ProjectInvariantValidator.ThrowIfInvalid(migrated);

        var backupPath = GetAvailableBackupPath(root, schemaVersion);
        File.Copy(fullPath, backupPath, overwrite: false);
        var migratedLocation = new ProjectLocation(
            root,
            fullPath,
            new ProjectMigrationNotice(schemaVersion, VideoProject.CurrentSchemaVersion, backupPath));
        await SaveAsync(migrated, migratedLocation, cancellationToken).ConfigureAwait(false);
        return (migrated, migratedLocation);
    }

    public async Task SaveAsync(
        VideoProject project,
        ProjectLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        ProjectInvariantValidator.ThrowIfInvalid(project);

        Directory.CreateDirectory(location.RootDirectory);
        var temporaryPath = $"{location.ProjectFilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer
                    .SerializeAsync(stream, ProjectPersistenceMapper.ToDto(project), SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, location.ProjectFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetAvailableBackupPath(string rootDirectory, int schemaVersion)
    {
        var baseName = $"project.backup-v{schemaVersion}";
        var candidate = Path.Combine(rootDirectory, $"{baseName}.json");
        var suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(rootDirectory, $"{baseName}-{suffix}.json");
            suffix++;
        }

        return candidate;
    }

    private static void RefreshPhysicalAvailability(VideoProject project, string rootDirectory)
    {
        foreach (var asset in project.Assets.Where(asset => asset.StorageKind == AssetStorageKind.Physical && asset.Physical is not null))
        {
            var candidate = Path.GetFullPath(Path.Combine(rootDirectory, asset.Physical!.RelativePath));
            var root = Path.GetFullPath(rootDirectory + Path.DirectorySeparatorChar);
            asset.Physical.Availability = candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
                ? PhysicalAssetAvailability.Available
                : PhysicalAssetAvailability.Missing;
        }
    }
}
