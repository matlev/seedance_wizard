using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class RecipeMediaMaterializer : IMediaMaterializer, ICompositionSegmentMaterializer, IDisposable
{
    private readonly PhysicalAssetMaterializer _physicalMaterializer;
    private readonly IExactVideoFrameService _exactFrameService;
    private readonly IContentHashService _contentHashService;
    private readonly MediaRenderCache _renderCache;
    private readonly ModifiedMediaRetentionService _retentionService;
    private readonly SavedClipTrimRenderer _trimRenderer;
    private readonly CompositionAuditionAudioRenderer _auditionAudioRenderer;
    private readonly CompositionAudioRenderer _compositionAudioRenderer;
    private readonly CompositionVideoRenderer _compositionVideoRenderer;
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
        _fingerprintProvider = new FfmpegRendererFingerprintProvider(runner, SavedClipTrimRenderer.AlgorithmVersion);
        _exactFrameService = exactFrameService;
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
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
        _compositionVideoRenderer = new CompositionVideoRenderer(
            runner, _renderCache, _fingerprintProvider, _compositionAudioRenderer, mediaInspector);
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
            video = await _compositionVideoRenderer.RenderAsync(
                    _ffmpegPath,
                    outputAsset,
                    composition,
                    request,
                    (segment, token) => MaterializeCompositionSegmentAsync(
                        project, location, outputAsset, segment, request, token),
                    cancellationToken)
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fingerprintProvider.Dispose();
        _renderCache.Dispose();
    }
}
