using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AExecutorTests
{
    [Fact]
    public void RetentionStateMachineClassifiesTheWholeCellAfterIndependentValidation()
    {
        var command = Imports() + "; " +
            "$rows=@();1..6|ForEach-Object{$rows += [pscustomobject]@{attemptId=('stage2a-'+$_);globalOrdinal=$_;phase=if($_ -eq 1){'warmup'}else{'measured'};disposition='passed'}}; $plan=Resolve-G05Stage2ACellRetentionPlan $rows; [pscustomobject]@{warmup=$plan.attempts[0].retentionClass;ordinary=$plan.attempts[1].retentionClass;repeat=$plan.attempts[2].retentionClass;reference=$plan.attempts[0].completeClosureReference}|ConvertTo-Json -Compress";
        using var json = JsonDocument.Parse(Run(command));
        Assert.Equal("compact", json.RootElement.GetProperty("warmup").GetString());
        Assert.Equal("compact", json.RootElement.GetProperty("repeat").GetString());
        Assert.Equal("stage2a-2", json.RootElement.GetProperty("reference").GetString());
        Assert.Equal("complete", json.RootElement.GetProperty("ordinary").GetString());
    }

    [Fact]
    public void StateMachineRejectsMultipleOrdinaryClosuresAndRecognizesIntegrityFailures()
    {
        var command = Imports() + "; " +
            "$a=[pscustomobject]@{attemptId='a';disposition='passed';phase='measured';retentionClass='complete'}; $b=[pscustomobject]@{attemptId='b';disposition='passed';phase='measured';retentionClass='complete'}; " +
            "$duplicate=$false;try{Get-G05Stage2ACompleteClosureReference @($a,$b)|Out-Null}catch{$duplicate=$true}; " +
            "$integrity=Test-G05Stage2ADeterministicIntegrityFailure ([pscustomobject]@{disposition='semantically-divergent'}); $cleanup=Test-G05Stage2ADeterministicIntegrityFailure ([pscustomobject]@{disposition='cleanup-failed'}); $slow=Test-G05Stage2ADeterministicIntegrityFailure ([pscustomobject]@{disposition='failed'}); [pscustomobject]@{duplicate=$duplicate;integrity=$integrity;cleanup=$cleanup;slow=$slow}|ConvertTo-Json -Compress";
        using var json = JsonDocument.Parse(Run(command));
        Assert.True(json.RootElement.GetProperty("duplicate").GetBoolean());
        Assert.True(json.RootElement.GetProperty("integrity").GetBoolean());
        Assert.False(json.RootElement.GetProperty("cleanup").GetBoolean());
        Assert.False(json.RootElement.GetProperty("slow").GetBoolean());
    }

    [Fact]
    public void ContentNormalizedAudioIdentityExcludesOnlyTheApprovedRawTail()
    {
        var path = Path.Combine(Path.GetTempPath(), $"g05-stage2a-audio-{Guid.NewGuid():N}.s16le");
        try
        {
            File.WriteAllBytes(path, [1, 0, 2, 0, 3, 0, 4, 0]);
            var command = Imports() + "; " +
                $"[pscustomobject]@{{normalized=(Get-G05Stage2AContentNormalizedAudioHash '{Escape(path)}' 4 1);expected=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]@(1,0,2,0)))}}|ConvertTo-Json -Compress";
            using var json = JsonDocument.Parse(Run(command));
            Assert.Equal(json.RootElement.GetProperty("expected").GetString(), json.RootElement.GetProperty("normalized").GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SummaryValidationRequiresSixPassingGatesAndSemanticHashes()
    {
        var command = Imports() + "; " +
            "$good=[pscustomobject]@{attemptId='a';globalOrdinal=1;disposition='passed';phase='measured';selectedComponents=[pscustomobject]@{};commands=[pscustomobject]@{};encodedByteEqualityClaim=$false;cleanup=[pscustomobject]@{processTreeRootExited=$true;processTreeOrphanFree=$true;noUnvalidatedPartialOutput=$true};validations=[pscustomobject]@{encode=$true;probe=$true;timing=$true;visual=$true;audio=$true;cleanup=$true};hashes=[pscustomobject]@{outputSha256='a';frameProbeSha256='b';packetProbeSha256='c';decodedVideoIdentitySha256='d';decodedAudioRawSha256='e';decodedAudioContentNormalizedSha256='f'}}; Assert-G05Stage2AAttemptSummary $good; $bad=$good.PSObject.Copy();$bad.validations.cleanup=$false;$blocked=$false;try{Assert-G05Stage2AAttemptSummary $bad}catch{$blocked=$true};$blocked";
        Assert.Equal("True", Run(command).Trim());
    }

    [Fact]
    public void ExactTimingRejectsWrongFrameCountWithoutMedia()
    {
        var command = Imports() + "; " +
            "$stream=[pscustomobject]@{time_base='1/1000';duration_ts=30000};$frames=0..748|ForEach-Object{[pscustomobject]@{best_effort_timestamp=($_*40);pkt_duration=40}};$blocked=$false;try{Get-G05Stage2AExactVideoTiming $stream @($frames)|Out-Null}catch{$blocked=$true};$blocked";
        Assert.Equal("True", Run(command).Trim());
    }

    [Fact]
    public void RunnerRejectsUnapprovedRootsBeforeAnyMediaCanBeUsed()
    {
        var runner = PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1");
        var result = RunResult($"& '{Escape(runner)}' -ExecuteMedia -RuntimeRoot 'C:\\not-used' -ArtifactRoot 'C:\\not-used' -StagingRoot 'C:\\not-used'");
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Output.Contains("exact existing non-reparse", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("execution authorization", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("implementation is pending", StringComparison.OrdinalIgnoreCase), result.Output);
        Assert.DoesNotContain("ffmpeg.exe", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerUsesApprovedFailureTaxonomyAndPrecommitsCellReceiptLinkage()
    {
        var text = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1")) +
            File.ReadAllText(PathInRepo("eng", "gate0", "G05Stage2ASemanticExecutor.psm1"));
        foreach (var disposition in new[] { "'failed'", "'blocked'", "'cleanup-failed'", "'orphan-producing'", "'byte-divergent'", "'semantically-divergent'", "'structurally-divergent'" })
            Assert.Contains(disposition, text, StringComparison.Ordinal);
        Assert.Contains("Test-G05Stage2ADeterministicIntegrityFailure", text, StringComparison.Ordinal);
        Assert.Contains("aggregate-precommit-run-result.json", text, StringComparison.Ordinal);
        Assert.Contains("entryIdentity='The enclosing immutable root-index entry", text, StringComparison.Ordinal);
        Assert.Contains("finalShardReceipt=$cells[-1].shard", text, StringComparison.Ordinal);
        Assert.DoesNotContain("failed-integrity", text, StringComparison.Ordinal);
        Assert.DoesNotContain("failed-oracle", text, StringComparison.Ordinal);
        Assert.DoesNotContain("failed-command", text, StringComparison.Ordinal);
        Assert.DoesNotContain("failed-cleanup", text, StringComparison.Ordinal);
        Assert.DoesNotContain("blocked-infrastructure", text, StringComparison.Ordinal);
    }

    private static string Imports() => $"Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2AMatrixHelpers.psm1"))}'; Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"))}'; Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2ASemanticHelpers.psm1"))}'; Import-Module '{Escape(PathInRepo("eng", "gate0", "G05Stage2ASemanticExecutor.psm1"))}'";
    private static string Run(string command)
    {
        var result = RunResult(command);
        Assert.True(result.ExitCode == 0, result.Output);
        return result.Output;
    }
    private static (int ExitCode, string Output) RunResult(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(command);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, output);
    }
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
