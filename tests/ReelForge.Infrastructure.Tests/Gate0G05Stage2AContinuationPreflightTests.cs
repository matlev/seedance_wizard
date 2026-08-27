using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AContinuationPreflightTests
{
    [Fact]
    public void PreflightIsSyntaxValidAndKeepsTheNoMediaBoundary()
    {
        var path = PathInRepo("eng", "gate0", "Test-G05Stage2AContinuationPreflight.ps1");
        var parse = Run("pwsh", ["-NoProfile", "-Command", "$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''", StringComparison.Ordinal) + "',[ref]$tokens,[ref]$errors)|Out-Null;$errors|% Message;if($errors.Count){exit 1}"]);
        Assert.Equal(0, parse.ExitCode);

        var script = File.ReadAllText(path);
        foreach (var expected in new[] {
            "G05Stage2AContinuationHelpers.psm1", "Read-G05Stage2AContinuationAuthorization",
            "Test-Gate0EvidenceContainment.ps1", "Test-Gate0EvidenceV2Containment.ps1",
            "Test-Gate0ArtifactRetention.ps1", "Test-Gate0ArtifactManifest.ps1",
            "g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json",
            "P2.BtbnLgplShared.WindowsX64.20260820", "805306368", "78538843", "38878888",
            "3758096384", "logicalProcessorCount -ne 16", "ffmpeg", "ffprobe",
            "noMediaInvoked", "RequireRemoteVerification", "-Remote", "REELFORGE_GATE0_TEST_INJECTION",
            "AllowCompletedContinuationAudit", "Completed continuation state requires the explicit AllowCompletedContinuationAudit switch.",
            "g0.5-stage2a-continuation-preflight-evidence.json", "Convert-ToPortableFailure" })
            Assert.Contains(expected, script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("catch { $null }", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentV2TotalsAreExplicitlyConsumedAsTheContinuationPredecessor()
    {
        using var root = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "evidence", "v2", "root-index.json")));
        Assert.Equal(2, root.RootElement.GetProperty("totals").GetProperty("runCount").GetInt32());
        Assert.Equal(8_136_177, root.RootElement.GetProperty("totals").GetProperty("logicalArtifactBytes").GetInt64());
        Assert.Equal(2, root.RootElement.GetProperty("runs").GetArrayLength());

        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Test-G05Stage2AContinuationPreflight.ps1"));
        Assert.Contains("v2Result.logicalArtifactBytes", script, StringComparison.Ordinal);
        Assert.Contains("78538843+[int64]$v2Result.logicalArtifactBytes", script, StringComparison.Ordinal);
        Assert.Contains("exact authorized continuation shard state", script, StringComparison.Ordinal);
        Assert.Contains("$maximumContinuationRuns=if($AllowCompletedContinuationAudit){12}else{11}", script, StringComparison.Ordinal);
        Assert.Contains("continuationCellsRemaining", script, StringComparison.Ordinal);
        Assert.Contains("requiredReservationForRemainingCellsBytes", script, StringComparison.Ordinal);
    }

    [Fact]
    public void V5ReevaluationBindingRequiresBothRoutesAndTheNoMediaResult()
    {
        using var summary = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json")));
        var root = summary.RootElement;
        Assert.Equal("passed-no-media-continuation-prerequisite", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("routes").GetArrayLength());
        Assert.All(root.GetProperty("routes").EnumerateArray(), route =>
        {
            Assert.True(route.GetProperty("v5Passed").GetBoolean());
            Assert.Equal(0, route.GetProperty("failureCount").GetInt32());
        });
        Assert.False(root.GetProperty("executionBoundary").GetProperty("reencodePerformed").GetBoolean());
        Assert.False(root.GetProperty("executionBoundary").GetProperty("mediaProcessesStarted").GetBoolean());
        Assert.True(root.GetProperty("retention").GetProperty("localByteVerified").GetBoolean());
        Assert.True(root.GetProperty("retention").GetProperty("r2IndependentlyRetrievedAndByteVerified").GetBoolean());

        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Test-G05Stage2AContinuationPreflight.ps1"));
        Assert.Contains("Both exact retained V5 routes must pass with zero failures", script, StringComparison.Ordinal);
        Assert.Contains("V5 reevaluation does not bind the immutable V2 infrastructure shard", script, StringComparison.Ordinal);
        Assert.Contains("originalV3RecordsModified", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteVerificationIsOptionalForTestsButRequiredWhenRequested()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Test-G05Stage2AContinuationPreflight.ps1"));
        Assert.Contains("if ($RequireRemoteVerification)", script, StringComparison.Ordinal);
        Assert.Contains("remoteByteVerificationPerformed", script, StringComparison.Ordinal);
        Assert.Contains("Exact remote V2 byte verification did not complete", script, StringComparison.Ordinal);
        Assert.Contains("remoteVerificationRequired", script, StringComparison.Ordinal);
        Assert.Contains("Exact remote V1 evidence byte verification did not complete", script, StringComparison.Ordinal);
        Assert.Contains("Exact remote durable source-inventory verification did not complete", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ResumePolicyBindsOnlyTheExactOrderedSchedulePrefix()
    {
        using var schedule = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2a-continuation-schedule.json")));
        var expected = schedule.RootElement.GetProperty("attempts").EnumerateArray()
            .GroupBy(row => row.GetProperty("proofRunId").GetString())
            .Select(group => (Proof: group.Key, Cell: group.First().GetProperty("cellId").GetString()))
            .ToArray();
        Assert.Equal(12, expected.Length);

        using var v2 = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "evidence", "v2", "root-index.json")));
        var actual = v2.RootElement.GetProperty("runs").EnumerateArray()
            .Where(run => run.GetProperty("runKind").GetString() == "stage2a-continuation-cell")
            .Select(run => (Proof: run.GetProperty("proofRunId").GetString(), Cell: run.GetProperty("cellId").GetString()))
            .ToArray();
        Assert.Empty(actual); // Pre-first-cell state is the valid empty prefix.
        Assert.Equal(expected.Take(actual.Length), actual);

        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Test-G05Stage2AContinuationPreflight.ps1"));
        Assert.Contains("exact ordered prefix of the frozen continuation schedule", script, StringComparison.Ordinal);
        Assert.Contains("continuationRuns[$i].proofRunId", script, StringComparison.Ordinal);
        Assert.Contains("continuationRuns[$i].cellId", script, StringComparison.Ordinal);
        Assert.Contains("continuationRuns[$i].evidenceGroupId", script, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
