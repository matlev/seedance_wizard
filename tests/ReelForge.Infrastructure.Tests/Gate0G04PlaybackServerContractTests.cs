namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04PlaybackServerContractTests
{
    [Fact]
    public void ServerRequiresThePinnedV2DispositionManifestAndBoundsItsProtocol()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Start-G04PlaybackHarnessServer.ps1"));

        Assert.Contains("schemaVersion') -ne 2", script);
        Assert.Contains("blockedRoutes", script);
        Assert.Contains("Manifest must disposition each approved route exactly once", script);
        Assert.Contains("Get-Sha256", script);
        Assert.Contains("Test-NonReparseFile", script);
        Assert.Contains("Content-Length", script);
        Assert.Contains("Length Required", script);
        Assert.Contains("Payload Too Large", script);
        Assert.Contains("Read-ExactBytes", script);
        Assert.Contains("ConvertTo-Json -Compress", script);
        Assert.Contains("ResultDirectory must be outside the immutable corpus and repository", script);
        Assert.Contains("retained native playback observation envelope", script);
        Assert.Contains("Corpus bound-artifact provenance does not exactly cover the immutable corpus", script);
        Assert.Contains("Observation top-level status does not match its route dispositions", script);
        Assert.Contains("Content-Range", script);
        Assert.Contains("Accept-Ranges", script);
        Assert.Contains("Method Not Allowed", script);
        Assert.Contains("TcpListener", script);
    }

    [Fact]
    public void HarnessAttachesApprovedMediaBeforeClickAndPreservesBlockedDispositions()
    {
        var html = File.ReadAllText(PathInRepo("eng", "gate0", "G04PlaybackHarness.html"));

        Assert.Contains("manifest.routes.length+manifest.blockedRoutes.length!==4", html);
        Assert.Contains("media.append(video)", html);
        Assert.Contains("const firstPlay=prepared.length?prepared[0].video.play():Promise.resolve()", html);
        Assert.Contains("status:route.status", html);
        Assert.Contains("completed-with-inherited-blocked-routes", html);
        Assert.Contains("getHighEntropyValues", html);
        Assert.Contains("No audible/perceptual sync conclusion", html);
        Assert.Contains("midpointSeek", html);
        Assert.Contains("replayAdvance", html);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
