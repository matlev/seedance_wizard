using System.Security.Cryptography;
using System.Text;

namespace ReelForge.Infrastructure;

internal sealed class FfmpegRendererFingerprintProvider : IDisposable
{
    private readonly IExternalProcessRunner _runner;
    private readonly string _algorithmVersion;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _fingerprint;
    private bool _disposed;

    public FfmpegRendererFingerprintProvider(
        IExternalProcessRunner runner,
        string algorithmVersion)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _algorithmVersion = algorithmVersion;
    }

    public void Reset() => _fingerprint = null;

    public async Task<string> GetAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fingerprint is not null) return _fingerprint;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_fingerprint is not null) return _fingerprint;
            var result = await _runner.RunAsync(
                    new ExternalProcessRequest(ffmpegPath, ["-version"]),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
            var versionLine = result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "unknown-ffmpeg-version";
            _fingerprint = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes($"{_algorithmVersion}|{versionLine}")))
                .ToLowerInvariant();
            return _fingerprint;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
    }
}
