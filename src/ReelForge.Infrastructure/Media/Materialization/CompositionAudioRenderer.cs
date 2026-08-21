using System.Globalization;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class CompositionAudioRenderer
{
    private const string AudioOverlayAlgorithmVersion = "composition-audio-overlay-v3";
    private const string SourceAudioAlgorithmVersion = "composition-source-audio-v1";

    private readonly IExternalProcessRunner _runner;
    private readonly MediaRenderCache _renderCache;
    private readonly FfmpegRendererFingerprintProvider _fingerprintProvider;
    private readonly IMediaInspectionService? _mediaInspector;

    public CompositionAudioRenderer(
        IExternalProcessRunner runner,
        MediaRenderCache renderCache,
        FfmpegRendererFingerprintProvider fingerprintProvider,
        IMediaInspectionService? mediaInspector)
    {
        _runner = runner;
        _renderCache = renderCache;
        _fingerprintProvider = fingerprintProvider;
        _mediaInspector = mediaInspector;
    }

    public async Task<MaterializedMediaLease> RenderOverlayAsync(
        string? ffmpegPath,
        VideoProject project,
        ProjectLocation location,
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        MaterializedMediaLease video,
        MaterializationRequest request,
        Func<
            VideoProject,
            ProjectLocation,
            ProjectAsset,
            MediaRenderPlanNode,
            MaterializationRequest,
            CancellationToken,
            Task<MaterializedMediaLease>> materializeNodeAsync,
        CancellationToken cancellationToken)
    {
        var audioLeases = new List<MaterializedMediaLease>();
        var audioDurations = new List<double?>();
        try
        {
            foreach (var clip in composition.AudioClips)
            {
                var sourceAsset = project.Assets.Single(asset => asset.Id == clip.Source.AssetId);
                var lease = await materializeNodeAsync(
                        project, location, sourceAsset, clip.Source, request, cancellationToken)
                    .ConfigureAwait(false);
                audioLeases.Add(lease);
                var audioEncoding = lease.Encoding ?? sourceAsset.Encoding ?? sourceAsset.Virtual?.ExpectedMediaProperties;
                if ((clip.FadeInMilliseconds > 0 || clip.FadeOutMilliseconds > 0) &&
                    audioEncoding?.DurationSeconds is not > 0 && _mediaInspector is not null)
                    audioEncoding = await _mediaInspector.InspectAsync(lease.Path, cancellationToken).ConfigureAwait(false);
                audioDurations.Add(audioEncoding?.DurationSeconds ?? sourceAsset.DurationSeconds);
            }

            var videoEncoding = video.Encoding;
            if (_mediaInspector is not null &&
                (videoEncoding?.Audio is null || videoEncoding.DurationSeconds is not > 0))
                videoEncoding = await _mediaInspector.InspectAsync(video.Path, cancellationToken).ConfigureAwait(false);
            var executablePath = ffmpegPath ?? throw new MediaToolUnavailableException(
                "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or export compositions.");
            var fingerprint = await _fingerprintProvider.GetAsync(executablePath, cancellationToken)
                .ConfigureAwait(false);
            var key = MediaRenderCache.HashText(string.Join('|',
                AudioOverlayAlgorithmVersion,
                composition.NodeHash,
                video.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty,
                string.Join(';', audioLeases.Select(lease =>
                    lease.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty)),
                string.Join(';', composition.AudioClips.Select(clip => string.Join(',',
                    clip.TimelineStartTicks,
                    clip.IsMuted,
                    clip.GainDecibels.ToString("R", CultureInfo.InvariantCulture),
                    clip.Pan.ToString("R", CultureInfo.InvariantCulture),
                    clip.FadeInMilliseconds,
                    clip.FadeOutMilliseconds))),
                videoEncoding?.Audio is not null,
                request.Purpose,
                request.Profile ?? string.Empty,
                fingerprint));
            var cacheDirectory = Path.Combine(_renderCache.RootDirectory, "compositions");
            var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
            if (MediaRenderCache.IsUsableFile(cachePath))
                return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

            var cacheLock = _renderCache.GetLock(key);
            await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (MediaRenderCache.IsUsableFile(cachePath))
                    return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
                Directory.CreateDirectory(cacheDirectory);
                using var fileCommit = AtomicFileCommit.Create(cachePath, "composition-audio", ".mp4");
                var arguments = FfmpegCommandBuilder.BuildAudioOverlayArguments(
                    video.Path,
                    videoEncoding?.Audio is not null,
                    audioLeases.Select((lease, index) =>
                    {
                        var clip = composition.AudioClips[index];
                        var timelineStart = TimeSpan.FromTicks(clip.TimelineStartTicks);
                        var audibleDuration = audioDurations[index];
                        if (videoEncoding?.DurationSeconds is > 0 and var videoDuration)
                        {
                            var remainingVideo = Math.Max(0, videoDuration - timelineStart.TotalSeconds);
                            audibleDuration = audibleDuration is > 0
                                ? Math.Min(audibleDuration.Value, remainingVideo)
                                : remainingVideo;
                        }

                        return new AudioOverlayInput(
                            lease.Path,
                            timelineStart,
                            clip.IsMuted,
                            clip.GainDecibels,
                            clip.Pan,
                            ClampFade(clip.FadeInMilliseconds, audibleDuration),
                            ClampFade(clip.FadeOutMilliseconds, audibleDuration),
                            audibleDuration);
                    }).ToArray(),
                    fileCommit.TemporaryPath);
                var result = await _runner.RunAsync(
                        new ExternalProcessRequest(executablePath, arguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded) throw new ExternalProcessException(executablePath, result);
                if (!MediaRenderCache.IsUsableFile(fileCommit.TemporaryPath))
                    throw new InvalidDataException("FFmpeg completed without producing the composition audio mix.");
                CommitDeterministicRender(fileCommit, cachePath);
                return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
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

    public async Task<MaterializedMediaLease> RenderWithoutSourceAudioAsync(
        string? ffmpegPath,
        ProjectAsset outputAsset,
        CompositionSegmentRenderPlan segment,
        MaterializedMediaLease input,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var executablePath = ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or export compositions.");
        var fingerprint = await _fingerprintProvider.GetAsync(executablePath, cancellationToken)
            .ConfigureAwait(false);
        var key = MediaRenderCache.HashText(string.Join('|',
            SourceAudioAlgorithmVersion,
            segment.SegmentHash,
            input.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty,
            request.Purpose,
            request.Profile ?? string.Empty,
            fingerprint));
        var cacheDirectory = Path.Combine(_renderCache.RootDirectory, "compositions");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.mp4");
        if (MediaRenderCache.IsUsableFile(cachePath))
            return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);

        var cacheLock = _renderCache.GetLock(key);
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (MediaRenderCache.IsUsableFile(cachePath))
                return await OpenLeaseAsync(cachePath, outputAsset, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(cacheDirectory);
            using var fileCommit = AtomicFileCommit.Create(cachePath, "muted-composition", ".mp4");
            var arguments = FfmpegCommandBuilder.BuildVideoWithoutAudioArguments(
                input.Path, fileCommit.TemporaryPath);
            var result = await _runner.RunAsync(
                    new ExternalProcessRequest(executablePath, arguments),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded) throw new ExternalProcessException(executablePath, result);
            if (!MediaRenderCache.IsUsableFile(fileCommit.TemporaryPath))
                throw new InvalidDataException("FFmpeg completed without producing the muted composition preview.");
            CommitDeterministicRender(fileCommit, cachePath);
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
        _renderCache.OpenLeaseAsync(cachePath, outputAsset.Virtual?.ExpectedMediaProperties, cancellationToken);

    private static void CommitDeterministicRender(AtomicFileCommit fileCommit, string cachePath)
    {
        try
        {
            fileCommit.Commit();
        }
        catch (IOException) when (MediaRenderCache.IsUsableFile(cachePath))
        {
            // Another process completed the deterministic render first.
        }
    }

    private static TimeSpan ClampFade(long milliseconds, double? audibleDurationSeconds)
    {
        if (milliseconds <= 0) return TimeSpan.Zero;
        var requested = TimeSpan.FromMilliseconds(milliseconds);
        return audibleDurationSeconds is { } duration
            ? TimeSpan.FromSeconds(Math.Min(requested.TotalSeconds, Math.Max(0, duration)))
            : requested;
    }
}
