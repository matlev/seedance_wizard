namespace ReelForge.Application;

public sealed class ProjectCloneService
{
    private readonly IProjectStore _projectStore;
    private readonly IProjectCloneFileSystem _fileSystem;
    private readonly ProjectSaveCoordinator _saveCoordinator;

    public ProjectCloneService(
        IProjectStore projectStore,
        IProjectCloneFileSystem fileSystem,
        ProjectSaveCoordinator saveCoordinator)
    {
        _projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
    }

    public async Task<ProjectCloneResult> CloneAsync(
        ProjectCloneRequest request,
        IProgress<ProjectCloneProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceProjectFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationParentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CloneName);

        progress?.Report(new ProjectCloneProgress(ProjectClonePhase.Validating));
        using var lease = await _saveCoordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        var (cloneProject, sourceLocation) = await _projectStore
            .OpenAsync(request.SourceProjectFilePath, cancellationToken)
            .ConfigureAwait(false);

        ProjectCloneStaging? staging = null;
        var published = false;
        try
        {
            staging = await _fileSystem.StageDurableContentAsync(
                    sourceLocation,
                    request.DestinationParentDirectory,
                    request.CloneName.Trim(),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            cloneProject.Id = Guid.NewGuid();
            cloneProject.Name = request.CloneName.Trim();
            cloneProject.CreatedAt = now;
            cloneProject.ModifiedAt = now;

            progress?.Report(new ProjectCloneProgress(
                ProjectClonePhase.WritingProject,
                staging.CopiedFileCount,
                staging.CopiedFileCount,
                staging.CopiedBytes,
                staging.CopiedBytes));
            await _projectStore.SaveAsync(cloneProject, staging.StagingLocation, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new ProjectCloneProgress(
                ProjectClonePhase.ValidatingClone,
                staging.CopiedFileCount,
                staging.CopiedFileCount,
                staging.CopiedBytes,
                staging.CopiedBytes));
            var validated = await _projectStore
                .OpenAsync(staging.StagingLocation.ProjectFilePath, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ProjectCloneProgress(
                ProjectClonePhase.Publishing,
                staging.CopiedFileCount,
                staging.CopiedFileCount,
                staging.CopiedBytes,
                staging.CopiedBytes));
            await _fileSystem.PublishAsync(staging, cancellationToken).ConfigureAwait(false);
            published = true;

            progress?.Report(new ProjectCloneProgress(
                ProjectClonePhase.Completed,
                staging.CopiedFileCount,
                staging.CopiedFileCount,
                staging.CopiedBytes,
                staging.CopiedBytes));
            return new ProjectCloneResult(
                validated.Project,
                staging.FinalLocation,
                staging.CopiedFileCount,
                staging.CopiedBytes);
        }
        catch
        {
            if (staging is not null && !published)
                await _fileSystem.RollbackAsync(staging).ConfigureAwait(false);
            throw;
        }
    }
}
