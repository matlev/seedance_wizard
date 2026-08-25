using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04W1MediaFoundationProofScriptTests
{
    [Fact]
    public void W1G04RunnerKeepsTheOptionalWindowsOnlyContractAndExplicitSelections()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-W1G04MediaFoundationProof.ps1"));

        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("fixture-source-inventory.json", script);
        Assert.Contains("generated-fixture-report.json", script);
        Assert.Contains("OutputDirectory must be outside the repository", script);
        Assert.Contains("PATH fallback is prohibited", script);
        Assert.Contains("h264_mf", script);
        Assert.Contains("'-hw_encoding','false'", script);
        Assert.Contains("'-rate_control','cbr'", script);
        Assert.Contains("'-b:v','2M'", script);
        Assert.Contains("'-g','25'", script);
        Assert.Contains("'-c:a','aac'", script);
        Assert.Contains("'-profile:a','aac_low'", script);
        Assert.Contains("'-b:a','192k'", script);
        Assert.Contains("'-an'", script);
        Assert.Contains("'-vf','format=yuv420p'", script);
        Assert.Contains("'-movflags','+faststart'", script);
        Assert.Contains("Constrained Baseline", script);
        Assert.Contains("H264 via MediaFoundation", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native FFmpeg AAC encoder", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moov is not before mdat", script);
        Assert.Contains("MAE", script);
        Assert.Contains("strictly better than every nonmatching source", script);
        Assert.Contains("primingPaddingToleranceSamples", script);
        Assert.Contains("OutputDirectory must not be a reparse point", script);
        Assert.Contains("OutputDirectory must not be beneath a reparse point", script);
        Assert.Contains("FixtureRoot must not be a reparse point", script);
        Assert.Contains("Write-PreflightFailureEvidence", script);
        Assert.Contains("preflight-failed", script);
        Assert.Contains("expected channel tones are not stronger", script);
        Assert.Contains("Win32_VideoController", script);
        Assert.Contains("optionalWindowsEvidence=$true", script);
        Assert.Contains("portableBaseline=$false", script);
        Assert.Contains("shippingConclusion=$false", script);
        Assert.Contains("environment-dependent", script);
        Assert.DoesNotContain("'-c:a','aac_mf'", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void W1G04RunnerRejectsRepositoryAndStaleOutputBeforeRuntimeOrFixtureUse()
    {
        var script = RepositoryPath("eng", "gate0", "Invoke-W1G04MediaFoundationProof.ps1");
        var repositoryResult = RunPowerShell(script, "C:\\not-a-runtime", "C:\\not-a-fixture", RepositoryPath());
        Assert.NotEqual(0, repositoryResult.ExitCode);
        Assert.Contains("outside the repository", repositoryResult.Output, StringComparison.OrdinalIgnoreCase);

        var stale = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-W1G04", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "stale.txt"), "stale evidence");
        try
        {
            var staleResult = RunPowerShell(script, "C:\\not-a-runtime", "C:\\not-a-fixture", stale);
            Assert.NotEqual(0, staleResult.ExitCode);
            Assert.Contains("new or empty", staleResult.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(stale, recursive: true); }
    }

    [Fact]
    public void FrameOrderOracleRejectsEqualMaeForANonmatchingSourceIndex()
    {
        var script = RepositoryPath("eng", "gate0", "Invoke-W1G04MediaFoundationProof.ps1");
        var raw = Path.Combine(Path.GetTempPath(), $"ReelForge-Gate0-W1-equal-{Guid.NewGuid():N}.rgb");
        File.WriteAllBytes(raw, new byte[3 * 320 * 180 * 3]);
        try
        {
            var literalScript = PowerShellLiteral(script);
            var literalRaw = PowerShellLiteral(raw);
            var command = $"$p={literalScript};$s=Get-Content -Raw -LiteralPath $p;$start=$s.IndexOf('function Assert-RootedDirectory');$end=$s.IndexOf('if ($env:OS -ne', $start);Invoke-Expression $s.Substring($start,$end-$start);$z=[byte[]]::new(320*180*3);try{{Assert-FrameOrder {literalRaw} @($z,$z,$z);exit 2}}catch{{if($_.Exception.Message -match 'source 1'){{exit 0}};exit 3}}";
            var result = RunPowerShellCommand(command);
            Assert.Equal(0, result.ExitCode);
        }
        finally { File.Delete(raw); }
    }

    [Gate0W1G04EvidenceFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void OptInW1G04EvidenceRecordsOnlyTheTwoOptionalCapabilityOutcomes()
    {
        var path = Environment.GetEnvironmentVariable("REELFORGE_GATE0_W1_G04_EVIDENCE_PATH")!;
        using var evidence = JsonDocument.Parse(File.ReadAllText(path));
        var root = evidence.RootElement;
        Assert.True(root.GetProperty("optionalWindowsEvidence").GetBoolean());
        Assert.False(root.GetProperty("portableBaseline").GetBoolean());
        Assert.False(root.GetProperty("shippingConclusion").GetBoolean());
        var capabilities = root.GetProperty("capabilities").EnumerateArray().ToArray();
        Assert.Equal(["W1.Video.Export.Mp4H264Aac.MediaFoundation", "W1.Video.Export.Mp4H264VideoOnly.MediaFoundation"], capabilities.Select(capability => capability.GetProperty("capabilityId").GetString()));
        Assert.All(capabilities, capability =>
        {
            Assert.Equal("passed", capability.GetProperty("status").GetString());
            var details = capability.GetProperty("details");
            Assert.Equal("h264_mf", details.GetProperty("components").GetProperty("videoEncoder").GetString());
            Assert.Equal("Constrained Baseline", details.GetProperty("observedSettings").GetProperty("videoProfile").GetString());
            Assert.Equal(20, details.GetProperty("observedSettings").GetProperty("videoLevel").GetInt32());
            var frames = details.GetProperty("videoIdentityOrder").GetProperty("frames").EnumerateArray().ToArray();
            Assert.Equal(3, frames.Length);
            Assert.All(frames, frame =>
            {
                Assert.True(frame.GetProperty("matchedMae").GetDouble() <= 18);
                var values = frame.GetProperty("maeBySourceFrame").EnumerateArray().Select(item => item.GetDouble()).ToArray();
                var matched = frame.GetProperty("matchedSourceFrame").GetInt32();
                Assert.All(values.Where((_, index) => index != matched), value => Assert.True(values[matched] < value));
            });
        });
        var av = capabilities[0].GetProperty("details");
        Assert.Equal("aac", av.GetProperty("components").GetProperty("audioEncoder").GetString());
        Assert.Equal("LC", av.GetProperty("observedSettings").GetProperty("audioProfile").GetString());
        Assert.Equal(5760, av.GetProperty("audioTimingTone").GetProperty("expectedSamplesPerChannel").GetInt32());
        Assert.True(av.GetProperty("audioTimingTone").GetProperty("primingPaddingToleranceSamples").GetInt32() > 0);
        Assert.Contains("AAC decoded sample count", av.GetProperty("audioTimingTone").GetProperty("assertion").GetString());
        Assert.Equal(JsonValueKind.Null, capabilities[1].GetProperty("details").GetProperty("components").GetProperty("audioEncoder").ValueKind);
    }

    private static (int ExitCode, string Output) RunPowerShell(string script, string runtimeRoot, string fixtureRoot, string outputDirectory)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-RuntimeRoot", runtimeRoot, "-FixtureRoot", fixtureRoot, "-OutputDirectory", outputDirectory }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static (int ExitCode, string Output) RunPowerShellCommand(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }

    private static string PowerShellLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }
}

public sealed class Gate0W1G04EvidenceFactAttribute : FactAttribute
{
    public Gate0W1G04EvidenceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REELFORGE_GATE0_W1_G04_EVIDENCE_PATH")))
        {
            Skip = "Gate 0 W1 G0.4 evidence assertion is opt-in and requires an explicit generated evidence path.";
        }
    }
}
