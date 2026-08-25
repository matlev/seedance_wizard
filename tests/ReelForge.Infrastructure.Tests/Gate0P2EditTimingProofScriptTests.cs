using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReelForge.Infrastructure.Tests;

namespace ReelForge.Tests;

public sealed class Gate0P2EditTimingProofScriptTests
{
    [Fact]
    public void EditTimingProofScriptKeepsTheApprovedRuntimeAndFixtureBoundariesExplicit()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2EditTimingProof.ps1"));

        Assert.Contains("[string] $RuntimeRoot", script);
        Assert.Contains("[string] $FixtureRoot", script);
        Assert.Contains("[string] $OutputDirectory", script);
        Assert.Contains("Assert-P2Identity", script);
        Assert.Contains("Assert-FixtureReport", script);
        Assert.Contains("PATH fallback is prohibited", script);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EditTimingProofScriptUsesEveryApprovedEditTimingContractCapabilityAndExplicitComponents()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2EditTimingProof.ps1"));

        foreach (var capability in new[]
        {
            "Video.Frame.ExtractExact",
            "Timeline.Trim.Exact",
            "Timeline.Concat.NormalizeAndContinueTimestamps",
            "Audio.Mix.Deterministic"
        })
        {
            Assert.Contains(capability, script);
        }

        foreach (var component in new[] { "select=eq(n\\,1)", "trim=start_frame=1:end_frame=3", "atrim=start_sample=1920:end_sample=5760", "concat=n=2:v=1:a=1", "amix=inputs=2:normalize=0" })
        {
            Assert.Contains(component, script);
        }

