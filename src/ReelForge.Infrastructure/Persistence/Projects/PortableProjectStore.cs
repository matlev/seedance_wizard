using System.Text.Json;
using System.Text.Json.Serialization;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class PortableProjectStore :
    IProjectStore,
    IProjectCommitGuardedStore,
    IProjectRecoveryStore,
    IProjectRecoveryCommitGuardedStore
{
    public const string ProjectFileExtension = ".rfp";
    public const string RecoveryFileExtension = ".recovery";

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
        _ = await SaveIfAsync(project, location, CommitUnconditionally, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SaveIfAsync(
        VideoProject project,
        ProjectLocation location,
        Func<Action, bool> tryCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(tryCommit);
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
        cancellationToken.ThrowIfCancellationRequested();
        return tryCommit(() => fileCommit.Commit(overwrite: true));
    }

    public static string GetRecoveryFilePath(ProjectLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return Path.GetFullPath(location.ProjectFilePath) + RecoveryFileExtension;
    }

    public async Task<ProjectRecoveryProbe> ProbeAsync(
        ProjectLocation location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        var recoveryPath = GetRecoveryFilePath(location);
        if (!File.Exists(recoveryPath))
            return ProjectRecoveryProbe.None;

        try
        {
            var json = await File.ReadAllBytesAsync(recoveryPath, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<ProjectRecoveryFileDto>(json, SerializerOptions)
                ?? throw new InvalidDataException("The recovery candidate did not contain a valid envelope.");
            if (envelope.RecoveryFormatVersion != ProjectRecoveryFileDto.CurrentRecoveryFormatVersion ||
                envelope.Project is null ||
                envelope.Project.FormatVersion != ProjectFileDto.CurrentFormatVersion ||
                !IsPayloadHashValid(envelope) ||
                !IsHash(envelope.BaseProjectSha256))
            {
                throw new InvalidDataException("The recovery candidate uses an unsupported format.");
            }

            var committedJson = await File.ReadAllBytesAsync(location.ProjectFilePath, cancellationToken).ConfigureAwait(false);
            var committedDto = JsonSerializer.Deserialize<ProjectFileDto>(committedJson, SerializerOptions)
                ?? throw new InvalidDataException("The committed project did not contain a valid ReelForge project.");
            if (PayloadHash(envelope.Project) == PayloadHash(committedDto))
            {
                await DiscardAsync(location, cancellationToken).ConfigureAwait(false);
                return ProjectRecoveryProbe.None;
            }

            if (!string.Equals(HashBytes(committedJson), envelope.BaseProjectSha256, StringComparison.Ordinal))
            {
                return new ProjectRecoveryProbe(
                    null,
                    "Recovery data was ignored because it is stale relative to the committed project.");
            }

            var project = ProjectPersistenceMapper.FromDto(envelope.Project);
            RefreshPhysicalAvailability(project, location);
            ProjectInvariantValidator.ThrowIfInvalid(project);
            var isDegraded = project.Assets.Any(asset =>
                asset.StorageKind == AssetStorageKind.Physical &&
                asset.Physical?.Availability != PhysicalAssetAvailability.Available);
            return new ProjectRecoveryProbe(new ProjectRecoveryCandidate(
                project,
                isDegraded,
                isDegraded ? "Recovered media has unavailable physical assets." : null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or
            UnauthorizedAccessException or ProjectValidationException)
        {
            return new ProjectRecoveryProbe(null, $"Recovery data was ignored because it is invalid: {exception.Message}");
        }
    }

    public async Task WriteAsync(
        VideoProject project,
        ProjectLocation location,
        CancellationToken cancellationToken = default)
    {
        _ = await WriteIfAsync(project, location, CommitUnconditionally, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> WriteIfAsync(
        VideoProject project,
        ProjectLocation location,
        Func<Action, bool> tryCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(tryCommit);
        ProjectInvariantValidator.ThrowIfInvalid(project);

        Directory.CreateDirectory(location.RootDirectory);
        var committedProject = await File.ReadAllBytesAsync(location.ProjectFilePath, cancellationToken)
            .ConfigureAwait(false);
        var dto = ProjectPersistenceMapper.ToDto(project);
        using var fileCommit = AtomicFileCommit.Create(GetRecoveryFilePath(location), "project-recovery");
        await using (var stream = new FileStream(
            fileCommit.TemporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                new ProjectRecoveryFileDto
                {
                    Project = dto,
                    ProjectPayloadSha256 = PayloadHash(dto),
                    BaseProjectSha256 = HashBytes(committedProject)
                },
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return tryCommit(() => fileCommit.Commit(overwrite: true));
    }

    public Task DiscardAsync(ProjectLocation location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        cancellationToken.ThrowIfCancellationRequested();
        var recoveryPath = GetRecoveryFilePath(location);
        if (File.Exists(recoveryPath))
            File.Delete(recoveryPath);
        return Task.CompletedTask;
    }

    private static void RefreshPhysicalAvailability(VideoProject project, ProjectLocation location)
    {
        foreach (var asset in project.Assets.Where(asset => asset.StorageKind == AssetStorageKind.Physical && asset.Physical is not null))
        {
            var physical = asset.Physical!;
            if (asset.IsDeleted)
            {
                physical.Availability = PhysicalAssetAvailability.Missing;
                continue;
            }
            if (!ProjectPathPolicy.TryResolveContainedPath(location, physical.RelativePath, out var candidate))
            {
                physical.Availability = PhysicalAssetAvailability.Missing;
                continue;
            }

            try
            {
                using var stream = new FileStream(
                    candidate,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1,
                    FileOptions.SequentialScan);
            }
            catch (FileNotFoundException)
            {
                physical.Availability = PhysicalAssetAvailability.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                physical.Availability = PhysicalAssetAvailability.Missing;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                physical.Availability = PhysicalAssetAvailability.Inaccessible;
            }
        }
    }

    private static bool IsPayloadHashValid(ProjectRecoveryFileDto envelope) =>
        IsHash(envelope.ProjectPayloadSha256) &&
        string.Equals(envelope.ProjectPayloadSha256, PayloadHash(envelope.Project!), StringComparison.Ordinal);

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string PayloadHash(ProjectFileDto dto) => HashBytes(JsonSerializer.SerializeToUtf8Bytes(dto, SerializerOptions));

    private static string HashBytes(byte[] bytes) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool CommitUnconditionally(Action commit)
    {
        commit();
        return true;
    }
}
