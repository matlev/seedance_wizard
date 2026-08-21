using ReelForge.Core;

namespace ReelForge.Application;

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

public sealed class GenerationValidationException : Exception
{
    public GenerationValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
