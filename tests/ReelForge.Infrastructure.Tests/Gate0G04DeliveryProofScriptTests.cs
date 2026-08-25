namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04DeliveryProofScriptTests
{
    [Fact]
    public void G04ContractDefinesTheElevenApprovedPortableProofRoutes()
    {
        using var contract = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "g0.4-delivery-proof-contract.json")));
        var capabilities = contract.RootElement.GetProperty("capabilities").EnumerateArray().ToArray();

        Assert.Equal(11, capabilities.Length);
        var expected = new Dictionary<string, (string[] Inputs, string[] InputDecoders, string[] Filters, string Muxer, string OutputDemuxer, string[] OutputDecoders)>
        {
            ["Video.Export.Compatibility.Mp4H264Aac.P2OpenH264"] = (["image2", "s16le"], ["ppm", "pcm_s16le"], ["format"], "mp4", "mov,mp4,m4a,3gp,3g2,mj2", ["h264", "aac"]),
            ["Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264"] = (["image2"], ["ppm"], ["format"], "mp4", "mov,mp4,m4a,3gp,3g2,mj2", ["h264"]),
            ["Video.Export.Open.WebmVp9Opus"] = (["image2", "s16le"], ["ppm", "pcm_s16le"], ["format"], "webm", "matroska,webm", ["vp9", "opus"]),
            ["Video.Export.Open.WebmVp9VideoOnly"] = (["image2"], ["ppm"], ["format"], "webm", "matroska,webm", ["vp9"]),
            ["Audio.Export.M4aAac"] = (["s16le"], ["pcm_s16le"], [], "ipod", "mov,mp4,m4a,3gp,3g2,mj2", ["aac"]),
            ["Audio.Export.Mp3"] = (["s16le"], ["pcm_s16le"], [], "mp3", "mp3", ["mp3"]),
            ["Audio.Export.OggOpus"] = (["s16le"], ["pcm_s16le"], [], "ogg", "ogg", ["opus"]),
            ["Audio.Export.WavPcm"] = (["s16le"], ["pcm_s16le"], [], "wav", "wav", ["pcm_s16le"]),
            ["Audio.Export.Flac"] = (["s16le"], ["pcm_s16le"], [], "flac", "flac", ["flac"]),
            ["Image.Export.Png"] = (["image2"], ["ppm"], [], "image2", "image2", ["png"]),
            ["Image.Export.Jpeg"] = (["image2"], ["ppm"], ["format"], "image2", "image2", ["mjpeg"])
        };
        foreach (var capability in capabilities)
        {
            var route = capability.GetProperty("route");
            var expectedRoute = expected[capability.GetProperty("id").GetString()!];
            Assert.Equal(expectedRoute.Inputs, route.GetProperty("inputDemuxers").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(expectedRoute.InputDecoders, route.GetProperty("inputDecoders").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(expectedRoute.Filters, route.GetProperty("filters").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(expectedRoute.Muxer, route.GetProperty("muxer").GetString());
            Assert.Equal(expectedRoute.OutputDemuxer, route.GetProperty("outputDemuxer").GetString());
            Assert.Equal(expectedRoute.OutputDecoders, route.GetProperty("outputDecoders").EnumerateArray().Select(value => value.GetString()));
        }
        Assert.Contains("neither a shipping runtime selection", contract.RootElement.GetProperty("purpose").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G04RunnerPreservesPortableProofBoundariesAndExplicitComponentSelection()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2G04DeliveryProof.ps1"));

        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("g0.4-delivery-proof-contract.json", script);
        Assert.Contains("fixture-source-inventory.json", script);
        Assert.Contains("PATH fallback is prohibited", script);
        Assert.Contains("OutputDirectory must be outside the repository", script);
        Assert.Contains("reparse-point", script);
        Assert.Contains("libopenh264", script);
        Assert.Contains("'-profile:v','constrained_baseline'", script);
        Assert.Contains("'-allow_skip_frames','false'", script);
        Assert.Contains("'-rc_mode','bitrate'", script);
        Assert.Contains("'-c:a','aac'", script);
        Assert.Contains("'-profile:a','aac_low'", script);
        Assert.Contains("'-movflags','+faststart'", script);
        Assert.Contains("Assert-Mp4Atoms", script);
        Assert.Contains("Read-BeUInt32", script);
        Assert.Contains("top-level moov box", script);
        Assert.Contains("libvpx-vp9", script);
        Assert.Contains("libopus", script);
        Assert.Contains("Assert-WebmCues", script);
        Assert.Contains("Read-EbmlVint", script);
        Assert.Contains("bounded Segment-level Cues", script);
        Assert.Contains("libmp3lame", script);
        Assert.Contains("pcm_s16le", script);
        Assert.Contains("'-rf64','never'", script);
        Assert.Contains("'-compression_level','5'", script);
        Assert.Contains("'-pred','mixed'", script);
        Assert.Contains("'-huffman','optimal'", script);
        Assert.Contains("presentation-order/frame identity oracle", script);
        Assert.Contains("codec-delay-aware sample oracle", script);
        Assert.Contains("stereo-tone oracle", script);
        Assert.Contains("opposed-phase oracle", script);
        Assert.Contains("F1 requires left 440 Hz and right 880 Hz", script);
        Assert.Contains("Fixture report/inventory/actual-root file set differs", script);
        Assert.Contains("OutputDirectory ancestor is a reparse-point", script);
        Assert.Contains("preflight", script);
        Assert.Contains(".partial-", script);
        Assert.Contains("Move-Item", script);
        Assert.Contains("invalid-oracle", script);
        Assert.Contains("not-run", script);
        Assert.Contains("not a shipping, distribution, patent, legal, independent-playback, or long-form conclusion", script);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G04RunnerRejectsRepositoryOutputBeforeRuntimeUse()
    {
        var result = RunPowerShell(RepositoryPath("eng", "gate0", "Invoke-P2G04DeliveryProof.ps1"), "C:\\not-a-runtime", "C:\\not-a-fixture-root", RepositoryPath());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the repository", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G04RunnerWritesStructuredEvidenceForAValidOutputWithPreflightFailure()
    {
        var output = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-G04-preflight-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = RunPowerShell(RepositoryPath("eng", "gate0", "Invoke-P2G04DeliveryProof.ps1"), "C:\\not-a-runtime", "C:\\not-a-fixture-root", output);
            Assert.NotEqual(0, result.ExitCode);
            var evidencePath = Path.Combine(output, "g0.4-delivery-proof-evidence.json");
            Assert.True(File.Exists(evidencePath), result.Output);
            using var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(evidencePath));
            Assert.Equal("failed", evidence.RootElement.GetProperty("preflight").GetProperty("status").GetString());
            Assert.Empty(evidence.RootElement.GetProperty("semanticProofs").EnumerateArray());
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    [Gate0RuntimeFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void OptInG04EvidenceHasElevenExplicitCapabilities()
    {
        var evidencePath = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_G04_DELIVERY_EVIDENCE");
        Assert.False(string.IsNullOrWhiteSpace(evidencePath));
        using var evidence = System.Text.Json.JsonDocument.Parse(File.ReadAllText(evidencePath!));
        var root = evidence.RootElement;
        Assert.True(root.GetProperty("fixtureReportVerified").GetBoolean());
        Assert.Matches("^[A-F0-9]{64}$", root.GetProperty("fixtureReportSha256").GetString()!);
        Assert.Contains("not a shipping", root.GetProperty("limitations").GetString(), StringComparison.OrdinalIgnoreCase);
        var proofs = root.GetProperty("semanticProofs").EnumerateArray().ToArray();
        Assert.Equal(11, proofs.Length);
        var expectedIds = new[]
        {
            "Video.Export.Compatibility.Mp4H264Aac.P2OpenH264", "Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264",
            "Video.Export.Open.WebmVp9Opus", "Video.Export.Open.WebmVp9VideoOnly", "Audio.Export.M4aAac", "Audio.Export.Mp3",
            "Audio.Export.OggOpus", "Audio.Export.WavPcm", "Audio.Export.Flac", "Image.Export.Png", "Image.Export.Jpeg"
        };
        Assert.Equal(expectedIds.Order(), proofs.Select(proof => proof.GetProperty("capabilityId").GetString()).Order());
        Assert.All(proofs, proof => Assert.Equal("passed", proof.GetProperty("status").GetString()));
        Assert.All(proofs, proof => Assert.True(proof.GetProperty("executedSemanticProof").GetBoolean()));
        Assert.All(proofs, proof =>
        {
            Assert.Equal("P2.BtbnLgplShared.WindowsX64.20260820", proof.GetProperty("runtimeProfileId").GetString());
            Assert.Equal("runtime-identity.json", proof.GetProperty("runtimeIdentityEvidence").GetString());
            Assert.Equal("runtime-identity.json", proof.GetProperty("dependencyIdentityReference").GetString());
            Assert.Matches("^[A-F0-9]{64}$", proof.GetProperty("dependencyIdentitySha256").GetString()!);
            var components = proof.GetProperty("details").GetProperty("componentSelection");
            Assert.True(components.GetProperty("inputDemuxerTokens").GetArrayLength() > 0);
            Assert.False(string.IsNullOrWhiteSpace(components.GetProperty("outputMuxerToken").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(components.GetProperty("probeDemuxerToken").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(components.GetProperty("decodeDemuxerToken").GetString()));
        });
        foreach (var imageId in new[] { "Image.Export.Png", "Image.Export.Jpeg" })
        {
            var oracle = proofs.Single(proof => proof.GetProperty("capabilityId").GetString() == imageId).GetProperty("details").GetProperty("oracle");
            Assert.True(oracle.GetProperty("passed").GetBoolean());
            Assert.True(oracle.TryGetProperty("kind", out _));
            Assert.True(oracle.TryGetProperty("expected", out _));
            Assert.True(oracle.TryGetProperty("observed", out _));
            Assert.True(oracle.TryGetProperty("threshold", out _));
        }
    }

    private static ProcessResult RunPowerShell(string script, string runtimeRoot, string fixtureRoot, string outputDirectory)
    {
        var start = new System.Diagnostics.ProcessStartInfo { FileName = "pwsh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-File", script, "-RuntimeRoot", runtimeRoot, "-FixtureRoot", fixtureRoot, "-OutputDirectory", outputDirectory }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout + stderr);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
