using System.Diagnostics;
using System.Text;

namespace ReelForge.Infrastructure;

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start '{request.ExecutablePath}'.");
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var runningProcess = (Process)state!;
                try
                {
                    if (!runningProcess.HasExited)
                    {
                        runningProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the state check and Kill.
                }
            },
            process);

        var standardOutput = CaptureAsync(process.StandardOutput, isError: false, progress, cancellationToken);
        var standardError = CaptureAsync(process.StandardError, isError: true, progress, cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ExternalProcessResult(
            process.ExitCode,
            await standardOutput.ConfigureAwait(false),
            await standardError.ConfigureAwait(false));
    }

    private static async Task<string> CaptureAsync(
        StreamReader reader,
        bool isError,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            output.AppendLine(line);
            progress?.Report(new ProcessOutputLine(isError, line));
        }

        return output.ToString();
    }
}
