using ReelForge.Core;

namespace ReelForge.Application;

public sealed class RenderedAssetPromotionService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService _mediaInspector;

    public RenderedAssetPromotionService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IContentHashService contentHashService,
        IMediaInspectionService mediaInspector)
    {
        _workspace = workspace;
        _materializer = materializer;
        _contentHashService = contentHashService;
        _mediaInspector = mediaInspector;
    }

    public async Task<ProjectAsset> SaveAsAssetAsync(
        Guid virtualAssetId,
        Guid recipeRevisionId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        ValidateVirtualRevision(project, virtualAssetId, recipeRevisionId);
        var fileName = MediaFileNamePolicy.ValidateRequiredExtension(
            requestedFileName,
            ".mp4",
            "Rendered composition assets",
            nameof(requestedFileName));
        var videosDirectory = Path.GetFullPath(Path.Combine(location.RootDirectory, "assets", "videos"));
        Directory.CreateDirectory(videosDirectory);
        var finalPath = CollisionFreeDestinationPolicy.GetAvailablePath(videosDirectory, fileName);
        var temporaryPath = Path.Combine(videosDirectory, $".promote-{Guid.NewGuid():N}.partial");
        var assetAdded = false;
        ProjectAsset? promoted = null;
        try
        {
            await using (var rendered = await _materializer.MaterializeAsync(
                             project,
                             location,
                             new MaterializationRequest(
                                 new AssetMaterializationTarget(virtualAssetId, recipeRevisionId),
                                 MaterializationPurpose.FinalExport,
                                 MaterializationRetentionPreference.PreferRetained),
                             cancellationToken).ConfigureAwait(false))
            {
                await CopyAsync(rendered.Path, temporaryPath, cancellationToken).ConfigureAwait(false);
            }

            var identity = await _contentHashService.ComputeAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            var encoding = await _mediaInspector.InspectAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (encoding.Video is null)
                throw new InvalidDataException("The rendered copy is not an inspectable video.");
            File.Move(temporaryPath, finalPath);
            promoted = new ProjectAsset
            {
                DisplayName = Path.GetFileName(finalPath),
                FileName = Path.GetFileName(finalPath),
                MediaType = MediaType.Video,
                StorageKind = AssetStorageKind.Physical,
                Origin = AssetOrigin.EditorDerived,
                DurationSeconds = encoding.DurationSeconds,
                Width = encoding.Video.Width,
                Height = encoding.Video.Height,
                Encoding = encoding,
                Provenance = new AssetProvenance
                {
                    Operation = "promoted-render",
                    SourceAssetIds = [virtualAssetId],
                    SourceRecipeRevisionId = recipeRevisionId
                },
                Physical = new PhysicalAssetStorage
                {
                    RelativePath = ProjectPathPolicy.GetRelativePath(location, finalPath),
                    Durability = PhysicalAssetDurability.Promoted,
                    ContentIdentity = identity,
                    Availability = PhysicalAssetAvailability.Available
                },
                Virtual = null
            };
            project.AddAsset(promoted);
            assetAdded = true;
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return promoted;
        }
        catch
        {
            if (assetAdded && promoted is not null) project.Assets.Remove(promoted);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<string> ExportAsync(
        Guid virtualAssetId,
        Guid recipeRevisionId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        ValidateVirtualRevision(project, virtualAssetId, recipeRevisionId);
        return await ExportTargetAsync(
            project,
            location,
            new AssetMaterializationTarget(virtualAssetId, recipeRevisionId),
            destinationPath,
            ".mp4",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ExportFrameAsync(
        Guid anchorId,
        Guid anchorRevisionId,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        if (!project.AnchorRevisions.Any(revision =>
                revision.Id == anchorRevisionId && revision.AnchorId == anchorId))
            throw new InvalidOperationException("The requested Saved Frame revision no longer exists.");
        return await ExportTargetAsync(
            project,
            location,
            new AnchorMaterializationTarget(anchorId, anchorRevisionId),
            destinationPath,
            ".png",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ExportTargetAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationTarget target,
        string destinationPath,
        string requiredExtension,
        CancellationToken cancellationToken)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (!Path.GetExtension(fullDestinationPath).Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Exports of this media must use the {requiredExtension} file type.", nameof(destinationPath));
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("Choose a valid export destination.", nameof(destinationPath));
        Directory.CreateDirectory(destinationDirectory);
        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.partial");
        try
        {
            await using (var rendered = await _materializer.MaterializeAsync(
                             project,
                             location,
                             new MaterializationRequest(
                                 target,
                                 MaterializationPurpose.FinalExport,
                                 MaterializationRetentionPreference.NormalCache),
                             cancellationToken).ConfigureAwait(false))
            {
                await CopyAsync(rendered.Path, temporaryPath, cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
            return fullDestinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<ProjectAsset> SaveFrameAsAssetAsync(
        Guid anchorId,
        Guid anchorRevisionId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        var anchor = project.Anchors.SingleOrDefault(candidate => candidate.Id == anchorId)
            ?? throw new InvalidOperationException("The Saved Frame no longer exists.");
        var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                           candidate.Id == anchorRevisionId && candidate.AnchorId == anchorId)
                       ?? throw new InvalidOperationException("The requested Saved Frame revision no longer exists.");
        var fileName = MediaFileNamePolicy.ValidateRequiredExtension(
            requestedFileName,
            ".png",
            "Saved Frame assets",
            nameof(requestedFileName));
        var imagesDirectory = Path.GetFullPath(Path.Combine(location.RootDirectory, "assets", "images"));
        Directory.CreateDirectory(imagesDirectory);
        var finalPath = CollisionFreeDestinationPolicy.GetAvailablePath(imagesDirectory, fileName);
        var temporaryPath = Path.Combine(imagesDirectory, $".promote-{Guid.NewGuid():N}.partial");
        var assetAdded = false;
        ProjectAsset? promoted = null;
        try
        {
            await using (var rendered = await _materializer.MaterializeAsync(
                             project,
                             location,
                             new MaterializationRequest(
                                 new AnchorMaterializationTarget(anchorId, anchorRevisionId),
                                 MaterializationPurpose.FinalExport,
                                 MaterializationRetentionPreference.PreferRetained,
                                 "png"),
                             cancellationToken).ConfigureAwait(false))
            {
                await CopyAsync(rendered.Path, temporaryPath, cancellationToken).ConfigureAwait(false);
            }

            var identity = await _contentHashService.ComputeAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            var encoding = await _mediaInspector.InspectAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (encoding.Video is null)
                throw new InvalidDataException("The rendered Saved Frame is not an inspectable image.");
            File.Move(temporaryPath, finalPath);
            promoted = new ProjectAsset
            {
                DisplayName = Path.GetFileName(finalPath),
                FileName = Path.GetFileName(finalPath),
                MediaType = MediaType.Image,
                StorageKind = AssetStorageKind.Physical,
                Origin = AssetOrigin.ExtractedFrame,
                Width = encoding.Video.Width,
                Height = encoding.Video.Height,
                Encoding = encoding,
                Provenance = new AssetProvenance
                {
                    Operation = "promoted-saved-frame",
                    SourceAssetIds = [revision.SourceAssetId],
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["anchorId"] = anchor.Id.ToString("N"),
                        ["anchorRevisionId"] = revision.Id.ToString("N")
                    }
                },
                Physical = new PhysicalAssetStorage
                {
                    RelativePath = ProjectPathPolicy.GetRelativePath(location, finalPath),
                    Durability = PhysicalAssetDurability.Promoted,
                    ContentIdentity = identity,
                    Availability = PhysicalAssetAvailability.Available
                },
                Virtual = null
            };
            project.AddAsset(promoted);
            assetAdded = true;
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return promoted;
        }
        catch
        {
            if (assetAdded && promoted is not null) project.Assets.Remove(promoted);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateVirtualRevision(VideoProject project, Guid virtualAssetId, Guid recipeRevisionId)
    {
        var source = project.Assets.SingleOrDefault(asset => asset.Id == virtualAssetId)
            ?? throw new InvalidOperationException("The virtual source no longer exists.");
        if (source.StorageKind != AssetStorageKind.Virtual || source.MediaType != MediaType.Video)
            throw new InvalidOperationException("Only a virtual video can be rendered.");
        if (!project.RecipeRevisions.Any(revision =>
                revision.Id == recipeRevisionId && revision.VirtualAssetId == virtualAssetId))
            throw new InvalidOperationException("The requested recipe revision no longer exists.");
    }

}
