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

/// <summary>
/// Extends project persistence when session ownership must be revalidated at the atomic
/// replacement boundary. Returning false leaves the committed project untouched.
/// </summary>
public interface IProjectCommitGuardedStore
{
    Task<bool> SaveIfAsync(
        VideoProject project,
        ProjectLocation location,
        Func<Action, bool> tryCommit,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores one project-local, non-authoritative recovery candidate. The committed project file
/// remains the source of truth until an explicit workspace save succeeds.
/// </summary>
public interface IProjectRecoveryStore
{
    Task<ProjectRecoveryProbe> ProbeAsync(
        ProjectLocation location,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        VideoProject project,
        ProjectLocation location,
        CancellationToken cancellationToken = default);

    Task DiscardAsync(
        ProjectLocation location,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Extends recovery persistence with a final session-ownership check before replacement.
/// </summary>
public interface IProjectRecoveryCommitGuardedStore
{
    Task<bool> WriteIfAsync(
        VideoProject project,
        ProjectLocation location,
        Func<Action, bool> tryCommit,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectRecoveryCandidate(
    VideoProject Project,
    bool IsDegraded = false,
    string? DegradationDetail = null);

public sealed record ProjectRecoveryProbe(
    ProjectRecoveryCandidate? Candidate,
    string? FailureDetail = null)
{
    public static ProjectRecoveryProbe None { get; } = new ProjectRecoveryProbe(null, null);
}

public enum ProjectWorkspaceState
{
    Clean,
    Dirty,
    Saving,
    Saved,
    RecoveryAvailable,
    Recovered,
    Degraded,
    Failed
}

public interface IAssetImportService
{
    Task<IReadOnlyList<ProjectAsset>> ImportAsync(
        ProjectLocation location,
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports assets without allocating any of the already-owned project-relative paths.
    /// The default keeps existing import-service implementations source-compatible while
    /// allowing production importers to honor the current project inventory.
    /// </summary>
    Task<IReadOnlyList<ProjectAsset>> ImportAsync(
        ProjectLocation location,
        IEnumerable<string> sourcePaths,
        IReadOnlyCollection<string> reservedRelativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservedRelativePaths);
        return ImportAsync(location, sourcePaths, cancellationToken);
    }
}
