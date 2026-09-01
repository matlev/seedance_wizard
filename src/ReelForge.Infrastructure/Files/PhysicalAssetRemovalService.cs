using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// Removes a physical asset from project state before removing its contained file.
/// A file deletion failure is deliberately propagated after the project change has been saved;
/// callers can report the failure, but this service does not attempt a potentially unsafe rollback.
/// </summary>
public sealed class PhysicalAssetRemovalService
{
#pragma warning disable CA1822 // Kept as an injected infrastructure service at the coordination boundary.
    public async Task RemoveAsync(
        ProjectWorkspace workspace,
        Guid assetId,
        bool preserveLogicalRecord = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var project = workspace.Project ?? throw new InvalidOperationException("Create or open a project first.");
        if (workspace.Location is null)
            throw new InvalidOperationException("Create or open a project first.");

        var assetIndex = project.Assets.FindIndex(asset => asset.Id == assetId);
        if (assetIndex < 0)
            throw new InvalidOperationException($"Asset '{assetId}' does not exist in this project.");
        var asset = project.Assets[assetIndex];
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException("Only physical assets can be removed from disk.");

        var absolutePath = workspace.GetAbsoluteAssetPath(asset);
        var priorModifiedAt = project.ModifiedAt;
        var priorAvailability = asset.Physical.Availability;
        if (preserveLogicalRecord)
        {
            asset.IsDeleted = true;
            asset.Physical.Availability = PhysicalAssetAvailability.Missing;
        }
        else
            project.Assets.RemoveAt(assetIndex);
        try
        {
            await workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (preserveLogicalRecord)
            {
                asset.IsDeleted = false;
                asset.Physical.Availability = priorAvailability;
            }
            else
                project.Assets.Insert(assetIndex, asset);
            project.ModifiedAt = priorModifiedAt;
            throw;
        }

        if (File.Exists(absolutePath)) File.Delete(absolutePath);
    }
#pragma warning restore CA1822
}
