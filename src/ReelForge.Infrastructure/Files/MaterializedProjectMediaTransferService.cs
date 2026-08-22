using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// Copies a cache-backed Project Media item by materializing its exact immutable
/// source into a physical asset in another project. Logical anchors, recipes,
/// and compositions remain in their source project.
/// </summary>
public sealed class MaterializedProjectMediaTransferService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly ProjectAssetTransferService _assetTransferService;

    public MaterializedProjectMediaTransferService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        ProjectAssetTransferService assetTransferService)
    {
        _workspace = workspace;
        _materializer = materializer;
        _assetTransferService = assetTransferService;
    }

    public Task<ProjectAssetCopyResult> CopySavedFrameAsync(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        string requestedFileName,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(revision);
        var source = CaptureSavedFrame(anchor, revision, targetProjectFilePath);
        return MaterializeAndImportAsync(
            source.Project,
            source.Location,
            new AnchorMaterializationTarget(source.AnchorId, source.RevisionId),
            EnsureExtension(requestedFileName, ".png"),
            targetProjectFilePath,
            AssetOrigin.ExtractedFrame,
            BuildFrameProvenance(source),
            cancellationToken);
    }

    public Task<ProjectAssetCopyResult> CopyVirtualVideoAsync(
        ProjectAsset asset,
        Guid recipeRevisionId,
        string requestedFileName,
        string targetProjectFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var source = CaptureVirtualVideo(asset, recipeRevisionId, targetProjectFilePath);
        return MaterializeAndImportAsync(
            source.Project,
            source.Location,
            new AssetMaterializationTarget(source.AssetId, source.RecipeRevisionId),
            EnsureExtension(requestedFileName, ".mp4"),
            targetProjectFilePath,
            AssetOrigin.EditorDerived,
            BuildVirtualVideoProvenance(source),
            cancellationToken);
    }

    private async Task<ProjectAssetCopyResult> MaterializeAndImportAsync(
        VideoProject sourceProject,
        ProjectLocation sourceLocation,
        MaterializationTarget target,
        string stagingFileName,
        string targetProjectFilePath,
        AssetOrigin origin,
        AssetProvenance provenance,
        CancellationToken cancellationToken)
    {
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "ReelForge", "materialized-project-copy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var stagingPath = Path.Combine(stagingDirectory, stagingFileName);
        try
        {
            await using (var materialized = await _materializer.MaterializeAsync(
                             sourceProject,
                             sourceLocation,
                             new MaterializationRequest(
                                 target,
                                 MaterializationPurpose.FinalExport,
                                 MaterializationRetentionPreference.PreferRetained),
                             cancellationToken).ConfigureAwait(false))
            {
                await CopyAsync(materialized.Path, stagingPath, cancellationToken).ConfigureAwait(false);
                provenance.Parameters["materializedContentHash"] =
                    materialized.ContentIdentity.Sha256 ?? string.Empty;
            }

            return await _assetTransferService.ImportFileToProjectAsync(
                stagingPath,
                targetProjectFilePath,
                new ProjectAssetCopyMetadata(origin, provenance),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteStagingDirectory(stagingDirectory);
        }
    }

    private SavedFrameSnapshot CaptureSavedFrame(
        FrameAnchor suppliedAnchor,
        FrameAnchorRevision suppliedRevision,
        string targetProjectFilePath)
    {
        var (project, location, sourceProjectPath) = CaptureProject(targetProjectFilePath);
        var anchor = project.Anchors.SingleOrDefault(candidate => candidate.Id == suppliedAnchor.Id)
                     ?? throw new InvalidOperationException("The selected Saved Frame no longer exists in this project.");
        var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                           candidate.Id == suppliedRevision.Id && candidate.AnchorId == anchor.Id)
                       ?? throw new InvalidOperationException("The selected Saved Frame revision no longer exists in this project.");
        return new SavedFrameSnapshot(
            project,
            location,
            sourceProjectPath,
            project.Id,
            anchor.Id,
            revision.Id,
            revision.SourceAssetId,
            revision.SourceRecipeRevisionId);
    }

    private VirtualVideoSnapshot CaptureVirtualVideo(
        ProjectAsset suppliedAsset,
        Guid recipeRevisionId,
        string targetProjectFilePath)
    {
        var (project, location, sourceProjectPath) = CaptureProject(targetProjectFilePath);
        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == suppliedAsset.Id)
                    ?? throw new InvalidOperationException("The selected Project Media item no longer exists in this project.");
        if (asset.StorageKind != AssetStorageKind.Virtual || asset.MediaType != MediaType.Video)
            throw new InvalidOperationException("Only a Saved Clip or Working Composition can be copied as rendered media.");
        if (asset.Virtual?.Kind is not (VirtualAssetKind.SavedClip or VirtualAssetKind.Composition))
            throw new InvalidOperationException("Only a Saved Clip or Working Composition can be copied as rendered media.");
        if (!project.RecipeRevisions.Any(revision => revision.Id == recipeRevisionId && revision.VirtualAssetId == asset.Id))
            throw new InvalidOperationException("The selected committed media revision no longer exists in this project.");
        return new VirtualVideoSnapshot(project, location, sourceProjectPath, project.Id, asset.Id, recipeRevisionId, asset.Virtual.Kind);
    }

    private (VideoProject Project, ProjectLocation Location, string SourceProjectPath) CaptureProject(string targetProjectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectFilePath);
        var project = _workspace.Project ?? throw new InvalidOperationException("Create or open the source project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("Create or open the source project first.");
        var sourceProjectPath = Path.GetFullPath(location.ProjectFilePath);
        if (sourceProjectPath.Equals(Path.GetFullPath(targetProjectFilePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different destination project.");
        return (project, location, sourceProjectPath);
    }

    private static AssetProvenance BuildFrameProvenance(SavedFrameSnapshot source) => new()
    {
        Operation = "copied-materialized-from-project",
        Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sourceProjectId"] = source.ProjectId.ToString("D"),
            ["sourceKind"] = "saved-frame",
            ["sourceAnchorId"] = source.AnchorId.ToString("D"),
            ["sourceAnchorRevisionId"] = source.RevisionId.ToString("D"),
            ["sourceAssetId"] = source.SourceAssetId.ToString("D"),
            ["sourceRecipeRevisionId"] = source.SourceRecipeRevisionId?.ToString("D") ?? string.Empty
        }
    };

    private static AssetProvenance BuildVirtualVideoProvenance(VirtualVideoSnapshot source) => new()
    {
        Operation = "copied-materialized-from-project",
        Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sourceProjectId"] = source.ProjectId.ToString("D"),
            ["sourceKind"] = source.Kind == VirtualAssetKind.SavedClip ? "saved-clip" : "working-composition",
            ["sourceVirtualAssetId"] = source.AssetId.ToString("D"),
            ["sourceRecipeRevisionId"] = source.RecipeRevisionId.ToString("D")
        }
    };

    private static string EnsureExtension(string requestedFileName, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFileName);
        var stem = Path.GetFileNameWithoutExtension(requestedFileName).Trim();
        if (string.IsNullOrWhiteSpace(stem)) stem = "ReelForge media";
        var invalid = Path.GetInvalidFileNameChars();
        stem = new string(stem.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(stem)) stem = "ReelForge media";
        return stem + extension;
    }

    private static async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void TryDeleteStagingDirectory(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The target project has already committed. A virus scanner or
            // deferred handle on this disposable staging directory must not
            // turn a successful copy into an apparent failure.
        }
    }

    private sealed record SavedFrameSnapshot(
        VideoProject Project,
        ProjectLocation Location,
        string ProjectFilePath,
        Guid ProjectId,
        Guid AnchorId,
        Guid RevisionId,
        Guid SourceAssetId,
        Guid? SourceRecipeRevisionId);

    private sealed record VirtualVideoSnapshot(
        VideoProject Project,
        ProjectLocation Location,
        string ProjectFilePath,
        Guid ProjectId,
        Guid AssetId,
        Guid RecipeRevisionId,
        VirtualAssetKind Kind);
}
