using ReelForge.Application;
using ReelForge.Core;
using System.Security.Cryptography;

namespace ReelForge.Infrastructure;

/// <summary>
/// Copies a verified relink candidate into project-controlled storage using a same-directory
/// atomic replacement. The Application layer decides whether the staged bytes become project truth.
/// </summary>
public sealed class PhysicalAssetRelinkStager : IPhysicalAssetRelinkStager
{
    private readonly Func<CancellationToken, Task>? _beforeCommitAsync;

    public PhysicalAssetRelinkStager()
    {
    }

    // Focused fault-injection seam for verifying that a post-copy cancellation leaves no file.
    internal PhysicalAssetRelinkStager(Func<CancellationToken, Task> beforeCommitAsync)
    {
        _beforeCommitAsync = beforeCommitAsync;
    }

    public async Task<StagedPhysicalAssetRelink> StageAsync(
        ProjectLocation location,
        ProjectAsset asset,
        string candidatePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        if (asset.Physical is null)
            throw new InvalidOperationException("Only physical assets can be staged for relinking.");

        var recordedDestination = ProjectPathPolicy.ResolveContainedPath(location, asset.Physical.RelativePath);
        var directory = Path.GetDirectoryName(recordedDestination)
            ?? throw new InvalidDataException("The recorded media path must have a parent directory.");
        Directory.CreateDirectory(directory);
        var destination = File.Exists(recordedDestination)
            ? CollisionFreeDestinationPolicy.GetAvailablePath(directory, Path.GetFileName(recordedDestination))
            : recordedDestination;

        using var fileCommit = AtomicFileCommit.Create(destination, "relink", Path.GetExtension(destination));
        await using (var source = new FileStream(
            candidatePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var target = new FileStream(
            fileCommit.TemporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_beforeCommitAsync is not null)
            await _beforeCommitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Once copying has completed, signature capture must finish before the atomic move so a
        // late cancellation cannot leave an untracked committed file behind.
        var signature = await CaptureSignatureAsync(fileCommit.TemporaryPath, CancellationToken.None).ConfigureAwait(false);
        fileCommit.Commit(overwrite: false);
        return new StagedPhysicalAssetRelink(
            destination,
            signature.Sha256,
            signature.LengthBytes,
            signature.LastWriteTimeUtc);
    }

    public async Task DiscardAsync(StagedPhysicalAssetRelink staged, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(staged.DestinationPath)) return;

        var observed = await CaptureSignatureAsync(staged.DestinationPath, cancellationToken).ConfigureAwait(false);
        if (observed != new StagedFileSignature(staged.Sha256, staged.LengthBytes, staged.LastWriteTimeUtc))
            return;

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(staged.DestinationPath);
    }

    private static async Task<StagedFileSignature> CaptureSignatureAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (!file.Exists)
            throw new FileNotFoundException("The staged relink artifact no longer exists.", path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return new StagedFileSignature(
            Convert.ToHexString(hash),
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc));
    }

    private sealed record StagedFileSignature(string Sha256, long LengthBytes, DateTimeOffset LastWriteTimeUtc);
}
