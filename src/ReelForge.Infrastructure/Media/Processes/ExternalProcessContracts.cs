namespace ReelForge.Infrastructure;

public sealed record ExternalProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null);

public sealed record ProcessOutputLine(bool IsError, string Text);

public sealed record ExternalProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IExternalProcessRunner
{
    Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalProcessException : Exception
{
    public ExternalProcessException(string tool, ExternalProcessResult result)
        : base($"{Path.GetFileName(tool)} exited with code {result.ExitCode}: {Summarize(result.StandardError)}")
    {
        Tool = tool;
        Result = result;
    }

    public string Tool { get; }
    public ExternalProcessResult Result { get; }

    private static string Summarize(string error)
    {
        var line = error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? "No error details were reported." : line;
    }
}

public sealed class MediaToolUnavailableException : Exception
{
    public MediaToolUnavailableException(string message) : base(message)
    {
    }
}
