using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed record ProjectAssetCopyResult(
    string TargetProjectName,
    string TargetProjectFilePath,
    ProjectAsset CopiedAsset);

public sealed class ProjectAssetTransferService
{
    private readonly IProjectStore _projectStore;
    private readonly IAssetImportService _assetImporter;

    public ProjectAssetTransferService(IProjectStore projectStore, IAssetImportService assetImporter)
    {
        _projectStore = projectStore;
        _assetImporter = assetImporter;
    }

    public async Task<ProjectAssetCopyResult> CopyToProjectAsync(
        ProjectWorkspace sourceWorkspace,
        ProjectAsset sourceAsset,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceWorkspace);
        ArgumentNullException.ThrowIfNull(sourceAsset);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectFilePath);
        if (sourceWorkspace.Project is null || sourceWorkspace.Location is null)
            throw new InvalidOperationException("Create or open the source project first.");
        var sourceProject = sourceWorkspace.Project;
        var sourceLocation = sourceWorkspace.Location;
        var capturedSourceAsset = sourceProject.Assets.SingleOrDefault(asset => asset.Id == sourceAsset.Id)
                                  ?? throw new InvalidOperationException("The selected asset no longer exists in this project.");
        if (capturedSourceAsset.StorageKind != AssetStorageKind.Physical || capturedSourceAsset.Physical is null)
            throw new InvalidOperationException("Virtual assets cannot be copied between projects until recipe materialization is available.");

        // Everything used after the first await is captured from the original project.
        // The shell may open a different project while the target import is in flight.
        var sourceProjectId = sourceProject.Id;
        var sourceAssetId = capturedSourceAsset.Id;
        var sourceContentHash = capturedSourceAsset.Physical.ContentIdentity.Sha256 ?? string.Empty;
        var sourceProjectPath = Path.GetFullPath(sourceLocation.ProjectFilePath);
        var sourceMediaPath = sourceWorkspace.GetAbsoluteAssetPath(capturedSourceAsset);
        var targetProjectPath = Path.GetFullPath(targetProjectFilePath);
        if (sourceProjectPath.Equals(targetProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different destination project.");

        if (!File.Exists(sourceMediaPath))
            throw new FileNotFoundException("The source media file is missing and cannot be copied.", sourceMediaPath);

        var (targetProject, targetLocation) = await _projectStore
            .OpenAsync(targetProjectPath, cancellationToken)
            .ConfigureAwait(false);
        var copiedAssets = await _assetImporter
            .ImportAsync(targetLocation, [sourceMediaPath], cancellationToken)
            .ConfigureAwait(false);
        var copiedAsset = copiedAssets.Count == 1
            ? copiedAssets[0]
            : throw new InvalidOperationException("Expected exactly one copied asset.");

        copiedAsset.Provenance = new AssetProvenance
        {
            Operation = "copied-from-project",
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sourceProjectId"] = sourceProjectId.ToString("D"),
                ["sourceAssetId"] = sourceAssetId.ToString("D"),
                ["sourceContentHash"] = sourceContentHash
            }
        };
        targetProject.AddAsset(copiedAsset);
        try
        {
            await _projectStore.SaveAsync(targetProject, targetLocation, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            targetProject.Assets.Remove(copiedAsset);
            var copiedPath = Path.GetFullPath(Path.Combine(
                targetLocation.RootDirectory,
                copiedAsset.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(copiedPath)) File.Delete(copiedPath);
            throw;
        }

        return new ProjectAssetCopyResult(targetProject.Name, targetLocation.ProjectFilePath, copiedAsset);
    }
}
