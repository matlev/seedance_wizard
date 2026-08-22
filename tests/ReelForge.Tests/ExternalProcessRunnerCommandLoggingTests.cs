using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ExternalProcessRunnerCommandLoggingTests
{
    [Theory]
    [InlineData("ffmpeg.exe", true, false, "ffmpeg")]
    [InlineData("FFMPEG", true, false, "ffmpeg")]
    [InlineData("ffprobe.exe", false, true, "ffprobe")]
    [InlineData("ffmpeg.exe", false, true, null)]
    [InlineData("ffprobe.exe", true, false, null)]
    [InlineData("other-tool.exe", true, true, null)]
    public void CommandLoggingToolRespectsIndependentFlags(string executable, bool ffmpeg, bool ffprobe, string? expected)
    {
        Assert.Equal(expected, ExternalProcessRunner.GetCommandLoggingTool(executable, ffmpeg, ffprobe));
    }

    [Fact]
    public void CommandLineFormatterUsesWindowsQuotingRules()
    {
        var command = ExternalProcessRunner.FormatCommandLine(
            @"C:\Tools With Spaces\ffmpeg.exe",
            ["-i", "", "two words", "say \"hello\"", "C:\\ends with slash\\"]);

        Assert.Equal(
            "\"C:\\Tools With Spaces\\ffmpeg.exe\" -i \"\" \"two words\" \"say \\\"hello\\\"\" \"C:\\ends with slash\\\\\"",
            command);
    }

    [Fact]
    public async Task LiveFlagUpdateChangesWhatTheRunnerLogs()
    {
        var log = new RecordingLog();
        var runner = new ExternalProcessRunner(log);
        var ffmpeg = new ExternalProcessRequest("ffmpeg.exe", ["-version"]);
        var ffprobe = new ExternalProcessRequest("ffprobe.exe", ["-version"]);

        await runner.LogCommandIfEnabledAsync(ffmpeg);
        runner.UpdateCommandLogging(logFfmpegCommands: true, logFfprobeCommands: false);
        await runner.LogCommandIfEnabledAsync(ffmpeg);
        await runner.LogCommandIfEnabledAsync(ffprobe);
        runner.UpdateCommandLogging(logFfmpegCommands: false, logFfprobeCommands: true);
        await runner.LogCommandIfEnabledAsync(ffprobe);

        Assert.Collection(
            log.Events,
            entry => Assert.Equal("ffmpeg", entry.Details["tool"]),
            entry => Assert.Equal("ffprobe", entry.Details["tool"]));
        Assert.All(log.Events, entry =>
        {
            Assert.Equal(DiagnosticLogLevel.Information, entry.Level);
            Assert.Equal("media-tool-command", entry.Category);
        });
    }

    [Fact]
    public async Task LoggingFailureIsIgnored()
    {
        var runner = new ExternalProcessRunner(new ThrowingLog(), logFfmpegCommands: true);

        await runner.LogCommandIfEnabledAsync(new ExternalProcessRequest("ffmpeg.exe", ["-version"]));
    }

    private sealed class RecordingLog : IApplicationDiagnosticLog
    {
        public string LogDirectory => string.Empty;
        public List<(DiagnosticLogLevel Level, string Category, string Message, IReadOnlyDictionary<string, string?> Details)> Events { get; } = [];

        public Task<DiagnosticLogReference?> WriteAsync(DiagnosticLogLevel level, string category, string message,
            IReadOnlyDictionary<string, string?> details, CancellationToken cancellationToken = default)
        {
            Events.Add((level, category, message, details));
            return Task.FromResult<DiagnosticLogReference?>(null);
        }
    }

    private sealed class ThrowingLog : IApplicationDiagnosticLog
    {
        public string LogDirectory => string.Empty;
        public Task<DiagnosticLogReference?> WriteAsync(DiagnosticLogLevel level, string category, string message,
            IReadOnlyDictionary<string, string?> details, CancellationToken cancellationToken = default) =>
            throw new IOException("Intentional test failure.");
    }
}
