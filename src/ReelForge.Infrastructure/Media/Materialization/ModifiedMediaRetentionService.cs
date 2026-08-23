using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class ModifiedMediaRetentionService
{
    private readonly MediaRenderCache _renderCache;
    private readonly IContentHashService _contentHashService;
    private volatile bool _persistModifiedMediaOnDisk;

    public ModifiedMediaRetentionService(
        MediaRenderCache renderCache,
        IContentHashService contentHashService,
        bool persistModifiedMediaOnDisk)
    {
        _renderCache = renderCache;
        _contentHashService = contentHashService;
        _persistModifiedMediaOnDisk = persistModifiedMediaOnDisk;
    }

    public void UpdatePreference(bool persistModifiedMediaOnDisk) =>
        _persistModifiedMediaOnDisk = persistModifiedMediaOnDisk;

    public async Task<MaterializedMediaLease> PersistIfRequestedAsync(
        VideoProject project,
        ProjectLocation location,
        MaterializationTarget target,
        MaterializedMediaLease media,
        CancellationToken cancellationToken)
    {
        if (!_persistModifiedMediaOnDisk) return media;
        try
        {
            var persistentPath = GetPersistentRepresentationPath(project, location, target, media.Path);
            var persistentLock = _renderCache.GetLock($"persistent|{persistentPath}");
            await persistentLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(persistentPath))
                {
                    var existingIdentity = await _contentHashService.ComputeAsync(
                        persistentPath, cancellationToken).ConfigureAwait(false);
                    if (existingIdentity.Sha256?.Equals(
                            media.ContentIdentity.Sha256,
                            StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return new MaterializedMediaLease(
                            persistentPath, existingIdentity, media.Encoding, isDurableSource: false);
                    }
                }

                var directory = Path.GetDirectoryName(persistentPath)!;
                Directory.CreateDirectory(directory);
                using var fileCommit = AtomicFileCommit.Create(
                    persistentPath,
                    "retain-modified",
                    Path.GetExtension(persistentPath));
                await CopyFileAsync(media.Path, fileCommit.TemporaryPath, cancellationToken).ConfigureAwait(false);
                fileCommit.Commit(overwrite: true);
                var identity = await _contentHashService.ComputeAsync(persistentPath, cancellationToken)
                    .ConfigureAwait(false);
                return new MaterializedMediaLease(
                    persistentPath, identity, media.Encoding, isDurableSource: false);
            }
            finally
            {
                persistentLock.Release();
            }
        }
        finally
        {
            await media.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string GetPersistentRepresentationPath(
        VideoProject project,
        ProjectLocation location,
        MaterializationTarget target,
        string materializedPath)
    {
        string category;
        string stem;
        switch (target)
        {
            case AnchorMaterializationTarget anchorTarget:
            {
                var anchor = project.Anchors.Single(candidate => candidate.Id == anchorTarget.AnchorId);
                var revision = project.AnchorRevisions.Single(candidate => candidate.Id == anchorTarget.AnchorRevisionId);
                category = "frames";
                stem = $"{SanitizeFileStem(anchor.DisplayLabel ?? "Saved Frame")}-r{revision.RevisionNumber}-{anchor.Id:N}";
                break;
            }
            case AssetMaterializationTarget assetTarget:
            {
                var asset = project.Assets.Single(candidate => candidate.Id == assetTarget.AssetId);
                var revisionId = assetTarget.RecipeRevisionId ?? asset.Virtual?.CurrentRecipeRevisionId
                    ?? throw new InvalidDataException("A persistent virtual representation requires a pinned recipe revision.");
                var revision = project.RecipeRevisions.Single(candidate => candidate.Id == revisionId);
                category = asset.Virtual?.Kind == VirtualAssetKind.Composition ? "compositions" : "clips";
                stem = $"{SanitizeFileStem(asset.EffectiveDisplayName)}-r{revision.RevisionNumber}-{asset.Id:N}";
                break;
            }
            default:
                throw new NotSupportedException(
                    $"Persistent representation target '{target.GetType().Name}' is unsupported.");
        }

        var extension = Path.GetExtension(materializedPath).ToLowerInvariant();
        if (extension is not (".png" or ".mp4"))
            throw new InvalidDataException("Persistent modified media must be a PNG image or MP4 video.");
        return Path.Combine(location.RootDirectory, "assets", "modified", category, $"{stem}{extension}");
    }

    private static string SanitizeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(sanitized)) return "Modified media";
        return sanitized.Length <= 80 ? sanitized : sanitized[..80].TrimEnd();
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
