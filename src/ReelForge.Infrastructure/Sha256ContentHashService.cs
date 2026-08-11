using System.Security.Cryptography;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class Sha256ContentHashService : IContentHashService
{
    public async Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("Media file was not found.", fullPath);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        info.Refresh();
        return new ContentIdentity
        {
            Algorithm = ContentIdentity.Sha256Algorithm,
            Sha256 = Convert.ToHexString(hash).ToLowerInvariant(),
            Status = ContentHashStatus.Verified,
            LengthBytes = info.Length,
            ObservedLastWriteTimeUtc = info.LastWriteTimeUtc
        };
    }

    public async Task<ContentVerificationResult> VerifyAsync(
        string path,
        ContentIdentity expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var observed = await ComputeAsync(path, cancellationToken).ConfigureAwait(false);
        var matches = string.IsNullOrWhiteSpace(expected.Sha256) ||
            IsValidSha256(expected.Sha256) &&
            CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected.Sha256),
                Convert.FromHexString(observed.Sha256!));
        if (!matches) observed.Status = ContentHashStatus.Mismatch;
        return new ContentVerificationResult(matches, observed);
    }

    private static bool IsValidSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
}
