using System.Text.Json;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class FileApplicationDiagnosticLog : IApplicationDiagnosticLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private string _logDirectory;

    public FileApplicationDiagnosticLog(string? logDirectory = null)
    {
        _logDirectory = ResolveLogDirectory(logDirectory);
    }

    public string LogDirectory => Volatile.Read(ref _logDirectory);

    public async Task<DiagnosticLogReference?> WriteErrorAsync(
        string category,
        string message,
        IReadOnlyDictionary<string, string?> details,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid().ToString("N");
        var entry = new DiagnosticLogEntry(
            timestamp,
            eventId,
            "error",
            category,
            message,
            details);

        try
        {
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var logDirectory = _logDirectory;
                Directory.CreateDirectory(logDirectory);
                var filePath = Path.Combine(logDirectory, $"reelforge-{timestamp:yyyy-MM-dd}.jsonl");
                await File.AppendAllTextAsync(filePath, line, cancellationToken).ConfigureAwait(false);
                return new DiagnosticLogReference(eventId, filePath);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Diagnostics must never replace or conceal the provider failure being diagnosed.
            return null;
        }
    }

    public static string GetDefaultLogDirectory()
        => ApplicationStoragePaths.GetDefaultLogDirectory();

    public static string ResolveLogDirectory(string? configuredPath) =>
        ApplicationStoragePaths.ResolveDirectory(configuredPath, GetDefaultLogDirectory());

    public static IReadOnlyList<string> FindExistingLogs(string directory)
    {
        var resolved = ResolveLogDirectory(directory);
        return Directory.Exists(resolved)
            ? Directory.EnumerateFiles(resolved, "reelforge-*.jsonl", SearchOption.TopDirectoryOnly).ToArray()
            : [];
    }

    public async Task RelocateAsync(
        string newDirectory,
        bool moveExistingLogs,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveLogDirectory(newDirectory);
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var oldDirectory = _logDirectory;
            if (PathEquals(oldDirectory, resolved)) return;
            if (moveExistingLogs)
            {
                Directory.CreateDirectory(resolved);
                foreach (var sourcePath in FindExistingLogs(oldDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var destinationPath = GetCollisionSafeDestination(resolved, Path.GetFileName(sourcePath));
                    File.Move(sourcePath, destinationPath);
                }
            }
            Volatile.Write(ref _logDirectory, resolved);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string GetCollisionSafeDestination(string directory, string fileName)
    {
        var destination = Path.Combine(directory, fileName);
        if (!File.Exists(destination)) return destination;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 1; ; suffix++)
        {
            destination = Path.Combine(directory, $"{stem}-moved-{suffix}{extension}");
            if (!File.Exists(destination)) return destination;
        }
    }

    private static bool PathEquals(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _writeLock.Dispose();

    private sealed record DiagnosticLogEntry(
        DateTimeOffset TimestampUtc,
        string EventId,
        string Level,
        string Category,
        string Message,
        IReadOnlyDictionary<string, string?> Details);
}
