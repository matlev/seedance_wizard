using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed record ProjectLocation(
    string RootDirectory,
    string ProjectFilePath);

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

public interface IContentHashService
{
    Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default);
    Task<ContentVerificationResult> VerifyAsync(
        string path,
        ContentIdentity expected,
        CancellationToken cancellationToken = default);
}

public sealed record ContentVerificationResult(bool MatchesExpected, ContentIdentity Observed);

public enum MaterializationPurpose
{
    Preview,
    ProviderUpload,
    FinalExport,
    FrameExtraction,
    Thumbnail,
    Waveform
}

public enum MaterializationRetentionPreference
{
    Unspecified,
    Ephemeral,
    NormalCache,
    PreferRetained,
    Persistent
}

public abstract record MaterializationTarget;

public sealed record AssetMaterializationTarget(
    Guid AssetId,
    Guid? RecipeRevisionId = null) : MaterializationTarget;

public sealed record AnchorMaterializationTarget(
    Guid AnchorId,
    Guid AnchorRevisionId) : MaterializationTarget;

public sealed record MaterializationRequest(
    MaterializationTarget Target,
    MaterializationPurpose Purpose,
    MaterializationRetentionPreference RetentionPreference = MaterializationRetentionPreference.Unspecified,
    string? Profile = null);

public sealed class MaterializedMediaLease : IAsyncDisposable
{
    private Func<ValueTask>? _release;

    public MaterializedMediaLease(
        string path,
        ContentIdentity contentIdentity,
        MediaEncodingMetadata? encoding,
        bool isDurableSource,
        Func<ValueTask>? release = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        ContentIdentity = contentIdentity;
        Encoding = encoding;
        IsDurableSource = isDurableSource;
        _release = release;
    }

    public string Path { get; }
    public ContentIdentity ContentIdentity { get; }
    public MediaEncodingMetadata? Encoding { get; }
    public bool IsDurableSource { get; }

    public ValueTask DisposeAsync()
    {
        var release = Interlocked.Exchange(ref _release, null);
        return release?.Invoke() ?? ValueTask.CompletedTask;
    }
}

public interface IMediaMaterializer
{
    Task<MaterializedMediaLease> MaterializeAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record VideoPresentationFrame(
    int VideoStreamIndex,
    long PresentationTimestamp,
    int TimeBaseNumerator,
    int TimeBaseDenominator,
    long? FrameNumber = null)
{
    public double TimestampSeconds =>
        PresentationTimestamp * (double)TimeBaseNumerator / TimeBaseDenominator;
}

public interface IExactVideoFrameService
{
    Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
        string mediaPath,
        double centerSeconds,
        double radiusSeconds = 2,
        CancellationToken cancellationToken = default);

    Task<MaterializedMediaLease> ExtractAsync(
        string mediaPath,
        string sourceContentHash,
        FrameAnchorRevision revision,
        MaterializationPurpose purpose,
        string? profile = null,
        CancellationToken cancellationToken = default);
}

public interface IMaterializationRetentionPolicy
{
    MaterializationRetentionPreference Resolve(
        MaterializationPurpose purpose,
        MaterializationTarget target,
        MaterializationRetentionPreference requestedPreference);
}

public sealed record PreparedProviderReference(
    GenerationReferenceSnapshot LogicalReference,
    string ProviderRepresentation,
    MaterializationReceipt? Receipt);

public interface IProviderAssetPreparationService
{
    Task<PreparedProviderReference> PrepareAsync(
        string providerId,
        GenerationReferenceSnapshot logicalReference,
        MaterializedMediaLease media,
        GenerationSubmissionAuthorization authorization,
        CancellationToken cancellationToken = default);
}

public enum GenerationProviderCostBehavior
{
    NoCharge,
    PotentiallyBillable
}

public sealed class GenerationSubmissionAuthorization
{
    private readonly bool _networkIsolatedTest;

    private GenerationSubmissionAuthorization(
        string providerId,
        DateTimeOffset confirmedAt,
        Guid userActionId,
        bool networkIsolatedTest)
    {
        ProviderId = providerId;
        ConfirmedAt = confirmedAt;
        UserActionId = userActionId;
        _networkIsolatedTest = networkIsolatedTest;
    }

    public string ProviderId { get; }
    public DateTimeOffset ConfirmedAt { get; }
    public Guid UserActionId { get; }

    internal static GenerationSubmissionAuthorization FromInteractiveUserConfirmation(
        string providerId,
        bool userConfirmedPotentialCharges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (!userConfirmedPotentialCharges)
            throw new InvalidOperationException("Potentially billable generation was not confirmed by the user.");

        return new GenerationSubmissionAuthorization(providerId, DateTimeOffset.UtcNow, Guid.NewGuid(), false);
    }

    internal static GenerationSubmissionAuthorization ForNetworkIsolatedTest(string providerId) =>
        new(providerId, DateTimeOffset.UtcNow, Guid.NewGuid(), true);

    public void Demand(string providerId, bool allowNetworkIsolatedTest = false)
    {
        if (!ProviderId.Equals(providerId, StringComparison.Ordinal) ||
            DateTimeOffset.UtcNow - ConfirmedAt > TimeSpan.FromMinutes(5) ||
            (_networkIsolatedTest && !allowNetworkIsolatedTest))
        {
            throw new InvalidOperationException(
                "A fresh, matching interactive confirmation is required before a potentially billable request.");
        }
    }
}

