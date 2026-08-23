using ReelForge.Core;

namespace ReelForge.Application;

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

    public async Task CreateAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Persists a captured project session only while it remains the workspace's exact current
    /// session. Unlike <see cref="SaveAsync"/>, this method never redirects a late request to a
    /// replacement project that happens to have the same logical identity.
    /// </summary>
    public async Task<bool> SaveIfCurrentAsync(
        VideoProject expectedProject,
        ProjectLocation expectedLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        ArgumentNullException.ThrowIfNull(expectedLocation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(Project, expectedProject) || !ReferenceEquals(Location, expectedLocation))
            return false;

        expectedProject.Touch();
        await _projectStore.SaveAsync(expectedProject, expectedLocation, cancellationToken).ConfigureAwait(false);
        return !cancellationToken.IsCancellationRequested &&
               ReferenceEquals(Project, expectedProject) &&
               ReferenceEquals(Location, expectedLocation);
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
            Project!.AddAsset(asset);

        await _projectStore.SaveAsync(Project!, Location!, cancellationToken).ConfigureAwait(false);
        return imported;
    }

    public string GetAbsoluteAssetPath(ProjectAsset asset)
    {
        EnsureProjectIsOpen();
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException(
                $"Virtual asset '{asset.Id}' must be materialized before a path is requested.");

        return ProjectPathPolicy.ResolveContainedPath(Location!, asset.Physical.RelativePath);
    }

    private void EnsureProjectIsOpen()
    {
        if (Project is null || Location is null)
            throw new InvalidOperationException("Create or open a project first.");
    }
}
