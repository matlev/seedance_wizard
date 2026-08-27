using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AAudioDiagnosticTests
{
    [Fact]
    public void DiagnosticModuleParsesAndItsPurePcmHelpersHandleSyntheticStereoWithoutStartingProcesses()
    {
        var module = PathInRepo("eng", "gate0", "G05Stage2AAudioDiagnostic.psm1").Replace("'", "''", StringComparison.Ordinal);
        var command = $$"""
            $t=$null;$e=$null;[Management.Automation.Language.Parser]::ParseFile('{{module}}',[ref]$t,[ref]$e)|Out-Null;if($e.Count){exit 10}
            Import-Module '{{module}}' -Force
            [int16[]]$samples=@(0,0,32767,16384,0,0,16384,8192,0,0,32767,16384)
            $full=Get-G05Stage2AAudioDiagnosticRms $samples 0 0 6
            $window=Get-G05Stage2AAudioDiagnosticMinimumWindow $samples 0 0 6 2
            if($full -le 0 -or $window.startSample -ne 2 -or $window.endSampleExclusive -ne 4 -or $window.rmsFullScale -le 0){exit 11}
            exit 0
            """;
        var result = RunPowerShell(command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DiagnosticIsExplicitlyNoMediaAndDoesNotContainMediaProcessInvocationSeams()
    {
        var module = File.ReadAllText(PathInRepo("eng", "gate0", "G05Stage2AAudioDiagnostic.psm1"));
        var runner = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AAudioDiagnostic.ps1"));

        Assert.Contains("noMediaInvoked = $true", module, StringComparison.Ordinal);
        Assert.Contains("retainedDispositionChanged = $false", module, StringComparison.Ordinal);
        Assert.Contains("A-oracle-descriptor-self-inconsistency", module, StringComparison.Ordinal);
        Assert.Contains("crossRouteMateriality", module, StringComparison.Ordinal);
        Assert.Contains("Get-G05Stage2AThrowFixRegression", module, StringComparison.Ordinal);
        Assert.Contains("structuredAudioAssignedBeforeThrow = $true", module, StringComparison.Ordinal);
        Assert.Contains("passFailBlockSemanticsChanged = $false", module, StringComparison.Ordinal);
        Assert.Contains("signedCorrelation", module, StringComparison.Ordinal);
        Assert.Contains("normalizedRmsError", module, StringComparison.Ordinal);
        Assert.Contains("21ECAFCD94F71E58AA43955079EF9959C135DB12530D015E8380CFD09B5E9FBC", module, StringComparison.Ordinal);
        Assert.Contains("E2EFFD683FFE21BE902D77D7564F81C550F555C0989871C5D98B2DBE580D4CB2", module, StringComparison.Ordinal);
        Assert.Contains("otherReferenceDescriptorsRemainExactV3", module, StringComparison.Ordinal);
        Assert.Contains("rolling window", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Diagnostics.Process", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffprobe", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffprobe", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThrowFixRegressionValidatesTheExactRunnerWithoutStartingMedia()
    {
        var module = PathInRepo("eng", "gate0", "G05Stage2AAudioDiagnostic.psm1").Replace("'", "''", StringComparison.Ordinal);
        var runner = PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1").Replace("'", "''", StringComparison.Ordinal);
        var command = $"Import-Module '{module}' -Force; $r=Get-G05Stage2AThrowFixRegression '{runner}'; if(-not $r.passed -or $r.mediaInvoked -or $r.passFailBlockSemanticsChanged){{exit 31}}";
        var result = RunPowerShell(command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void DiagnosticRequiresExactRetainedSummaryAndPcmHashBindings()
    {
        var module = File.ReadAllText(PathInRepo("eng", "gate0", "G05Stage2AAudioDiagnostic.psm1"));

        Assert.Contains("$Label SHA-256 mismatch", module, StringComparison.Ordinal);
        Assert.Contains("retained decoded PCM binding is invalid", module, StringComparison.Ordinal);
        Assert.Contains("Reference self-check produced", module, StringComparison.Ordinal);
        Assert.Contains("ExpectedRetainedFindingCount = 25", module, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultSummaryPreservesTheNoMediaDispositionAndFailClosedRetentionBoundary()
    {
        using var summary = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2a-audio-diagnostic-result-summary.json")));
        var root = summary.RootElement;

        Assert.Equal("completed-no-media-retention-capacity-owner-decision-required", root.GetProperty("status").GetString());
        Assert.Equal("A-oracle-descriptor-self-inconsistency", root.GetProperty("classification").GetString());
        Assert.False(root.GetProperty("referenceSelfCheck").GetProperty("passed").GetBoolean());
        Assert.Equal(25, root.GetProperty("referenceSelfCheck").GetProperty("findingCount").GetInt32());
        Assert.False(root.GetProperty("retainedOutputComparison").GetProperty("routeDefectInferred").GetBoolean());
        Assert.Equal(0, root.GetProperty("throwRegression").GetProperty("mediaProcessesInvoked").GetInt32());
        Assert.Equal("not-written-capacity-blocked-before-mutation", root.GetProperty("retention").GetProperty("r2Status").GetString());
        Assert.True(root.GetProperty("retention").GetProperty("existingRootIndexUnchanged").GetBoolean());
        Assert.Equal(70, root.GetProperty("minimumContinuationIfApproved").GetProperty("recommendedPhysicalMediaAttempts").GetInt32());

        var currentStatus = File.ReadAllText(PathInRepo("docs", "gate-0-current-status.md"));
        Assert.Contains("gate-0-g0.5-stage2a-audio-diagnostic-results.md", currentStatus, StringComparison.Ordinal);
        Assert.Contains("45 cumulative actual media executions", currentStatus, StringComparison.Ordinal);
        Assert.Contains("72 new media executions", currentStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("115 total physical media attempts", currentStatus, StringComparison.Ordinal);

        var continuationApproval = File.ReadAllText(PathInRepo("docs", "gate-0-g0.5-stage2a-continuation-approval.md"));
        Assert.Contains("70 blocked authoritative records with no media execution", continuationApproval, StringComparison.Ordinal);
        Assert.Contains("No continuation media execution is authorized before those gates complete", currentStatus, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunPowerShell(string command) => RunProcess("pwsh", ["-NoProfile", "-Command", command]);

    private static (int ExitCode, string Output) RunProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory); return Path.Combine([directory!.FullName, .. parts]);
    }
}
