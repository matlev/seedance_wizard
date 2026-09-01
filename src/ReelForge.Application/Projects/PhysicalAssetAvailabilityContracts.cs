using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Reconciles machine-observed availability for active physical project media before portable
/// dependency analysis runs. Implementations own filesystem and hashing details.
/// </summary>
public interface IPhysicalAssetAvailabilityReconciler
{
    Task<PhysicalAssetAvailabilityReconciliationResult> ReconcileActivePhysicalAssetsAsync(
        VideoProject selectedProject,
        ProjectLocation selectedLocation,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a batch availability reconciliation. A failure is reported after all in-memory
/// availability changes have been rolled back by the workspace mutation transaction.
/// </summary>
public sealed record PhysicalAssetAvailabilityReconciliationResult(bool Changed, bool IsStale, Exception? Failure = null)
{
    public static readonly PhysicalAssetAvailabilityReconciliationResult Unchanged = new(false, false);
    public static readonly PhysicalAssetAvailabilityReconciliationResult Stale = new(false, true);
}
