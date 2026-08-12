using System.Text.Json;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class FileApplicationDiagnosticLog : IApplicationDiagnosticLog, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileApplicationDiagnosticLog(string? logDirectory = null)
    {
        LogDirectory = Path.GetFullPath(logDirectory ?? GetDefaultLogDirectory());
    }

    public string LogDirectory { get; }

    public async Task<DiagnosticLogReference?> WriteErrorAsync(
        string category,
        string message,
        IReadOnlyDictionary<string, string?> details,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var eventId = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(LogDirectory, $"reelforge-{timestamp:yyyy-MM-dd}.jsonl");
        var entry = new DiagnosticLogEntry(
            timestamp,
            eventId,
            "error",
            category,
            message,
            details);

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(filePath, line, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            return new DiagnosticLogReference(eventId, filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Diagnostics must never replace or conceal the provider failure being diagnosed.
            return null;
        }
    }

    public static string GetDefaultLogDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localApplicationData, "ReelForge", "Logs");
    }

    public void Dispose() => _writeLock.Dispose();

    private sealed record DiagnosticLogEntry(
        DateTimeOffset TimestampUtc,
        string EventId,
        string Level,
        string Category,
        string Message,
        IReadOnlyDictionary<string, string?> Details);
}
