using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class JsonGenerationJobStore : IGenerationJobStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> JobFileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonGenerationJobStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<IReadOnlyList<TrackedGenerationJob>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var fileLock = GetFileLock();
        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath)) return [];
            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<List<TrackedGenerationJob>>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                       .ConfigureAwait(false)
                   ?? [];
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<TrackedGenerationJob> jobs,
        CancellationToken cancellationToken = default)
    {
        var fileLock = GetFileLock();
        await fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidOperationException("The active-job registry must have a parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, jobs, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath);
            fileLock.Release();
        }
    }

    private SemaphoreSlim GetFileLock() => JobFileLocks.GetOrAdd(
        Path.GetFullPath(FilePath),
        static _ => new SemaphoreSlim(1, 1));
}
