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
