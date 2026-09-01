using ReelForge.Core;

namespace ReelForge.Application;

public sealed class GenerationJobFinalizedEventArgs : EventArgs
{
    public GenerationJobFinalizedEventArgs(
        Guid generationId,
        string projectFilePath,
        GenerationStatus status,
        bool activeProjectUpdated)
    {
        GenerationId = generationId;
        ProjectFilePath = projectFilePath;
        Status = status;
        ActiveProjectUpdated = activeProjectUpdated;
    }

    public Guid GenerationId { get; }
    public string ProjectFilePath { get; }
    public GenerationStatus Status { get; }
    public bool ActiveProjectUpdated { get; }
}

public sealed class GenerationJobFinalizer : IGenerationJobFinalizer
{
    private readonly ProjectWorkspace _activeWorkspace;
    private readonly IProjectStore _projectStore;
    private readonly IGeneratedOutputIngestionService _outputIngestion;

    public GenerationJobFinalizer(
        ProjectWorkspace activeWorkspace,
        IProjectStore projectStore,
        IGeneratedOutputIngestionService outputIngestion)
    {
        _activeWorkspace = activeWorkspace;
        _projectStore = projectStore;
        _outputIngestion = outputIngestion;
    }

    public event EventHandler<GenerationJobFinalizedEventArgs>? Finalized;

    public async Task FinalizeAsync(
        TrackedGenerationJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var activeLocation = _activeWorkspace.Location;
        var isActiveProject = activeLocation is not null &&
                              PathsEqual(activeLocation.ProjectFilePath, job.ProjectFilePath);

        VideoProject project;
        ProjectLocation location;
        if (isActiveProject)
        {
            project = _activeWorkspace.Project
                ?? throw new InvalidOperationException("The active project could not be loaded for job completion.");
            location = activeLocation!;
        }
        else
        {
            (project, location) = await _projectStore.OpenAsync(job.ProjectFilePath, cancellationToken)
                .ConfigureAwait(false);
        }

        var generation = project.Generations.SingleOrDefault(candidate => candidate.Id == job.GenerationId)
            ?? throw new InvalidOperationException("The generation record no longer exists in its project.");
        generation.Status = job.Status;
        generation.Error = job.Error;
        foreach (var pair in job.ResponseMetadata) generation.ResponseMetadata[pair.Key] = pair.Value;
        generation.ResponseMetadata["localMonitoring"] = "application-job-coordinator";

        if (job.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
        {
            generation.CompletedAt = DateTimeOffset.UtcNow;
            await SaveProjectAsync(project, location, generation.Id).ConfigureAwait(false);
        }
        else if (job.Status == GenerationStatus.Succeeded &&
                 generation.IngestionStatus != OutputIngestionStatus.Succeeded)
        {
            await IngestSucceededOutputAsync(project, location, generation, job, cancellationToken)
                .ConfigureAwait(false);
        }

        Finalized?.Invoke(this, new GenerationJobFinalizedEventArgs(
            job.GenerationId,
            job.ProjectFilePath,
            job.Status,
            isActiveProject));
    }

    private async Task IngestSucceededOutputAsync(
        VideoProject project,
        ProjectLocation location,
        GenerationRecord generation,
        TrackedGenerationJob job,
        CancellationToken cancellationToken)
    {
        generation.CompletedAt = DateTimeOffset.UtcNow;
        generation.IngestionStatus = OutputIngestionStatus.Running;
        await SaveProjectAsync(project, location, generation.Id).ConfigureAwait(false);
        IReadOnlyList<ProjectAsset> assets;
        try
        {
            assets = await _outputIngestion
                .IngestAsync(location, generation.Id, job.Outputs, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            generation.IngestionStatus = OutputIngestionStatus.Failed;
            generation.Error = new GenerationError
            {
                ProviderCode = "local_ingestion_failed",
                Message = exception.Message,
                TechnicalDetails = exception.ToString()
            };
            await SaveProjectAsync(project, location, generation.Id).ConfigureAwait(false);
            throw;
        }

        foreach (var asset in assets)
        {
            project.AddAsset(asset);
            generation.OutputAssetIds.Add(asset.Id);
        }
        generation.IngestionStatus = OutputIngestionStatus.Succeeded;
        generation.Error = null;
        await SaveProjectAsync(project, location, generation.Id).ConfigureAwait(false);
    }

    private async Task SaveProjectAsync(VideoProject project, ProjectLocation location, Guid generationId)
    {
        if (ReferenceEquals(_activeWorkspace.Project, project) &&
            ReferenceEquals(_activeWorkspace.Location, location) &&
            await _activeWorkspace
                .SaveIfCurrentAsync(project, location, CancellationToken.None)
                .ConfigureAwait(false))
        {
            return;
        }

        await _activeWorkspace
            .UpdateDetachedAsync(
                location.ProjectFilePath,
                (latestProject, _) => MergeGenerationFinalization(latestProject, project, generationId),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static void MergeGenerationFinalization(
        VideoProject latestProject,
        VideoProject finalizedProject,
        Guid generationId)
    {
        var finalizedGeneration = finalizedProject.Generations.Single(candidate => candidate.Id == generationId);
        var index = latestProject.Generations.FindIndex(candidate => candidate.Id == generationId);
        if (index < 0)
            throw new InvalidOperationException("The generation record no longer exists in its project.");
        latestProject.Generations[index] = finalizedGeneration;

        var outputIds = finalizedGeneration.OutputAssetIds.ToHashSet();
        foreach (var output in finalizedProject.Assets.Where(asset => outputIds.Contains(asset.Id)))
        {
            if (latestProject.Assets.All(asset => asset.Id != output.Id))
                latestProject.AddAsset(output);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
