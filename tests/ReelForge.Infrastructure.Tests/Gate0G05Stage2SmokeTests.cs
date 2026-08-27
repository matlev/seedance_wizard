using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2SmokeTests
{
    [Fact]
    public void ContractOnlyPathExpandsOnlyTheThreeFrozenCandidatesAndExecutesNoMedia()
    {
        var result = Run("pwsh", ["-NoProfile", "-File", PathInRepo("eng", "gate0", "Invoke-G05Stage2PreMatrixSmoke.ps1"), "-ContractOnly"]);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("contract-only", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("noMediaExecuted").GetBoolean());
        Assert.Equal(
            ["mp4-openh264-aac|one", "webm-vp9-opus|one", "webm-vp9-opus|half-logical"],
            json.RootElement.GetProperty("candidates").EnumerateArray().Select(x => x.GetProperty("candidateId").GetString()));
    }

    [Fact]
    public void HelperPreservesExactTimingDemuxerAndPortableEvidenceContracts()
    {
        var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
        var contract = PsQuote(PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json"));
        var script = $"Import-Module '{module}' -Force;" +
            $"$c=Get-Content '{contract}' -Raw|ConvertFrom-Json -Depth 64;" +
            "$w=@($c.workloads|? id -eq 'typical-2v4a')[0];$v=@($w.resolutionVariants|? id -eq '1080p')[0];" +
            "$g=Get-G05SmokeCombinedGraph $w $v;if(-not $g.Contains(';')-or-not $g.Contains('[vout]')-or-not $g.Contains('[aout]')){exit 10};" +
            "if((Convert-G05SmokeTicks 512 '1/12800')-ne 40){exit 11};" +
            "try{Convert-G05SmokeTicks 1 '1/3'|Out-Null;exit 12}catch{};" +
            "if((Get-G05SmokeDemuxer mp4)-ne'mov,mp4,m4a,3gp,3g2,mj2'){exit 13};" +
            "if((Get-G05SmokeDemuxer webm)-ne'matroska,webm'){exit 14};" +
            "$root='C:\\proof\\root';$slashRoot=$root.Replace('\\','/');$hybridRoot=$root.Replace('\\root','/root');$tokens=ConvertTo-G05SmokePortableTokens @((Join-Path $root 'out.mp4'),($slashRoot+'/mixed.mp4'),$slashRoot,($hybridRoot+'/hybrid.mp4')) @{stage=$root};if($tokens[0]-ne'{stage}/out.mp4'-or$tokens[1]-ne'{stage}/mixed.mp4'-or$tokens[2]-ne'{stage}'-or$tokens[3]-ne'{stage}/hybrid.mp4'){exit 15}";

        var result = Run("pwsh", ["-NoProfile", "-Command", script]);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void AudioOracleAcceptsIdentityAndRejectsUnapprovedGain()
    {
        var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
        var script = $"Import-Module '{module}' -Force;$m=Get-Module G05Stage2SmokeHelpers;& $m {{" +
            "Initialize-G05SmokeAudioOracle;" +
            "$r=[int16[]]::new(4800);$low=[int16[]]::new(4800);for($n=0;$n-lt2400;$n++){$r[$n*2]=[int16](10000*[Math]::Sin(2*[Math]::PI*440*$n/48000));$r[$n*2+1]=[int16](10000*[Math]::Sin(2*[Math]::PI*660*$n/48000));$low[$n*2]=[int16]($r[$n*2]*0.75);$low[$n*2+1]=[int16]($r[$n*2+1]*0.75)};" +
            "$t=[pscustomobject]@{minimumSignedNormalizedCrossCorrelationPerChannel=0.995;maximumNormalizedRmsErrorPerChannel=0.10;minimumSnrDbPerChannel=20;minimumOutputToReferenceRmsRatioPerChannel=0.90;maximumOutputToReferenceRmsRatioPerChannel=1.10;maximumAbsoluteDcOffsetFullScalePerChannel=0.005;minimumExpectedToneOutputToReferenceAmplitudeRatio=0.90;maximumExpectedToneOutputToReferenceAmplitudeRatio=1.10;minimumExpectedToForbiddenTonePowerRatioWhenDescriptorProvidesForbiddenTones=100;minimumActiveChannelRmsFullScale=0.05;silenceWindowSamples=96;minimumActiveReferenceWindowOutputRmsFullScale=0.05;nearClippingSampleAbsoluteThreshold=32760;maximumUnexpectedNearClippedSamplesPerChannel=0};" +
            "$expected=[object[]]::new(2);$expected[0]=[int[]]@(440);$expected[1]=[int[]]@(660);$identity=Invoke-G05SmokeAudioRegion identity $r $r 0 2400 $expected $null $t;$gain=Invoke-G05SmokeAudioRegion gain $r $low 0 2400 $expected $null $t;if(-not$identity.Passed-or$gain.Passed-or-not(@($gain.Failures)-match'rms-ratio')){exit 20}}";

        var result = Run("pwsh", ["-NoProfile", "-Command", script]);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void AudioOnsetOracleRejectsAHiddenShiftAtTheDeclaredBoundary()
    {
        var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
        var script = $"Import-Module '{module}' -Force;$m=Get-Module G05Stage2SmokeHelpers;& $m {{Initialize-G05SmokeAudioOracle;" +
            "$r=[int16[]]::new(40000);$shifted=[int16[]]::new(40000);for($n=0;$n-lt20000;$n++){$value=if($n-lt6000){0}else{[int16](10000*[Math]::Sin(2*[Math]::PI*660*($n-6000)/48000))};$r[$n*2]=$value;$r[$n*2+1]=$value};for($n=128;$n-lt20000;$n++){$shifted[$n*2]=$r[($n-128)*2];$shifted[$n*2+1]=$r[($n-128)*2+1]};" +
            "$identity=[ReelForge.Gate0.SmokeAudioOracle]::LocateTransition('identity',$r,$r,6000,512,2048);$delayed=[ReelForge.Gate0.SmokeAudioOracle]::LocateTransition('delayed',$r,$shifted,6000,512,2048);if(-not$identity.Passed-or$identity.ObservedOffsetSamples-ne0-or$delayed.Passed-or$delayed.ObservedOffsetSamples-ne128){exit 30}}";

        var result = Run("pwsh", ["-NoProfile", "-Command", script]);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void VisualOracleFailureTerminatesAndReapsABlockedProducer()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G05-visual-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
            var ready = PsQuote(Path.Combine(root, "producer.pid"));
            var log = PsQuote(Path.Combine(root, "visual.stderr.txt"));
            var metrics = PsQuote(Path.Combine(root, "metrics.ndjson"));
            var fixture = PsQuote(root);
            var script = $"Import-Module '{module}' -Force;$producer='$PID|Set-Content -LiteralPath \"{ready}\";$s=[Console]::OpenStandardOutput();$b=[byte[]]::new(1048576);while($true){{$s.Write($b,0,$b.Length)}}';$clock=[Diagnostics.Stopwatch]::StartNew();try{{Test-G05SmokeVisual (Get-Process -Id $PID).Path x x x '{fixture}' '{log}' '{metrics}' @('-NoProfile','-Command',$producer) '{ready}';exit 40}}catch{{$clock.Stop()}};$child=[int](Get-Content '{ready}');if($clock.ElapsedMilliseconds-ge15000-or(Get-Process -Id $child -ErrorAction SilentlyContinue)){{exit 41}}";

            var result = Run("pwsh", ["-NoProfile", "-Command", script]);
            Assert.True(result.ExitCode == 0, result.Output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SnapshotBindingRejectsSourceMutationBeforeOperationalUse()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G05-snapshot-" + Guid.NewGuid().ToString("N"));
        var snapshots = Path.Combine(root, "snapshots");
        Directory.CreateDirectory(snapshots);
        try
        {
            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, "approved");
            var module = PsQuote(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
            var script = $"Import-Module '{module}' -Force;$sources=@{{'source.txt'='{PsQuote(source)}'}};$bindings=New-G05SmokeSnapshotBinding '{PsQuote(snapshots)}' $sources;$hash=Get-G05SmokeSnapshotHash $bindings 'source.txt';if($hash-ne$bindings.sha256){{exit 52}};Set-Content -LiteralPath '{PsQuote(source)}' -Value changed -NoNewline;$blocked=$false;try{{Assert-G05SmokeSnapshotBinding $bindings $sources}}catch{{if($_.Exception.Message-notmatch'Snapshot source changed after binding'){{exit 51}};$blocked=$true}};if(-not$blocked){{exit 50}}";

            var result = Run("pwsh", ["-NoProfile", "-Command", script]);
            Assert.True(result.ExitCode == 0, result.Output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunnerIsManualFailClosedRetainsEveryDispositionAndAvoidsUnapprovedComponents()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2PreMatrixSmoke.ps1"));
        var helper = File.ReadAllText(PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1"));
        foreach (var text in new[]
        {
            "-ManualExecution", "-AppendRetention", "CBB93CC1483FECD65489485CB1BBF03CD3BF24C2419D28C587C62758C3EAD7EC",
            "119A4C179BFA010F3202DBF6AA368E42EDE5FD0FC23EF2781AA9C7F63540CBE4", "Test-Gate0ArtifactRetention.ps1",
            "Validate-P2Runtime.ps1", "Add-Gate0RetainedProof.ps1", "route-fail-fast-blocked", "candidateBytes",
            "process-samples.ndjson", "visual-mae.ndjson", "Get-G05DecodedAudioTiming", "ConvertTo-G05SmokePortableTokens",
            "snapshotWorkload", "snapshotAudioContract", "snapshotR2Summary", "-ManifestPath $snapshotP2Manifest"
        }) Assert.Contains(text, script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audioOracleIdentity", script, StringComparison.Ordinal);
        foreach (var text in new[] { "FrameMeanAbsoluteErrors", "MinimumWindowRmsFullScale", "OutputToReferenceAmplitudeRatio", "Kill($true)" })
            Assert.Contains(text, helper, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$env:PATH", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"Where-Object\s+[A-Za-z_][A-Za-z0-9_]*\s+-eq'", script);
    }

    private static (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string PsQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
