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
            "pending-append", "retained-and-revalidated", "Invoke-RequiredScript",
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
    public void RequiredPowerShellScriptCallsDoNotDependOnNativeLastExitCodeState()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-required-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var success = Path.Combine(root, "success.ps1");
            var failure = Path.Combine(root, "failure.ps1");
            File.WriteAllText(success, "param([string] $Value)\nSet-StrictMode -Version Latest\n[pscustomobject]@{value=$Value}\n");
            File.WriteAllText(failure, "throw 'expected-child-failure'\n");
            var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
            var command = $$$"""
                Set-StrictMode -Version Latest
                $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
                if($errors.Count){exit 10}
                $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-RequiredScript'},$true)
                if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
                $result=Invoke-RequiredScript '{{{success.Replace("'", "''", StringComparison.Ordinal)}}}' @{Value='ok'}
                if($result.value-ne'ok'){exit 12}
                try{Invoke-RequiredScript '{{{failure.Replace("'", "''", StringComparison.Ordinal)}}}' @{};exit 13}catch{if($_.Exception.Message-notmatch'expected-child-failure'){exit 14}}
                exit 0
                """;
            var result = RunPowerShell(command);
            Assert.Equal(0, result.ExitCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CommandBuilderReturnsAMutableClosedArgumentListWithoutUsingAutomaticArgs()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G05-command-" + Guid.NewGuid().ToString("N"));
        var f1 = Path.Combine(root, "F1");
        Directory.CreateDirectory(f1);
        try
        {
            File.WriteAllBytes(Path.Combine(f1, "f1-pattern-000.ppm"), [1]);
            File.WriteAllBytes(Path.Combine(f1, "f1-sync-440hz-880hz-48000-stereo.pcm"), [1]);
            var script = PathInRepo("eng", "gate0", "Invoke-P2G05Calibration.ps1").Replace("'", "''", StringComparison.Ordinal);
            var command = $$$"""
                $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile('{{{script}}}',[ref]$tokens,[ref]$errors)
                if($errors.Count){exit 10}
                foreach($name in 'Get-Property','Assert-ContainedFile','New-FfmpegArguments'){
                  $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
                  if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
                }
                $row=[pscustomobject]@{width=1280;height=720;codecThreads=1;filterThreads=1}
                $route=[pscustomobject]@{videoEncoder='libopenh264';audioEncoder='aac';videoOptions=@('-b:v','2M');audioOptions=@('-b:a','192k');muxerOptions=@('-movflags','+faststart');muxer='mp4'}
                $arguments=@(New-FfmpegArguments $row $route ([pscustomobject]@{}) '{{{root.Replace("'", "''", StringComparison.Ordinal)}}}' 'output.mp4' 8)
                if($arguments.Count-lt40-or$arguments[0]-ne'-nostdin'-or$arguments[-1]-ne'output.mp4'){exit 12}
                $maps=@();for($i=0;$i-lt$arguments.Count-1;$i++){if($arguments[$i]-eq'-map'){$maps+=$arguments[$i+1]}}
                if($maps.Count-ne2-or$maps[0]-ne'0:v:0'-or$maps[1]-ne'1:a:0'-or'libopenh264'-notin$arguments-or'aac'-notin$arguments){exit 13}
                $durationIndex=[Array]::IndexOf($arguments,'-t');$muxerIndex=[Array]::IndexOf($arguments,'-f',$durationIndex);if($durationIndex-lt0-or$muxerIndex-le$durationIndex-or$muxerIndex-ge($arguments.Count-1)){exit 14}
                $routeWithoutMuxerOptions=[pscustomobject]@{videoEncoder='libvpx-vp9';audioEncoder='libopus';videoOptions=@('-crf','32');audioOptions=@('-b:a','128k');muxer='webm'}
                $openArguments=@(New-FfmpegArguments $row $routeWithoutMuxerOptions ([pscustomobject]@{}) '{{{root.Replace("'", "''", StringComparison.Ordinal)}}}' 'output.webm' 8)
                if($openArguments[-1]-ne'output.webm'-or'libvpx-vp9'-notin$openArguments-or'libopus'-notin$openArguments){exit 15}
                exit 0
                """;
            var result = RunPowerShell(command);
            Assert.Equal(0, result.ExitCode);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
            foreach($name in 'Get-Property','Get-ProbedVideoFrames','Assert-IndexedFrameTimestamps','New-VisualOracleInterop','Assert-VisualIdentityCycle','New-AudioOracleInterop','Assert-AudioWaveform','Assert-StreamDescriptor'){
              $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
              if($null-eq$fn){exit 11};. ([scriptblock]::Create($fn.Extent.Text))
            }
            $frames=@(0..199|ForEach-Object{[pscustomobject]@{best_effort_timestamp=$_*40}})
            Assert-IndexedFrameTimestamps $frames 1 1000
            $frames[73].best_effort_timestamp++
            try{Assert-IndexedFrameTimestamps $frames 1 1000;exit 12}catch{if($_.Exception.Message-notmatch'Frame timestamp oracle failed'){exit 13}}
            $combined=[pscustomobject]@{packets_and_frames=@([pscustomobject]@{type='packet';media_type='video'},[pscustomobject]@{type='frame';media_type='audio'},[pscustomobject]@{type='frame';media_type='video';best_effort_timestamp=0})}
            if(@(Get-ProbedVideoFrames $combined).Count-ne1){exit 24}
            try{Get-ProbedVideoFrames ([pscustomobject]@{});exit 25}catch{if($_.Exception.Message-notmatch'Combined packet/frame inspection oracle is missing'){exit 26}}

            $visualTruth=[byte[][]]@([byte[]](1,1,1,1),[byte[]](20,20,20,20),[byte[]](40,40,40,40));$visualActual=[byte[]](1,1,1,1,20,20,20,20,40,40,40,40,1,1,1,1,20,20,20,20,40,40,40,40)
            if(@(Assert-VisualIdentityCycle $visualTruth $visualActual 4 6 1).Count-ne6){exit 27}
            $visualActual[12]=40;try{Assert-VisualIdentityCycle $visualTruth $visualActual 4 6 1;exit 28}catch{if($_.Exception.Message-notmatch'Frame identity-cycle oracle failed'){exit 29}}

            $truth=[byte[]]::new(8);[BitConverter]::GetBytes([int16]1000).CopyTo($truth,0);[BitConverter]::GetBytes([int16]-1000).CopyTo($truth,2);[BitConverter]::GetBytes([int16]2000).CopyTo($truth,4);[BitConverter]::GetBytes([int16]-2000).CopyTo($truth,6)
            $actual=[byte[]]::new(16);for($i=0;$i-lt4;$i++){[Array]::Copy($truth,($i%2)*4,$actual,$i*4,4)}
            if((Assert-AudioWaveform $truth $actual 4 3072)-ne0){exit 14}
            [BitConverter]::GetBytes([int16]4072).CopyTo($actual,0);if((Assert-AudioWaveform $truth $actual 4 3072)-ne3072){exit 15}
            [BitConverter]::GetBytes([int16]4073).CopyTo($actual,0);try{Assert-AudioWaveform $truth $actual 4 3072;exit 16}catch{if($_.Exception.Message-notmatch'maximum absolute delta 3073'){exit 17}}
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
