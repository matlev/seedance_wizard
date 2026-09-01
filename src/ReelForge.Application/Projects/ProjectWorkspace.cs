using ReelForge.Core;
using System.Diagnostics.CodeAnalysis;

namespace ReelForge.Application;

public sealed record ProjectPhysicalAssetRelinkSaveResult(bool Committed, Exception? Failure = null)
{
    public static ProjectPhysicalAssetRelinkSaveResult NotCommitted { get; } = new(false);
    public static ProjectPhysicalAssetRelinkSaveResult CommittedResult { get; } = new(true);
}

/// <summary>
/// Result of a focused active-project mutation that was persisted through the workspace's
/// recovery-aware save transaction.
/// </summary>
public sealed record ProjectWorkspaceMutationSaveResult(bool Committed, Exception? Failure = null)
{
    public static ProjectWorkspaceMutationSaveResult NotCommitted { get; } = new(false);
    public static ProjectWorkspaceMutationSaveResult CommittedResult { get; } = new(true);
}

internal sealed record ProjectWorkspaceOperationalState(
    ProjectWorkspaceState State,
    bool IsDegraded,
    string? FailureDetail,
    ProjectRecoveryCandidate? RecoveryCandidate);

/// <summary>
/// Captures the exact active workspace session for a relocation that is serialized by the shared
/// project save coordinator. It is intentionally not a portable project value.
/// </summary>
internal sealed record ProjectWorkspaceRelocationSnapshot(
    VideoProject Project,
    ProjectLocation Location,
    long SessionGeneration,
    ProjectWorkspaceState State);

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification =
    "The asynchronous gates and active-session token are held for the workspace lifetime and own no external resources.")]
public sealed class ProjectWorkspace
{
    private readonly IProjectStore _projectStore;
    private readonly IAssetImportService _assetImporter;
    private readonly IProjectRecoveryStore? _recoveryStore;
    private readonly ProjectSaveCoordinator _saveCoordinator;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly object _sessionCommitBarrier = new();
    private CancellationTokenSource _sessionCancellation = new();
    private long _sessionGeneration;

    public ProjectWorkspace(
        IProjectStore projectStore,
        IAssetImportService assetImporter,
        IProjectRecoveryStore? recoveryStore = null,
        ProjectSaveCoordinator? saveCoordinator = null)
    {
        _projectStore = projectStore;
        _assetImporter = assetImporter;
        _recoveryStore = recoveryStore;
        _saveCoordinator = saveCoordinator ?? new ProjectSaveCoordinator();
    }

    public VideoProject? Project { get; private set; }
    public ProjectLocation? Location { get; private set; }
    public ProjectWorkspaceState State { get; private set; } = ProjectWorkspaceState.Clean;
    public bool IsDegraded { get; private set; }
    public string? FailureDetail { get; private set; }
    public ProjectRecoveryCandidate? RecoveryCandidate { get; private set; }