        Assert.Contains("commands = $script:commands", script);
        Assert.Contains("maps = @('0:v:0','0:a:0','1:v:0','2:a:0')", script);
        Assert.DoesNotContain("maps = @('0:v:0','0:a:0','1:v:0','1:a:0')", script);
        Assert.Contains("artifacts = $artifacts", script);
        Assert.Contains("capabilities = $results", script);
        Assert.Contains("Move-Atomic", script);
    }

    [Gate0RuntimeFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void EditTimingProofEmitsPassingEvidenceAndRejectsStaleOutput()
    {
        var runtimeRoot = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(runtimeRoot));

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-EditTimingTest", Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(temporaryRoot, "fixtures");
        var proofRoot = Path.Combine(temporaryRoot, "proof");
        var staleRoot = Path.Combine(temporaryRoot, "stale-output");

        try
        {
            var ffmpeg = Path.Combine(runtimeRoot!, "bin", "ffmpeg.exe");
            var ffprobe = Path.Combine(runtimeRoot!, "bin", "ffprobe.exe");
            var fixtureResult = RunPowerShell(
                RepositoryPath("eng", "gate0", "Generate-Fixtures.ps1"),
                "-FfmpegPath", ffmpeg,
                "-FfprobePath", ffprobe,
                "-ApprovedRuntimeRoot", runtimeRoot!,
                "-OutputDirectory", fixtureRoot);
            Assert.True(fixtureResult.ExitCode == 0, fixtureResult.AllOutput);

            var proofResult = RunPowerShell(
                RepositoryPath("eng", "gate0", "Invoke-P2EditTimingProof.ps1"),
                "-RuntimeRoot", runtimeRoot!,
                "-FixtureRoot", fixtureRoot,
                "-OutputDirectory", proofRoot);
            Assert.True(proofResult.ExitCode == 0, proofResult.AllOutput);

            using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(proofRoot, "p2-edit-timing-proof.json")));
            var capabilities = evidence.RootElement.GetProperty("capabilities").EnumerateArray().ToArray();
            Assert.Equal(
                ["Video.Frame.ExtractExact", "Timeline.Trim.Exact", "Timeline.Concat.NormalizeAndContinueTimestamps", "Audio.Mix.Deterministic"],
                capabilities.Select(capability => capability.GetProperty("id").GetString()));
            Assert.All(capabilities, capability =>
            {
                Assert.Equal("pass", capability.GetProperty("status").GetString());
                Assert.True(capability.GetProperty("detail").TryGetProperty("oracle", out var oracle));
                Assert.False(string.IsNullOrWhiteSpace(oracle.GetString()));
            });

            var concat = capabilities.Single(capability => capability.GetProperty("id").GetString() == "Timeline.Concat.NormalizeAndContinueTimestamps").GetProperty("detail");
            Assert.Equal(0.12, concat.GetProperty("segmentBoundarySeconds").GetDouble(), precision: 3);
            Assert.Equal("F1 authored color bars", concat.GetProperty("identities").GetProperty("frames0To2").GetString());
            Assert.Equal("F2 authored portrait with letterbox", concat.GetProperty("identities").GetProperty("frames3To5").GetString());
            var audioTiming = concat.GetProperty("audioTiming");
            Assert.Equal(48000, audioTiming.GetProperty("sampleRate").GetInt32());
            Assert.Equal(2, audioTiming.GetProperty("channels").GetInt32());
            Assert.Equal(11520, audioTiming.GetProperty("expectedSampleCountPerChannel").GetInt64());
            Assert.Equal(11520, audioTiming.GetProperty("actualSampleCountPerChannel").GetInt64());
            Assert.Equal(0, audioTiming.GetProperty("startSeconds").GetDouble(), precision: 3);
            Assert.Equal(0.24, audioTiming.GetProperty("endSeconds").GetDouble(), precision: 3);
            Assert.True(audioTiming.GetProperty("crossesBoundary").GetBoolean());
            Assert.Equal(11520 * 2 * 2, audioTiming.GetProperty("decodedPcmByteCount").GetInt64());
            var concatCommand = evidence.RootElement.GetProperty("commands").EnumerateArray().Single(command => command.GetProperty("name").GetString() == "concat-normalized");
            Assert.Equal(["0:v:0", "0:a:0", "1:v:0", "2:a:0"], concatCommand.GetProperty("components").GetProperty("maps").EnumerateArray().Select(value => value.GetString()));

            Directory.CreateDirectory(staleRoot);
            File.WriteAllText(Path.Combine(staleRoot, "stale.txt"), "stale evidence must be rejected");
            var staleResult = RunPowerShell(
                RepositoryPath("eng", "gate0", "Invoke-P2EditTimingProof.ps1"),
                "-RuntimeRoot", runtimeRoot!,
                "-FixtureRoot", fixtureRoot,
                "-OutputDirectory", staleRoot);
            Assert.NotEqual(0, staleResult.ExitCode);
            Assert.Contains("new or empty", staleResult.AllOutput, StringComparison.OrdinalIgnoreCase);

            var forgedFixtureRoot = Path.Combine(temporaryRoot, "forged-fixtures");
            Assert.True(GenerateFixtures(runtimeRoot!, forgedFixtureRoot).ExitCode == 0);
            var forgedReportPath = Path.Combine(forgedFixtureRoot, "generated-fixture-report.json");
            var forgedReport = JsonNode.Parse(File.ReadAllText(forgedReportPath))!.AsObject();
            forgedReport["approvedInventory"]!["sha256"] = new string('0', 64);
            File.WriteAllText(forgedReportPath, forgedReport.ToJsonString());
            var forgedResult = RunProof(runtimeRoot!, forgedFixtureRoot, Path.Combine(temporaryRoot, "forged-proof"));
            Assert.NotEqual(0, forgedResult.ExitCode);
            Assert.Contains("exact checked-in approved inventory", forgedResult.AllOutput, StringComparison.OrdinalIgnoreCase);

            var truncatedFixtureRoot = Path.Combine(temporaryRoot, "truncated-fixtures");
            Assert.True(GenerateFixtures(runtimeRoot!, truncatedFixtureRoot).ExitCode == 0);
            var truncatedReportPath = Path.Combine(truncatedFixtureRoot, "generated-fixture-report.json");
            var truncatedReport = JsonNode.Parse(File.ReadAllText(truncatedReportPath))!.AsObject();
            truncatedReport["sourceFiles"]!.AsArray().RemoveAt(truncatedReport["sourceFiles"]!.AsArray().Count - 1);
            File.WriteAllText(truncatedReportPath, truncatedReport.ToJsonString());
            var truncatedResult = RunProof(runtimeRoot!, truncatedFixtureRoot, Path.Combine(temporaryRoot, "truncated-proof"));
            Assert.NotEqual(0, truncatedResult.ExitCode);
            Assert.Contains("file-set mismatch", truncatedResult.AllOutput, StringComparison.OrdinalIgnoreCase);

            var escapeFixtureRoot = Path.Combine(temporaryRoot, "escape-fixtures");
            Assert.True(GenerateFixtures(runtimeRoot!, escapeFixtureRoot).ExitCode == 0);
            var escapeReportPath = Path.Combine(escapeFixtureRoot, "generated-fixture-report.json");
            var escapeReport = JsonNode.Parse(File.ReadAllText(escapeReportPath))!.AsObject();
            escapeReport["sourceFiles"]![0]!["path"] = "../outside-fixture.bin";
            File.WriteAllText(escapeReportPath, escapeReport.ToJsonString());
            var escapeResult = RunProof(runtimeRoot!, escapeFixtureRoot, Path.Combine(temporaryRoot, "escape-proof"));
            Assert.NotEqual(0, escapeResult.ExitCode);
            Assert.Contains("unsafe path", escapeResult.AllOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static ProcessResult RunPowerShell(string script, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell proof runner.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static ProcessResult GenerateFixtures(string runtimeRoot, string fixtureRoot) =>
        RunPowerShell(
            RepositoryPath("eng", "gate0", "Generate-Fixtures.ps1"),
            "-FfmpegPath", Path.Combine(runtimeRoot, "bin", "ffmpeg.exe"),
            "-FfprobePath", Path.Combine(runtimeRoot, "bin", "ffprobe.exe"),
            "-ApprovedRuntimeRoot", runtimeRoot,
            "-OutputDirectory", fixtureRoot);

    private static ProcessResult RunProof(string runtimeRoot, string fixtureRoot, string outputRoot) =>
        RunPowerShell(
            RepositoryPath("eng", "gate0", "Invoke-P2EditTimingProof.ps1"),
            "-RuntimeRoot", runtimeRoot,
            "-FixtureRoot", fixtureRoot,
            "-OutputDirectory", outputRoot);

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + Environment.NewLine + StandardError;
    }
}
