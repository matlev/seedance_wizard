using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05CalibrationTests
{
    [Fact]
    public void ContractDefinesTheBoundedSequentialFortyEightRunCalibration()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-calibration-contract.json")));
        var root = document.RootElement;
        Assert.Equal("Gate0.G05.Calibration.V1", root.GetProperty("contractId").GetString());
        var calibration = root.GetProperty("calibration");
        Assert.Equal(1, calibration.GetProperty("concurrentProcessCount").GetInt32());
        Assert.Equal(1, calibration.GetProperty("warmupRepetitions").GetInt32());
        Assert.Equal(2, calibration.GetProperty("measuredRepetitions").GetInt32());
        Assert.Equal(2, calibration.GetProperty("resolutions").GetArrayLength());
        Assert.Equal(4, calibration.GetProperty("threadPolicies").GetArrayLength());
        Assert.Equal(2, root.GetProperty("routes").GetArrayLength());
        Assert.Equal(48, 2 * 2 * 4 * (1 + 2));
        Assert.Contains("60-120 minute execution", calibration.GetProperty("prohibitedWithoutNextOwnerCheckpoint").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(500, root.GetProperty("measurement").GetProperty("sampleIntervalMilliseconds").GetInt32());
        Assert.Equal(200, root.GetProperty("outputOracle").GetProperty("expectedFrameCount").GetInt32());
        Assert.Equal(384000, root.GetProperty("outputOracle").GetProperty("expectedAudioSamplesPerChannel").GetInt32());
    }

    [Fact]
    public void RunnerKeepsPreflightStagingMeasurementOracleAndRetentionBoundariesExplicit()
    {
        var scriptPath = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1");
        var parse = RunPowerShell($"$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('{scriptPath.Replace("'", "''", StringComparison.Ordinal)}',[ref]$tokens,[ref]$errors)|Out-Null;$errors|% Message;if($errors.Count){{exit 1}}");
        Assert.Equal(0, parse.ExitCode);
        var script = File.ReadAllText(scriptPath);
        foreach (var expected in new[]
        {
            "Test-Gate0ArtifactRetention.ps1", "Validate-P2Runtime.ps1", "Add-Gate0RetainedProof.ps1",
            "ContractOnly", "AppendRetention", "G0.5 contract matrix must expand to exactly 48 rows",
            "ReelForge.Gate0Staging", "Collision-resistant staging directory", "reparse-point", "TrustedAncestor", "SourceTrustBoundary",
            "PATH fallback", "GetProcessIoCounters", "monotonicTimestampMilliseconds", "cpuNormalizationFormula",
            "-progress","-stats_period","pipe:1", "human", "filter_threads", "-threads:v",
            "-stream_loop", "-c:v','ppm", "-c:a','pcm_s16le", "scale=", "setpts=PTS-STARTPTS", "asetpts=PTS-STARTPTS",
            "'-map','0:v:0'", "'-map','1:a:0'", "libopenh264", "libvpx-vp9", "libopus",
            "Frame identity-cycle oracle failed", "Frame timestamp oracle failed", "Audio waveform oracle failed", "Audio left/right tone identity oracle failed",
            "activeProgressMonotonicMs", "output-size-ceiling", "forcedTermination", "blocked-startup", "not-exercised",
            "pending-append", "retained-and-revalidated", "Post-append retained corpus validation failed",
            "repository:eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json",
            "artifact:p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1/LICENSE.txt",
            "artifact:$destination/g0.5-calibration-evidence.json"
        }) Assert.Contains(expected, script);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stage2", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContractOnlyModeEmitsClosedEvidenceAndNeverRequiresRuntimeOrArtifacts()
    {
        var output = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-contract-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = RunProcess("pwsh", ["-NoProfile", "-File", PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1"), "-ContractOnly", "-OutputDirectory", output]);
            Assert.Equal(0, result.ExitCode);
            using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "g0.5-calibration-evidence.json")));
            Assert.Equal("contract-only", evidence.RootElement.GetProperty("status").GetString());
            Assert.True(evidence.RootElement.GetProperty("contractOnly").GetBoolean());
            var rows = evidence.RootElement.GetProperty("matrix").EnumerateArray().ToArray();
            Assert.Equal(48, rows.Length);
            Assert.Equal(16, rows.Count(row => row.GetProperty("repetitionKind").GetString() == "warmup"));
            Assert.Equal(32, rows.Count(row => row.GetProperty("repetitionKind").GetString() == "measured"));
            Assert.Empty(evidence.RootElement.GetProperty("attempts").EnumerateArray());
            Assert.Empty(evidence.RootElement.GetProperty("cancellationProbes").EnumerateArray());
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [Fact]
    public void ContractOnlyModeRejectsExistingOutputToPreventStaleEvidence()
    {
        var output = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var result = RunProcess("pwsh", ["-NoProfile", "-File", PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1"), "-ContractOnly", "-OutputDirectory", output]);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("must be new", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(output, true); }
    }

    [Fact]
    public void ProcessObservationHelperDrainsBothStreamsKeepsExitCodeAndWritesRawEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
            var prefix = Path.Combine(root, "fake").Replace("'", "''", StringComparison.Ordinal);
            var command = $$$"""
                $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
                if($errors.Count){exit 10}
                foreach($name in 'New-ProcessIoInterop','Get-ProcessSample','Get-Summary','Invoke-ObservedFfmpeg'){
                  $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                  if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
                }
                $p=(Get-Command pwsh).Source
                $r=Invoke-ObservedFfmpeg $p @('-NoProfile','-Command','Write-Output out-one;Write-Output out_time_ms=1;Write-Error err-one;Start-Sleep -Milliseconds 1150;Write-Output out-two;exit 7') '{{{prefix}}}' (Join-Path '{{{root.Replace("'", "''", StringComparison.Ordinal)}}}' 'unused') $false ([pscustomobject]@{})
                if($r.exitCode-ne7 -or @($r.samples).Count-lt2 -or $r.progress.Count-lt1){exit 12}
                if(-not((Get-Content '{{{prefix}}}.stdout.txt' -Raw)-match'out-one')-or-not((Get-Content '{{{prefix}}}.stderr.txt' -Raw)-match'err-one')){exit 13}
                exit 0
                """;
            var result = RunPowerShell(command);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("out-two", File.ReadAllText(Path.Combine(root, "fake.stdout.txt")), StringComparison.Ordinal);
            Assert.Contains("err-one", File.ReadAllText(Path.Combine(root, "fake.stderr.txt")), StringComparison.Ordinal);
            Assert.True(File.ReadLines(Path.Combine(root, "fake.samples.ndjson")).Count() >= 2);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ProcessObservationHelperMeasuresForcedCancellationFromActiveProgress()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
            var prefix = Path.Combine(root, "cancel").Replace("'", "''", StringComparison.Ordinal);
            var escapedRoot = root.Replace("'", "''", StringComparison.Ordinal);
            var command = $$$"""
                $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
                if($errors.Count){exit 10}
                foreach($name in 'New-ProcessIoInterop','Get-ProcessSample','Get-Summary','Invoke-ObservedFfmpeg'){
                  $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                  if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
                }
                $p=(Get-Command pwsh).Source
                $contract=[pscustomobject]@{cancellation=[pscustomobject]@{requestAfterConfirmedProgressMilliseconds=200;activeProgressDeadlineSeconds=3;hardWallClockLimitSeconds=5;maximumOutputBytes=1048576}}
                $r=Invoke-ObservedFfmpeg $p @('-NoProfile','-Command','Write-Output out_time_ms=1;Start-Sleep -Seconds 10') '{{{prefix}}}' (Join-Path '{{{escapedRoot}}}' 'unused') $true $contract
                if(-not$r.cancellation.forcedTermination-or$r.cancellation.terminationTrigger-ne'requested-cancellation'){exit 12}
                if($null-eq$r.cancellation.requestMonotonicMs-or$null-eq$r.cancellation.exitMonotonicMs-or$r.cancellation.requestToExitMilliseconds-lt0){exit 13}
                exit 0
                """;
            var result = RunPowerShell(command);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("out_time_ms=1", File.ReadAllText(Path.Combine(root, "cancel.stdout.txt")), StringComparison.Ordinal);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CancellationDispositionRejectsNeverActiveHardWallAndOutputCeilingStops()
    {
        var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
        var command = $$$"""
            $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
            if($errors.Count){exit 10}
            foreach($name in 'Get-CancellationEvidenceDisposition','Get-FinalCancellationRecordStatus'){
              $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
              if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
            }
            $active=[pscustomobject]@{forcedTermination=$true;terminationTrigger='requested-cancellation';activeProgressMonotonicMs=10;requestMonotonicMs=20}
            $never=[pscustomobject]@{forcedTermination=$true;terminationTrigger='active-progress-deadline';activeProgressMonotonicMs=$null;requestMonotonicMs=$null}
            $wall=[pscustomobject]@{forcedTermination=$true;terminationTrigger='hard-wall-clock';activeProgressMonotonicMs=10;requestMonotonicMs=$null}
            $size=[pscustomobject]@{forcedTermination=$true;terminationTrigger='output-size-ceiling';activeProgressMonotonicMs=10;requestMonotonicMs=$null}
            if((Get-CancellationEvidenceDisposition $active)-ne'forced-termination-evidence'){exit 12}
            if((Get-CancellationEvidenceDisposition $never)-ne'blocked-startup'){exit 13}
            if((Get-CancellationEvidenceDisposition $wall)-ne'blocked-hard-wall-clock'){exit 14}
            if((Get-CancellationEvidenceDisposition $size)-ne'blocked-output-size-ceiling'){exit 15}
            $cleanupFailed=[pscustomobject]@{status='failed';cancellation=$active}
            if((Get-FinalCancellationRecordStatus $cleanupFailed)-ne'failed'){exit 16}
            exit 0
            """;
        var result = RunPowerShell(command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void ProcessObservationHelperPreservesStartupFailureInsteadOfMaskingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
            var prefix = Path.Combine(root, "startup").Replace("'", "''", StringComparison.Ordinal);
            var command = $$$"""
                $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
                if($errors.Count){exit 10}
                foreach($name in 'New-ProcessIoInterop','Get-ProcessSample','Get-Summary','Invoke-ObservedFfmpeg'){
                  $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                  if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
                }
                try{Invoke-ObservedFfmpeg 'Z:\definitely-missing\ffmpeg.exe' @() '{{{prefix}}}' 'unused' $false ([pscustomobject]@{});exit 12}catch{$message=$_.Exception.ToString()}
                if($message-match'No process is associated'){exit 13}
                if(-not((Get-Content '{{{prefix}}}.stderr.txt' -Raw)-match'PROCESS_OBSERVATION_FAILURE')){exit 14}
                exit 0
                """;
            var result = RunPowerShell(command);
            Assert.Equal(0, result.ExitCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OracleHelpersRejectTimestampAudioProfileAndFrameRateViolations()
    {
        var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
        var command = $$$"""
            $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
            if($errors.Count){exit 10}
            foreach($name in 'Assert-IndexedFrameTimestamps','New-AudioOracleInterop','Assert-AudioWaveform','Assert-StreamDescriptor'){
              $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
              if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
            }
            $frames=@(0..199|ForEach-Object{[pscustomobject]@{best_effort_timestamp=$_*40}})
            Assert-IndexedFrameTimestamps $frames 1 1000
            $frames[73].best_effort_timestamp++
            try{Assert-IndexedFrameTimestamps $frames 1 1000;exit 12}catch{if($_.Exception.Message-notmatch'Frame timestamp oracle failed'){exit 13}}

            $truth=[byte[]]::new(8);[BitConverter]::GetBytes([int16]1000).CopyTo($truth,0);[BitConverter]::GetBytes([int16]-1000).CopyTo($truth,2);[BitConverter]::GetBytes([int16]2000).CopyTo($truth,4);[BitConverter]::GetBytes([int16]-2000).CopyTo($truth,6)
            $actual=[byte[]]::new(16);for($i=0;$i-lt4;$i++){[Array]::Copy($truth,($i%2)*4,$actual,$i*4,4)}
            if((Assert-AudioWaveform $truth $actual 4 3072)-ne0){exit 14}
            [BitConverter]::GetBytes([int16]4072).CopyTo($actual,0);if((Assert-AudioWaveform $truth $actual 4 3072)-ne3072){exit 15}
            [BitConverter]::GetBytes([int16]4073).CopyTo($actual,0);try{Assert-AudioWaveform $truth $actual 4 3072;exit 16}catch{if($_.Exception.Message-notmatch'Audio waveform oracle failed'){exit 17}}
            try{Assert-AudioWaveform $truth ([byte[]]::new(12)) 4 3072;exit 18}catch{if($_.Exception.Message-notmatch'Audio sample-count oracle failed'){exit 19}}

            $video=[pscustomobject]@{avg_frame_rate='25/1';r_frame_rate='25/1';profile='Constrained Baseline'};$audio=[pscustomobject]@{profile='LC';sample_rate='48000';channels=2};$row=[pscustomobject]@{};$route=[pscustomobject]@{videoEncoder='libopenh264';audioEncoder='aac'}
            Assert-StreamDescriptor $video $audio $row $route|Out-Null
            $video.profile='Baseline';try{Assert-StreamDescriptor $video $audio $row $route;exit 20}catch{if($_.Exception.Message-notmatch'constrained-baseline'){exit 21}}
            $video.profile='Constrained Baseline';$video.avg_frame_rate='24/1';try{Assert-StreamDescriptor $video $audio $row $route;exit 22}catch{if($_.Exception.Message-notmatch'Frame-rate oracle failed'){exit 23}}
            exit 0
            """;
        var result = RunPowerShell(command);
        Assert.Equal(0, result.ExitCode);
    }

    private static (int ExitCode, string Output) RunPowerShell(string command) => RunProcess("pwsh", ["-NoProfile", "-Command", command]);
    private static (int ExitCode, string Output) RunProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
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