public interface IVideoGenerationProvider
{
    GenerationProviderCapabilities Capabilities { get; }
    GenerationProviderCostBehavior CostBehavior { get; }

    Task<GenerationSubmission> SubmitAsync(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets,
        GenerationSubmissionAuthorization? authorization = null,
        CancellationToken cancellationToken = default);
}

public interface IApiKeyVideoGenerationProvider : IVideoGenerationProvider
{
    string ApiKeyCredentialKey { get; }
}

public sealed record ProviderGenerationOutput(string DownloadUrl);

public sealed class ProviderGenerationJob
{
    public string ProviderJobId { get; init; } = string.Empty;
    public GenerationStatus Status { get; init; }
    public IReadOnlyList<ProviderGenerationOutput> Outputs { get; init; } = [];
    public GenerationError? Error { get; init; }
    public Dictionary<string, string> ResponseMetadata { get; init; } = new(StringComparer.Ordinal);
}

public interface IAsyncVideoGenerationProvider : IVideoGenerationProvider
{
    Task<ProviderGenerationJob> GetJobAsync(
        string providerJobId,
        CancellationToken cancellationToken = default);
}

public interface IGeneratedOutputIngestionService
{
    Task<IReadOnlyList<ProjectAsset>> IngestAsync(
        ProjectLocation location,
        Guid generationId,
        IReadOnlyList<ProviderGenerationOutput> outputs,
        CancellationToken cancellationToken = default);
}

public sealed record GenerationWorkflowProgress(
    GenerationStatus RemoteStatus,
    OutputIngestionStatus IngestionStatus,
    string Message);

public sealed class GenerationWorkflowOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromMinutes(15);
}

public interface ISecretStore
{
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

public sealed record TemporaryAssetHostRequest(
    GenerationReferenceSnapshot LogicalReference,
    MaterializedMediaLease Media,
    string ContentType,
    TimeSpan ReadUrlLifetime);

public sealed record HostedAssetReference(
    string HostingProvider,
    string ObjectKey,
    string ContentSha256,
    Uri ReadUrl,
    DateTimeOffset ReadUrlExpiresAt,
    bool Uploaded);

public interface ITemporaryAssetHost
{
    string ProviderId { get; }
    Task<HostedAssetReference> EnsureHostedAsync(
        TemporaryAssetHostRequest request,
        CancellationToken cancellationToken = default);
    Task RemoveAsync(string objectKey, CancellationToken cancellationToken = default);
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

public enum ConnectionFailureKind
{
    None,
    MissingConfiguration,
    MissingCredential,
    AuthenticationRejected,
    InsufficientPermissions,
    NetworkFailure,
    EndpointUnavailable,
    Unknown
}

public sealed record ConnectionTestResult(
    bool Succeeded,
    string Message,
    ConnectionFailureKind FailureKind = ConnectionFailureKind.None);

public sealed class MediaToolConfiguration
{
    public const long DefaultCacheSizeBytes = 10L * 1024 * 1024 * 1024;

    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
    public long CacheSizeBytes { get; set; } = DefaultCacheSizeBytes;
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

public sealed record DiagnosticLogReference(string EventId, string FilePath);

public interface IApplicationDiagnosticLog
{
    string LogDirectory { get; }

    Task<DiagnosticLogReference?> WriteErrorAsync(
        string category,
        string message,
        IReadOnlyDictionary<string, string?> details,
        CancellationToken cancellationToken = default);
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
        GenerationSubmissionAuthorization? authorization = null,
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
            RequestSnapshot = CreateSnapshot(provider, request, Project.Assets),
            RequestedAt = DateTimeOffset.UtcNow,
            Status = GenerationStatus.Queued
        };

        Project.Generations.Add(record);
        Project.Touch();
        await _projectStore.SaveAsync(Project, Location!, cancellationToken).ConfigureAwait(false);

        try
        {
            var submission = await provider
                .SubmitAsync(request, Project.Assets, authorization, cancellationToken)
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
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
        {
            throw new InvalidOperationException($"Virtual asset '{asset.Id}' must be materialized before a path is requested.");
        }

        return Path.GetFullPath(Path.Combine(Location!.RootDirectory, asset.Physical.RelativePath));
    }

    private static GenerationRequestSnapshot CreateSnapshot(
        IVideoGenerationProvider provider,
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> assets)
    {
        var references = request.ReferenceAssetIds.Select(
            (id, index) =>
            {
                var asset = assets.Single(candidate => candidate.Id == id);
                return new GenerationReferenceSnapshot
                {
                    ObjectKind = GenerationReferenceObjectKind.Asset,
                    LogicalObjectId = asset.Id,
                    RecipeRevisionId = asset.Virtual?.CurrentRecipeRevisionId,
                    ContentHash = asset.Physical?.ContentIdentity.Sha256,
                    Role = GenerationReferenceRole.GeneralReference,
                    Order = index
                };
            }).ToArray();

        return new GenerationRequestSnapshot
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Mode = request.Mode,
            Prompt = request.Prompt,
            DurationSeconds = request.DurationSeconds,
            AspectRatio = request.AspectRatio,
            Resolution = request.Resolution,
            References = Array.AsReadOnly(references),
            ProviderParameters = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(request.ProviderParameters, StringComparer.Ordinal))
        };
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
