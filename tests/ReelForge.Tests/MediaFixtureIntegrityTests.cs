using System.Security.Cryptography;

namespace ReelForge.Tests;

public sealed class MediaFixtureIntegrityTests
{
    private const string DegradedTimingFixtureSha256 =
        "F005A77C048912A6964DF6C492A9D66E11FBD473B45ABFC691E536D854339FC7";

    [Fact]
    public async Task DegradedTimingFixtureKeepsItsVerifiedBytes()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "degraded_timing_gap.webm");

        await using var fixture = File.OpenRead(fixturePath);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(fixture));

        Assert.Equal(27_343, fixture.Length);
        Assert.Equal(DegradedTimingFixtureSha256, sha256);
    }
}
