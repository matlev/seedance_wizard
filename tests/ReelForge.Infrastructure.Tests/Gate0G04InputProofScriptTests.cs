using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04InputProofScriptTests
{
    [Fact]
    public void RunnerRetainsApprovedProofBoundariesAndIntegrationSeams()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-P2G04InputProof.ps1"));

        foreach (var required in new[]
        {
            "Validate-P2Runtime.ps1",
            "g0.4-input-proof-contract.json",
            "fixture-source-inventory.json",
            "Read-G04InputContract",
            "Test-G04FixtureClosure",
            "New-G04CaseArtifact",
            "Test-G04CaseEvidence",
            "Test-G04SelectionCases",
            "Test-G04ClassificationCases",
            "ArtifactsByCase",
            "CaseById",
            "blocked-fixture-provenance",
            "executedSemanticProof",
            "semanticCapabilityProven=$false",
            "Get-G04ConcreteDemuxer",
            "Split(',')",
            "observedListingToken",
            "nativeDecodersUnderTest",
            "componentRoles.fixtureProductionOnly",
            "completed-with-blockers",
            "completed-with-failures",
            "Get-G04CommandEvidence",
            "Get-G04GeneratedArtifacts",
            "$Context.Media, $Context.Logs, $Context.Work",
            "No shipping-runtime, bundling, redistribution",
        }) Assert.Contains(required, script);

        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerAndModulesParseWithoutPowerShellErrors()
    {
        foreach (var path in new[]
        {
            PathInRepo("eng", "gate0", "Invoke-P2G04InputProof.ps1"),
            PathInRepo("eng", "gate0", "input-proof", "Common.ps1"),
            PathInRepo("eng", "gate0", "input-proof", "Authoring.ps1"),
            PathInRepo("eng", "gate0", "input-proof", "Oracles.ps1"),
            PathInRepo("eng", "gate0", "input-proof", "Policy.ps1"),
        })
        {
            var quotedPath = path.Replace("'", "''", StringComparison.Ordinal);
            var result = RunPowerShell($"$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('{quotedPath}',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){{$errors|% Message;exit 1}}");
            Assert.True(result.ExitCode == 0, $"PowerShell parser rejected {path}:{Environment.NewLine}{result.Output}");
        }
    }

    [Fact]
    public void RunnerRejectsRepositoryOutputBeforeRuntimePreflight()
    {
        var result = RunRunner("C:\\not-a-runtime", "C:\\not-a-fixtures", PathInRepo());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the repository", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerWritesTruthfulPreflightEvidence()
    {
        var output = Path.Combine(Path.GetTempPath(), "ReelForge-G0-G04-input-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = RunRunner("C:\\not-a-runtime", "C:\\not-a-fixtures", output);
            Assert.NotEqual(0, result.ExitCode);
            using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "g0.4-input-proof-evidence.json")));
            var root = evidence.RootElement;
            Assert.Equal("preflight-failed", root.GetProperty("run").GetProperty("status").GetString());
            Assert.Empty(root.GetProperty("capabilities").EnumerateArray());
            Assert.Equal("not-run", root.GetProperty("componentPresence").GetProperty("status").GetString());
            Assert.Contains("not the selected shipping runtime", root.GetProperty("statement").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(root.GetProperty("limitations").EnumerateArray(), item => item.GetString()!.Contains("without substitution", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    private static (int ExitCode, string Output) RunRunner(string runtime, string fixtures, string output)
    {
        var script = PathInRepo("eng", "gate0", "Invoke-P2G04InputProof.ps1");
        return RunProcess("pwsh", ["-NoProfile", "-File", script, "-RuntimeRoot", runtime, "-FixtureRoot", fixtures, "-OutputDirectory", output]);
    }

    private static (int ExitCode, string Output) RunPowerShell(string command) =>
        RunProcess("pwsh", ["-NoProfile", "-Command", command]);

    private static (int ExitCode, string Output) RunProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
