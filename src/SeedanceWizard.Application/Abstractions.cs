using SeedanceWizard.Core;

namespace SeedanceWizard.Application;

public sealed record ProjectLocation(string RootDirectory, string ProjectFilePath);

public interface IProjectStore
{
    Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default);

    Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
        string projectFilePath,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        VideoProject project,
        ProjectLocation location,
        CancellationToken cancellationToken = default);
}

public interface IAssetImportService
{
    Task<IReadOnlyList<ProjectAsset>> ImportAsync(
        ProjectLocation location,
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default);
}

public interface IMediaInspectionService
{
    Task<MediaEncodingMetadata> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);
}

public interface IVideoGenerationProvider
{
    GenerationProviderCapabilities Capabilities { get; }

    Task<GenerationSubmission> SubmitAsync(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets,
        CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class MediaToolConfiguration
{
    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
}

public interface IMediaToolSettingsStore
{
    Task<MediaToolConfiguration> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MediaToolConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed record MediaToolAvailability(
    string? FfmpegPath,
    string? FfprobePath,
    string Summary)
{
    public bool IsReady => FfmpegPath is not null && FfprobePath is not null;
}

public interface IMediaToolDiscovery
{
    MediaToolAvailability Discover(string? configuredFfmpegPath = null, string? configuredFfprobePath = null);
}

public sealed class VideoGenerationProviderException : Exception
{
    public VideoGenerationProviderException(
        string message,
        int? httpStatus = null,
        string? providerCode = null,
        string? technicalDetails = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
        TechnicalDetails = technicalDetails;
    }

    public int? HttpStatus { get; }
    public string? ProviderCode { get; }
    public string? TechnicalDetails { get; }
}

public sealed class ProjectWorkspace
{
    private readonly IProjectStore _projectStore;
    private readonly IAssetImportService _assetImporter;

    public ProjectWorkspace(IProjectStore projectStore, IAssetImportService assetImporter)
    {
        _projectStore = projectStore;
        _assetImporter = assetImporter;
    }

    public VideoProject? Project { get; private set; }
    public ProjectLocation? Location { get; private set; }

    public async Task CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default)
    {
        (Project, Location) = await _projectStore
            .CreateAsync(rootDirectory, name, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task OpenAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        (Project, Location) = await _projectStore
            .OpenAsync(projectFilePath, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        EnsureProjectIsOpen();
        Project!.Touch();
        return _projectStore.SaveAsync(Project, Location!, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectAsset>> ImportAssetsAsync(
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectIsOpen();
        var imported = await _assetImporter
            .ImportAsync(Location!, sourcePaths, cancellationToken)
            .ConfigureAwait(false);

        foreach (var asset in imported)
        {
            Project!.AddAsset(asset);
        }

        await _projectStore.SaveAsync(Project!, Location!, cancellationToken).ConfigureAwait(false);
        return imported;
    }

    public async Task<GenerationRecord> SubmitGenerationAsync(
        IVideoGenerationProvider provider,
        GenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectIsOpen();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);

        var validationErrors = provider.Capabilities.Validate(request, Project!.Assets);
        if (validationErrors.Count > 0)
        {
            throw new GenerationValidationException(validationErrors);
        }

        var record = new GenerationRecord
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Request = request,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = GenerationStatus.Queued
        };

        Project.Generations.Add(record);
        Project.Touch();
        await _projectStore.SaveAsync(Project, Location!, cancellationToken).ConfigureAwait(false);

        try
        {
            var submission = await provider
                .SubmitAsync(request, Project.Assets, cancellationToken)
                .ConfigureAwait(false);

            record.ProviderJobId = submission.ProviderJobId;
            record.Status = submission.Status;
            record.ResponseMetadata = submission.ResponseMetadata;
            if (record.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
            {
                record.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            record.Status = GenerationStatus.Cancelled;
            record.CompletedAt = DateTimeOffset.UtcNow;
            throw;
        }
        catch (VideoGenerationProviderException exception)
        {
            record.Status = GenerationStatus.Failed;
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.Error = new GenerationError
            {
                HttpStatus = exception.HttpStatus,
                ProviderCode = exception.ProviderCode,
                Message = exception.Message,
                TechnicalDetails = exception.TechnicalDetails ?? exception.ToString()
            };
        }
        catch (Exception exception)
        {
            record.Status = GenerationStatus.Failed;
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.Error = new GenerationError
            {
                Message = exception.Message,
                TechnicalDetails = exception.ToString()
            };
        }
        finally
        {
            Project.Touch();
            await _projectStore.SaveAsync(Project, Location!, CancellationToken.None).ConfigureAwait(false);
        }

        return record;
    }

    public string GetAbsoluteAssetPath(ProjectAsset asset)
    {
        EnsureProjectIsOpen();
        ArgumentNullException.ThrowIfNull(asset);
        return Path.GetFullPath(Path.Combine(Location!.RootDirectory, asset.RelativePath));
    }

    private void EnsureProjectIsOpen()
    {
        if (Project is null || Location is null)
        {
            throw new InvalidOperationException("Create or open a project first.");
        }
    }
}

public sealed class GenerationValidationException : Exception
{
    public GenerationValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
