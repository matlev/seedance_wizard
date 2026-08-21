using ReelForge.Core;

namespace ReelForge.Application;

public sealed class AudioExtractionService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly IAudioExtractionEngine _extractionEngine;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService _mediaInspector;

    public AudioExtractionService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IAudioExtractionEngine extractionEngine,
        IContentHashService contentHashService,
        IMediaInspectionService mediaInspector)
    {
        _workspace = workspace;
        _materializer = materializer;
        _extractionEngine = extractionEngine;
        _contentHashService = contentHashService;
        _mediaInspector = mediaInspector;
    }

    public async Task<ProjectAsset> ExtractAsAssetAsync(
        Guid sourceAssetId,
        Guid? sourceRecipeRevisionId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        var source = ValidateSource(project, sourceAssetId, sourceRecipeRevisionId);
        var fileName = MediaFileNamePolicy.ValidateRequiredExtension(
            requestedFileName,
            ".m4a",
            "Extracted audio",
            nameof(requestedFileName));
        var audioDirectory = Path.GetFullPath(Path.Combine(location.RootDirectory, "assets", "audio"));
        Directory.CreateDirectory(audioDirectory);
        var finalPath = CollisionFreeDestinationPolicy.GetAvailablePath(audioDirectory, fileName);
        var temporaryPath = Path.Combine(audioDirectory, $".extract-audio-{Guid.NewGuid():N}.partial.m4a");
        ProjectAsset? extracted = null;
        try
        {
            await using (var media = await _materializer.MaterializeAsync(
                             project,
                             location,
                             new MaterializationRequest(
                                 new AssetMaterializationTarget(source.Id, sourceRecipeRevisionId),
                                 MaterializationPurpose.FinalExport,
                                 MaterializationRetentionPreference.NormalCache),
                             cancellationToken).ConfigureAwait(false))
            {
                var sourceEncoding = media.Encoding ??
                                     await _mediaInspector.InspectAsync(media.Path, cancellationToken)
                                         .ConfigureAwait(false);
                if (sourceEncoding.Audio is null)
                    throw new InvalidOperationException($"'{source.EffectiveDisplayName}' has no audio stream to extract.");
                await _extractionEngine.ExtractToM4aAsync(media.Path, temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            var encoding = await _mediaInspector.InspectAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (encoding.Audio is null || encoding.Video is not null)
                throw new InvalidDataException("The extracted file is not an inspectable audio-only file.");
            var identity = await _contentHashService.ComputeAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, finalPath);

            extracted = new ProjectAsset
            {
                DisplayName = Path.GetFileName(finalPath),
                FileName = Path.GetFileName(finalPath),
                MediaType = MediaType.Audio,
                StorageKind = AssetStorageKind.Physical,
                Origin = AssetOrigin.ExtractedAudio,
                DurationSeconds = encoding.DurationSeconds,
                Encoding = encoding,
                Provenance = new AssetProvenance
                {
                    Operation = "extract-audio",
                    SourceAssetIds = [source.Id],
                    SourceRecipeRevisionId = sourceRecipeRevisionId,
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["format"] = "m4a",
                        ["audioCodec"] = encoding.Audio.Codec ?? "unknown"
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
            project.AddAsset(extracted);
            await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
            return extracted;
        }
        catch
        {
            if (extracted is not null) project.Assets.Remove(extracted);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static ProjectAsset ValidateSource(
        VideoProject project,
        Guid sourceAssetId,
        Guid? sourceRecipeRevisionId)
    {
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceAssetId)
            ?? throw new InvalidOperationException("The selected video no longer exists.");
        if (source.MediaType != MediaType.Video)
            throw new InvalidOperationException("Choose a video or Saved Clip to extract audio.");
        if (source.StorageKind == AssetStorageKind.Physical)
        {
            if (sourceRecipeRevisionId is not null)
                throw new InvalidOperationException("A physical video does not use a recipe revision.");
            return source;
        }

        if (source.Virtual?.Kind != VirtualAssetKind.SavedClip || sourceRecipeRevisionId is not { } revisionId)
            throw new InvalidOperationException("Only a physical video or pinned Saved Clip can extract audio.");
        if (!project.RecipeRevisions.Any(revision =>
                revision.Id == revisionId && revision.VirtualAssetId == source.Id))
            throw new InvalidOperationException("The requested Saved Clip revision no longer exists.");
        return source;
    }

}
