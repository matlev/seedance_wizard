using System.Text.Json;
using System.Text.Json.Serialization;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class JsonGenerationJobStore : IGenerationJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonGenerationJobStore(string? filePath = null)
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        FilePath = filePath ?? Path.Combine(localApplicationData, "ReelForge", "active-jobs.json");
    }

    public string FilePath { get; }

    public async Task<IReadOnlyList<TrackedGenerationJob>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return [];
        await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<List<TrackedGenerationJob>>(stream, SerializerOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyCollection<TrackedGenerationJob> jobs,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The active-job registry must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
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
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
