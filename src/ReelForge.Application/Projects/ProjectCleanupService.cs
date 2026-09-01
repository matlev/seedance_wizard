using ReelForge.Core;

namespace ReelForge.Application;

public sealed record ProjectCleanupResult(
    ProjectDegradationReport Analysis,
    int ArchivedSavedFrames,
    int TombstonedSavedClips,
    int TombstonedCompositions)
{
    public int TotalRemovedFromProjectMedia => ArchivedSavedFrames + TombstonedSavedClips + TombstonedCompositions;
}

/// <summary>
/// Explicitly retires active derived Project Media whose pinned dependencies no longer work.
/// Historical recipes, revisions, provenance, and generation snapshots deliberately remain.
/// </summary>
public sealed class ProjectCleanupService(ProjectDegradationAnalyzer degradationAnalyzer)
{
    public ProjectCleanupService() : this(new ProjectDegradationAnalyzer()) { }

    public ProjectDegradationReport Analyze(VideoProject project) => degradationAnalyzer.Analyze(project);

    public async Task<ProjectCleanupResult> CleanupAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var project = workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        var location = workspace.Location ?? throw new InvalidOperationException("Create or open a project first.");
        if (workspace.State is not (ProjectWorkspaceState.Clean or ProjectWorkspaceState.Saved or ProjectWorkspaceState.Degraded))
            throw new InvalidOperationException(
                "Save or discard pending project or recovery changes before cleaning up this project.");
        var analysis = degradationAnalyzer.Analyze(project);
        if (analysis.CleanupCandidateCount == 0)
            return new ProjectCleanupResult(analysis, 0, 0, 0);

        var anchors = project.Anchors.Where(anchor => analysis.IsDegradedAnchor(anchor.Id))
            .Select(anchor => (Anchor: anchor, WasArchived: anchor.IsArchived)).ToArray();
        var assets = project.Assets.Where(asset => analysis.IsDegradedAsset(asset.Id))
            .Select(asset => (Asset: asset, WasDeleted: asset.IsDeleted)).ToArray();
        var previousWorkingCompositionId = project.WorkingCompositionAssetId;

        var archivedSavedFrames = anchors.Count(entry => !entry.WasArchived);
        var tombstonedSavedClips = assets.Count(entry => !entry.WasDeleted &&
            entry.Asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        var tombstonedCompositions = assets.Count(entry => !entry.WasDeleted &&
            entry.Asset.Virtual?.Kind == VirtualAssetKind.Composition);

        void Apply()
        {
            foreach (var entry in anchors) entry.Anchor.IsArchived = true;
            foreach (var entry in assets) entry.Asset.IsDeleted = true;
            if (project.WorkingCompositionAssetId is { } workingId && analysis.IsDegradedAsset(workingId))
                project.WorkingCompositionAssetId = null;
        }

        Task RollbackAsync()
        {
            foreach (var entry in anchors) entry.Anchor.IsArchived = entry.WasArchived;
            foreach (var entry in assets) entry.Asset.IsDeleted = entry.WasDeleted;
            project.WorkingCompositionAssetId = previousWorkingCompositionId;
            return Task.CompletedTask;
        }

        var saved = await workspace.SaveMutationIfCurrentAsync(
            project, location, Apply, RollbackAsync, cancellationToken).ConfigureAwait(false);
        if (saved.Failure is not null) throw new InvalidOperationException(
            "Project cleanup could not be saved; no Project Media was removed.", saved.Failure);
        if (!saved.Committed)
            throw new OperationCanceledException("Project cleanup did not commit because the active project changed or the operation was cancelled.", cancellationToken);

        return new ProjectCleanupResult(analysis, archivedSavedFrames, tombstonedSavedClips, tombstonedCompositions);
    }
}
