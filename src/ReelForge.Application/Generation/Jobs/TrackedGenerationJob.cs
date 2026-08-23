using ReelForge.Core;

namespace ReelForge.Application;

public sealed class TrackedGenerationJob
{
    public Guid GenerationId { get; set; }
    public string ProjectFilePath { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string ProviderJobId { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ProviderSubmittedAt { get; set; }
    public DateTimeOffset? UndoSendExpiresAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public GenerationStatus Status { get; set; } = GenerationStatus.Queued;
    public OutputIngestionStatus IngestionStatus { get; set; } = OutputIngestionStatus.NotRequired;
    public bool IsReconciled { get; set; }
    public bool IsAwaitingSubmission { get; set; }
    public bool WasCancelledBeforeSubmission { get; set; }
    public string Message { get; set; } = "Waiting to check provider status…";
    public List<ProviderGenerationOutput> Outputs { get; set; } = [];
    public GenerationError? Error { get; set; }
    public Dictionary<string, string> ResponseMetadata { get; set; } = new(StringComparer.Ordinal);
}
