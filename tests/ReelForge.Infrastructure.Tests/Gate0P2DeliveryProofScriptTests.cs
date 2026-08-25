namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0P2DeliveryProofScriptTests
{
    [Fact]
    public void DeliveryProofRunnerKeepsReviewedDeliveryBoundariesExplicit()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2DeliveryProof.ps1"));

        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("generated-fixture-report.json", script);
        Assert.Contains("fixture-source-inventory.json", script);
        Assert.Contains("approved inventory does not match", script);
        Assert.Contains("reparse-point", script);
        Assert.Contains("unsafe path", script);
        Assert.Contains("Fixture report hash or length mismatch", script);
        Assert.Contains("PATH fallback is prohibited", script);
        Assert.Contains("OutputDirectory must be outside the repository", script);
        Assert.Contains("libvpx-vp9", script);
        Assert.Contains("libopus", script);
        Assert.Contains("'-c:a','flac'", script);
        Assert.Contains("'-f','webm'", script);
        Assert.Contains("'-f','ogg'", script);
        Assert.Contains("'-map','0:v:0'", script);
        Assert.Contains("'-map','0:a:0'", script);
        Assert.Contains("-filter_complex", script);
        Assert.Contains(".partial", script);
        Assert.Contains("Move-Item", script);
        Assert.Contains("Decode $proxy 'video' 'vp9'", script);
        Assert.Contains("Decode $selected 'audio' 'opus'", script);
        Assert.Contains("Decode $flac 'audio' 'flac'", script);
        Assert.Contains("Assert-ProxyAspectAndPadding", script);
        Assert.Contains("Composition F1 identity/order oracle", script);
        Assert.Contains("Composition F2 identity/order oracle", script);
        Assert.Contains("expectedFrameCount=6", script);
        Assert.Contains("opusPaddingToleranceMilliseconds=20", script);
        Assert.Contains("fixtureReportSha256", script);
        Assert.Contains("finalArtifacts", script);
        Assert.Contains("$activeCapability", script);
        Assert.Contains("'not-run'", script);
        Assert.Contains("invalid-oracle", script);
        Assert.Contains("stream-layout oracle failed", script);
        Assert.Contains("$streams.Count -ne 2", script);
        Assert.Contains("inspectedStreamMap", script);
        Assert.Contains("'-framerate','30000/1001'", script);
        Assert.Contains("avg_frame_rate -ne '15/1'", script);
        Assert.Contains("Get-ToneMagnitude", script);
        Assert.Contains("Assert-ExpectedToneAgainstComparisons", script);
        Assert.DoesNotContain("Assert-DominantFrequency", script);
        Assert.Contains("stronger than the declared comparison frequencies", script);
        Assert.Contains("'0:v:0','0:a:0','1:v:0','2:a:0'", script);
        Assert.Contains("not the final ReelForge default", script);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeliveryProofRunnerRejectsRepositoryOutputBeforeRuntimeUse()
    {
        var script = RepositoryPath("eng", "gate0", "Invoke-P2DeliveryProof.ps1");
        var result = RunPowerShell(script, "C:\\not-a-runtime", "C:\\not-a-fixture-root", RepositoryPath());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the repository", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeliveryProofRunnerRejectsStaleOutputBeforeRuntimeUse()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-DeliveryProofTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "stale.txt"), "stale evidence must not be accepted");
        try
        {
            var result = RunPowerShell(RepositoryPath("eng", "gate0", "Invoke-P2DeliveryProof.ps1"), "C:\\not-a-runtime", "C:\\not-a-fixture-root", root);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("new or empty", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Gate0RuntimeFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void OptInDeliveryEvidenceRecordsCompleteArtifactsAndCapabilityAttribution()
    {
        var evidencePath = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_DELIVERY_EVIDENCE");
        Assert.False(string.IsNullOrWhiteSpace(evidencePath));
        using var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(evidencePath!));
        Assert.True(evidence.RootElement.GetProperty("fixtureReportVerified").GetBoolean());
        Assert.Matches("^[A-F0-9]{64}$", evidence.RootElement.GetProperty("fixtureReportSha256").GetString()!);
        var artifacts = evidence.RootElement.GetProperty("finalArtifacts").EnumerateArray().ToArray();
        Assert.Equal(6, artifacts.Length);
        Assert.All(artifacts, artifact =>
        {
            Assert.StartsWith("media/", artifact.GetProperty("path").GetString());
            Assert.True(artifact.GetProperty("length").GetInt64() > 0);
            Assert.Matches("^[A-F0-9]{64}$", artifact.GetProperty("sha256").GetString()!);
        });
        var proofs = evidence.RootElement.GetProperty("semanticProofs").EnumerateArray().ToArray();
        Assert.Equal(["Preview.GenerateDraftProxy", "Video.Export.OpenDelivery.SelectedMedia", "Video.Export.OpenDelivery.Composition", "Audio.Export.Standalone"], proofs.Select(proof => proof.GetProperty("capabilityId").GetString()));
        Assert.All(proofs, proof => Assert.Equal("passed", proof.GetProperty("status").GetString()));
    }

    [Gate0RuntimeFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void OptInDeliveryProofRejectsTruncatedForgedAndEscapingFixtureReports()
    {
        var evidencePath = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_DELIVERY_EVIDENCE");
        Assert.False(string.IsNullOrWhiteSpace(evidencePath));
        using var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(evidencePath!));
        var fixtureRoot = evidence.RootElement.GetProperty("fixtureRoot").GetString();
        var runtimeRoot = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(fixtureRoot));
        Assert.False(string.IsNullOrWhiteSpace(runtimeRoot));
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-DeliveryReportTest", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var mutation in new[] { "truncated", "forged", "escaping" })
            {
                var copy = Path.Combine(root, mutation, "fixtures");
                CopyDirectory(fixtureRoot!, copy);
                var report = Path.Combine(copy, "generated-fixture-report.json");
                var text = File.ReadAllText(report);
                File.WriteAllText(report, mutation switch
                {
                    "truncated" => "{",
                    "forged" => text.Replace("eng/gate0/fixture-source-inventory.json", "eng/gate0/forged-inventory.json", StringComparison.Ordinal),
                    _ => text.Replace("\"expected-truths.json\"", "\"../escape.json\"", StringComparison.Ordinal)
                });
                var result = RunPowerShell(RepositoryPath("eng", "gate0", "Invoke-P2DeliveryProof.ps1"), runtimeRoot!, copy, Path.Combine(root, mutation, "output"));
                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains(mutation switch { "truncated" => "truncated or invalid JSON", "forged" => "approved inventory does not match", _ => "unsafe path" }, result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static ProcessResult RunPowerShell(string script, string runtimeRoot, string fixtureRoot, string outputDirectory)
    {
        var start = new System.Diagnostics.ProcessStartInfo { FileName = "pwsh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-RuntimeRoot", runtimeRoot, "-FixtureRoot", fixtureRoot, "-OutputDirectory", outputDirectory }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
