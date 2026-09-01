using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// Coordinates a physical asset transfer between projects. A move first commits the
/// target copy, then removes the source only when no durable source-project state
/// depends on it. This intentionally is not a cross-project transaction.
/// </summary>
public sealed class ProjectAssetTransferWorkflow
{
    private readonly ProjectWorkspace _workspace;
    private readonly ProjectAssetTransferService _assetTransferService;
    private readonly ProjectAssetDependencyAnalyzer _dependencyAnalyzer;
    private readonly PhysicalAssetRemovalService _physicalAssetRemovalService;

    public ProjectAssetTransferWorkflow(
        ProjectWorkspace workspace,
        ProjectAssetTransferService assetTransferService,
        ProjectAssetDependencyAnalyzer dependencyAnalyzer,
        PhysicalAssetRemovalService physicalAssetRemovalService)
    {
        _workspace = workspace;
        _assetTransferService = assetTransferService;
        _dependencyAnalyzer = dependencyAnalyzer;
        _physicalAssetRemovalService = physicalAssetRemovalService;
    }

    public Task<ProjectAssetCopyResult> CopyAsync(
        ProjectAsset sourceAsset,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default)
    {
        var asset = GetPhysicalSourceAsset(sourceAsset);
        return _assetTransferService.CopyToProjectAsync(
            _workspace,
            asset,
            targetProjectFilePath,
            cancellationToken);
    }

    public async Task<ProjectAssetMoveResult> MoveAsync(
        ProjectAsset sourceAsset,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default)
    {
        var source = CapturePhysicalSource(sourceAsset);
        var dependencyReport = _dependencyAnalyzer.Analyze(source.Project, source.Asset.Id);

        // Copy first: a failed target write leaves the source untouched.
        var copyResult = await _assetTransferService.CopyToProjectAsync(
            _workspace,
            source.Asset,
            targetProjectFilePath,
            cancellationToken).ConfigureAwait(false);

        if (dependencyReport.IsInUse)
            return new ProjectAssetMoveResult(copyResult, sourceRemoved: false, dependencyReport);

        if (!IsStillCurrentSource(source))
        {
            throw new InvalidOperationException(
                "The target project copy succeeded, but the source project changed before removal. " +
                "The source asset was retained in its original project.");
        }

        // A failure here deliberately does not roll back the durable target copy.
        await _physicalAssetRemovalService.RemoveAsync(_workspace, source.Asset.Id, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new ProjectAssetMoveResult(copyResult, sourceRemoved: true, dependencyReport);
    }

    private ProjectAsset GetPhysicalSourceAsset(ProjectAsset sourceAsset) => CapturePhysicalSource(sourceAsset).Asset;

    private SourceSnapshot CapturePhysicalSource(ProjectAsset sourceAsset)
    {
        ArgumentNullException.ThrowIfNull(sourceAsset);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open the source project first.");
        if (_workspace.Location is null)
            throw new InvalidOperationException("Create or open the source project first.");

        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == sourceAsset.Id)
                    ?? throw new InvalidOperationException("The selected asset no longer exists in this project.");
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException("Only physical assets can be transferred between projects.");
        return new SourceSnapshot(
            project,
            Path.GetFullPath(_workspace.Location.ProjectFilePath),
            asset);
    }

    private bool IsStillCurrentSource(SourceSnapshot source) =>
        ReferenceEquals(_workspace.Project, source.Project) &&
        _workspace.Project?.Id == source.Project.Id &&
        _workspace.Location is not null &&
        Path.GetFullPath(_workspace.Location.ProjectFilePath)
            .Equals(source.ProjectFilePath, StringComparison.OrdinalIgnoreCase);

    private sealed record SourceSnapshot(VideoProject Project, string ProjectFilePath, ProjectAsset Asset);
}

/// <summary>
/// Immutable summary of a non-atomic physical asset move.
/// </summary>
public sealed class ProjectAssetMoveResult
{
    public ProjectAssetMoveResult(
        ProjectAssetCopyResult copyResult,
        bool sourceRemoved,
        ProjectAssetDependencyReport dependencyReport)
    {
        CopyResult = copyResult ?? throw new ArgumentNullException(nameof(copyResult));
        SourceRemoved = sourceRemoved;
        ArgumentNullException.ThrowIfNull(dependencyReport);
        DependencyReport = new ProjectAssetDependencyReport(dependencyReport.Dependencies);
    }

    public ProjectAssetCopyResult CopyResult { get; }
    public bool SourceRemoved { get; }
    public ProjectAssetDependencyReport DependencyReport { get; }
}
