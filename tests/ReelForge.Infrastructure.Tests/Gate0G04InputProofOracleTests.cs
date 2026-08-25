namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04InputProofOracleTests
{
    [Fact]
    public void OraclePropertyReaderSupportsRecordedCommandDictionaries()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Oracles.ps1");
        var result = RunPowerShell($$""". '{{Escape(module)}}'; $record=[ordered]@{stdout='{"streams":[]}';stderr=''}; [ordered]@{stdout=(Get-G04Property $record 'stdout' 'missing');stderr=(Get-G04Property $record 'stderr' 'missing')} | ConvertTo-Json -Compress""");

        Assert.Equal(0, result.ExitCode);
        using var json = System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.Equal("{\"streams\":[]}", json.RootElement.GetProperty("stdout").GetString());
        Assert.Equal(string.Empty, json.RootElement.GetProperty("stderr").GetString());
    }
    [Fact]
    public void OraclesRequireFreshInspectionStrictDiagnosticsAndConcreteCommands()
    {
        var script = ReadScript();

        Assert.Contains("-show_format','-show_streams','-show_frames','-show_packets','-show_data_hash','sha256", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-G04RecordedCommand -Context $Context", script, StringComparison.Ordinal);
        Assert.Contains("Test-G04UndeclaredDiagnostics -Stderr", script, StringComparison.Ordinal);
        Assert.Contains("$null = Test-G04UndeclaredDiagnostics", script, StringComparison.Ordinal);
        Assert.Contains("'-xerror','-err_detect','explode'", script, StringComparison.Ordinal);
        Assert.Contains("packets_and_frames", script, StringComparison.Ordinal);
        Assert.Contains("'lc'='aac_low'", script, StringComparison.Ordinal);
        Assert.Contains("Get-G04Property $expect 'sampleEnvelope'", script, StringComparison.Ordinal);
        Assert.Contains("ReelForge.Gate0.ByteOracle", script, StringComparison.Ordinal);
        Assert.Contains("'strict complete decode'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("exit code", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OraclesMapCombinedDemuxerDisplayNamesWithoutPassingThemToFfmpeg()
    {
        var script = ReadScript();

        Assert.Contains("'mov,mp4,m4a,3gp,3g2,mj2' = 'mov'", script, StringComparison.Ordinal);
        Assert.Contains("'matroska,webm' = 'matroska'", script, StringComparison.Ordinal);
        Assert.Contains("'matroska' = 'matroska'", script, StringComparison.Ordinal);
        Assert.Contains("Get-G04ConcreteDemuxer", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'-f',[string]$Case.requiredComponents.demuxer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OraclesExplicitlyDecodeEveryDeclaredStreamBeforeInputAndMapEachOne()
    {
        var script = ReadScript();

        Assert.Contains("$args += @(\"-c:$type\",$byType[$type])", script, StringComparison.Ordinal);
        Assert.Contains("$args += @('-i',$ArtifactPath,'-map',$StreamMap", script, StringComparison.Ordinal);
        Assert.Contains("foreach($map in $allMaps)", script, StringComparison.Ordinal);
        Assert.Contains("Get-G04StreamMaps", script, StringComparison.Ordinal);
        Assert.Contains("$maps.Add(\"0:$kind`:$($ordinals[$kind])\")", script, StringComparison.Ordinal);
        Assert.Contains("allStreams=$true", script, StringComparison.Ordinal);
    }

    [Fact]
    public void OraclesExecuteFrameTimingAudioAndImageSemanticChecks()
    {
        var script = ReadScript();

        foreach (var required in new[]
        {
            "expectedDecodedFrameCount", "presentationPts", "preserveSignedNonZeroPts",
            "exactSampleCount", "sampleEnvelope", "Get-G04PpmPixels", "Get-G04Mae",
            "maximumMeanAbsoluteError", "exact decoded raster SHA-256", "Concrete $kind oracle failed"
        }) Assert.Contains(required, script, StringComparison.Ordinal);

        Assert.DoesNotContain("placeholder", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("same-runtime production", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OraclesGuardNormalizedProfilesMarkersToneBytesJpegAndF7Timebase()
    {
        var script = ReadScript();

        foreach (var required in new[]
        {
            "Normalize-G04Profile", "constrainedbaseline", "profile0", "aaclc",
            "avg_frame_rate", "Get-G04ToneEvidence", "opposedPhaseCorrelation",
            "exactSampleCount", "Get-G04SofMarker", "presentationIntervals",
            "streamTimeBase", "expectedAudioDecode", "-show_packets"
        }) Assert.Contains(required, script, StringComparison.Ordinal);

        Assert.DoesNotContain("executed by runner", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exitCode -eq 0", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OraclesCompareF1AndF7FrameIdentityAgainstDeterministicReferences()
    {
        var script = ReadScript();

        foreach (var required in new[]
        {
            "Get-G04RawVideoFrames", "scale=320:180:flags=bilinear", "Get-G04PpmReferenceFrames",
            "Assert-G04FrameSequence", "f1-pattern-000", "f1-pattern-001", "f1-pattern-002",
            "@('red','green','blue','white','black')", "f1MaximumMeanAbsoluteError", "f7MaximumMeanAbsoluteError",
            "Reference frame identities are not byte-distinct", "Remove-G04OracleArtifact $raw"
        }) Assert.Contains(required, script, StringComparison.Ordinal);
    }

    [Fact]
    public void OraclesDeriveLosslessPcmFromDeclaredRecipeAndCompareEveryByte()
    {
        var script = ReadScript();

        foreach (var required in new[]
        {
            "Get-G04LosslessExpectedPcm", "declaredFormat", "-f','s16le','-c:a','pcm_s16le",
            "declared lossless PCM transform oracle", "Test-G04ByteIdentity",
            "full decoded s16le SHA-256 and byte equality", "expectedPcmSha256", "exactSamples"
        }) Assert.Contains(required, script, StringComparison.Ordinal);
    }

    [Fact]
    public void OraclesProveRemuxAgainstItsSourceCaseRatherThanOnlyTheTarget()
    {
        var script = ReadScript();

        foreach (var required in new[]
        {
            "Assert-G04RemuxIdentity", "Context.CaseById", "Context.ArtifactsByCase",
            "sourceCaseId", "Get-G04ComparableStreamStructure", "streamStructureEqual",
            "timingEqual", "sourceSha256", "targetSha256", "streamCopyPayloads",
            "independentCompleteDecode", "packetDataHash='sha256'", "streamCopyOnly"
        }) Assert.Contains(required, script, StringComparison.Ordinal);
    }

    [Fact]
    public void OracleFailureRemovesEveryRawAndReferenceArtifactCreatedByCaseEvidence()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Oracles.ps1");
        var command = """
            $ErrorActionPreference='Stop'
            . '__MODULE__'
            $work=Join-Path ([IO.Path]::GetTempPath()) ('reelforge-g04-oracle-cleanup-'+[guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $work | Out-Null
            try {
              $artifact=Join-Path $work 'artifact.bin'; [IO.File]::WriteAllBytes($artifact,[byte[]]@(1))
              function Get-G04Probe { param($Case,$ArtifactPath,$Context) return @{record=[ordered]@{};data=[pscustomobject]@{streams=@([pscustomobject]@{codec_type='video';width=1;height=1});frames=@([pscustomobject]@{media_type='video'});format=[pscustomobject]@{}}} }
              function Assert-G04StreamContract { param($Case,$Probe) return @() }
              function Invoke-G04StrictDecode { param($Case,$ArtifactPath,$Context,$StreamMap,$OutputPath,$OutputArguments) [IO.File]::WriteAllBytes($OutputPath,[byte[]]@(1,2,3)); return [ordered]@{} }
              function Invoke-G04OracleCommand { param($Context,$Name,$Executable,$Arguments,$Components,$CaseId) [IO.File]::WriteAllBytes($Arguments[-1],[byte[]]@(1,2,3)); throw 'forced reference failure' }
              $source=Join-Path $work 'source.ppm'; [IO.File]::WriteAllBytes($source,[byte[]]@(80,54,10,49,32,49,10,50,53,53,10,0,0,0))
              $case=[pscustomobject]@{id='cleanup-case';streams=@([pscustomobject]@{type='image';codec='png'});requiredComponents=[pscustomobject]@{demuxer='image2'}}
              $recipe=[pscustomobject]@{id='cleanup-recipe';sourceArtifacts=@([pscustomobject]@{fileIds=@('source.ppm');declaredFormat='ppm-rgb24'})}
              $oracle=[pscustomobject]@{id='O-IMAGE-PNG-EXACT';kind='image'}
              $context=@{Ffmpeg='unused';Ffprobe='unused';FixtureRoot=$work;Work=$work;Commands=[Collections.Generic.List[object]]::new()}
              try { Test-G04CaseEvidence -Case $case -Recipe $recipe -Oracle $oracle -ArtifactPath $artifact -Context $context | Out-Null; throw 'expected failure was not raised' } catch { if($_.Exception.Message -notmatch 'forced reference failure'){ throw } }
              $left=@(@('cleanup-case-0_v_0.raw','cleanup-case-image.rgb24','cleanup-case-reference.rgb24') | Where-Object { Test-Path -LiteralPath (Join-Path $work $_) })
              if($left.Count -ne 0){throw ('oracle artifacts were not removed: '+($left -join ','))}
            } finally { if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force} }
            """.Replace("__MODULE__", Escape(module), StringComparison.Ordinal);

        var result = RunPowerShell(command);

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void DeterministicByteAndFrameGuardAlgorithmsRejectWrongOrderAndBytes()
    {
        var red = new byte[] { 255, 0, 0 };
        var green = new byte[] { 0, 255, 0 };
        var blue = new byte[] { 0, 0, 255 };

        Assert.True(red.SequenceEqual(red));
        Assert.False(red.SequenceEqual(green));
        Assert.NotEqual(0d, MeanAbsoluteError(red, green));
        Assert.True(MeanAbsoluteError(red, red) <= 20d);
        Assert.False(new[] { red, blue, green }.Select((frame, index) => frame.SequenceEqual(new[] { red, green, blue }[index])).All(x => x));
    }

    private static double MeanAbsoluteError(byte[] left, byte[] right) =>
        left.Zip(right, (a, b) => Math.Abs(a - b)).Average();

    private static ProcessResult RunPowerShell(string command)
    {
        var start = new System.Diagnostics.ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record ProcessResult(int ExitCode, string Output);

    private static string ReadScript() => File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Oracles.ps1"));

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }
}
