namespace ReelForge.Application;

public sealed record DiagnosticLogReference(string EventId, string FilePath);

public interface IApplicationDiagnosticLog
{
    string LogDirectory { get; }

    Task<DiagnosticLogReference?> WriteErrorAsync(
        string category,
        string message,
        IReadOnlyDictionary<string, string?> details,
        CancellationToken cancellationToken = default);
}
