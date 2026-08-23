using System.Globalization;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class CompositionAuditionAudioRenderer
{
    private const string AlgorithmVersion = "composition-audition-audio-v1";

    private readonly IExternalProcessRunner _runner;
    private readonly MediaRenderCache _renderCache;
    private readonly FfmpegRendererFingerprintProvider _fingerprintProvider;
    private readonly IMediaInspectionService? _mediaInspector;

    public CompositionAuditionAudioRenderer(
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

    public async Task<MaterializedMediaLease?> RenderAsync(
        string? ffmpegPath,
        VideoProject project,
        ProjectLocation location,
        CompositionRenderPlanNode composition,
        double compositionDurationSeconds,
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
        var activeClips = composition.AudioClips
            .Where(clip => !clip.IsMuted &&
                           TimeSpan.FromTicks(clip.TimelineStartTicks).TotalSeconds < compositionDurationSeconds)
            .ToArray();
        if (activeClips.Length == 0) return null;

        var leases = new List<MaterializedMediaLease>();
        try
        {
            var inputs = new List<AudioOverlayInput>();
            foreach (var clip in activeClips)
            {
                var sourceAsset = project.Assets.Single(asset => asset.Id == clip.Source.AssetId);
                var request = new MaterializationRequest(
                    new AssetMaterializationTarget(clip.Source.AssetId, GetRecipeRevisionId(clip.Source)),
                    MaterializationPurpose.Preview,
                    Profile: "audio-only-audition-source");
                var lease = await materializeNodeAsync(
                        project, location, sourceAsset, clip.Source, request, cancellationToken)
                    .ConfigureAwait(false);
                leases.Add(lease);
                var encoding = lease.Encoding ?? sourceAsset.Encoding ?? sourceAsset.Virtual?.ExpectedMediaProperties;
                if ((clip.FadeInMilliseconds > 0 || clip.FadeOutMilliseconds > 0) &&
                    encoding?.DurationSeconds is not > 0 && _mediaInspector is not null)
                    encoding = await _mediaInspector.InspectAsync(lease.Path, cancellationToken).ConfigureAwait(false);
                var timelineStart = TimeSpan.FromTicks(clip.TimelineStartTicks);
                var remainingComposition = Math.Max(0, compositionDurationSeconds - timelineStart.TotalSeconds);
                var audibleDuration = encoding?.DurationSeconds ?? sourceAsset.DurationSeconds;
                audibleDuration = audibleDuration is > 0
                    ? Math.Min(audibleDuration.Value, remainingComposition)
                    : remainingComposition;
                inputs.Add(new AudioOverlayInput(
                    lease.Path,
                    timelineStart,
                    IsMuted: false,
                    clip.GainDecibels,
                    clip.Pan,
                    ClampFade(clip.FadeInMilliseconds, audibleDuration),
                    ClampFade(clip.FadeOutMilliseconds, audibleDuration),
                    audibleDuration));
            }

            var executablePath = ffmpegPath ?? throw new MediaToolUnavailableException(
                "FFmpeg is not configured. Configure it in Settings > Media Tools to audition composition audio.");
            var fingerprint = await _fingerprintProvider.GetAsync(executablePath, cancellationToken)
                .ConfigureAwait(false);
            var key = MediaRenderCache.HashText(string.Join('|',
                AlgorithmVersion,
                composition.VirtualAssetId.ToString("N"),
                composition.RecipeRevisionId.ToString("N"),
                compositionDurationSeconds.ToString("R", CultureInfo.InvariantCulture),
                string.Join(';', leases.Select(lease =>
                    lease.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty)),
                fingerprint));
            var cacheDirectory = Path.Combine(_renderCache.RootDirectory, "compositions");
            var cachePath = Path.Combine(cacheDirectory, $"{key}.m4a");
            if (MediaRenderCache.IsUsableFile(cachePath))
                return await _renderCache.OpenLeaseAsync(cachePath, fallbackEncoding: null, cancellationToken)
                    .ConfigureAwait(false);

            var cacheLock = _renderCache.GetLock(key);
            await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (MediaRenderCache.IsUsableFile(cachePath))
                    return await _renderCache.OpenLeaseAsync(cachePath, fallbackEncoding: null, cancellationToken)
                        .ConfigureAwait(false);
                Directory.CreateDirectory(cacheDirectory);
                using var fileCommit = AtomicFileCommit.Create(cachePath, "audition-audio", ".m4a");
                var arguments = FfmpegCommandBuilder.BuildAuditionAudioMixArguments(
                    inputs,
                    compositionDurationSeconds,
                    fileCommit.TemporaryPath);
                var result = await _runner.RunAsync(
                        new ExternalProcessRequest(executablePath, arguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded) throw new ExternalProcessException(executablePath, result);
                if (!MediaRenderCache.IsUsableFile(fileCommit.TemporaryPath))
                    throw new InvalidDataException("FFmpeg completed without producing the audition audio mix.");
                try
                {
                    fileCommit.Commit();
                }
                catch (IOException) when (MediaRenderCache.IsUsableFile(cachePath))
                {
                    // Another process completed the deterministic render first.
                }

                return await _renderCache.OpenLeaseAsync(cachePath, fallbackEncoding: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                cacheLock.Release();
            }
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Guid? GetRecipeRevisionId(MediaRenderPlanNode node) => node switch
    {
        TrimRenderPlanNode trim => trim.RecipeRevisionId,
        ExtractFrameRenderPlanNode frame => frame.RecipeRevisionId,
        CompositionRenderPlanNode nested => nested.RecipeRevisionId,
        _ => null
    };

    private static TimeSpan ClampFade(long milliseconds, double? audibleDurationSeconds)
    {
        if (milliseconds <= 0) return TimeSpan.Zero;
        var requested = TimeSpan.FromMilliseconds(milliseconds);
        return audibleDurationSeconds is { } duration
            ? TimeSpan.FromSeconds(Math.Min(requested.TotalSeconds, Math.Max(0, duration)))
            : requested;
    }
}
