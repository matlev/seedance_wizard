using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class AtomicFileCommitTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ReelForge atomic file commit",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CommitAtomicallyMovesTemporaryFileAndPreservesDestination()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "project.rfp");
        using (var commit = AtomicFileCommit.Create(destination, "save"))
        {
            Assert.Equal(_root, Path.GetDirectoryName(commit.TemporaryPath));
            File.WriteAllText(commit.TemporaryPath, "new state");

            commit.Commit();

            Assert.True(commit.IsCommitted);
            Assert.False(File.Exists(commit.TemporaryPath));
        }

        Assert.Equal("new state", File.ReadAllText(destination));
    }

    [Fact]
    public void DisposeDeletesUncommittedTemporaryFileWithoutTouchingDestination()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "project.rfp");
        File.WriteAllText(destination, "old state");
        string temporaryPath;
        using (var commit = AtomicFileCommit.Create(destination, "save"))
        {
            temporaryPath = commit.TemporaryPath;
            File.WriteAllText(temporaryPath, "incomplete state");
        }

        Assert.False(File.Exists(temporaryPath));
        Assert.Equal("old state", File.ReadAllText(destination));
    }

    [Fact]
    public void OverwriteCommitReplacesExistingDestination()
    {
        Directory.CreateDirectory(_root);
        var destination = Path.Combine(_root, "settings.json");
        File.WriteAllText(destination, "old state");
        using var commit = AtomicFileCommit.Create(destination, "save");
        File.WriteAllText(commit.TemporaryPath, "new state");

        commit.Commit(overwrite: true);

        Assert.Equal("new state", File.ReadAllText(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
