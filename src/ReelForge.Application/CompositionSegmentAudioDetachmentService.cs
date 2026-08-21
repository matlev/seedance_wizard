using System.Globalization;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed class CompositionSegmentAudioDetachmentService
{
    private readonly ProjectWorkspace _workspace;
    private readonly ICompositionSegmentMaterializer _segmentMaterializer;
    private readonly IAudioExtractionEngine _extractionEngine;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService _mediaInspector;

    public CompositionSegmentAudioDetachmentService(
        ProjectWorkspace workspace,
        ICompositionSegmentMaterializer segmentMaterializer,
        IAudioExtractionEngine extractionEngine,
        IContentHashService contentHashService,
        IMediaInspectionService mediaInspector)
    {
        _workspace = workspace;
        _segmentMaterializer = segmentMaterializer;
        _extractionEngine = extractionEngine;
        _contentHashService = contentHashService;
        _mediaInspector = mediaInspector;
    }

    public async Task<DetachedCompositionAudioResult> DetachAsync(
        Guid segmentId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        var compositionService = new WorkingCompositionService(_workspace);
        var (composition, revision, recipe) = compositionService.GetCurrent();
        var segmentIndex = recipe.Segments.FindIndex(segment => segment.Id == segmentId);
        if (segmentIndex < 0)
            throw new InvalidOperationException("The selected composition segment no longer exists.");
        var segment = recipe.Segments[segmentIndex];
        if (recipe.AudioClips.Any(clip =>
                project.Assets.SingleOrDefault(asset => asset.Id == clip.Source.AssetId)?.Provenance is
                {
                    Operation: "detach-segment-audio"
                } provenance &&
                provenance.Parameters.GetValueOrDefault("compositionSegmentId") == segmentId.ToString("D")))
            throw new InvalidOperationException("This composition segment already has detached audio on the timeline.");
        var timelineStart = ResolveTimelineStart(project, recipe, segmentIndex);
        var fileName = ValidateFileName(requestedFileName);
        var audioDirectory = Path.GetFullPath(Path.Combine(location.RootDirectory, "assets", "audio"));
        Directory.CreateDirectory(audioDirectory);
        var finalPath = GetAvailablePath(audioDirectory, fileName);
        var temporaryPath = Path.Combine(audioDirectory, $".detach-audio-{Guid.NewGuid():N}.partial.m4a");
        ProjectAsset? detachedAsset = null;
        try
        {
            await using (var media = await _segmentMaterializer.MaterializeSegmentAsync(
                             project,
                             location,
                             composition.Id,
                             revision.Id,
                             segmentId,
                             MaterializationPurpose.FinalExport,
                             cancellationToken).ConfigureAwait(false))
            {
                var sourceEncoding = media.Encoding ??
                                     await _mediaInspector.InspectAsync(media.Path, cancellationToken)
                                         .ConfigureAwait(false);
                if (sourceEncoding.Audio is null)
                    throw new InvalidOperationException("The selected composition segment has no audio stream to detach.");
                await _extractionEngine.ExtractToM4aAsync(media.Path, temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            var encoding = await _mediaInspector.InspectAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (encoding.Audio is null || encoding.Video is not null)
                throw new InvalidDataException("The detached file is not an inspectable audio-only file.");
            var identity = await _contentHashService.ComputeAsync(temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, finalPath);

            detachedAsset = new ProjectAsset
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
                    Operation = "detach-segment-audio",
                    SourceAssetIds = [segment.Source.AssetId],
                    SourceRecipeRevisionId = segment.Source.RecipeRevisionId,
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["compositionAssetId"] = composition.Id.ToString("D"),
                        ["compositionRecipeRevisionId"] = revision.Id.ToString("D"),
                        ["compositionSegmentId"] = segment.Id.ToString("D"),
                        ["timelineStartSeconds"] = timelineStart.TotalSeconds.ToString("R", CultureInfo.InvariantCulture),
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
            project.AddAsset(detachedAsset);
            var compositionResult = await compositionService.AddDetachedSegmentAudioAsync(
                    segmentId,
                    detachedAsset.Id,
                    timelineStart,
                    cancellationToken)
                .ConfigureAwait(false);
            return new DetachedCompositionAudioResult(
                detachedAsset,
                compositionResult.Revision,
                compositionResult.AudioClipId,
                timelineStart);
        }
        catch
        {
            if (detachedAsset is not null) project.Assets.Remove(detachedAsset);
            if (File.Exists(finalPath)) File.Delete(finalPath);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static TimeSpan ResolveTimelineStart(
        VideoProject project,
        CompositionRecipe recipe,
        int segmentIndex)
    {
        var seconds = 0d;
        for (var index = 0; index < segmentIndex; index++)
        {
            var segment = recipe.Segments[index];
            var source = project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId);
            var duration = CompositionSegmentTiming.ResolveDuration(project, segment, source)
                ?? throw new InvalidDataException("A preceding composition segment has no known duration.");
            seconds += duration;
        }
        return TimeSpan.FromMilliseconds(Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
    }

    private static string ValidateFileName(string requestedFileName)
    {
        var fileName = requestedFileName.Trim();
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.EndsWith(' ') || fileName.EndsWith('.'))
            throw new ArgumentException("Enter a valid filename without a folder path.", nameof(requestedFileName));
        if (!Path.GetExtension(fileName).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Detached audio must keep the .m4a file type.", nameof(requestedFileName));
        return fileName;
    }

    private static string GetAvailablePath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;
        while (File.Exists(candidate)) candidate = Path.Combine(directory, $"{stem} ({suffix++}){extension}");
        return candidate;
    }
}

public sealed record DetachedCompositionAudioResult(
    ProjectAsset AudioAsset,
    RecipeRevision CompositionRevision,
    Guid AudioClipId,
    TimeSpan TimelineStart);
