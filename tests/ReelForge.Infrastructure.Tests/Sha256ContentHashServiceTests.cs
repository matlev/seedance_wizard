using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Infrastructure.Tests;

public sealed class Sha256ContentHashServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge sha256 tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task UsesCanonicalByteFingerprintIndependentlyOfName()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var first = Path.Combine(_temporaryRoot, "friendly name.txt");
        var renamed = Path.Combine(_temporaryRoot, "renamed.txt");
        await File.WriteAllTextAsync(first, "abc");
        File.Copy(first, renamed);
        var service = new Sha256ContentHashService();

        var firstIdentity = await service.ComputeAsync(first);
        var renamedIdentity = await service.ComputeAsync(renamed);
        await File.WriteAllTextAsync(renamed, "different bytes");
        var changedIdentity = await service.VerifyAsync(renamed, firstIdentity);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", firstIdentity.Sha256);
        Assert.Equal(firstIdentity.Sha256, renamedIdentity.Sha256);
        Assert.False(changedIdentity.MatchesExpected);
        Assert.NotEqual(firstIdentity.Sha256, changedIdentity.Observed.Sha256);
        Assert.Equal(ContentHashStatus.Mismatch, changedIdentity.Observed.Status);
        Assert.Equal(ContentHashStatus.Verified, firstIdentity.Status);
    }

    [Fact]
    public async Task MissingPathThrowsFileNotFoundException()
    {
        var service = new Sha256ContentHashService();
        var missingPath = Path.Combine(_temporaryRoot, "missing", "source.mp4");

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => service.ComputeAsync(missingPath));

        Assert.Equal(Path.GetFullPath(missingPath), exception.FileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
