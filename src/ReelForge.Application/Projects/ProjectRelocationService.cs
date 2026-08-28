using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Coordinates relocation with the same save gate used by project mutations. The committed
/// project remains at its original location until a copied destination has passed store validation.
/// </summary>
public sealed class ProjectRelocationService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IProjectStore _projectStore;
    private readonly IProjectRelocationFileSystem _fileSystem;
    private readonly ProjectSaveCoordinator _saveCoordinator;

    public ProjectRelocationService(
        ProjectWorkspace workspace,
        IProjectStore projectStore,
        IProjectRelocationFileSystem fileSystem,
        ProjectSaveCoordinator saveCoordinator)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
    }

    public async Task<ProjectRelocationResult> RelocateAsync(
        ProjectRelocationRequest request,
        IProgress<ProjectRelocationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRootDirectory);

        using var lease = await _saveCoordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = await _workspace.CaptureRelocationSnapshotAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new ProjectRelocationProgress(ProjectRelocationPhase.Validating));

        ProjectRelocationPlan? plan = null;
        var published = false;
        try
        {
            plan = await _fileSystem.PrepareAsync(
                    snapshot.Location,
                    request.DestinationRootDirectory,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            VideoProject validatedProject;
            if (plan.UsesStaging)
            {
                progress?.Report(new ProjectRelocationProgress(
                    ProjectRelocationPhase.ValidatingDestination,
                    plan.CopiedFileCount,
                    plan.CopiedFileCount,
                    plan.CopiedBytes,
                    plan.CopiedBytes));
                var staged = await _projectStore.OpenAsync(
                        plan.StagingLocation!.ProjectFilePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (staged.Project.Id != snapshot.Project.Id)
                    throw new InvalidDataException("The staged project does not match the active project identity.");
                validatedProject = staged.Project;
            }
            else
                validatedProject = snapshot.Project;

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ProjectRelocationProgress(
                ProjectRelocationPhase.Publishing,
                plan.CopiedFileCount,
                plan.CopiedFileCount,
                plan.CopiedBytes,
                plan.CopiedBytes));
            // This is the irreversible hand-off point. Finish publication even when the UI closes
            // or cancels after preparation; otherwise the active workspace can be stranded.
            await _fileSystem.PublishAsync(plan, CancellationToken.None).ConfigureAwait(false);
            published = true;

            // Publication makes the destination authoritative. Rebind with the already validated
            // project rather than reopening after the source has moved: no fallible store read may
            // leave the live workspace pointing at the now-absent source folder.
            await _workspace.RebindRelocatedProjectAsync(
                    snapshot,
                    validatedProject,
                    plan.FinalLocation,
                    CancellationToken.None)
                .ConfigureAwait(false);

            var sourceCleanupCompleted = true;
            string? warning = null;
            if (plan.UsesStaging)
            {
                progress?.Report(new ProjectRelocationProgress(ProjectRelocationPhase.RemovingSource));
                try
                {
                    await _fileSystem.RemoveSourceAsync(plan, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    sourceCleanupCompleted = false;
                    warning = $"The project was moved, but the original folder could not be removed: {exception.Message}";
                }
            }

            progress?.Report(new ProjectRelocationProgress(ProjectRelocationPhase.Completed));
            return new ProjectRelocationResult(validatedProject, plan.FinalLocation, sourceCleanupCompleted, warning);
        }
        catch
        {
            if (plan is not null && !published)
                await _fileSystem.RollbackAsync(plan).ConfigureAwait(false);
            throw;
        }
    }
}
