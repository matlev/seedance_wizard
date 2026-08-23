using ReelForge.Core;

namespace ReelForge.Application;

public interface IGenerationJobStore
{
    Task<IReadOnlyList<TrackedGenerationJob>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<TrackedGenerationJob> jobs, CancellationToken cancellationToken = default);
}

public interface IGenerationJobFinalizer
{
    Task FinalizeAsync(TrackedGenerationJob job, CancellationToken cancellationToken = default);
}

public sealed class GenerationJobStatusChangedEventArgs : EventArgs
{
    public GenerationJobStatusChangedEventArgs(
        Guid generationId,
        string projectName,
        GenerationStatus previousStatus,
        GenerationStatus currentStatus)
    {
        GenerationId = generationId;
        ProjectName = projectName;
        PreviousStatus = previousStatus;
        CurrentStatus = currentStatus;
    }

    public Guid GenerationId { get; }
    public string ProjectName { get; }
    public GenerationStatus PreviousStatus { get; }
    public GenerationStatus CurrentStatus { get; }
}
