using System.Text.Json;
using System.Text.Json.Serialization;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class PortableProjectStore : IProjectStore
{
    public const string ProjectFileExtension = ".rfp";

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
        var location = new ProjectLocation(root, Path.Combine(root, GetProjectFileName(name)));
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

    public static string GetProjectFileName(string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        var trimmedName = projectName.Trim();
        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The project name contains characters that cannot be used in a project filename.", nameof(projectName));
        return $"{trimmedName}{ProjectFileExtension}";
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
        if (!document.RootElement.TryGetProperty("formatVersion", out var formatProperty) ||
            !formatProperty.TryGetInt32(out var formatVersion))
        {
            throw new InvalidDataException(
                "This project uses an obsolete development format and cannot be opened. Create a new ReelForge project and re-import any media you want to keep.");
        }

        if (formatVersion != ProjectFileDto.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"This project uses unsupported development format {formatVersion}; this build requires format {ProjectFileDto.CurrentFormatVersion}.");
        }

        var root = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The project file must have a parent directory.");

        var dto = JsonSerializer.Deserialize<ProjectFileDto>(json, SerializerOptions)
            ?? throw new InvalidDataException("The project file did not contain a valid ReelForge project.");
        var project = ProjectPersistenceMapper.FromDto(dto);
        RefreshPhysicalAvailability(project, new ProjectLocation(root, fullPath));
        ProjectInvariantValidator.ThrowIfInvalid(project);
        return (project, new ProjectLocation(root, fullPath));
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
        using var fileCommit = AtomicFileCommit.Create(location.ProjectFilePath, "project-save");
        await using (var stream = new FileStream(
            fileCommit.TemporaryPath,
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
        fileCommit.Commit(overwrite: true);
    }

    private static void RefreshPhysicalAvailability(VideoProject project, ProjectLocation location)
    {
        foreach (var asset in project.Assets.Where(asset => asset.StorageKind == AssetStorageKind.Physical && asset.Physical is not null))
        {
            asset.Physical!.Availability =
                ProjectPathPolicy.TryResolveContainedPath(location, asset.Physical.RelativePath, out var candidate) &&
                File.Exists(candidate)
                ? PhysicalAssetAvailability.Available
                : PhysicalAssetAvailability.Missing;
        }
    }
}
