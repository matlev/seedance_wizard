using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>
/// A best-effort, cache-local lookup table for successfully produced derived representations.
/// It deliberately contains no project meaning beyond stable logical IDs and is safe to discard.
/// </summary>
internal sealed class CachedProjectMediaRepresentationIndex : IDisposable
{
    private const string IndexFileName = "derived-representations.json";
    private readonly string _cacheRoot;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CachedProjectMediaRepresentationIndex(string cacheRoot)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot);
        _indexPath = Path.Combine(_cacheRoot, IndexFileName);
    }

    public async Task RecordAsync(
        VideoProject project,
        MaterializationTarget target,
        string representationPath,
        CancellationToken cancellationToken)
    {
        var key = TryCreateKey(project, target);
        if (key is null || !TryGetRelativeCachePath(representationPath, out var relativePath)) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
            entries[key] = relativePath;
            Directory.CreateDirectory(_cacheRoot);
            using var commit = AtomicFileCommit.Create(_indexPath, "cache-index", ".json");
            await using (var stream = new FileStream(
                             commit.TemporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            commit.Commit(overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasCachedRepresentationAsync(
        VideoProject project,
        MaterializationTarget target,
        CancellationToken cancellationToken)
    {
        var key = TryCreateKey(project, target);
        if (key is null) return false;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!entries.TryGetValue(key, out var relativePath)) return false;
            if (!TryGetCachePath(relativePath, out var cachePath) || !MediaRenderCache.IsUsableFile(cachePath))
                return false;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> FindCachedRepresentationPathAsync(
        VideoProject project,
        MaterializationTarget target,
        CancellationToken cancellationToken)
    {
        var key = TryCreateKey(project, target);
        if (key is null) return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!entries.TryGetValue(key, out var relativePath) ||
                !TryGetCachePath(relativePath, out var cachePath) ||
                !MediaRenderCache.IsUsableFile(cachePath))
                return null;
            return cachePath;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath)) return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await using var stream = new FileStream(
                _indexPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(
                       stream,
                       cancellationToken: cancellationToken)
                   .ConfigureAwait(false)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // The cache index is disposable. A partial or old index simply has no availability information.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string? TryCreateKey(VideoProject project, MaterializationTarget target) => target switch
    {
        AnchorMaterializationTarget anchor when project.AnchorRevisions.Any(revision =>
            revision.Id == anchor.AnchorRevisionId && revision.AnchorId == anchor.AnchorId) =>
            $"{project.Id:N}|anchor|{anchor.AnchorId:N}|{anchor.AnchorRevisionId:N}",
        AssetMaterializationTarget asset => TryCreateAssetKey(project, asset),
        _ => null
    };

    private static string? TryCreateAssetKey(VideoProject project, AssetMaterializationTarget target)
    {
        if (project.Assets.SingleOrDefault(candidate => candidate.Id == target.AssetId)
            is not { StorageKind: AssetStorageKind.Virtual, Virtual: { } virtualAsset })
            return null;

        var recipeRevisionId = target.RecipeRevisionId ?? virtualAsset.CurrentRecipeRevisionId;
        if (recipeRevisionId is null ||
            !project.RecipeRevisions.Any(revision =>
                revision.Id == recipeRevisionId.Value && revision.VirtualAssetId == target.AssetId))
            return null;

        return $"{project.Id:N}|asset|{target.AssetId:N}|{recipeRevisionId.Value:N}";
    }

    private bool TryGetRelativeCachePath(string path, out string relativePath)
    {
        relativePath = Path.GetRelativePath(_cacheRoot, Path.GetFullPath(path));
        return TryGetCachePath(relativePath, out _);
    }

    private bool TryGetCachePath(string relativePath, out string cachePath)
    {
        cachePath = Path.GetFullPath(Path.Combine(_cacheRoot, relativePath));
        var rootWithSeparator = _cacheRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _cacheRoot
            : _cacheRoot + Path.DirectorySeparatorChar;
        return cachePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _gate.Dispose();
}
