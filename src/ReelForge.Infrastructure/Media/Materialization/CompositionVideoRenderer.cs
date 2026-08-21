using System.Globalization;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class CompositionVideoRenderer
{
    private const string AlgorithmVersion = "composition-concat-v2";

    private readonly IExternalProcessRunner _runner;
    private readonly MediaRenderCache _renderCache;
    private readonly FfmpegRendererFingerprintProvider _fingerprintProvider;
    private readonly CompositionAudioRenderer _audioRenderer;
    private readonly IMediaInspectionService? _mediaInspector;

    public CompositionVideoRenderer(
        IExternalProcessRunner runner,
        MediaRenderCache renderCache,
        FfmpegRendererFingerprintProvider fingerprintProvider,
        CompositionAudioRenderer audioRenderer,
        IMediaInspectionService? mediaInspector)
    {
        _runner = runner;
        _renderCache = renderCache;
        _fingerprintProvider = fingerprintProvider;
        _audioRenderer = audioRenderer;
        _mediaInspector = mediaInspector;
    }

    public async Task<MaterializedMediaLease> RenderAsync(
        string? ffmpegPath,
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        MaterializationRequest request,
        Func<CompositionSegmentRenderPlan, CancellationToken, Task<MaterializedMediaLease>> materializeSegmentAsync,
        CancellationToken cancellationToken)
    {
        if (composition.Segments is [var segment])
        {
            var media = await materializeSegmentAsync(segment, cancellationToken).ConfigureAwait(false);
            if (segment.AudioEnabled) return media;
            try
            {
                return await _audioRenderer.RenderWithoutSourceAudioAsync(
                        ffmpegPath, outputAsset, segment, media, request, cancellationToken)
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
                leases.Add(await materializeSegmentAsync(plannedSegment, cancellationToken).ConfigureAwait(false));

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
                    ffmpegPath,
                    outputAsset,
                    composition,
                    leases,
                    encodings,
                    includeAudio,
                    normalize,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
                await leases[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<MaterializedMediaLease> RenderConcatAsync(
        string? ffmpegPath,
        ProjectAsset outputAsset,
        CompositionRenderPlanNode composition,
        IReadOnlyList<MaterializedMediaLease> inputs,
        List<MediaEncodingMetadata?> encodings,
        bool includeAudio,
        bool normalize,
        MaterializationRequest request,
        CancellationToken cancellationToken)
    {
        var executablePath = ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to preview or export compositions.");
        var fingerprint = await _fingerprintProvider.GetAsync(executablePath, cancellationToken)
            .ConfigureAwait(false);
        var key = MediaRenderCache.HashText(string.Join('|',
            AlgorithmVersion,
            composition.NodeHash,
            string.Join(';', inputs.Select(input =>
                input.ContentIdentity.Sha256?.ToLowerInvariant() ?? string.Empty)),
            includeAudio,
            normalize,
            string.Join(';', composition.Segments.Select(segment => segment.AudioEnabled)),
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
            using var fileCommit = AtomicFileCommit.Create(cachePath, "composition-video", ".mp4");
            var arguments = normalize
                ? FfmpegCommandBuilder.BuildNormalizedConcatArguments(
                    inputs.Select((input, index) => new NormalizedConcatInput(
                        input.Path,
                        encodings[index]?.DurationSeconds
                            ?? throw new NotSupportedException(
                                "A composition segment has no known duration for normalization."),
                        encodings[index]?.Audio is not null,
                        composition.Segments[index].AudioEnabled)).ToArray(),
                    fileCommit.TemporaryPath,
                    CreateNormalizationProfile(encodings))
                : FfmpegCommandBuilder.BuildCompatibleConcatArguments(
                    inputs.Select(input => input.Path).ToArray(), fileCommit.TemporaryPath, includeAudio);
            var result = await _runner.RunAsync(
                    new ExternalProcessRequest(executablePath, arguments),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded) throw new ExternalProcessException(executablePath, result);
            if (!MediaRenderCache.IsUsableFile(fileCommit.TemporaryPath))
                throw new InvalidDataException("FFmpeg completed without producing the composition preview.");
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
        _renderCache.OpenLeaseAsync(cachePath, outputAsset.Virtual?.ExpectedMediaProperties, cancellationToken);

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
}
