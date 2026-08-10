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

        await using var stream = File.OpenRead(fullPath);
        var project = await JsonSerializer
            .DeserializeAsync<VideoProject>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The project file did not contain a valid project.");

        if (project.SchemaVersion > VideoProject.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Project schema {project.SchemaVersion} is newer than this application supports ({VideoProject.CurrentSchemaVersion}).");
        }

        var root = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("The project file must have a parent directory.");

        return (project, new ProjectLocation(root, fullPath));
    }

    public async Task SaveAsync(
        VideoProject project,
        ProjectLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);

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
                    .SerializeAsync(stream, project, SerializerOptions, cancellationToken)
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
}
