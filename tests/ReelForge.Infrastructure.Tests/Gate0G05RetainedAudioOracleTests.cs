using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05RetainedAudioOracleTests
{
    [Fact]
    public void FrozenContractAndRunnerPreserveTheRetainedOnlyBoundary()
    {
        using var freeze = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-lossy-audio-oracle-freeze.json")));
        Assert.Equal("Gate0.G05.LossyAudioOracle.Freeze.20260826", freeze.RootElement.GetProperty("freezeId").GetString());
        Assert.False(freeze.RootElement.GetProperty("guards").GetProperty("routeReencodePerformed").GetBoolean());
        Assert.False(freeze.RootElement.GetProperty("guards").GetProperty("thresholdSelectionReadCodecRouteOutcomes").GetBoolean());
        Assert.Equal(3, freeze.RootElement.GetProperty("frozenFiles").GetArrayLength());

        var scriptPath = PathInRepo("eng", "gate0", "Invoke-G05RetainedAudioOracle.ps1");
        var parse = RunPowerShell($"$t=$null;$e=$null;[Management.Automation.Language.Parser]::ParseFile('{scriptPath.Replace("'", "''", StringComparison.Ordinal)}',[ref]$t,[ref]$e)|Out-Null;$e|% Message;if($e.Count){{exit 1}}");
        Assert.Equal(0, parse.ExitCode);
        var script = File.ReadAllText(scriptPath);
        foreach (var value in new[]
        {
            "Gate0.G05.Calibration.20260826T004152697Z.FDABDAA2", "Test-Gate0ArtifactRetention.ps1",
            "Invoke-G05LossyAudioOracleControls.ps1", "LossyAudioControls]::Analyze", "PATH discovery is prohibited",
            "mov,mp4,m4a,3gp,3g2,mj2", "matroska,webm", "'-map','0:a:0'", "$decoder=if($isMp4){'aac'}else{'opus'}",
            "routeReencodePerformed=$false", "thresholdSelectionReadOutputs=$false", "Expected exactly 48",
            "p2/runtime/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1/bin", "must equal the exact approved P2 relative paths",
            "Assert-G05Stage1Matrix", "originalAttempt", "Get-G05AudioTiming",
            "Get-G05AudioTiming", "Original FFmpeg attempt did not exit zero", "new direct child beneath approved staging root",
            "approved project-parent trust boundary",
            "-Arguments @('-v','error','-f',$demuxer",
            "packets_and_frames",
            "selectedAudioStream", "Video.Export.Compatibility.Mp4H264Aac.P2OpenH264",
            "completed-with-failures", "infrastructure-failed"
        }) Assert.Contains(value, script);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Drawing", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PureHelperRejectsInvalidMatrixAndAppliesTimingPrimingRules()
    {
        var module = PathInRepo("eng", "gate0", "G05RetainedAudioOracleHelpers.psm1").Replace("'", "''", StringComparison.Ordinal);
        var command = """
            Import-Module '__MODULE__' -Force
            $routes=@('Video.Export.Compatibility.Mp4H264Aac.P2OpenH264','Video.Export.Open.WebmVp9Opus');$attempts=@()
            foreach($r in $routes){foreach($s in '720p','1080p'){foreach($t in 'auto','one','half-logical','full-logical'){foreach($p in @(@('warmup',1),@('measured',1),@('measured',2))){$attempts += [pscustomobject]@{row=[pscustomobject]@{routeId=$r;resolutionId=$s;threadPolicyId=$t;repetitionKind=$p[0];repetitionOrdinal=$p[1]};status=if($r-like'*OpenH264'){'failed'}else{'passed'};measurement=[pscustomobject]@{exitCode=0}}}}}}
            Assert-G05Stage1Matrix $attempts
            $duplicate=@($attempts);$duplicate[0]=$duplicate[1];try{Assert-G05Stage1Matrix $duplicate;exit 10}catch{if($_.Exception.Message-notmatch'Duplicate|Missing'){exit 11}}
            $missingExit=@($attempts|ForEach-Object{$_});$missingExit[0]=[pscustomobject]@{row=$attempts[0].row;status='failed';measurement=[pscustomobject]@{}};try{Assert-G05Stage1Matrix $missingExit;exit 17}catch{if($_.Exception.Message-notmatch'numeric exit code'){exit 18}}
            $frames=@(for($i=0;$i-lt400;$i++){[pscustomobject]@{pts=7+($i*20);nb_samples=960}});$stream=[pscustomobject]@{time_base='1/1000';start_time='-0.007';start_pts=-7;duration_ts='N/A'};$skipPacket=[pscustomobject]@{side_data_list=@([pscustomobject]@{side_data_type='Skip Samples';skip_samples=312;discard_padding=0})};$timing=Get-G05AudioTiming $stream @($skipPacket) $frames 384000;if(-not$timing.timingPassed-or$timing.endpointSource-ne'decoded-frame-sample-sum'-or$timing.endpointCandidates.decodedFrameSampleSum-ne384000-or$timing.maximumRecordedSkipSamples-ne312){exit 12}
            $excessive=Get-G05AudioTiming $stream @([pscustomobject]@{side_data_list=@([pscustomobject]@{side_data_type='Skip Samples';skip_samples=1025;discard_padding=0})}) $frames 384000;if($excessive.timingPassed-or'excessive'-in$excessive.failures){exit 13};if('priming-or-discard-padding-out-of-range'-notin$excessive.failures){exit 14}
            $missing=Get-G05AudioTiming ([pscustomobject]@{time_base=$null;duration_ts=$null}) @() @() 384000;if($missing.timingPassed-or'presentation-timing-metadata-unavailable'-notin$missing.failures){exit 15}
            exit 0
            """.Replace("__MODULE__", module, StringComparison.Ordinal);
        var result = RunPowerShell(command);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void RejectsAnOutputDirectoryInsideTheRepositoryBeforeCorpusUse()
    {
        var result = RunProcess("pwsh", ["-NoProfile", "-File", PathInRepo("eng", "gate0", "Invoke-G05RetainedAudioOracle.ps1"), "-ArtifactRoot", "C:\\not-artifacts", "-FfmpegPath", "C:\\not-ffmpeg.exe", "-FfprobePath", "C:\\not-ffprobe.exe", "-OutputDirectory", PathInRepo("g05-audio-oracle-test-output")]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ArtifactRoot", result.Output, StringComparison.OrdinalIgnoreCase);
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
