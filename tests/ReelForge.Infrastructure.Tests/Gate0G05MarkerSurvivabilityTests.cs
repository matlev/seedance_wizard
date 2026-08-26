using System.Diagnostics;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05MarkerSurvivabilityTests
{
    [Fact]
    public void HarnessPinsTheProofOnlyMarkerContractAndExplicitRoutes()
    {
        var path = PathInRepo("eng", "gate0", "Invoke-G05MarkerSurvivability.ps1");
        Assert.Equal(0, Run("pwsh", $"$t=$null;$e=$null;[Management.Automation.Language.Parser]::ParseFile('{path.Replace("'", "''")}',[ref]$t,[ref]$e)|Out-Null;if($e.Count){{exit 1}}").ExitCode);
        var script = File.ReadAllText(path);
        foreach (var required in new[] { "Gate0.G05.MarkerSurvivability.V1", "Test-Gate0ArtifactRetention.ps1", "Generate-G05Stage2MarkerAtlas.ps1", "g0.5-stage2-workload-contract.json", "markerQualification.videoFilterGraph", "long-form-adapter-1v1a-60m", "contractRoutes", "videoOptions", "audioOptions", "muxerOptions", "Parse-DecimalInvariant", "qualityProfileId", "observedDescriptor", "rFrameRate", "avgFrameRate", "'-xerror'", "decoded-audio", "expectedSamplesPerChannel=1440000", "executedAudioDecoder", "completed-with-failures", "audioDecoder", "Explicit stream identity gate", "immediateNoExtraFrameConclusion", "partialMedia", "partialMarkerStrip", "commands=[ordered]@{encode=$encode;probe=$probe;decode=$decode;audioDecode=$audioDecode}", "[object]$Expected", "lacks explicit size/SHA", "BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97", "'-map','[vout]'", "'-map','0:v:0'", "crop=272:16:16:16,format=gray", "routeReencodePerformed=$true", "new direct child beneath approved staging root", "PATH discovery is prohibited", "expectedFrames=750" }) Assert.Contains(required, script);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settb", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReelForge.App", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PureMarkerOracleAcceptsSequentialFramesAndRejectsSemanticFailures()
    {
        var module = PathInRepo("eng", "gate0", "G05MarkerSurvivabilityHelpers.psm1").Replace("'", "''");
        var command = """
            Import-Module '__MODULE__' -Force
            $frameBytes=272*16;$data=[byte[]]::new($frameBytes*3)
            for($f=0;$f-lt3;$f++){for($b=0;$b-lt17;$b++){$bit=(($f-shr(16-$b))-band1);$v=if($bit){255}else{0};$data[$f*$frameBytes+8*272+$b*16+8]=$v}}
            $ok=Get-G05MarkerDecode $data 3 @([int64]0,[int64]40,[int64]80);if(-not$ok.passed){exit 10}
            $amb=[byte[]]$data.Clone();$amb[8*272+8]=128;$r=Get-G05MarkerDecode $amb 3 @([int64]0,[int64]40,[int64]80);if($r.passed-or'ambiguous-marker-bit-cell'-notin$r.failures){exit 11}
            $dup=[byte[]]$data.Clone();[Array]::Copy($dup,0,$dup,$frameBytes,$frameBytes);$r=Get-G05MarkerDecode $dup 3 @([int64]0,[int64]40,[int64]80);if($r.passed-or'duplicate-marker-id'-notin$r.failures-or'marker-id-collision'-notin$r.failures-or$r.collisions-ne1-or'marker-id-misidentification'-notin$r.failures){exit 12}
            $unexpected=[byte[]]$data.Clone();$unexpected[2*$frameBytes+8*272+16*16+8]=255;$r=Get-G05MarkerDecode $unexpected 3 @([int64]0,[int64]40,[int64]80);if($r.passed-or'unexpected-marker-id'-notin$r.failures-or$r.unexpectedIds-ne1-or$r.unexpectedValues[0]-ne3-or$r.missingIds-ne1-or$r.missingValues[0]-ne2){exit 14}
            $r=Get-G05MarkerDecode $data 4 @([int64]0,[int64]40,[int64]80);if($r.passed-or'decoded-marker-frame-count-mismatch'-notin$r.failures){exit 13};exit 0
            """.Replace("__MODULE__", module);
        Assert.Equal(0, Run("pwsh", command).ExitCode);
    }

    [Fact]
    public void JsonDerivedArtifactExpectationRemainsAnObjectWithExplicitSizeAndHash()
    {
        var command = """
            $expected='{"size":4590016,"sha256":"BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97"}'|ConvertFrom-Json
            function Verify([object]$value){if($null-eq$value.PSObject.Properties['size']-or$null-eq$value.PSObject.Properties['sha256']-or[int64]$value.size-ne4590016){throw 'bad expectation'}}
            Verify $expected
            """;
        Assert.Equal(0, Run("pwsh", command).ExitCode);
    }

    private static (int ExitCode, string Output) Run(string exe, string command)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(command);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }
    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory); return Path.Combine([directory!.FullName, .. parts]);
    }
}
