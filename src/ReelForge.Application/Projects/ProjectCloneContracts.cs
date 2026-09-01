using ReelForge.Core;

namespace ReelForge.Application;

public sealed record ProjectCloneRequest(
    string SourceProjectFilePath,
    string DestinationParentDirectory,
    string CloneName);

public enum ProjectClonePhase
{
    Validating,
    Scanning,
    Copying,
    WritingProject,
    ValidatingClone,
    Publishing,
    Completed
}

public sealed record ProjectCloneProgress(
    ProjectClonePhase Phase,
    int CopiedFileCount = 0,
    int TotalFileCount = 0,
    long CopiedBytes = 0,
    long TotalBytes = 0,
    string? CurrentRelativePath = null);

public sealed record ProjectCloneResult(
    VideoProject Project,
    ProjectLocation Location,
    int CopiedFileCount,
    long CopiedBytes);

public sealed record ProjectCloneStaging(
    ProjectLocation StagingLocation,
    ProjectLocation FinalLocation,
    int CopiedFileCount,
    long CopiedBytes);

public interface IProjectCloneFileSystem
{
    Task<ProjectCloneStaging> StageDurableContentAsync(
        ProjectLocation sourceLocation,
        string destinationParentDirectory,
        string cloneName,
        IProgress<ProjectCloneProgress>? progress,
        CancellationToken cancellationToken);

    Task PublishAsync(ProjectCloneStaging staging, CancellationToken cancellationToken);

    Task RollbackAsync(ProjectCloneStaging staging);
}
