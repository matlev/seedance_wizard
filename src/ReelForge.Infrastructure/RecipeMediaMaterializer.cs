using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class RecipeMediaMaterializer : IMediaMaterializer, IDisposable
{
    private const string TrimAlgorithmVersion = "saved-clip-trim-v1";
    private readonly PhysicalAssetMaterializer _physicalMaterializer;
    private readonly IExactVideoFrameService _exactFrameService;
    private readonly IExternalProcessRunner _runner;
    private readonly IContentHashService _contentHashService;
    private readonly string _cacheRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _fingerprintLock = new(1, 1);
    private string? _ffmpegPath;
    private string? _rendererFingerprint;
    private bool _disposed;

    public RecipeMediaMaterializer(
        string? ffmpegPath,
        IExternalProcessRunner runner,
        IExactVideoFrameService exactFrameService,
        string cacheRoot,
        IContentHashService? contentHashService = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(exactFrameService);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _ffmpegPath = ffmpegPath;
        _runner = runner;
        _exactFrameService = exactFrameService;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
        _physicalMaterializer = new PhysicalAssetMaterializer(_contentHashService, exactFrameService);
    }

    public void UpdateExecutablePath(string? ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
        _rendererFingerprint = null;
    }

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
            return await _physicalMaterializer.MaterializeAsync(project, location, request, cancellationToken)
                .ConfigureAwait(false);
        }

        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == assetTarget.AssetId)
            ?? throw new InvalidOperationException($"Asset '{assetTarget.AssetId}' no longer exists.");
        if (asset.StorageKind == AssetStorageKind.Physical)
        {
            return await _physicalMaterializer.MaterializeAsync(project, location, request, cancellationToken)
                .ConfigureAwait(false);
        }

        return await MaterializeTrimAsync(project, location, asset, assetTarget, request, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<MaterializedMediaLease> MaterializeTrimAsync(
        VideoProject project,
        ProjectLocation location,
        ProjectAsset asset,
        AssetMaterializationTarget target,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        if (asset.Virtual is null)
            throw new InvalidDataException($"Virtual asset '{asset.EffectiveDisplayName}' has no virtual state.");
        var revisionId = target.RecipeRevisionId ?? asset.Virtual.CurrentRecipeRevisionId
            ?? throw new InvalidOperationException($"Virtual asset '{asset.EffectiveDisplayName}' has no committed recipe.");
        var revision = project.RecipeRevisions.SingleOrDefault(candidate =>
                candidate.Id == revisionId && candidate.VirtualAssetId == asset.Id)
            ?? throw new InvalidOperationException($"Recipe revision '{revisionId}' no longer exists.");
        if (revision.Recipe is CompositionRecipe composition)
        {
            return await MaterializeInitialCompositionAsync(
                project, location, composition, request, cancellationToken).ConfigureAwait(false);
        }
        if (revision.Recipe is not TrimRecipe trim)
            throw new NotSupportedException(
                $"Recipe '{revision.Recipe.GetType().Name}' is not part of the current Saved Clip materialization slice.");
        if (trim.Source.RecipeRevisionId is not null)
            throw new NotSupportedException("Saved Clips of virtual sources are not supported in the current materialization slice.");

        var sourceAsset = project.Assets.SingleOrDefault(candidate => candidate.Id == trim.Source.AssetId)
            ?? throw new InvalidOperationException($"Clip source '{trim.Source.AssetId}' no longer exists.");
        await using var source = await _physicalMaterializer.MaterializeAsync(
                project,
                location,
                new MaterializationRequest(
                    new AssetMaterializationTarget(sourceAsset.Id),
                    request.Purpose,
                    request.RetentionPreference,
                    request.Profile),
                cancellationToken)
            .ConfigureAwait(false);
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or use Saved Clips.");
        var fingerprint = await GetRendererFingerprintAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
        var key = HashText(string.Join('|',
            TrimAlgorithmVersion,
            revision.Id.ToString("N"),
            source.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty,
            request.Purpose.ToString(),
            request.Profile ?? string.Empty,
            fingerprint));
        var cacheDirectory = Path.Combine(_cacheRoot, "clips");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
        if (IsUsableCacheFile(cachePath))
            return await OpenCacheLeaseAsync(cachePath, asset, cancellationToken).ConfigureAwait(false);

        var cacheLock = _cacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsableCacheFile(cachePath))
                return await OpenCacheLeaseAsync(cachePath, asset, cancellationToken).ConfigureAwait(false);
            var startSeconds = await ResolveBoundarySecondsAsync(
                project, sourceAsset, source.Path, trim.Start, isEnd: false, cancellationToken).ConfigureAwait(false);
            var endSeconds = await ResolveBoundarySecondsAsync(
                project, sourceAsset, source.Path, trim.End, isEnd: true, cancellationToken).ConfigureAwait(false);
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

            return await OpenCacheLeaseAsync(cachePath, asset, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private Task<MaterializedMediaLease> MaterializeInitialCompositionAsync(
        VideoProject project,
        ProjectLocation location,
        CompositionRecipe composition,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        if (composition.Segments is not [var segment] ||
            segment.Start.Kind != RecipeBoundaryKind.SourceStart ||
            segment.End.Kind != RecipeBoundaryKind.SourceEnd)
            throw new NotSupportedException(
                "Composition rendering is not part of this milestone. The initial one-source Working Composition can be previewed directly.");
        return MaterializeAsync(
            project,
            location,
            request with
            {
                Target = new AssetMaterializationTarget(
                    segment.Source.AssetId,
                    segment.Source.RecipeRevisionId)
            },
            cancellationToken);
    }

    private async Task<double> ResolveBoundarySecondsAsync(
        VideoProject project,
        ProjectAsset sourceAsset,
        string sourcePath,
        RecipeBoundary boundary,
        bool isEnd,
        CancellationToken cancellationToken)
    {
        if (boundary.Kind == RecipeBoundaryKind.SourceStart) return 0;
        if (boundary.Kind == RecipeBoundaryKind.SourceEnd)
            return ResolveSourceDuration(sourceAsset);
        if (boundary.Kind == RecipeBoundaryKind.Timestamp && boundary.TimestampSeconds is { } timestamp)
            return timestamp;
        if (boundary.Kind != RecipeBoundaryKind.Anchor || boundary.Anchor is null || boundary.Edge is null)
            throw new InvalidDataException("The Saved Clip contains an incomplete boundary.");

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
        if (isEnd) return ResolveSourceDuration(sourceAsset);
        throw new InvalidDataException("The frame following the Saved Clip start could not be resolved.");
    }

    private static double ResolveSourceDuration(ProjectAsset sourceAsset) =>
        sourceAsset.DurationSeconds ?? sourceAsset.Encoding?.DurationSeconds
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
