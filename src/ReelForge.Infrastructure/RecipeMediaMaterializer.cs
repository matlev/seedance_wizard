using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class RecipeMediaMaterializer : IMediaMaterializer, IDisposable
{
    private const string TrimAlgorithmVersion = "saved-clip-trim-v1";
    private const string ConcatAlgorithmVersion = "composition-concat-v2";
    private const string AudioOverlayAlgorithmVersion = "composition-audio-overlay-v1";
    private readonly PhysicalAssetMaterializer _physicalMaterializer;
    private readonly IExactVideoFrameService _exactFrameService;
    private readonly IExternalProcessRunner _runner;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService? _mediaInspector;
    private readonly string _cacheRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _fingerprintLock = new(1, 1);
    private string? _ffmpegPath;
    private string? _rendererFingerprint;
    private volatile bool _persistModifiedMediaOnDisk;
    private bool _disposed;

    public RecipeMediaMaterializer(
        string? ffmpegPath,
        IExternalProcessRunner runner,
        IExactVideoFrameService exactFrameService,
        string cacheRoot,
        IContentHashService? contentHashService = null,
        IMediaInspectionService? mediaInspector = null,
        bool persistModifiedMediaOnDisk = false)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(exactFrameService);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _ffmpegPath = ffmpegPath;
        _runner = runner;
        _exactFrameService = exactFrameService;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
        _mediaInspector = mediaInspector;
        _persistModifiedMediaOnDisk = persistModifiedMediaOnDisk;
        _physicalMaterializer = new PhysicalAssetMaterializer(_contentHashService, exactFrameService);
    }

    public void UpdateExecutablePath(string? ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        _rendererFingerprint = null;
    }

    public void UpdatePersistencePreference(bool persistModifiedMediaOnDisk) =>
        _persistModifiedMediaOnDisk = persistModifiedMediaOnDisk;

    public async Task<MaterializedMediaLease> MaterializeAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is not AssetMaterializationTarget assetTarget)
        {
            var anchorMedia = await _physicalMaterializer.MaterializeAsync(
                    project, location, request, cancellationToken)
                .ConfigureAwait(false);
            return await PersistIfRequestedAsync(
                    project, location, request.Target, anchorMedia, cancellationToken)
                .ConfigureAwait(false);
        }

        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == assetTarget.AssetId)
            ?? throw new InvalidOperationException($"Asset '{assetTarget.AssetId}' no longer exists.");
        if (asset.StorageKind == AssetStorageKind.Physical)
        {
            return await _physicalMaterializer.MaterializeAsync(project, location, request, cancellationToken)
                .ConfigureAwait(false);
        }

        var plan = RecipeRenderPlanner.Plan(project, assetTarget, request.Purpose, request.Profile);
        var media = await ExecuteNodeAsync(project, location, asset, plan.Root, request, cancellationToken)
            .ConfigureAwait(false);
        return await PersistIfRequestedAsync(
                project, location, request.Target, media, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MaterializedMediaLease> PersistIfRequestedAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationTarget target,
        MaterializedMediaLease media,
        CancellationToken cancellationToken)
    {
        if (!_persistModifiedMediaOnDisk) return media;
        try
        {
            var persistentPath = GetPersistentRepresentationPath(project, location, target, media.Path);
            var key = $"persistent|{persistentPath}";
            var persistentLock = _cacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await persistentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(persistentPath))
                {
                    var existingIdentity = await _contentHashService.ComputeAsync(
                        persistentPath, cancellationToken).ConfigureAwait(false);
                    if (existingIdentity.Sha256?.Equals(
                            media.ContentIdentity.Sha256,
                            StringComparison.OrdinalIgnoreCase) == true)
                        return new MaterializedMediaLease(
                            persistentPath, existingIdentity, media.Encoding, isDurableSource: false);
                }

                var directory = Path.GetDirectoryName(persistentPath)!;
                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(persistentPath)}.{Guid.NewGuid():N}.partial");
                try
                {
                    await CopyFileAsync(media.Path, temporaryPath, cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, persistentPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                var identity = await _contentHashService.ComputeAsync(persistentPath, cancellationToken)
                    .ConfigureAwait(false);
                return new MaterializedMediaLease(
                    persistentPath, identity, media.Encoding, isDurableSource: false);
            }
            finally
            {
                persistentLock.Release();
            }
        }
        finally
        {
            await media.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string GetPersistentRepresentationPath(
        VideoProject project,
        ProjectLocation location,
        MaterializationTarget target,
        string materializedPath)
    {
        string category;
        string stem;
        switch (target)
        {
            case AnchorMaterializationTarget anchorTarget:
            {
                var anchor = project.Anchors.Single(candidate => candidate.Id == anchorTarget.AnchorId);
                var revision = project.AnchorRevisions.Single(candidate => candidate.Id == anchorTarget.AnchorRevisionId);
                category = "frames";
                stem = $"{SanitizeFileStem(anchor.DisplayLabel ?? "Saved Frame")}-r{revision.RevisionNumber}-{anchor.Id:N}";
                break;
            }
            case AssetMaterializationTarget assetTarget:
            {
                var asset = project.Assets.Single(candidate => candidate.Id == assetTarget.AssetId);
                var revisionId = assetTarget.RecipeRevisionId ?? asset.Virtual?.CurrentRecipeRevisionId
                    ?? throw new InvalidDataException("A persistent virtual representation requires a pinned recipe revision.");
                var revision = project.RecipeRevisions.Single(candidate => candidate.Id == revisionId);
                category = asset.Virtual?.Kind == VirtualAssetKind.Composition ? "compositions" : "clips";
                stem = $"{SanitizeFileStem(asset.EffectiveDisplayName)}-r{revision.RevisionNumber}-{asset.Id:N}";
                break;
            }
            default:
                throw new NotSupportedException($"Persistent representation target '{target.GetType().Name}' is unsupported.");
        }
        var extension = Path.GetExtension(materializedPath).ToLowerInvariant();
        if (extension is not (".png" or ".mp4"))
            throw new InvalidDataException("Persistent modified media must be a PNG image or MP4 video.");
        return Path.Combine(location.RootDirectory, "assets", "modified", category, $"{stem}{extension}");
    }

    private static string SanitizeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized)) return "Modified media";
        return sanitized.Length <= 80 ? sanitized : sanitized[..80].TrimEnd();
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
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

    private async Task<MaterializedMediaLease> ExecuteNodeAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        MediaRenderPlanNode node,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case PhysicalSourceRenderPlanNode physical:
                return await _physicalMaterializer.MaterializeAsync(
                        project,
                        location,
                        request with { Target = new AssetMaterializationTarget(physical.AssetId) },
                        cancellationToken)
                    .ConfigureAwait(false);
            case TrimRenderPlanNode trim:
                return await MaterializeTrimNodeAsync(
                        project, location, outputAsset, trim, request, cancellationToken)
                    .ConfigureAwait(false);
            case ExtractFrameRenderPlanNode frame:
                return await MaterializeExtractFrameNodeAsync(
                        project, location, outputAsset, frame, request, cancellationToken)
                    .ConfigureAwait(false);
            case CompositionRenderPlanNode composition:
                return await MaterializeCompositionNodeAsync(
                        project, location, outputAsset, composition, request, cancellationToken)
                    .ConfigureAwait(false);
            default:
                throw new NotSupportedException($"Render node '{node.GetType().Name}' is not supported.");
        }
    }

    private async Task<MaterializedMediaLease> MaterializeTrimNodeAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        TrimRenderPlanNode trim,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        await using var source = await ExecuteNodeAsync(
                project,
                location,
                project.Assets.Single(asset => asset.Id == trim.Source.AssetId),
                trim.Source,
                request,
                cancellationToken)
            .ConfigureAwait(false);
        return await RenderTrimAsync(
                project, outputAsset, trim.Source.AssetId, trim.NodeHash, trim.Start, trim.End,
                source, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MaterializedMediaLease> MaterializeCompositionNodeAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        MaterializedMediaLease? video = null;
        try
        {
            video = await MaterializeCompositionVideoAsync(
                    project, location, outputAsset, composition, request, cancellationToken)
                .ConfigureAwait(false);
            if (composition.AudioClips.Count == 0)
            {
                var result = video;
                video = null;
                return result;
            }

            return await RenderCompositionAudioAsync(
                    project, location, outputAsset, composition, video, request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (video is not null) await video.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<MaterializedMediaLease> MaterializeCompositionVideoAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        if (composition.Segments is [var segment])
            return await MaterializeCompositionSegmentAsync(
                    project, location, outputAsset, segment, request, cancellationToken)
                .ConfigureAwait(false);

        var leases = new List<MaterializedMediaLease>();
        try
        {
            foreach (var plannedSegment in composition.Segments)
            {
                leases.Add(await MaterializeCompositionSegmentAsync(
                        project, location, outputAsset, plannedSegment, request, cancellationToken)
                    .ConfigureAwait(false));
            }

            var encodings = new List<MediaEncodingMetadata?>();
            foreach (var lease in leases)
            {
                var encoding = lease.Encoding;
                if (_mediaInspector is not null &&
                    (!lease.IsDurableSource || encoding?.Video is null || encoding.DurationSeconds is null))
                    encoding = await _mediaInspector.InspectAsync(lease.Path, cancellationToken).ConfigureAwait(false);
                encodings.Add(encoding);
            }
            var compatibility = MediaCompatibilityAnalyzer.Analyze(encodings);
            var allAudioEnabled = composition.Segments.All(segment => segment.AudioEnabled);
            var noAudioEnabled = composition.Segments.All(segment => !segment.AudioEnabled);
            var includeAudio = allAudioEnabled && encodings.All(encoding => encoding?.Audio is not null);
            var normalize = !compatibility.CanConcatWithoutNormalization || (!allAudioEnabled && !noAudioEnabled);
            if (normalize && _mediaInspector is null && encodings.Any(encoding => !CanNormalize(encoding)))
                throw new NotSupportedException(
                    "Composition inputs require normalization, but complete video duration and stream metadata are unavailable.");
            return await RenderConcatAsync(
                    outputAsset, composition, leases, encodings, includeAudio, normalize, request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<MaterializedMediaLease> RenderCompositionAudioAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        MaterializedMediaLease video,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var audioLeases = new List<MaterializedMediaLease>();
        try
        {
            foreach (var clip in composition.AudioClips)
            {
                var sourceAsset = project.Assets.Single(asset => asset.Id == clip.Source.AssetId);
                audioLeases.Add(await ExecuteNodeAsync(
                        project, location, sourceAsset, clip.Source, request, cancellationToken)
                    .ConfigureAwait(false));
            }

            var videoEncoding = video.Encoding;
            if (_mediaInspector is not null && videoEncoding?.Audio is null)
                videoEncoding = await _mediaInspector.InspectAsync(video.Path, cancellationToken).ConfigureAwait(false);
            var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
                "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or export compositions.");
            var fingerprint = await GetRendererFingerprintAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
            var key = HashText(string.Join('|',
                AudioOverlayAlgorithmVersion,
                composition.NodeHash,
                video.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty,
                string.Join(';', audioLeases.Select(lease => lease.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty)),
                string.Join(';', composition.AudioClips.Select(clip => clip.TimelineStartTicks)),
                videoEncoding?.Audio is not null,
                request.Purpose,
                request.Profile ?? string.Empty,
                fingerprint));
            var cacheDirectory = Path.Combine(_cacheRoot, "compositions");
            var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
            if (IsUsableCacheFile(cachePath))
                return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

            var cacheLock = _cacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
            await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsUsableCacheFile(cachePath))
                    return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(cacheDirectory);
                var temporaryPath = Path.Combine(cacheDirectory, $".{key}.{Guid.NewGuid():N}.tmp.mp4");
                try
                {
                    var arguments = FfmpegCommandBuilder.BuildAudioOverlayArguments(
                        video.Path,
                        videoEncoding?.Audio is not null,
                        audioLeases.Select((lease, index) => new AudioOverlayInput(
                            lease.Path,
                            TimeSpan.FromTicks(composition.AudioClips[index].TimelineStartTicks))).ToArray(),
                        temporaryPath);
                    var result = await _runner.RunAsync(
                            new ExternalProcessRequest(ffmpegPath, arguments),
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
                    if (!IsUsableCacheFile(temporaryPath))
                        throw new InvalidDataException("FFmpeg completed without producing the composition audio mix.");
                    try
                    {
                        File.Move(temporaryPath, cachePath, overwrite: false);
                    }
                    catch (IOException) when (IsUsableCacheFile(cachePath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                cacheLock.Release();
            }
        }
        finally
        {
            for (var index = audioLeases.Count - 1; index >= 0; index--)
                await audioLeases[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<MaterializedMediaLease> MaterializeCompositionSegmentAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        CompositionSegmentRenderPlan segment,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var sourceAsset = project.Assets.Single(asset => asset.Id == segment.Source.AssetId);
        if (segment.Start.Kind == RecipeBoundaryKind.SourceStart &&
            segment.End.Kind == RecipeBoundaryKind.SourceEnd)
            return await ExecuteNodeAsync(project, location, sourceAsset, segment.Source, request, cancellationToken)
                .ConfigureAwait(false);

        await using var source = await ExecuteNodeAsync(
                project, location, sourceAsset, segment.Source, request, cancellationToken)
            .ConfigureAwait(false);
        return await RenderTrimAsync(
                project, outputAsset, segment.Source.AssetId, segment.SegmentHash,
                segment.Start, segment.End, source, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MaterializedMediaLease> RenderConcatAsync(
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        IReadOnlyList<MaterializedMediaLease> inputs,
        List<MediaEncodingMetadata?> encodings,
        bool includeAudio,
        bool normalize,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or export compositions.");
        var fingerprint = await GetRendererFingerprintAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
        var key = HashText(string.Join('|',
            ConcatAlgorithmVersion,
            composition.NodeHash,
            string.Join(';', inputs.Select(input => input.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty)),
            includeAudio,
            normalize,
            string.Join(';', composition.Segments.Select(segment => segment.AudioEnabled)),
            request.Purpose,
            request.Profile ?? string.Empty,
            fingerprint));
        var cacheDirectory = Path.Combine(_cacheRoot, "compositions");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
        if (IsUsableCacheFile(cachePath))
            return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

        var cacheLock = _cacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsableCacheFile(cachePath))
                return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(cacheDirectory);
            var temporaryPath = Path.Combine(cacheDirectory, $".{key}.{Guid.NewGuid():N}.tmp.mp4");
            try
            {
                var arguments = normalize
                    ? FfmpegCommandBuilder.BuildNormalizedConcatArguments(
                        inputs.Select((input, index) => new NormalizedConcatInput(
                            input.Path,
                            encodings[index]?.DurationSeconds
                                ?? throw new NotSupportedException("A composition segment has no known duration for normalization."),
                            encodings[index]?.Audio is not null,
                            composition.Segments[index].AudioEnabled)).ToArray(),
                        temporaryPath,
                        CreateNormalizationProfile(encodings))
                    : FfmpegCommandBuilder.BuildCompatibleConcatArguments(
                        inputs.Select(input => input.Path).ToArray(), temporaryPath, includeAudio);
                var result = await _runner.RunAsync(
                        new ExternalProcessRequest(ffmpegPath, arguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
                if (!IsUsableCacheFile(temporaryPath))
                    throw new InvalidDataException("FFmpeg completed without producing the composition preview.");
                try
                {
                    File.Move(temporaryPath, cachePath, overwrite: false);
                }
                catch (IOException) when (IsUsableCacheFile(cachePath))
                {
                    File.Delete(temporaryPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static NormalizedConcatProfile CreateNormalizationProfile(
        IReadOnlyList<MediaEncodingMetadata?> encodings)
    {
        if (encodings.Any(encoding => !CanNormalize(encoding)))
            throw new NotSupportedException(
                "Composition normalization requires a known duration, width, height, and frame rate for every segment.");
        var width = encodings.Max(encoding => encoding!.Video!.Width!.Value);
        var height = encodings.Max(encoding => encoding!.Video!.Height!.Value);
        var frameRate = encodings.Select(encoding => ParseFrameRate(encoding!.Video!.FrameRate!))
            .FirstOrDefault(value => value > 0);
        if (frameRate <= 0)
            throw new NotSupportedException("Composition normalization requires a valid frame rate.");
        return new NormalizedConcatProfile(width, height, frameRate);
    }

    private static bool CanNormalize(MediaEncodingMetadata? encoding) =>
        encoding is
        {
            DurationSeconds: > 0,
            Video.Width: > 0,
            Video.Height: > 0,
            Video.FrameRate: not null
        } && ParseFrameRate(encoding.Video.FrameRate) > 0;

    private static double ParseFrameRate(string value)
    {
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0)
            return numerator / denominator;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private Task<MaterializedMediaLease> MaterializeExtractFrameNodeAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        ExtractFrameRenderPlanNode frame,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        if (frame.Source is not PhysicalSourceRenderPlanNode physical)
            throw new NotSupportedException(
                "Extracting a frame from virtual video requires Phase 2D time mapping.");
        return _physicalMaterializer.MaterializeAsync(
            project,
            location,
            request with
            {
                Target = new AnchorMaterializationTarget(
                    frame.Anchor.AnchorId,
                    frame.Anchor.AnchorRevisionId),
                Profile = frame.ImageProfile ?? request.Profile
            },
            cancellationToken);
    }

    private async Task<MaterializedMediaLease> RenderTrimAsync(
        VideoProject project,
        ProjectAsset outputAsset,
        Guid sourceAssetId,
        string nodeHash,
        RecipeBoundary start,
        RecipeBoundary end,
        MaterializedMediaLease source,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var sourceAsset = project.Assets.Single(candidate => candidate.Id == sourceAssetId);
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or use Saved Clips.");
        var fingerprint = await GetRendererFingerprintAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
        var key = HashText(string.Join('|',
            TrimAlgorithmVersion,
            nodeHash,
            source.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty,
            request.Purpose.ToString(),
            request.Profile ?? string.Empty,
            fingerprint));
        var cacheDirectory = Path.Combine(_cacheRoot, "clips");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
        if (IsUsableCacheFile(cachePath))
            return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

        var cacheLock = _cacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsableCacheFile(cachePath))
                return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
            var durationSeconds = source.Encoding?.DurationSeconds ??
                                  sourceAsset.DurationSeconds ??
                                  sourceAsset.Encoding?.DurationSeconds;
            var startSeconds = await ResolveBoundarySecondsAsync(
                project, sourceAsset, source.Path, start, durationSeconds, isEnd: false, cancellationToken).ConfigureAwait(false);
            var endSeconds = await ResolveBoundarySecondsAsync(
                project, sourceAsset, source.Path, end, durationSeconds, isEnd: true, cancellationToken).ConfigureAwait(false);
            if (startSeconds < 0 || endSeconds <= startSeconds)
                throw new InvalidDataException("The Saved Clip recipe resolves to an empty or invalid source range.");
            Directory.CreateDirectory(cacheDirectory);
            var temporaryPath = Path.Combine(cacheDirectory, $".{key}.{Guid.NewGuid():N}.tmp.mp4");
            try
            {
                var arguments = FfmpegCommandBuilder.BuildFrameAccurateTrimArguments(
                    source.Path, temporaryPath, startSeconds, endSeconds);
                var result = await _runner.RunAsync(
                        new ExternalProcessRequest(ffmpegPath, arguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
                if (!IsUsableCacheFile(temporaryPath))
                    throw new InvalidDataException("FFmpeg completed without producing the Saved Clip preview.");
                try
                {
                    File.Move(temporaryPath, cachePath, overwrite: false);
                }
                catch (IOException) when (IsUsableCacheFile(cachePath))
                {
                    File.Delete(temporaryPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<double> ResolveBoundarySecondsAsync(
        VideoProject project,
        ProjectAsset sourceAsset,
        string sourcePath,
        RecipeBoundary boundary,
        double? sourceDurationSeconds,
        bool isEnd,
        CancellationToken cancellationToken)
    {
        if (boundary.Kind == RecipeBoundaryKind.SourceStart) return 0;
        if (boundary.Kind == RecipeBoundaryKind.SourceEnd)
            return ResolveSourceDuration(sourceAsset, sourceDurationSeconds);
        if (boundary.Kind == RecipeBoundaryKind.Timestamp && boundary.TimestampSeconds is { } timestamp)
            return timestamp;
        if (boundary.Kind != RecipeBoundaryKind.Anchor || boundary.Anchor is null || boundary.Edge is null)
            throw new InvalidDataException("The Saved Clip contains an incomplete boundary.");
        if (sourceAsset.StorageKind != AssetStorageKind.Physical)
            throw new NotSupportedException(
                "Anchor boundaries on virtual video require Phase 2D time mapping.");

        var anchorRevision = project.AnchorRevisions.SingleOrDefault(candidate =>
                candidate.Id == boundary.Anchor.AnchorRevisionId && candidate.AnchorId == boundary.Anchor.AnchorId)
            ?? throw new InvalidOperationException(
                $"Clip boundary revision '{boundary.Anchor.AnchorRevisionId}' no longer exists.");
        if (anchorRevision.SourceAssetId != sourceAsset.Id)
            throw new InvalidDataException("A Saved Clip boundary points at a different source asset.");
        if (!string.Equals(
                anchorRevision.SourceContentHash,
                sourceAsset.Physical?.ContentIdentity.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Saved Clip boundary no longer matches its source content.");

        if (boundary.Edge == AnchorBoundaryEdge.BeforeFrame) return anchorRevision.TimestampSeconds;
        var nearbyFrames = await _exactFrameService.IndexWindowAsync(
                sourcePath,
                Math.Max(0, anchorRevision.TimestampSeconds),
                radiusSeconds: 2,
                cancellationToken)
            .ConfigureAwait(false);
        var next = nearbyFrames
            .Where(frame => frame.VideoStreamIndex == anchorRevision.VideoStreamIndex &&
                            frame.PresentationTimestamp > anchorRevision.PresentationTimestamp)
            .OrderBy(frame => frame.PresentationTimestamp)
            .FirstOrDefault();
        if (next is not null) return next.TimestampSeconds;
        if (isEnd) return ResolveSourceDuration(sourceAsset, sourceDurationSeconds);
        throw new InvalidDataException("The frame following the Saved Clip start could not be resolved.");
    }

    private static double ResolveSourceDuration(ProjectAsset sourceAsset, double? materializedDurationSeconds) =>
        materializedDurationSeconds ?? sourceAsset.DurationSeconds ?? sourceAsset.Encoding?.DurationSeconds ??
        sourceAsset.Virtual?.ExpectedMediaProperties?.DurationSeconds
        ?? throw new InvalidDataException("The source duration is required to resolve the end of this Saved Clip.");

    private async Task<string> GetRendererFingerprintAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (_rendererFingerprint is not null) return _rendererFingerprint;
        await _fingerprintLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_rendererFingerprint is not null) return _rendererFingerprint;
            var result = await _runner.RunAsync(
                    new ExternalProcessRequest(ffmpegPath, ["-version"]),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
            var versionLine = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "unknown-ffmpeg-version";
            _rendererFingerprint = HashText($"{TrimAlgorithmVersion}|{versionLine}");
            return _rendererFingerprint;
        }
        finally
        {
            _fingerprintLock.Release();
        }
    }

    private async Task<MaterializedMediaLease> OpenCacheLeaseAsync(
        string cachePath,
        ProjectAsset asset,
        CancellationToken cancellationToken)
    {
        var normalizedPath = MediaCacheLeaseRegistry.Acquire(cachePath);
        try
        {
            File.SetLastWriteTimeUtc(normalizedPath, DateTime.UtcNow);
            var identity = await _contentHashService.ComputeAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            return new MaterializedMediaLease(
                normalizedPath,
                identity,
                asset.Virtual?.ExpectedMediaProperties,
                isDurableSource: false,
                release: () =>
                {
                    MediaCacheLeaseRegistry.Release(normalizedPath);
                    return ValueTask.CompletedTask;
                });
        }
        catch
        {
            MediaCacheLeaseRegistry.Release(normalizedPath);
            throw;
        }
    }

    private static bool IsUsableCacheFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fingerprintLock.Dispose();
        foreach (var cacheLock in _cacheLocks.Values) cacheLock.Dispose();
        _cacheLocks.Clear();
    }
}