    public async Task CreateAsync(
        string rootDirectory,
        string name,
        CancellationToken cancellationToken = default)
    {
        using var saveLease = await _saveCoordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (project, location) = await _projectStore
                .CreateAsync(rootDirectory, name, cancellationToken)
                .ConfigureAwait(false);
            Publish(project, location, ProjectWorkspaceState.Saved);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task OpenAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        using var saveLease = await _saveCoordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (project, location) = await _projectStore
                .OpenAsync(projectFilePath, cancellationToken)
                .ConfigureAwait(false);
            ProjectRecoveryProbe? probe = _recoveryStore is null
                ? null
                : await _recoveryStore.ProbeAsync(location, cancellationToken).ConfigureAwait(false);
            Publish(project, location, IsProjectDegraded(project)
                ? ProjectWorkspaceState.Degraded
                : ProjectWorkspaceState.Clean);

            if (probe is null)
                return;

            if (probe.FailureDetail is not null)
            {
                FailureDetail = probe.FailureDetail;
                State = ProjectWorkspaceState.Failed;
                return;
            }

            if (probe.Candidate is not null && probe.Candidate.Project.Id != project.Id)
            {
                FailureDetail = "Recovery data belongs to a different project and was not activated.";
                State = ProjectWorkspaceState.Failed;
                return;
            }

            RecoveryCandidate = probe.Candidate;
            if (RecoveryCandidate is not null)
                State = ProjectWorkspaceState.RecoveryAvailable;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        VideoProject project;
        ProjectLocation location;
        CancellationToken sessionCancellation = default;
        long sessionGeneration = default;
        try
        {
            EnsureProjectIsOpen();
            project = Project!;
            location = Location!;
            sessionCancellation = _sessionCancellation.Token;
            sessionGeneration = _sessionGeneration;
            project.Touch();
        }
        finally
        {
            _sessionGate.Release();
        }

        await SaveCapturedAsync(
            project, location, sessionGeneration, sessionCancellation, cancellationToken).ConfigureAwait(false);
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
        CancellationToken sessionCancellation;
        long sessionGeneration;
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(expectedProject, expectedLocation))
                return false;
            expectedProject.Touch();
            sessionCancellation = _sessionCancellation.Token;
            sessionGeneration = _sessionGeneration;
        }
        finally
        {
            _sessionGate.Release();
        }

        var committed = await SaveCapturedAsync(
                expectedProject, expectedLocation, sessionGeneration, sessionCancellation, cancellationToken)
            .ConfigureAwait(false);
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return committed && IsCurrentSession(expectedProject, expectedLocation, sessionGeneration);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task<IReadOnlyList<ProjectAsset>> ImportAssetsAsync(
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectIsOpen();
        var reservedRelativePaths = Project!.Assets
            .Where(asset =>
                !asset.IsDeleted &&
                asset.StorageKind == AssetStorageKind.Physical &&
                asset.Physical is not null &&
                !string.IsNullOrWhiteSpace(asset.Physical.RelativePath))
            .Select(asset => asset.Physical!.RelativePath)
            .ToArray();
        var imported = await _assetImporter
            .ImportAsync(Location!, sourcePaths, reservedRelativePaths, cancellationToken)
            .ConfigureAwait(false);

        foreach (var asset in imported)
            Project!.AddAsset(asset);

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return imported;
    }

    /// <summary>
    /// Persists a verified physical-media relink and runs its compensating file and metadata
    /// work inside the same serialized save transaction only when the project commit did not
    /// occur. A committed project remains authoritative even if its active session changes
    /// immediately afterwards.
    /// </summary>
    public async Task<ProjectPhysicalAssetRelinkSaveResult> SavePhysicalAssetRelinkIfCurrentAsync(
        VideoProject expectedProject,
        ProjectLocation expectedLocation,
        Action applyRelinkMetadata,
        Func<Task> rollBackUncommittedAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        ArgumentNullException.ThrowIfNull(expectedLocation);
        ArgumentNullException.ThrowIfNull(applyRelinkMetadata);
        ArgumentNullException.ThrowIfNull(rollBackUncommittedAsync);

        CancellationToken sessionCancellation = default;
        long sessionGeneration = default;
        ProjectWorkspaceOperationalState? priorOperationalState = null;
        var sessionWasStale = false;
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(expectedProject, expectedLocation))
            {
                sessionWasStale = true;
            }
            else
            {
                sessionCancellation = _sessionCancellation.Token;
                sessionGeneration = _sessionGeneration;
                priorOperationalState = new ProjectWorkspaceOperationalState(
                    State, IsDegraded, FailureDetail, RecoveryCandidate);
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        if (sessionWasStale)
        {
            await rollBackUncommittedAsync().ConfigureAwait(false);
            return ProjectPhysicalAssetRelinkSaveResult.NotCommitted;
        }

        Task RestorePriorOperationalStateAsync() => RestoreOperationalStateIfCurrentAsync(
            expectedProject, expectedLocation, sessionGeneration, priorOperationalState!);

        try
        {
            var committed = await SaveCapturedAsync(
                    expectedProject,
                    expectedLocation,
                    sessionGeneration,
                    sessionCancellation,
                    cancellationToken,
                    applyRelinkMetadata,
                    rollBackUncommittedAsync,
                    requireRelinkEligibility: true,
                    restoreUncommittedCallerCancellationAsync: RestorePriorOperationalStateAsync)
                .ConfigureAwait(false);
            return committed
                ? ProjectPhysicalAssetRelinkSaveResult.CommittedResult
                : ProjectPhysicalAssetRelinkSaveResult.NotCommitted;
        }
        catch (Exception exception)
        {
            return new ProjectPhysicalAssetRelinkSaveResult(false, exception);
        }
    }

    /// <summary>
    /// Applies a focused active-project mutation immediately before the authoritative save. If
    /// that save cannot commit, the caller's compensating action restores the in-memory project
    /// state before this method returns or throws.
    /// </summary>
    public Task<ProjectWorkspaceMutationSaveResult> SaveMutationIfCurrentAsync(
        VideoProject expectedProject,
        ProjectLocation expectedLocation,
        Action applyMutation,
        Func<Task> rollbackUncommittedAsync,
        CancellationToken cancellationToken = default) =>
        SaveMutationIfCurrentCoreAsync(
            expectedProject,
            expectedLocation,
            captureBeforeMutation: null,
            applyMutation,
            rollbackUncommittedAsync,
            cancellationToken);

    internal Task<ProjectWorkspaceMutationSaveResult> SaveMutationIfCurrentWithSnapshotAsync(
        VideoProject expectedProject,
        ProjectLocation expectedLocation,
        Action captureBeforeMutation,
        Action applyMutation,
        Func<Task> rollbackUncommittedAsync,
        CancellationToken cancellationToken = default) =>
        SaveMutationIfCurrentCoreAsync(
            expectedProject,
            expectedLocation,
            captureBeforeMutation,
            applyMutation,
            rollbackUncommittedAsync,
            cancellationToken);

    private async Task<ProjectWorkspaceMutationSaveResult> SaveMutationIfCurrentCoreAsync(
        VideoProject expectedProject,
        ProjectLocation expectedLocation,
        Action? captureBeforeMutation,
        Action applyMutation,
        Func<Task> rollbackUncommittedAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedProject);
        ArgumentNullException.ThrowIfNull(expectedLocation);
        ArgumentNullException.ThrowIfNull(applyMutation);
        ArgumentNullException.ThrowIfNull(rollbackUncommittedAsync);

        CancellationToken sessionCancellation = default;
        long sessionGeneration = default;
        ProjectWorkspaceOperationalState? priorOperationalState = null;
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrent(expectedProject, expectedLocation))
                return ProjectWorkspaceMutationSaveResult.NotCommitted;

            sessionCancellation = _sessionCancellation.Token;
            sessionGeneration = _sessionGeneration;
            priorOperationalState = new ProjectWorkspaceOperationalState(
                State, IsDegraded, FailureDetail, RecoveryCandidate);
        }
        finally
        {
            _sessionGate.Release();
        }

        Task RestorePriorOperationalStateAsync() => RestoreOperationalStateIfCurrentAsync(
            expectedProject, expectedLocation, sessionGeneration, priorOperationalState!);

        try
        {
            var committed = await SaveCapturedAsync(
                    expectedProject,
                    expectedLocation,
                    sessionGeneration,
                    sessionCancellation,
                    cancellationToken,
                    applyMutation,
                    rollbackUncommittedAsync,
                    captureBeforeMutation: captureBeforeMutation,
                    restoreUncommittedCallerCancellationAsync: RestorePriorOperationalStateAsync)
                .ConfigureAwait(false);
            return committed
                ? ProjectWorkspaceMutationSaveResult.CommittedResult
                : ProjectWorkspaceMutationSaveResult.NotCommitted;
        }
        catch (Exception exception)
        {
            return new ProjectWorkspaceMutationSaveResult(false, exception);
        }
    }

    /// <summary>
    /// Prevents a relink from writing or retiring recovery data until an existing recovery
    /// decision or failed save has been explicitly resolved.
    /// </summary>
    public async Task EnsurePhysicalAssetRelinkCanStartAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProjectIsOpen();
            if (State is ProjectWorkspaceState.RecoveryAvailable or ProjectWorkspaceState.Recovered or
                ProjectWorkspaceState.Dirty or ProjectWorkspaceState.Saving or ProjectWorkspaceState.Failed)
            {
                throw new InvalidOperationException(
                    "Save or discard the pending recovery or failed project state before relinking media.");
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <summary>
    /// Captures an operationally stable current session for an external folder relocation. Callers
    /// must hold the shared <see cref="ProjectSaveCoordinator"/> for the complete relocation.
    /// </summary>
    internal async Task<ProjectWorkspaceRelocationSnapshot> CaptureRelocationSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProjectIsOpen();
            if (!IsRelocationStateEligible())
            {
                throw new InvalidOperationException(
                    "Save or discard pending recovery or failed project changes before moving the project.");
            }

            return new ProjectWorkspaceRelocationSnapshot(Project!, Location!, _sessionGeneration, State);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <summary>
    /// Publishes a store-validated relocation only if the exact source session is still current.
    /// A relocation changes machine-local location, not portable project meaning.
    /// </summary>
    internal async Task RebindRelocatedProjectAsync(
        ProjectWorkspaceRelocationSnapshot snapshot,
        VideoProject relocatedProject,
        ProjectLocation relocatedLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(relocatedProject);
        ArgumentNullException.ThrowIfNull(relocatedLocation);
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsCurrentSession(snapshot.Project, snapshot.Location, snapshot.SessionGeneration))
                throw new InvalidOperationException("The active project changed before relocation could be completed.");
            if (relocatedProject.Id != snapshot.Project.Id)
                throw new InvalidDataException("A relocated project must retain the active project identity.");

            Publish(relocatedProject, relocatedLocation, IsProjectDegraded(relocatedProject)
                ? ProjectWorkspaceState.Degraded
                : snapshot.State is ProjectWorkspaceState.Degraded
                    ? ProjectWorkspaceState.Degraded
                    : ProjectWorkspaceState.Saved);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    /// <summary>
    /// Reloads an owning project and applies a focused mutation under the same recovery transaction
    /// and serialization used by active-project saves, so detached stale aggregates cannot overwrite
    /// unrelated committed changes.
    /// </summary>
    public Task UpdateDetachedAsync(
        string projectFilePath,
        Action<VideoProject, ProjectLocation> update,
        CancellationToken cancellationToken = default)
        => UpdateDetachedCoreAsync(projectFilePath, update, discardRecoveryOnFailure: false, cancellationToken);

    /// <summary>
    /// Applies a detached update whose caller rolls back associated file work when persistence fails.
    /// Its recovery candidate is retired before the serialized transaction is released.
    /// </summary>
    public Task UpdateDetachedWithRollbackAsync(
        string projectFilePath,
        Action<VideoProject, ProjectLocation> update,
        CancellationToken cancellationToken = default)
        => UpdateDetachedCoreAsync(projectFilePath, update, discardRecoveryOnFailure: true, cancellationToken);

    private async Task UpdateDetachedCoreAsync(
        string projectFilePath,
        Action<VideoProject, ProjectLocation> update,
        bool discardRecoveryOnFailure,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        ArgumentNullException.ThrowIfNull(update);
        using var saveLease = await _saveCoordinator.EnterAsync(cancellationToken).ConfigureAwait(false);
        var (project, location) = await _projectStore
            .OpenAsync(projectFilePath, cancellationToken)
            .ConfigureAwait(false);
        update(project, location);
        project.Touch();

        try
        {
            await PersistDetachedAsync(project, location, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (discardRecoveryOnFailure && _recoveryStore is not null)
                await _recoveryStore.DiscardAsync(location, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task PersistDetachedAsync(
        VideoProject project,
        ProjectLocation location,
        CancellationToken cancellationToken)
    {
        if (_recoveryStore is IProjectRecoveryCommitGuardedStore guardedRecoveryStore)
            _ = await guardedRecoveryStore.WriteIfAsync(project, location, CommitUnconditionally, cancellationToken)
                .ConfigureAwait(false);
        else if (_recoveryStore is not null)
            await _recoveryStore.WriteAsync(project, location, cancellationToken).ConfigureAwait(false);

        if (_projectStore is IProjectCommitGuardedStore guardedProjectStore)
            _ = await guardedProjectStore
                .SaveIfAsync(project, location, CommitUnconditionally, cancellationToken)
                .ConfigureAwait(false);
        else
            await _projectStore.SaveAsync(project, location, cancellationToken).ConfigureAwait(false);

        if (_recoveryStore is not null)
        {
            try
            {
                await _recoveryStore.DiscardAsync(location, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The committed project is authoritative; an identical leftover candidate is retired on probe.
            }
        }
    }

    public async Task AcceptRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProjectIsOpen();
            if (RecoveryCandidate is null)
                throw new InvalidOperationException("There is no recovery candidate to accept.");

            var recovered = RecoveryCandidate.Project;
            Publish(recovered, Location!, ProjectWorkspaceState.Recovered);
            RecoveryCandidate = null;
            FailureDetail = null;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task DiscardRecoveryAsync(CancellationToken cancellationToken = default)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureProjectIsOpen();
            if (_recoveryStore is null)
                throw new InvalidOperationException("Recovery storage is not configured for this workspace.");

            await _recoveryStore.DiscardAsync(Location!, cancellationToken).ConfigureAwait(false);
            RecoveryCandidate = null;
            FailureDetail = null;
            State = IsDegraded ? ProjectWorkspaceState.Degraded : ProjectWorkspaceState.Clean;
        }
        finally
        {
            _sessionGate.Release();
        }
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

    private async Task<bool> SaveCapturedAsync(
        VideoProject project,
        ProjectLocation location,
        long sessionGeneration,
        CancellationToken sessionCancellation,
        CancellationToken cancellationToken,
        Action? applyBeforeCommit = null,
        Func<Task>? rollBackUncommittedAsync = null,
        bool requireRelinkEligibility = false,
        Func<Task>? restoreUncommittedCallerCancellationAsync = null,
        Action? captureBeforeMutation = null)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, sessionCancellation);
        var operationCancellation = linkedCancellation.Token;
        var recoveryWritten = false;
        var committed = false;
        var rollbackCompleted = false;
        IDisposable? saveLease = null;
        async Task<bool> RollBackUncommittedAsync()
        {
            if (rollBackUncommittedAsync is not null && !rollbackCompleted)
            {
                await rollBackUncommittedAsync().ConfigureAwait(false);
                rollbackCompleted = true;
            }

            if (rollBackUncommittedAsync is not null && recoveryWritten && !committed)
                await DiscardStaleRecoveryAsync(location).ConfigureAwait(false);

            return false;
        }

        try
        {
            saveLease = await _saveCoordinator.EnterAsync(operationCancellation).ConfigureAwait(false);
            await _sessionGate.WaitAsync(operationCancellation).ConfigureAwait(false);
            try
            {
                if (!IsCurrentSession(project, location, sessionGeneration))
                    return await RollBackUncommittedAsync().ConfigureAwait(false);
                if (requireRelinkEligibility && !IsPhysicalAssetRelinkStateEligible())
                    return await RollBackUncommittedAsync().ConfigureAwait(false);
                State = ProjectWorkspaceState.Dirty;
                FailureDetail = null;
                if (applyBeforeCommit is not null)
                {
                    captureBeforeMutation?.Invoke();
                    project.Touch();
                    applyBeforeCommit();
                }
            }
            finally
            {
                _sessionGate.Release();
            }

            Func<Action, bool> tryCommit = commit => TryCommitForSession(sessionGeneration, commit);
            if (_recoveryStore is IProjectRecoveryCommitGuardedStore guardedRecoveryStore)
            {
                if (!await guardedRecoveryStore
                        .WriteIfAsync(project, location, tryCommit, operationCancellation)
                        .ConfigureAwait(false))
                    return await RollBackUncommittedAsync().ConfigureAwait(false);
                recoveryWritten = true;
            }
            else if (_recoveryStore is not null)
            {
                if (!IsSessionGenerationCurrent(sessionGeneration))
                    return await RollBackUncommittedAsync().ConfigureAwait(false);
                await _recoveryStore.WriteAsync(project, location, operationCancellation).ConfigureAwait(false);
                recoveryWritten = true;
                if (!IsSessionGenerationCurrent(sessionGeneration))
                {
                    await DiscardStaleRecoveryAsync(location).ConfigureAwait(false);
                    return await RollBackUncommittedAsync().ConfigureAwait(false);
                }
            }

            await _sessionGate.WaitAsync(operationCancellation).ConfigureAwait(false);
            try
            {
                if (!IsCurrentSession(project, location, sessionGeneration) ||
                    sessionCancellation.IsCancellationRequested)
                {
                    if (recoveryWritten)
                        await DiscardStaleRecoveryAsync(location).ConfigureAwait(false);
                    return await RollBackUncommittedAsync().ConfigureAwait(false);
                }
                State = ProjectWorkspaceState.Saving;
                FailureDetail = null;
            }
            finally
            {
                _sessionGate.Release();
            }

            operationCancellation.ThrowIfCancellationRequested();
            var projectCommitted = _projectStore is IProjectCommitGuardedStore guardedProjectStore
                ? await guardedProjectStore
                    .SaveIfAsync(project, location, tryCommit, operationCancellation)
                    .ConfigureAwait(false)
                : await SaveWithoutCommitGuardAsync(
                    project, location, sessionGeneration, operationCancellation).ConfigureAwait(false);
            if (!projectCommitted)
            {
                if (recoveryWritten && !IsSessionGenerationCurrent(sessionGeneration))
                    await DiscardStaleRecoveryAsync(location).ConfigureAwait(false);
                return await RollBackUncommittedAsync().ConfigureAwait(false);
            }

            committed = true;

            await _sessionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (IsCurrentSession(project, location, sessionGeneration) &&
                    !sessionCancellation.IsCancellationRequested)
                {
                    RecoveryCandidate = null;
                    IsDegraded = IsProjectDegraded(project);
                    State = ProjectWorkspaceState.Saved;
                }
            }
            finally
            {
                _sessionGate.Release();
            }

            if (_recoveryStore is not null)
            {
                try
                {
                    await _recoveryStore.DiscardAsync(location, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await SetRecoveryCleanupWarningIfCurrentAsync(project, location, exception).ConfigureAwait(false);
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
            return await RollBackUncommittedAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !committed)
        {
            await RollBackUncommittedAsync().ConfigureAwait(false);
            if (restoreUncommittedCallerCancellationAsync is not null)
                await restoreUncommittedCallerCancellationAsync().ConfigureAwait(false);
            return false;
        }
        catch (Exception exception)
        {
            await RollBackUncommittedAsync().ConfigureAwait(false);

            await SetOperationalStateIfCurrentAsync(project, location, ProjectWorkspaceState.Failed, exception.Message)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            saveLease?.Dispose();
        }
    }

    private async Task SetRecoveryCleanupWarningIfCurrentAsync(
        VideoProject project,
        ProjectLocation location,
        Exception exception)
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsCurrent(project, location))
            {
                State = ProjectWorkspaceState.Saved;
                FailureDetail = $"Project was saved, but recovery cleanup failed: {exception.Message}";
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private bool IsPhysicalAssetRelinkStateEligible() =>
        State is ProjectWorkspaceState.Clean or ProjectWorkspaceState.Saved or ProjectWorkspaceState.Degraded;

    private bool IsRelocationStateEligible() =>
        State is ProjectWorkspaceState.Clean or ProjectWorkspaceState.Saved or ProjectWorkspaceState.Degraded;

    private async Task RestoreOperationalStateIfCurrentAsync(
        VideoProject project,
        ProjectLocation location,
        long sessionGeneration,
        ProjectWorkspaceOperationalState state)
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrentSession(project, location, sessionGeneration))
                return;

            State = state.State;
            IsDegraded = state.IsDegraded;
            FailureDetail = state.FailureDetail;
            RecoveryCandidate = state.RecoveryCandidate;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private void ResetOperationalState(ProjectWorkspaceState state)
    {
        State = state;
        IsDegraded = Project is not null && IsProjectDegraded(Project);
        FailureDetail = null;
        RecoveryCandidate = null;
    }

    private void Publish(VideoProject project, ProjectLocation location, ProjectWorkspaceState state)
    {
        lock (_sessionCommitBarrier)
        {
            var previousSession = _sessionCancellation;
            previousSession.Cancel();
            previousSession.Dispose();
            _sessionCancellation = new CancellationTokenSource();
            Interlocked.Increment(ref _sessionGeneration);
            Project = project;
            Location = location;
            ResetOperationalState(state);
        }
    }

    private static bool IsProjectDegraded(VideoProject project) => project.Assets.Any(asset =>
        asset.StorageKind == AssetStorageKind.Physical &&
        asset.Physical is not null &&
        asset.Physical.Availability != PhysicalAssetAvailability.Available);

    private bool IsCurrent(VideoProject project, ProjectLocation location) =>
        ReferenceEquals(Project, project) && ReferenceEquals(Location, location);

    private bool IsCurrentSession(VideoProject project, ProjectLocation location, long sessionGeneration) =>
        IsCurrent(project, location) && IsSessionGenerationCurrent(sessionGeneration);

    private bool IsSessionGenerationCurrent(long sessionGeneration) =>
        Volatile.Read(ref _sessionGeneration) == sessionGeneration;

    private async Task<bool> SaveWithoutCommitGuardAsync(
        VideoProject project,
        ProjectLocation location,
        long sessionGeneration,
        CancellationToken cancellationToken)
    {
        if (!IsSessionGenerationCurrent(sessionGeneration))
            return false;
        await _projectStore.SaveAsync(project, location, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool TryCommitForSession(long sessionGeneration, Action commit)
    {
        lock (_sessionCommitBarrier)
        {
            if (!IsSessionGenerationCurrent(sessionGeneration))
                return false;
            commit();
            return true;
        }
    }

    private static bool CommitUnconditionally(Action commit)
    {
        commit();
        return true;
    }

    private async Task DiscardStaleRecoveryAsync(ProjectLocation location)
    {
        if (_recoveryStore is null)
            return;
        try
        {
            await _recoveryStore.DiscardAsync(location, CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A stale candidate is still bound to its exact committed base and will fail closed if it cannot be retired.
        }
        catch (UnauthorizedAccessException)
        {
            // A stale candidate is still bound to its exact committed base and will fail closed if it cannot be retired.
        }
    }

    private async Task SetOperationalStateIfCurrentAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectWorkspaceState state,
        string? failureDetail)
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsCurrent(project, location))
                return;

            State = state;
            FailureDetail = failureDetail;
        }
        finally
        {
            _sessionGate.Release();
        }
    }
}
