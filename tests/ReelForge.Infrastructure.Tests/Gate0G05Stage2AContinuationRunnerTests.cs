using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AContinuationRunnerTests
{
    [Fact]
    public void FixedContinuationScheduleHasOnlyTheApprovedSeventyTwoRows()
    {
        using var schedule = ReadJson("eng", "gate0", "g0.5-stage2a-continuation-schedule.json");
        var rows = schedule.RootElement.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal("Gate0.G05.Stage2A.ContinuationSchedule.V1", schedule.RootElement.GetProperty("scheduleId").GetString());
        Assert.Equal(72, rows.Length);
        Assert.Equal(12, rows.Select(row => row.GetProperty("cellId").GetString()).Distinct().Count());
        Assert.Equal(12, rows.Count(row => row.GetProperty("phase").GetString() == "warmup"));
        Assert.Equal(60, rows.Count(row => row.GetProperty("phase").GetString() == "measured"));
        Assert.DoesNotContain(rows, row =>
            (row.GetProperty("workloadId").GetString() == "baseline-1v1a" || row.GetProperty("workloadId").GetString() == "typical-2v4a") &&
            row.GetProperty("resolutionId").GetString() == "720p");
        foreach (var cell in rows.GroupBy(row => row.GetProperty("cellId").GetString()))
        {
            Assert.Equal(6, cell.Count());
            Assert.Single(cell.Select(row => row.GetProperty("proofRunId").GetString()).Distinct());
            Assert.Equal("warmup", cell.OrderBy(row => row.GetProperty("continuationOrdinal").GetInt32()).First().GetProperty("phase").GetString());
        }
    }

    [Fact]
    public void RunnerDefaultsToContractOnlyWithoutMediaOrCredentials()
    {
        var result = RunPwsh($"& '{Escape(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"))}' | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal("contract-only", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("noMediaExecuted").GetBoolean());
        Assert.Equal(72, root.GetProperty("schedule").GetProperty("attemptCount").GetInt32());
        Assert.Equal(12, root.GetProperty("schedule").GetProperty("cellCount").GetInt32());
        Assert.Equal("Add-Gate0EvidenceV2Shard.ps1", root.GetProperty("retention").GetProperty("writer").GetString());
    }

    [Fact]
    public void ContractOnlyBootstrapLoadsNoRepositoryModule()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));
        var contractOnlyReturn = source.IndexOf("if(-not$ExecuteMedia){[pscustomobject]$contract;return}", StringComparison.Ordinal);
        var firstImport = source.IndexOf("Import-Module", StringComparison.Ordinal);

        Assert.True(contractOnlyReturn >= 0 && firstImport > contractOnlyReturn,
            "Contract-only mode must validate and return before importing repository modules.");
        Assert.Contains("Read-G05Stage2AContinuationBootstrapAuthorization", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("Read-G05Stage2AContinuationBootstrapAuthorization", StringComparison.Ordinal) < firstImport,
            "Live authorization bootstrap must occur before repository imports.");
    }

    [Fact]
    public void EveryPostBootstrapModuleImportIsBoundByTheExactBootstrapRoleMap()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));
        var importedModules = Regex.Matches(source, "Import-Module \\(Join-Path \\$PSScriptRoot '([^']+)'\\)")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(importedModules);
        Assert.DoesNotContain("G05Stage2AMatrixHelpers.psm1", importedModules);
        foreach (var module in importedModules)
        {
            Assert.Contains("eng/gate0/" + module, source, StringComparison.Ordinal);
        }
        Assert.Contains("G05MarkerSurvivabilityHelpers.psm1", importedModules, StringComparer.Ordinal);
        Assert.Contains("function Get-G05Stage2AStatistics", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostBootstrapImportsKeepTheSemanticExecutorIndependentOfMatrixHelpers()
    {
        var contract = PathInRepo("eng", "gate0", "g0.5-stage2a-retention-contract.json");
        var command = $@"
$ErrorActionPreference = 'Stop'
$before = @(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2AContinuationHelpers.psm1"))}' -Force
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"))}' -Force
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2ASemanticHelpers.psm1"))}' -Force
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2ASemanticExecutor.psm1"))}' -Force
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2AV5AudioOracle.psm1"))}' -Force
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2AV5FreezeValidation.psm1"))}' -Force
Import-Module '{Escape(PathInRepo("eng", "gate0", "G05MarkerSurvivabilityHelpers.psm1"))}' -Force
$result = Read-G05Stage2ARetentionContract '{Escape(contract)}'
$after = @(Get-Process -Name ffmpeg,ffprobe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
[pscustomobject]@{{
    matrixHelpersLoaded = @((Get-Module | Where-Object {{ $_.Path -like '*G05Stage2AMatrixHelpers.psm1' }})).Count -gt 0
    contractId = [string]$result.Contract.contractId
    contractSha256 = [string]$result.Sha256
    newMediaProcessIds = @($after | Where-Object {{ $_ -notin $before }})
}} | ConvertTo-Json -Compress
";

        var result = RunPwsh(command);
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.False(root.GetProperty("matrixHelpersLoaded").GetBoolean());
        Assert.Equal("Gate0.G05.Stage2A.Retention.V1", root.GetProperty("contractId").GetString());
        Assert.Equal("4E27689ECF0ACE0996C682D2F42B43E7D12A184F46EEFD8605A46EC687270E98", root.GetProperty("contractSha256").GetString());
        Assert.Empty(root.GetProperty("newMediaProcessIds").EnumerateArray());
    }

    [Fact]
    public void RunnerKeepsBootstrapAndEvidenceContainmentFailClosed()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));

        Assert.DoesNotContain("-NoClobber", source, StringComparison.Ordinal);
        Assert.Contains("Continuation preflight evidence destination already exists.", source, StringComparison.Ordinal);
        Assert.Contains("Join-Path $PSScriptRoot 'evidence/v2'", source, StringComparison.Ordinal);
        Assert.Contains("Retained continuation shard is no longer hash-bound", source, StringComparison.Ordinal);
        Assert.Contains("$archiveHash-ne[string]$Reevaluation.retention.archiveSha256", source, StringComparison.Ordinal);
        Assert.Contains("V5 closure extraction root escapes the validated staging root", source, StringComparison.Ordinal);
        Assert.Contains("Assert-G05Stage2AContinuationNoActiveMedia 'final continuation accounting'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunnerContainsTheRequiredFailClosedActivationAndContainmentSeams()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));
        foreach (var required in new[]
        {
            "[switch] $ExecuteMedia", "Read-G05Stage2AContinuationBootstrapSchedule", "Read-G05Stage2AContinuationAuthorization",
            "Test-G05Stage2AContinuationPreflight.ps1", "Add-Gate0EvidenceV2Shard.ps1", "-Remote",
            "g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json", "Assert-G05Stage2AV5StressOverlay",
            "freshWarmupsAlwaysExecute", "incrementalCombinedV1V2Headroom", "localAndRemoteValidationAfterEachAppend"
        })
        {
            Assert.Contains(required, source, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("Add-Gate0EvidenceShard.ps1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunnerOwnsItsSemanticLoopWithoutV1RedirectionOrShadowing()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));

        Assert.Contains("function Test-G05Stage2AContinuationSemanticCell", source, StringComparison.Ordinal);
        Assert.Contains("Test-G05Stage2AV5StressAudio", source, StringComparison.Ordinal);
        Assert.Contains("Add-Gate0EvidenceV2Shard.ps1", source, StringComparison.Ordinal);
        Assert.Contains("Invoke-G05Stage2AContinuationPostAppendValidation", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-G05Stage2AMatrix.ps1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("function Join-Path", source, StringComparison.Ordinal);
        Assert.DoesNotContain("function Test-G05SmokeAudio", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecutionAdapter", source, StringComparison.Ordinal);
        Assert.DoesNotContain(". $v1Runner", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RunnerRetainsTheRequiredContinuationSemanticAndAccountingBoundaries()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));

        foreach (var required in new[]
        {
            "g0.5-lossy-audio-oracle-amendment-v5.json",
            "frozenInputs|Where-Object",
            "Read-G05Stage2ARetentionContract",
            "compactPassingRepeatMaximumBytes",
            "decodedAudioContentNormalizedSha256",
            "process-samples.ndjson",
            "Test-G05Stage2ADeterministicIntegrityFailure",
            "Restore-G05Stage2AContinuationSuspendedRoutes",
            "Assert-G05Stage2AContinuationV4Closure",
            "Assert-G05Stage2AContinuationV5Closure",
            "orphan-producing",
            "blocked-before-media-insufficient-global-headroom-for-worst-case-full-closure",
            "complete-output-retained-for-exceptional-oracle-or-structure-evidence",
            "cell-summary.json",
            "Get-G05Stage2AStatistics",
            "ConvertTo-G05SmokePortableTokens",
            "remainingHeadroomBytes",
            "initialPreflightEvidenceSha256",
            "resumedAttemptCount",
            "newlyProcessedAttemptCount",
            "totalRetainedAttemptCount",
        })
        {
            Assert.Contains(required, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RunnerKeepsExecutionFailuresDistinctFromOracleDivergence()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));

        var probe = source.IndexOf("Forced frame probe failed.", StringComparison.Ordinal);
        var structural = source.IndexOf("$disposition='structurally-divergent'", probe, StringComparison.Ordinal);
        Assert.True(probe >= 0 && structural > probe, "Probe execution must fail before a structural oracle disposition is possible.");

        var decode = source.IndexOf("Strict native audio decode failed.", StringComparison.Ordinal);
        var semantic = source.IndexOf("$disposition='semantically-divergent'", decode, StringComparison.Ordinal);
        Assert.True(decode >= 0 && semantic > decode, "Audio decoder execution must fail before a semantic oracle disposition is possible.");

        var visual = source.IndexOf("Test-G05Stage2AVisual", StringComparison.Ordinal);
        var visualFailed = source.LastIndexOf("$disposition='failed'", visual, StringComparison.Ordinal);
        var visualSemantic = source.IndexOf("$disposition='semantically-divergent'", visual, StringComparison.Ordinal);
        Assert.True(visual >= 0 && visualFailed >= 0 && visualSemantic > visual,
            "The strict visual helper must run while the attempt remains failed, before an oracle divergence disposition.");
    }

    [Fact]
    public void RunnerUsesTheFullFrozenV4ClosureChecks()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));

        foreach (var required in new[]
        {
            "structuredControlCount -ne 5",
            "legacyV3ControlCount -ne 12",
            "allFrozenHashesAndDispositionsPreserved",
            "allNoOverlayEffectiveOracleDispositionsPreserved",
            "controlReport.sha256 -ne [string]$freeze.controlEvidence.sha256",
            "retention.controlGroup.controlReportSha256 -ne [string]$freeze.controlEvidence.sha256",
        })
        {
            Assert.Contains(required, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RunnerPreflightsBeforeResumeReadsAndContainsRetainedRecordPaths()
    {
        var source = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AContinuation.ps1"));

        var initialPreflight = source.IndexOf("$initialPreflightOutput=Join-Path", StringComparison.Ordinal);
        var v4Closure = source.LastIndexOf("$v4Closure=Assert-G05Stage2AContinuationV4Closure", StringComparison.Ordinal);
        var v5Closure = source.LastIndexOf("$v5Closure=Assert-G05Stage2AContinuationV5Closure", StringComparison.Ordinal);
        var resumeRead = source.IndexOf("$completed=Assert-G05Stage2AContinuationResumePrefix", StringComparison.Ordinal);
        Assert.True(initialPreflight >= 0 && resumeRead > initialPreflight,
            "An initial full no-media preflight must complete before retained V2 resume reads.");
        Assert.True(v4Closure > initialPreflight && v5Closure > v4Closure && resumeRead > v5Closure,
            "V4/V5 closure must run after root-validating preflight and before resume accounting, including completed audits.");
        Assert.Contains("Retained continuation attempt record path is unsafe.", source, StringComparison.Ordinal);
        Assert.Contains("Retained continuation attempt record escapes ArtifactRoot.", source, StringComparison.Ordinal);
        Assert.Contains("$relative.Contains('\\')", source, StringComparison.Ordinal);
        Assert.DoesNotContain("executedAttemptCount", source, StringComparison.Ordinal);
    }

    private static JsonDocument ReadJson(params string[] parts) => JsonDocument.Parse(File.ReadAllText(PathInRepo(parts)));

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ReelForge.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }

    private static string Escape(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static (int ExitCode, string Output) RunPwsh(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("pwsh", $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"") }\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        });
        Assert.NotNull(process);
        var output = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
