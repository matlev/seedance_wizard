using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed record ProjectAssetCopyResult(
    string TargetProjectName,
    string TargetProjectFilePath,
    ProjectAsset CopiedAsset);

/// <summary>
/// Describes the source-independent metadata assigned after a file has been
/// imported into a different project. This keeps the target-project import,
/// persistence, and rollback boundary in one place for physical and
/// materialized copies alike.
/// </summary>
public sealed record ProjectAssetCopyMetadata(
    AssetOrigin Origin,
    AssetProvenance Provenance);

public sealed class ProjectAssetTransferService
{
    private readonly IProjectStore _projectStore;
    private readonly IAssetImportService _assetImporter;
    private readonly ProjectWorkspace _saveWorkspace;

    public ProjectAssetTransferService(
        IProjectStore projectStore,
        IAssetImportService assetImporter,
        ProjectWorkspace? saveWorkspace = null)
    {
        _projectStore = projectStore;
        _assetImporter = assetImporter;
        _saveWorkspace = saveWorkspace ?? new ProjectWorkspace(
            projectStore,
            assetImporter,
            projectStore as IProjectRecoveryStore);
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

        return await ImportFileToProjectAsync(
            sourceMediaPath,
            targetProjectPath,
            new ProjectAssetCopyMetadata(
                AssetOrigin.Imported,
                new AssetProvenance
                {
                    Operation = "copied-from-project",
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["sourceProjectId"] = sourceProjectId.ToString("D"),
                        ["sourceAssetId"] = sourceAssetId.ToString("D"),
                        ["sourceContentHash"] = sourceContentHash
                    }
                }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports one already-materialized file into a target project and commits
    /// both the target metadata and file as a single target-project operation.
    /// On a target save failure the newly-imported target metadata and file are
    /// removed; the caller's source is deliberately untouched.
    /// </summary>
    public async Task<ProjectAssetCopyResult> ImportFileToProjectAsync(
        string sourceMediaPath,
        string targetProjectFilePath,
        ProjectAssetCopyMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectFilePath);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!File.Exists(sourceMediaPath))
            throw new FileNotFoundException("The materialized media file is missing and cannot be copied.", sourceMediaPath);

        var targetProjectPath = Path.GetFullPath(targetProjectFilePath);
        var (_, targetLocation) = await _projectStore
            .OpenAsync(targetProjectPath, cancellationToken)
            .ConfigureAwait(false);
        var copiedAssets = await _assetImporter
            .ImportAsync(targetLocation, [sourceMediaPath], cancellationToken)
            .ConfigureAwait(false);
        var copiedAsset = copiedAssets.Count == 1
            ? copiedAssets[0]
            : throw new InvalidOperationException("Expected exactly one copied asset.");

        copiedAsset.Origin = metadata.Origin;
        copiedAsset.Provenance = metadata.Provenance;
        string? targetProjectName = null;
        try
        {
            await _saveWorkspace
                .UpdateDetachedWithRollbackAsync(
                    targetProjectPath,
                    (targetProject, _) =>
                    {
                        targetProject.AddAsset(copiedAsset);
                        targetProjectName = targetProject.Name;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            var copiedPath = Path.GetFullPath(Path.Combine(
                targetLocation.RootDirectory,
                copiedAsset.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(copiedPath)) File.Delete(copiedPath);
            throw;
        }

        return new ProjectAssetCopyResult(targetProjectName!, targetLocation.ProjectFilePath, copiedAsset);
    }
}
