using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class FileApplicationDiagnosticLogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ReelForge diagnostic log tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RelocationMovesExistingLogsAndWritesNewEventsToNewDirectory()
    {
        var oldDirectory = Path.Combine(_root, "old");
        var newDirectory = Path.Combine(_root, "new");
        using var log = new FileApplicationDiagnosticLog(oldDirectory);
        var oldEntry = await log.WriteErrorAsync("test", "before move", new Dictionary<string, string?>());

        await log.RelocateAsync(newDirectory, moveExistingLogs: true);
        var newEntry = await log.WriteErrorAsync("test", "after move", new Dictionary<string, string?>());

        Assert.NotNull(oldEntry);
        Assert.NotNull(newEntry);
        Assert.Equal(Path.GetFullPath(newDirectory), log.LogDirectory);
        Assert.Empty(FileApplicationDiagnosticLog.FindExistingLogs(oldDirectory));
        var movedLogs = FileApplicationDiagnosticLog.FindExistingLogs(newDirectory);
        Assert.NotEmpty(movedLogs);
        var combined = string.Join(Environment.NewLine, movedLogs.Select(File.ReadAllText));
        Assert.Contains("before move", combined, StringComparison.Ordinal);
        Assert.Contains("after move", combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RelocationNeverOverwritesSameNamedDestinationLog()
    {
        var oldDirectory = Path.Combine(_root, "old");
        var newDirectory = Path.Combine(_root, "new");
        Directory.CreateDirectory(oldDirectory);
        Directory.CreateDirectory(newDirectory);
        var fileName = "reelforge-2026-08-13.jsonl";
        await File.WriteAllTextAsync(Path.Combine(oldDirectory, fileName), "old-log");
        await File.WriteAllTextAsync(Path.Combine(newDirectory, fileName), "new-log");
        using var log = new FileApplicationDiagnosticLog(oldDirectory);

        await log.RelocateAsync(newDirectory, moveExistingLogs: true);

        Assert.Equal("new-log", await File.ReadAllTextAsync(Path.Combine(newDirectory, fileName)));
        Assert.Contains(
            FileApplicationDiagnosticLog.FindExistingLogs(newDirectory),
            path => File.ReadAllText(path) == "old-log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
