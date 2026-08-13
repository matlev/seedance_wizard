using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class ExactVideoFrameService : IExactVideoFrameService, IDisposable
{
    private const string ExtractionAlgorithmVersion = "exact-pts-png-v1";
    private readonly IExternalProcessRunner _runner;
    private readonly IContentHashService _contentHashService;
    private readonly string _cacheRoot;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _leasedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fingerprintLock = new(1, 1);
    private readonly SemaphoreSlim _trimLock = new(1, 1);
    private string? _ffmpegPath;
    private string? _ffprobePath;
    private string? _rendererFingerprint;
    private long _maximumCacheBytes;
    private bool _disposed;

    public ExactVideoFrameService(
        string? ffmpegPath,
        string? ffprobePath,
        IExternalProcessRunner runner,
        string cacheRoot,
        IContentHashService? contentHashService = null,
        long maximumCacheBytes = MediaToolConfiguration.DefaultCacheSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _runner = runner;
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _contentHashService = contentHashService ?? new Sha256ContentHashService();
        UpdateMaximumCacheBytes(maximumCacheBytes);
    }

    public void UpdateExecutablePaths(string? ffmpegPath, string? ffprobePath)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        _rendererFingerprint = null;
    }

    public void UpdateMaximumCacheBytes(long maximumCacheBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCacheBytes);
        Interlocked.Exchange(ref _maximumCacheBytes, maximumCacheBytes);
    }

    public async Task TrimCacheAsync(CancellationToken cancellationToken = default)
    {
        await _trimLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_cacheRoot)) return;

            var entries = new List<CacheEntry>();
            long totalBytes = 0;
            foreach (var path in Directory.EnumerateFiles(_cacheRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).Contains(".tmp.", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;
                    entries.Add(new CacheEntry(info.FullName, info.Length, info.LastWriteTimeUtc));
                    totalBytes += info.Length;
                }
                catch (IOException)
                {
                    // A concurrent cache operation owns this entry; leave it for the next trim.
                }
                catch (UnauthorizedAccessException)
                {
                    // Cache cleanup is best effort and must not prevent frame materialization.
                }
            }

            var maximumBytes = Interlocked.Read(ref _maximumCacheBytes);
            foreach (var entry in entries.OrderBy(candidate => candidate.LastUsedUtc))
            {
                if (totalBytes <= maximumBytes) break;
                if (_leasedPaths.ContainsKey(entry.Path)) continue;
                try
                {
                    File.Delete(entry.Path);
                    totalBytes -= entry.Length;
                }
                catch (IOException)
                {
                    // The entry became busy after enumeration; retry on a later trim.
                }
                catch (UnauthorizedAccessException)
                {
                    // Cache cleanup is best effort.
                }
            }
        }
        finally
        {
            _trimLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _fingerprintLock.Dispose();
        _trimLock.Dispose();
        foreach (var cacheLock in _cacheLocks.Values) cacheLock.Dispose();
        _cacheLocks.Clear();
    }

    public async Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        var ffprobePath = _ffprobePath ?? throw new MediaToolUnavailableException(
            "ffprobe is not configured. Configure it in Settings > Media Tools to browse exact frames.");
        var arguments = new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-show_entries", "stream=index,time_base:frame=best_effort_timestamp",
            "-show_streams",
            "-show_frames",
            "-print_format", "json",
            mediaPath
        };
        var result = await _runner.RunAsync(
                new ExternalProcessRequest(ffprobePath, arguments),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded) throw new ExternalProcessException(ffprobePath, result);
        return ParseIndex(result.StandardOutput);
    }

    public async Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
        string mediaPath,
        double centerSeconds,
        double radiusSeconds = 2,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentOutOfRangeException.ThrowIfNegative(centerSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusSeconds);
        if (!double.IsFinite(centerSeconds) || !double.IsFinite(radiusSeconds))
            throw new ArgumentOutOfRangeException(nameof(centerSeconds), "Frame-window timing must be finite.");
        var ffprobePath = _ffprobePath ?? throw new MediaToolUnavailableException(
            "ffprobe is not configured. Configure it in Settings > Media Tools to browse exact frames.");
        var startSeconds = Math.Max(0, centerSeconds - radiusSeconds);
        var durationSeconds = radiusSeconds * 2;
        var interval = $"{startSeconds.ToString("0.######", CultureInfo.InvariantCulture)}%+" +
                       durationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
        var arguments = new[]
        {
            "-v", "error",
            "-select_streams", "v:0",
            "-read_intervals", interval,
            "-show_entries", "stream=index,time_base:frame=best_effort_timestamp",
            "-show_streams",
            "-show_frames",
            "-print_format", "json",
            mediaPath
        };
        var result = await _runner.RunAsync(
                new ExternalProcessRequest(ffprobePath, arguments),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded) throw new ExternalProcessException(ffprobePath, result);
        return ParseIndex(result.StandardOutput);
    }

    public async Task<MaterializedMediaLease> ExtractAsync(
        string mediaPath,
        string sourceContentHash,
        FrameAnchorRevision revision,
        MaterializationPurpose purpose,
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceContentHash);
        ArgumentNullException.ThrowIfNull(revision);
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to extract Saved Frames.");
        if (!sourceContentHash.Equals(revision.SourceContentHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The extraction source does not match the content identity pinned by the Saved Frame revision.");

        var fingerprint = await GetRendererFingerprintAsync(ffmpegPath, cancellationToken).ConfigureAwait(false);
        var key = CreateCacheKey(sourceContentHash, revision, purpose, profile, fingerprint);
        var cacheDirectory = Path.Combine(_cacheRoot, "frames");
        var cachePath = Path.Combine(cacheDirectory, $"{key}.png");
        if (IsUsableCacheFile(cachePath))
            return await OpenAndTrimCacheLeaseAsync(cachePath, cancellationToken).ConfigureAwait(false);

        var cacheLock = _cacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsUsableCacheFile(cachePath))
                return await OpenAndTrimCacheLeaseAsync(cachePath, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(cacheDirectory);
            var temporaryPath = Path.Combine(cacheDirectory, $".{key}.{Guid.NewGuid():N}.tmp.png");
            try
            {
                var arguments = FfmpegCommandBuilder.BuildExtractExactFrameArguments(
                    mediaPath,
                    temporaryPath,
                    revision.VideoStreamIndex,
                    revision.PresentationTimestamp);
                var result = await _runner.RunAsync(
                        new ExternalProcessRequest(ffmpegPath, arguments),
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
                if (!IsUsableCacheFile(temporaryPath))
                    throw new InvalidDataException("FFmpeg completed without producing the requested presentation frame.");

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

            return await OpenAndTrimCacheLeaseAsync(cachePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public static IReadOnlyList<VideoPresentationFrame> ParseIndex(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
            throw new InvalidDataException("ffprobe did not report a decodable video stream.");
        var stream = streams[0];
        if (!TryReadInt32(stream, "index", out var streamIndex) ||
            !TryParseRational(GetString(stream, "time_base"), out var numerator, out var denominator))
            throw new InvalidDataException("ffprobe did not report a valid video stream index and time base.");
        if (!root.TryGetProperty("frames", out var frames))
            throw new InvalidDataException("ffprobe did not report decoded video frames.");

        var positions = new SortedSet<long>();
        foreach (var frame in frames.EnumerateArray())
        {
            if (TryReadInt64(frame, "best_effort_timestamp", out var pts)) positions.Add(pts);
        }
        if (positions.Count == 0)
            throw new InvalidDataException("ffprobe did not report presentation timestamps for decoded video frames.");

        return positions.Select(pts => new VideoPresentationFrame(
            streamIndex,
            pts,
            numerator,
            denominator)).ToArray();
    }

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
            _rendererFingerprint = HashText($"{ExtractionAlgorithmVersion}|{versionLine}");
            return _rendererFingerprint;
        }
        finally
        {
            _fingerprintLock.Release();
        }
    }

    private static string CreateCacheKey(
        string sourceContentHash,
        FrameAnchorRevision revision,
        MaterializationPurpose purpose,
        string? profile,
        string rendererFingerprint) =>
        HashText(string.Join('|',
            ExtractionAlgorithmVersion,
            sourceContentHash.ToLowerInvariant(),
            revision.Id.ToString("N"),
            revision.VideoStreamIndex.ToString(CultureInfo.InvariantCulture),
            revision.PresentationTimestamp.ToString(CultureInfo.InvariantCulture),
            revision.TimeBaseNumerator.ToString(CultureInfo.InvariantCulture),
            revision.TimeBaseDenominator.ToString(CultureInfo.InvariantCulture),
            purpose.ToString(),
            profile ?? string.Empty,
            rendererFingerprint));

    private async Task<MaterializedMediaLease> OpenAndTrimCacheLeaseAsync(
        string cachePath,
        CancellationToken cancellationToken)
    {
        var normalizedPath = Path.GetFullPath(cachePath);
        _leasedPaths.AddOrUpdate(normalizedPath, 1, static (_, count) => checked(count + 1));
        try
        {
            File.SetLastWriteTimeUtc(normalizedPath, DateTime.UtcNow);
            var identity = await _contentHashService.ComputeAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            await TrimCacheAsync(cancellationToken).ConfigureAwait(false);
            return new MaterializedMediaLease(
                normalizedPath,
                identity,
                new MediaEncodingMetadata { ContainerFormat = "png" },
                isDurableSource: false,
                release: () =>
                {
                    ReleaseLease(normalizedPath);
                    return ValueTask.CompletedTask;
                });
        }
        catch
        {
            ReleaseLease(normalizedPath);
            throw;
        }
    }

    private void ReleaseLease(string path)
    {
        while (_leasedPaths.TryGetValue(path, out var count))
        {
            if (count <= 1)
            {
                if (_leasedPaths.TryRemove(new KeyValuePair<string, int>(path, count))) return;
            }
            else if (_leasedPaths.TryUpdate(path, count - 1, count))
            {
                return;
            }
        }
    }

    private static bool IsUsableCacheFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static bool TryReadInt32(JsonElement element, string propertyName, out int value) =>
        int.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryReadInt64(JsonElement element, string propertyName, out long value) =>
        long.TryParse(GetString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseRational(string? value, out int numerator, out int denominator)
    {
        numerator = denominator = 0;
        var parts = value?.Split('/', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 } &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out numerator) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out denominator) &&
               numerator > 0 && denominator > 0;
    }

    private sealed record CacheEntry(string Path, long Length, DateTime LastUsedUtc);
}
