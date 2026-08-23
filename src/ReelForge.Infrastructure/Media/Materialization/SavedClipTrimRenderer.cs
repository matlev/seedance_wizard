using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class SavedClipTrimRenderer
{
    public const string AlgorithmVersion = "saved-clip-trim-v1";

    private readonly IExternalProcessRunner _runner;
    private readonly MediaRenderCache _renderCache;
    private readonly FfmpegRendererFingerprintProvider _fingerprintProvider;
    private readonly RecipeBoundaryResolver _boundaryResolver;

    public SavedClipTrimRenderer(
        IExternalProcessRunner runner,
        MediaRenderCache renderCache,
        FfmpegRendererFingerprintProvider fingerprintProvider,
        RecipeBoundaryResolver boundaryResolver)
    {
        _runner = runner;
        _renderCache = renderCache;
        _fingerprintProvider = fingerprintProvider;
        _boundaryResolver = boundaryResolver;
    }

    public async Task<MaterializedMediaLease> RenderAsync(
        string? ffmpegPath,
        VideoProject project,
        ProjectAsset outputAsset,
        AssetRevisionReference sourceReference,
        string nodeHash,
        RecipeBoundary start,
        RecipeBoundary end,
        MaterializedMediaLease source,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var sourceAsset = project.Assets.Single(candidate => candidate.Id == sourceReference.AssetId);
        var executablePath = ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or use Saved Clips.");
        var fingerprint = await _fingerprintProvider.GetAsync(executablePath, cancellationToken).ConfigureAwait(false);
        var key = MediaRenderCache.HashText(string.Join('|',
            AlgorithmVersion,
            nodeHash,
            source.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty,
            request.Purpose.ToString(),
            request.Profile ?? string.Empty,
            fingerprint));
        var cacheDirectory = Path.Combine(_renderCache.RootDirectory, "clips");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
        if (MediaRenderCache.IsUsableFile(cachePath))
            return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

        var cacheLock = _renderCache.GetLock(key);
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (MediaRenderCache.IsUsableFile(cachePath))
                return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
            var durationSeconds = source.Encoding?.DurationSeconds ??
                                  sourceAsset.DurationSeconds ??
                                  sourceAsset.Encoding?.DurationSeconds;
            var startSeconds = await _boundaryResolver.ResolveSecondsAsync(
                project, sourceReference, sourceAsset, source, start, durationSeconds, isEnd: false, cancellationToken)
                .ConfigureAwait(false);
            var endSeconds = await _boundaryResolver.ResolveSecondsAsync(
                project, sourceReference, sourceAsset, source, end, durationSeconds, isEnd: true, cancellationToken)
                .ConfigureAwait(false);
            if (startSeconds < 0 || endSeconds <= startSeconds)
                throw new InvalidDataException("The Saved Clip recipe resolves to an empty or invalid source range.");

            Directory.CreateDirectory(cacheDirectory);
            using var fileCommit = AtomicFileCommit.Create(cachePath, "trim-render", ".mp4");
            var arguments = FfmpegCommandBuilder.BuildFrameAccurateTrimArguments(
                source.Path, fileCommit.TemporaryPath, startSeconds, endSeconds);
            var result = await _runner.RunAsync(
                    new ExternalProcessRequest(executablePath, arguments),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded) throw new ExternalProcessException(executablePath, result);
            if (!MediaRenderCache.IsUsableFile(fileCommit.TemporaryPath))
                throw new InvalidDataException("FFmpeg completed without producing the Saved Clip preview.");
            try
            {
                fileCommit.Commit();
            }
            catch (IOException) when (MediaRenderCache.IsUsableFile(cachePath))
            {
                // Another process completed the deterministic render first.
            }

            return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private Task<MaterializedMediaLease> OpenLeaseAsync(
        string cachePath,
        ProjectAsset outputAsset,
        CancellationToken cancellationToken) =>
        _renderCache.OpenLeaseAsync(
            cachePath,
            outputAsset.Virtual?.ExpectedMediaProperties,
            cancellationToken);
}
