namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04PlaybackHarnessTests
{
    [Fact]
    public void PreparationScriptPreservesPlaybackOnlyAndExactConcatBoundaries()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-P2G04PlaybackCorpusPreparation.ps1"));
        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("semanticProofs).Count-ne 11", script);
        Assert.Contains("repeatCount=40", script);
        Assert.Contains("'-c','copy'", script);
        Assert.Contains("'-f','concat'", script);
        Assert.Contains(".partial-", script);
        Assert.Contains("reparse-point", script);
        Assert.Contains("Playback-only derived corpus", script);
        Assert.DoesNotContain("-c:v", script);
        Assert.DoesNotContain("-vf", script);
    }

    [Fact]
    public void HarnessRequiresExplicitClickAndRecordsNativePlaybackSemantics()
    {
        var html = File.ReadAllText(PathInRepo("eng", "gate0", "G04PlaybackHarness.html"));
        Assert.Contains("Start native playback checks", html);
        Assert.Contains("start.addEventListener('click'", html);
        Assert.Contains("HTMLMediaElement", html);
        Assert.Contains("canPlayType", html);
        Assert.Contains("loadedmetadata", html);
        Assert.Contains("canplay", html);
        Assert.Contains("pauseStable", html);
        Assert.Contains("midpointSeek", html);
        Assert.Contains("endedCount", html);
        Assert.Contains("replayAdvance", html);
        Assert.Contains("fetch('results'", html);
        Assert.Contains("No audible/perceptual sync conclusion", html);
    }

    [Fact]
    public void ServerIsLocalOnlyAndSupportsSingleRangeWithoutDirectoryListing()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Start-G04PlaybackHarnessServer.ps1"));
        Assert.Contains("127.0.0.1", script);
        Assert.Contains("TcpListener", script);
        Assert.Contains("Accept-Ranges", script);
        Assert.Contains("Content-Range", script);
        Assert.Contains("$status=206", script);
        Assert.Contains("$head=$method-eq 'HEAD'", script);
        Assert.Contains("Contains('..')", script);
        Assert.Contains("saved-local-only", script);
        Assert.Contains(".partial-", script);
        Assert.Contains("1048576", script);
        Assert.DoesNotContain("Get-ChildItem", script);
    }

    [Fact]
    public void PreparationRejectsForgedEvidenceAndRepositoryOutputBeforeRuntimeUse()
    {
        var forged = Path.Combine(Path.GetTempPath(), "ReelForge-G04-playback-forged-" + Guid.NewGuid().ToString("N") + ".json");
        var output = Path.Combine(Path.GetTempPath(), "ReelForge-G04-playback-output-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(forged, "{\"profileId\":\"forged\",\"semanticProofs\":[]}");
            var result = RunPreparation("C:\\not-a-runtime", forged, output);
            Assert.NotEqual(0, result.ExitCode);
            using (var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "g0.4-playback-corpus-evidence.json"))))
                Assert.Contains("schemaVersion", evidence.RootElement.GetProperty("preflight").GetProperty("failure").GetString(), StringComparison.OrdinalIgnoreCase);

            var repositoryOutput = RunPreparation("C:\\not-a-runtime", forged, PathInRepo());
            Assert.NotEqual(0, repositoryOutput.ExitCode);
        }
        finally { File.Delete(forged); if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private static (int ExitCode, string Output) RunPreparation(string runtime, string evidence, string output)
    {
        var start = new System.Diagnostics.ProcessStartInfo { FileName = "pwsh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", PathInRepo("eng", "gate0", "Invoke-P2G04PlaybackCorpusPreparation.ps1"), "-RuntimeRoot", runtime, "-SourceEvidencePath", evidence, "-OutputDirectory", output }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }
}
