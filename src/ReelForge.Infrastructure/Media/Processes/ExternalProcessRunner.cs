using System.Diagnostics;
using System.Text;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    private readonly IApplicationDiagnosticLog? _diagnosticLog;
    private int _logFfmpegCommands;
    private int _logFfprobeCommands;

    public ExternalProcessRunner(
        IApplicationDiagnosticLog? diagnosticLog = null,
        bool logFfmpegCommands = false,
        bool logFfprobeCommands = false)
    {
        _diagnosticLog = diagnosticLog;
        UpdateCommandLogging(logFfmpegCommands, logFfprobeCommands);
    }

    public void UpdateCommandLogging(bool logFfmpegCommands, bool logFfprobeCommands)
    {
        Volatile.Write(ref _logFfmpegCommands, logFfmpegCommands ? 1 : 0);
        Volatile.Write(ref _logFfprobeCommands, logFfprobeCommands ? 1 : 0);
    }

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

        await LogCommandIfEnabledAsync(request).ConfigureAwait(false);

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

    internal static string FormatCommandLine(string executablePath, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        var parts = new string[arguments.Count + 1];
        parts[0] = FormatCommandLineArgument(executablePath);
        for (var index = 0; index < arguments.Count; index++)
        {
            parts[index + 1] = FormatCommandLineArgument(arguments[index]);
        }
        return string.Join(' ', parts);
    }

    internal static string? GetCommandLoggingTool(string executablePath, bool logFfmpegCommands, bool logFfprobeCommands)
    {
        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (logFfmpegCommands && executableName.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase)) return "ffmpeg";
        if (logFfprobeCommands && executableName.Equals("ffprobe", StringComparison.OrdinalIgnoreCase)) return "ffprobe";
        return null;
    }

    internal async Task LogCommandIfEnabledAsync(ExternalProcessRequest request)
    {
        if (_diagnosticLog is null) return;
        var tool = GetCommandLoggingTool(
            request.ExecutablePath,
            Volatile.Read(ref _logFfmpegCommands) != 0,
            Volatile.Read(ref _logFfprobeCommands) != 0);
        if (tool is null) return;

        try
        {
            await _diagnosticLog.WriteAsync(
                DiagnosticLogLevel.Information,
                "media-tool-command",
                FormatCommandLine(request.ExecutablePath, request.Arguments),
                new Dictionary<string, string?>
                {
                    ["tool"] = tool,
                    ["executable"] = request.ExecutablePath
                }).ConfigureAwait(false);
        }
        catch
        {
            // Command diagnostics are opt-in and must never prevent the tool from running.
        }
    }

    private static string FormatCommandLineArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length != 0 && !argument.Any(char.IsWhiteSpace) && !argument.Contains('"')) return argument;

        var formatted = new StringBuilder("\"");
        var backslashCount = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                formatted.Append('\\', backslashCount * 2 + 1);
                formatted.Append(character);
                backslashCount = 0;
                continue;
            }

            formatted.Append('\\', backslashCount);
            formatted.Append(character);
            backslashCount = 0;
        }

        formatted.Append('\\', backslashCount * 2);
        formatted.Append('"');
        return formatted.ToString();
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
