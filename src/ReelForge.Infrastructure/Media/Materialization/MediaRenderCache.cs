using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class MediaRenderCache : IDisposable
{
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService? _mediaInspector;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private bool _disposed;

    public MediaRenderCache(
        string rootDirectory,
        IContentHashService contentHashService,
        IMediaInspectionService? mediaInspector)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        _contentHashService = contentHashService;
        _mediaInspector = mediaInspector;
    }

    public string RootDirectory { get; }

    public SemaphoreSlim GetLock(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<MaterializedMediaLease> OpenLeaseAsync(
        string cachePath,
        MediaEncodingMetadata? fallbackEncoding,
        CancellationToken cancellationToken,
        bool updateLastUsed = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalizedPath = MediaCacheLeaseRegistry.Acquire(cachePath);
        try
        {
            if (updateLastUsed) File.SetLastWriteTimeUtc(normalizedPath, DateTime.UtcNow);
            var identity = await _contentHashService.ComputeAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
            var encoding = fallbackEncoding;
            if (_mediaInspector is not null)
            {
                try
                {
                    encoding = await _mediaInspector.InspectAsync(normalizedPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Optional representation metadata must not make a valid cached render unusable.
                }
            }

            return new MaterializedMediaLease(
                normalizedPath,
                identity,
                encoding,
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

    public static bool IsUsableFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    public static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cacheLock in _locks.Values) cacheLock.Dispose();
        _locks.Clear();
    }
}
