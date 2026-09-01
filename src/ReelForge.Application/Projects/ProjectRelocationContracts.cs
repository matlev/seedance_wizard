using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Moves an existing project folder without changing its project identity or persisted meaning.
/// The requested destination is the exact new project root, not a parent folder.
/// </summary>
public sealed record ProjectRelocationRequest(string DestinationRootDirectory);

public enum ProjectRelocationPhase
{
    Validating,
    Scanning,
    Copying,
    ValidatingDestination,
    Publishing,
    RemovingSource,
    Completed
}

public sealed record ProjectRelocationProgress(
    ProjectRelocationPhase Phase,
    int CopiedFileCount = 0,
    int TotalFileCount = 0,
    long CopiedBytes = 0,
    long TotalBytes = 0,
    string? CurrentRelativePath = null);

public sealed record ProjectRelocationPlan(
    ProjectLocation SourceLocation,
    ProjectLocation FinalLocation,
    ProjectLocation? StagingLocation,
    bool UsesStaging,
    int CopiedFileCount,
    long CopiedBytes);

public sealed record ProjectRelocationResult(
    VideoProject Project,
    ProjectLocation Location,
    bool SourceCleanupCompleted,
    string? SourceCleanupWarning = null);

public interface IProjectRelocationFileSystem
{
    Task<ProjectRelocationPlan> PrepareAsync(
        ProjectLocation sourceLocation,
        string destinationRootDirectory,
        IProgress<ProjectRelocationProgress>? progress,
        CancellationToken cancellationToken);

    Task PublishAsync(ProjectRelocationPlan plan, CancellationToken cancellationToken);

    Task RemoveSourceAsync(ProjectRelocationPlan plan, CancellationToken cancellationToken);

    Task RollbackAsync(ProjectRelocationPlan plan);
}
