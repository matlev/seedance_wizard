namespace ReelForge.Application;

public sealed record DiagnosticLogReference(string EventId, string FilePath);

public enum DiagnosticLogLevel
{
    Information,
    Error
}

public interface IApplicationDiagnosticLog
{
    string LogDirectory { get; }

    Task<DiagnosticLogReference?> WriteAsync(
        DiagnosticLogLevel level,
        string category,
        string message,
        IReadOnlyDictionary<string, string?> details,
        CancellationToken cancellationToken = default);

    Task<DiagnosticLogReference?> WriteErrorAsync(
        string category,
        string message,
        IReadOnlyDictionary<string, string?> details,
        CancellationToken cancellationToken = default) =>
        WriteAsync(DiagnosticLogLevel.Error, category, message, details, cancellationToken);
}
