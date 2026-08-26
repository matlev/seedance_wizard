using System.Diagnostics;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05MarkerSurvivabilityTests
{
    [Fact]
    public void ResultSummaryPinsTheAuthoritativeBoundedQualificationWithoutClosingStage2()
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-marker-survivability-result-summary.json")));
        var root = document.RootElement;

        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("matrix").GetProperty("evaluatedRoutes").GetInt32());
        Assert.Equal(2, root.GetProperty("matrix").GetProperty("passedRoutes").GetInt32());
        Assert.Equal(1500, root.GetProperty("matrix").GetProperty("uniquelyRecoveredFrames").GetInt32());
        Assert.Equal("48611C1D670AEA59CA7192537B36237FE769B7F52BC074A5F9387B666FDEBFA9", root.GetProperty("authoritativeEvidence").GetProperty("reportSha256").GetString());
        Assert.Equal("bc93906", root.GetProperty("authoritativeEvidence").GetProperty("executionCommit").GetString());
        Assert.Equal(3, root.GetProperty("supersededAttempts").GetArrayLength());
        Assert.Equal(4, root.GetProperty("remainingBeforePreMatrixSmoke").GetArrayLength());

        foreach (var route in root.GetProperty("routeDispositions").EnumerateArray())
        {
            var marker = route.GetProperty("marker");
            Assert.Equal(750, marker.GetProperty("expectedFrames").GetInt32());
            Assert.Equal(750, marker.GetProperty("decodedFrames").GetInt32());
            foreach (var field in new[] { "ambiguous", "misidentified", "duplicates", "collisions", "missing", "unexpected", "badPts" })
                Assert.Equal(0, marker.GetProperty(field).GetInt32());
            Assert.False(route.GetProperty("audioTiming").GetProperty("proofSideTrimmingPerformed").GetBoolean());
        }
    }

    [Fact]
    public void HarnessPinsTheProofOnlyMarkerContractAndExplicitRoutes()
    {
        var path = PathInRepo("eng", "gate0", "Invoke-G05MarkerSurvivability.ps1");
        Assert.Equal(0, Run("pwsh", $"$t=$null;$e=$null;[Management.Automation.Language.Parser]::ParseFile('{path.Replace("'", "''")}',[ref]$t,[ref]$e)|Out-Null;if($e.Count){{exit 1}}").ExitCode);
        var script = File.ReadAllText(path);
        foreach (var required in new[] { "Gate0.G05.MarkerSurvivability.V1", "Test-Gate0ArtifactRetention.ps1", "Generate-G05Stage2MarkerAtlas.ps1", "g0.5-stage2-workload-contract.json", "g0.5-lossy-audio-oracle-contract.json", "markerQualification.videoFilterGraph", "long-form-adapter-1v1a-60m", "contractRoutes", "videoOptions", "audioOptions", "muxerOptions", "Parse-DecimalInvariant", "Get-PSDrive -Name $driveName", "$free=[int64]$drive.Free", "'-show_packets'", "packetProbe", "Get-G05DecodedAudioTiming", "Optional $audioStream[0] 'profile'", "qualityProfileId", "observedDescriptor", "rFrameRate", "avgFrameRate", "'-xerror'", "decoded-audio", "executedAudioDecoder", "completed-with-failures", "audioDecoder", "Explicit stream identity gate", "immediateNoExtraFrameConclusion", "partialMedia", "partialMarkerStrip", "[object]$Expected", "lacks explicit size/SHA", "BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97", "'-map','[vout]'", "'-map','0:v:0'", "crop=272:16:16:16,format=gray", "routeReencodePerformed=$true", "new direct child beneath approved staging root", "PATH discovery is prohibited", "expectedFrames=750" }) Assert.Contains(required, script);
        var helper = File.ReadAllText(PathInRepo("eng", "gate0", "G05MarkerSurvivabilityHelpers.psm1"));
        Assert.Contains("proofSideTrimmingPerformed = $false", helper);
        Assert.Contains("maximumRawDecoderTailSamples", helper);
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
    public void JsonAndDictionaryArtifactExpectationsExposeExplicitSizeAndHash()
    {
        var command = """
            $expected='{"size":4590016,"sha256":"BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97"}'|ConvertFrom-Json
            $dictionary=[ordered]@{size=4590016;sha256='BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97'}
            function Verify([object]$value){if($value-is[Collections.IDictionary]){if(-not$value.Contains('size')-or-not$value.Contains('sha256')-or[int64]$value['size']-ne4590016){throw 'bad dictionary expectation'}}elseif($null-eq$value.PSObject.Properties['size']-or$null-eq$value.PSObject.Properties['sha256']-or[int64]$value.size-ne4590016){throw 'bad object expectation'}}
            Verify $expected
            Verify $dictionary
            """;
        Assert.Equal(0, Run("pwsh", command).ExitCode);
    }

    [Fact]
    public void AudioTimingSeparatesRawDecoderTailFromExactPresentationEndpoint()
    {
        var module = PathInRepo("eng", "gate0", "G05MarkerSurvivabilityHelpers.psm1").Replace("'", "''");
        var command = """
            Import-Module '__MODULE__' -Force
            $mp4Stream=[pscustomobject]@{duration_ts=1440000;time_base='1/48000'}
            $mp4Frames=@([pscustomobject]@{nb_samples=1024;pts=0},[pscustomobject]@{nb_samples=1024;pts=1439744})
            $mp4Packets=@([pscustomobject]@{pts=-1024;duration=1024;side_data_list=@([pscustomobject]@{side_data_type='Skip Samples';skip_samples=1024;discard_padding=0})},[pscustomobject]@{pts=0;duration=1024},[pscustomobject]@{pts=1439744;duration=256})
            $mp4=Get-G05DecodedAudioTiming (1440768*4) $mp4Stream $mp4Frames $mp4Packets
            if(-not$mp4.passed-or$mp4.endpointSource-ne'stream-duration-ts'-or$mp4.rawDecoderTailSamples-ne768-or-not$mp4.rawTailMetadataMatched-or$mp4.tailFromFinalPacketFrame-ne768-or$mp4.maximumRecordedSkipSamples-ne1024-or$mp4.proofSideTrimmingPerformed){exit 10}
            $webmStream=[pscustomobject]@{time_base='1/1000'}
            $webmFrames=@([pscustomobject]@{nb_samples=1439688},[pscustomobject]@{nb_samples=312})
            $webmPackets=@([pscustomobject]@{duration=20;side_data_list=@([pscustomobject]@{side_data_type='Skip Samples';skip_samples=312;discard_padding=0})},[pscustomobject]@{duration=7;side_data_list=@([pscustomobject]@{side_data_type='Skip Samples';skip_samples=0;discard_padding=648})})
            $webm=Get-G05DecodedAudioTiming (1440000*4) $webmStream $webmFrames $webmPackets
            if(-not$webm.passed-or$webm.endpointSource-ne'decoded-frame-sample-sum'-or$webm.rawDecoderTailSamples-ne0-or$webm.maximumRecordedDiscardPaddingSamples-ne648){exit 11}
            $bad=Get-G05DecodedAudioTiming (1441025*4) $mp4Stream $mp4Frames $mp4Packets
            if($bad.passed-or'decoded-audio-raw-tail-out-of-range'-notin$bad.failures){exit 12}
            """.Replace("__MODULE__", module);
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
