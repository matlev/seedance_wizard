using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0P2FullProofScriptTests
{
    [Fact]
    public void FullProofOrchestratorAggregatesEveryModuleWithoutPromotingPendingWork()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2FullProof.ps1"));

        Assert.Contains("Invoke-P2SemanticProof.ps1", script);
        Assert.Contains("Invoke-P2EditTimingProof.ps1", script);
        Assert.Contains("Invoke-P2VisualProof.ps1", script);
        Assert.Contains("Invoke-P2DeliveryProof.ps1", script);
        Assert.Contains("exactly 15 unique reviewed capability IDs", script);
        Assert.Contains("fixtureEvidence.capabilityVerdicts", script);
        Assert.Contains("fixtureEvidence.inspectionReadiness", script);
        Assert.Contains("dedicated reviewed inspection-readiness record", script);
        Assert.DoesNotContain("mediaFixtureProofs", script);
        Assert.Contains("incomplete-with-explicit-blockers", script);
        Assert.Contains("Text.Render.UnicodeTitlesAndCaptions", script);
        Assert.Contains("Delivery.Validate.IndependentPlayback", script);
        Assert.Contains("Project.LongForm.Integrity", script);
        Assert.Contains("not a shipping-runtime, public-distribution, or legal approval", script);
    }

    [Gate0RuntimeFact]
    public void FullProofAgainstApprovedP2EmitsOneTruthfulVerdictPerCapability()
    {
        var runtimeRoot = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(runtimeRoot));

        var output = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-FullProofTest", Guid.NewGuid().ToString("N"));
        var result = RunPowerShell(
            RepositoryPath("eng", "gate0", "Invoke-P2FullProof.ps1"),
            "-RuntimeRoot", runtimeRoot,
            "-OutputDirectory", output);

        Assert.True(result.ExitCode == 0, result.StandardError);
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "p2-full-proof-evidence.json")));
        var root = evidence.RootElement;
        Assert.Equal("incomplete-with-explicit-blockers", root.GetProperty("aggregateStatus").GetString());
        var verdicts = root.GetProperty("capabilityVerdicts").EnumerateArray().ToArray();
        Assert.Equal(15, verdicts.Length);
        Assert.Equal(15, verdicts.Select(v => v.GetProperty("capabilityId").GetString()).Distinct().Count());
        var inspection = verdicts.Single(v => v.GetProperty("capabilityId").GetString() == "Media.Inspect.StructureAndTiming");
        Assert.Equal("passed", inspection.GetProperty("status").GetString());
        Assert.Equal("dedicated-inspection-proof", inspection.GetProperty("source").GetString());
        var inspectionDetails = inspection.GetProperty("details");
        Assert.True(inspectionDetails.GetProperty("executedInspectionProof").GetBoolean());
        Assert.Equal(["F1", "F7", "F8"], inspectionDetails.GetProperty("fixtureIds").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(3, inspectionDetails.GetProperty("acceptance").GetArrayLength());
        Assert.Equal("1/1000", inspectionDetails.GetProperty("fixtures").GetProperty("F7").GetProperty("containerTimeBase").GetString());
        Assert.Contains(verdicts, v => v.GetProperty("capabilityId").GetString() == "Video.Composite.TransformAlphaAndColor" && v.GetProperty("status").GetString() == "passed");
        Assert.Contains(verdicts, v => v.GetProperty("capabilityId").GetString() == "Text.Render.UnicodeTitlesAndCaptions" && v.GetProperty("status").GetString() == "approved-proof-pending");
        Assert.Contains(verdicts, v => v.GetProperty("capabilityId").GetString() == "Delivery.Validate.IndependentPlayback" && v.GetProperty("status").GetString() == "not-run");
        Assert.Contains(verdicts, v => v.GetProperty("capabilityId").GetString() == "Project.LongForm.Integrity" && v.GetProperty("status").GetString() == "not-run");
    }

    private static (int ExitCode, string StandardError) RunPowerShell(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var path = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(path, "ReelForge.sln"))) path = Directory.GetParent(path)!.FullName;
        return segments.Aggregate(path, Path.Combine);
    }
}
