using System.Globalization;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class RecipeMediaMaterializer : IMediaMaterializer, ICompositionSegmentMaterializer, IDisposable
{
    private const string ConcatAlgorithmVersion = "composition-concat-v2";
    private readonly PhysicalAssetMaterializer _physicalMaterializer;
    private readonly IExactVideoFrameService _exactFrameService;
    private readonly IExternalProcessRunner _runner;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService? _mediaInspector;
    private readonly MediaRenderCache _renderCache;
    private readonly ModifiedMediaRetentionService _retentionService;
    private readonly SavedClipTrimRenderer _trimRenderer;
    private readonly CompositionAuditionAudioRenderer _auditionAudioRenderer;
    private readonly CompositionAudioRenderer _compositionAudioRenderer;
    private readonly FfmpegRendererFingerprintProvider _fingerprintProvider;
    private string? _ffmpegPath;
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
        _fingerprintProvider = new FfmpegRendererFingerprintProvider(runner, SavedClipTrimRenderer.AlgorithmVersion);
        _exactFrameService = exactFrameService;
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
        _mediaInspector = mediaInspector;
        _renderCache = new MediaRenderCache(cacheRoot, _contentHashService, mediaInspector);
        _retentionService = new ModifiedMediaRetentionService(
            _renderCache, _contentHashService, persistModifiedMediaOnDisk);
        var boundaryResolver = new RecipeBoundaryResolver(exactFrameService);
        _trimRenderer = new SavedClipTrimRenderer(
            runner, _renderCache, _fingerprintProvider, boundaryResolver);
        _auditionAudioRenderer = new CompositionAuditionAudioRenderer(
            runner, _renderCache, _fingerprintProvider, mediaInspector);
        _compositionAudioRenderer = new CompositionAudioRenderer(
            runner, _renderCache, _fingerprintProvider, mediaInspector);
        _physicalMaterializer = new PhysicalAssetMaterializer(_contentHashService, exactFrameService);
    }

    public void UpdateExecutablePath(string? ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        _fingerprintProvider.Reset();
    }

    public void UpdatePersistencePreference(bool persistModifiedMediaOnDisk) =>
        _retentionService.UpdatePreference(persistModifiedMediaOnDisk);

    public async Task<MaterializedMediaLease> MaterializeAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Target is AnchorMaterializationTarget anchorTarget)
        {
            var anchorMedia = await MaterializeAnchorAsync(
                    project, location, anchorTarget, request.Purpose, request.Profile, cancellationToken)
                .ConfigureAwait(false);
            return await _retentionService.PersistIfRequestedAsync(
                    project, location, request.Target, anchorMedia, cancellationToken)
                .ConfigureAwait(false);
        }
        if (request.Target is not AssetMaterializationTarget assetTarget)
            throw new NotSupportedException(
                $"Materialization target '{request.Target.GetType().Name}' is not supported.");

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
        return await _retentionService.PersistIfRequestedAsync(
                project, location, request.Target, media, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MaterializedMediaLease?> MaterializeCompositionAuditionAudioAsync(
        VideoProject project,
        ProjectLocation location,
        Guid compositionAssetId,
        Guid recipeRevisionId,
        double compositionDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        if (!double.IsFinite(compositionDurationSeconds) || compositionDurationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(compositionDurationSeconds));
        if (project.Assets.All(asset => asset.Id != compositionAssetId))
            throw new InvalidOperationException($"Composition asset '{compositionAssetId}' no longer exists.");
        var plan = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(compositionAssetId, recipeRevisionId),
            MaterializationPurpose.Preview,
            "audio-only-audition");
        if (plan.Root is not CompositionRenderPlanNode composition)
            throw new InvalidDataException("Audio audition requires a composition recipe.");
        return await _auditionAudioRenderer.RenderAsync(
                _ffmpegPath,
                project,
                location,
                composition,
                compositionDurationSeconds,
                ExecuteNodeAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MaterializedMediaLease> MaterializeSegmentAsync(
        VideoProject project,
        ProjectLocation location,
        Guid compositionAssetId,
        Guid recipeRevisionId,
        Guid segmentId,
        MaterializationPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(location);
        var outputAsset = project.Assets.SingleOrDefault(asset => asset.Id == compositionAssetId)
            ?? throw new InvalidOperationException($"Composition asset '{compositionAssetId}' no longer exists.");
        var plan = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(compositionAssetId, recipeRevisionId),
            purpose,
            "single-composition-segment");
        if (plan.Root is not CompositionRenderPlanNode composition)
            throw new InvalidDataException("Segment materialization requires a composition recipe.");
        var segment = composition.Segments.SingleOrDefault(candidate => candidate.SegmentId == segmentId)
            ?? throw new InvalidOperationException("The selected composition segment no longer exists.");
        var request = new MaterializationRequest(
            new AssetMaterializationTarget(compositionAssetId, recipeRevisionId),
            purpose,
            Profile: "single-composition-segment");
        return await MaterializeCompositionSegmentAsync(
                project, location, outputAsset, segment, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MaterializedMediaLease> MaterializeAnchorAsync(
        VideoProject project,
        ProjectLocation location,
        AnchorMaterializationTarget target,
        MaterializationPurpose purpose,
        string? profile,
        CancellationToken cancellationToken)
    {
        var revision = project.AnchorRevisions.SingleOrDefault(candidate =>
                candidate.Id == target.AnchorRevisionId && candidate.AnchorId == target.AnchorId)
            ?? throw new InvalidOperationException($"Frame anchor revision '{target.AnchorRevisionId}' no longer exists.");
        var sourceAsset = project.Assets.SingleOrDefault(candidate => candidate.Id == revision.SourceAssetId)
            ?? throw new InvalidOperationException($"Anchor source asset '{revision.SourceAssetId}' no longer exists.");
        if (sourceAsset.StorageKind == AssetStorageKind.Physical)
            return await _physicalMaterializer.MaterializeAsync(
                    project,
                    location,
                    new MaterializationRequest(target, purpose, Profile: profile),
                    cancellationToken)
                .ConfigureAwait(false);
        if (revision.SourceRecipeRevisionId is not { } sourceRevisionId)
            throw new InvalidDataException("An exact position in virtual media is missing its pinned source revision.");

        await using var source = await MaterializeAsync(
                project,
                location,
                new MaterializationRequest(
                    new AssetMaterializationTarget(sourceAsset.Id, sourceRevisionId),
                    MaterializationPurpose.FrameExtraction),
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                source.ContentIdentity.Sha256,
                revision.SourceContentHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The materialized virtual source no longer matches the content identity pinned by the exact position.");
        return await _exactFrameService.ExtractAsync(
                source.Path,
                revision.SourceContentHash,
                revision,
                purpose,
                profile,
                cancellationToken)
            .ConfigureAwait(false);
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
        var sourceReference = RecipeBoundaryResolver.GetAssetRevisionReference(trim.Source);
        var sourceRequest = RecipeBoundaryResolver.GetBoundarySourceRequest(
            project, sourceReference, trim.Start, trim.End, request);
        await using var source = await ExecuteNodeAsync(
                project,
                location,
                project.Assets.Single(asset => asset.Id == trim.Source.AssetId),
                trim.Source,
                sourceRequest,
                cancellationToken)
            .ConfigureAwait(false);
        return await _trimRenderer.RenderAsync(
                _ffmpegPath, project, outputAsset, sourceReference, trim.NodeHash, trim.Start, trim.End,
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

            return await _compositionAudioRenderer.RenderOverlayAsync(
                    _ffmpegPath,
                    project,
                    location,
                    outputAsset,
                    composition,
                    video,
                    request,
                    ExecuteNodeAsync,
                    cancellationToken)
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
        {
            var media = await MaterializeCompositionSegmentAsync(
                    project, location, outputAsset, segment, request, cancellationToken)
                .ConfigureAwait(false);
            if (segment.AudioEnabled) return media;
            try
            {
                return await _compositionAudioRenderer.RenderWithoutSourceAudioAsync(
                        _ffmpegPath, outputAsset, segment, media, request, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await media.DisposeAsync().ConfigureAwait(false);
            }
        }

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

        var sourceReference = RecipeBoundaryResolver.GetAssetRevisionReference(segment.Source);
        var sourceRequest = RecipeBoundaryResolver.GetBoundarySourceRequest(
            project, sourceReference, segment.Start, segment.End, request);
        await using var source = await ExecuteNodeAsync(
                project, location, sourceAsset, segment.Source, sourceRequest, cancellationToken)
            .ConfigureAwait(false);
        return await _trimRenderer.RenderAsync(
                _ffmpegPath, project, outputAsset, sourceReference, segment.SegmentHash,
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
        var fingerprint = await _fingerprintProvider.GetAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
        var key = MediaRenderCache.HashText(string.Join('|',
            ConcatAlgorithmVersion,
            composition.NodeHash,
            string.Join(';', inputs.Select(input => input.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty)),
            includeAudio,
            normalize,
            string.Join(';', composition.Segments.Select(segment => segment.AudioEnabled)),
            request.Purpose,
            request.Profile ?? string.Empty,
            fingerprint));
        var cacheDirectory = Path.Combine(_renderCache.RootDirectory, "compositions");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
        if (MediaRenderCache.IsUsableFile(cachePath))
            return await OpenCacheLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

        var cacheLock = _renderCache.GetLock(key);
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (MediaRenderCache.IsUsableFile(cachePath))
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
                if (!MediaRenderCache.IsUsableFile(temporaryPath))
                    throw new InvalidDataException("FFmpeg completed without producing the composition preview.");
                try
                {
                    File.Move(temporaryPath, cachePath, overwrite: false);
                }
                catch (IOException) when (MediaRenderCache.IsUsableFile(cachePath))
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

    private async Task<MaterializedMediaLease> OpenCacheLeaseAsync(
        string cachePath,
        ProjectAsset asset,
        CancellationToken cancellationToken) =>
        await _renderCache.OpenLeaseAsync(cachePath, asset.Virtual?.ExpectedMediaProperties, cancellationToken)
            .ConfigureAwait(false);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fingerprintProvider.Dispose();
        _renderCache.Dispose();
    }
}
