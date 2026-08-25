using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0P2VisualProofScriptTests
{
    [Fact]
    public void VisualProofRunnerIsBoundToApprovedRuntimeFixturesAndCapabilities()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2VisualProof.ps1"));

        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("generated-fixture-report.json", script);
        Assert.Contains("Fixture source hash/length mismatch against checked-in inventory", script);
        Assert.Contains("PATH fallback is prohibited", script);
        Assert.Contains("OutputDirectory must be outside the repository", script);
        Assert.Contains("Video.Composite.TransformAlphaAndColor", script);
        Assert.Contains("Video.Transition.CrossDissolveAndBlack", script);
        Assert.Contains("Audio.Waveform.Generate", script);
        Assert.Contains("showwavespic", script);
        Assert.Contains("xfade", script);
        Assert.Contains("colorlevels=romin=0.1:gomin=0.1:bomin=0.1:romax=1:gomax=1:bomax=1", script);
        Assert.Contains("colorlevels=romin=0.2:gomin=0.2:bomin=0.2:romax=0.8:gomax=0.8:bomax=0.8", script);
        Assert.Contains("hue=s=0", script);
        Assert.Contains("Assert-RepeatedHash", script);
        Assert.Contains("[0:v:0]format=rgb24[base]", script);
        Assert.Contains("[1:v:0]crop=80:60:0:0", script);
        Assert.Contains("'-map','0:v:0','-vf'", script);
        Assert.Contains("outputFilterMap='[out]'", script);
        Assert.Contains("rawvideo", script);
        Assert.Contains("ffv1", script);
        Assert.Contains("matroska", script);
        Assert.Contains("ProbeVideoTimestamps", script);
        Assert.Contains("digital-silence", script);
        Assert.Contains("Move-Item", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("Get-ArtifactBindings", script);
        Assert.Contains("execution-failed", script);
        Assert.Contains("invalid-oracle", script);
        Assert.Contains("Visual proof execution failed", script);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eq=", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisualProofRunnerRejectsStaleEvidenceOutputBeforeRuntimeOrFixtureUse()
    {
        var output = Path.Combine(Path.GetTempPath(), $"ReelForge-Gate0-stale-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "stale.txt"), "stale");
        try
        {
            var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(RepositoryPath("eng", "gate0", "Invoke-P2VisualProof.ps1"));
            startInfo.ArgumentList.Add("-RuntimeRoot");
            startInfo.ArgumentList.Add(Path.GetPathRoot(output)!);
            startInfo.ArgumentList.Add("-FixtureRoot");
            startInfo.ArgumentList.Add(Path.GetPathRoot(output)!);
            startInfo.ArgumentList.Add("-OutputDirectory");
            startInfo.ArgumentList.Add(output);
            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            process!.WaitForExit();
            var outputText = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("new or empty", outputText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void FixtureValidationRejectsForgedAndEscapingReportsAgainstCheckedInInventory()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"ReelForge-Gate0-fixture-forgery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        try
        {
            var inventoryPath = RepositoryPath("eng", "gate0", "fixture-source-inventory.json");
            using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
            var root = inventory.RootElement;
            var approved = root.GetProperty("schemaVersion").GetInt32();
            var inventoryVersion = root.GetProperty("inventoryVersion").GetInt32();
            var inventoryHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(inventoryPath)));

            File.WriteAllText(Path.Combine(fixtureRoot, "generated-fixture-report.json"), $$"""
                {"schemaVersion":1,"profileId":"P2.BtbnLgplShared.WindowsX64.20260820","externalMediaCommandsExecuted":false,"approvedInventory":{"schemaVersion":{{approved}},"inventoryVersion":{{inventoryVersion}},"path":"eng/gate0/fixture-source-inventory.json","sha256":"{{inventoryHash}}"},"sourceFiles":[]}
                """);
            var forged = RunFixtureValidationProbe(fixtureRoot, inventoryPath);
            Assert.NotEqual(0, forged.ExitCode);
            Assert.Contains("does not exactly match", forged.Output, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(Path.Combine(fixtureRoot, "generated-fixture-report.json"), $$"""
                {"schemaVersion":1,"profileId":"P2.BtbnLgplShared.WindowsX64.20260820","externalMediaCommandsExecuted":false,"approvedInventory":{"schemaVersion":{{approved}},"inventoryVersion":{{inventoryVersion}},"path":"eng/gate0/fixture-source-inventory.json","sha256":"{{inventoryHash}}"},"sourceFiles":[{"path":"../escape.pcm","length":0,"sha256":"00"}]}
                """);
            var escaped = RunFixtureValidationProbe(fixtureRoot, inventoryPath);
            Assert.NotEqual(0, escaped.ExitCode);
            Assert.Contains("invalid path segment", escaped.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [Fact]
    public void UnexpectedProofFailuresAreClassifiedAndCannotBeRecordedAsContractBlocks()
    {
        var scriptPath = RepositoryPath("eng", "gate0", "Invoke-P2VisualProof.ps1");
        var command = "$scriptPath=" + PowerShellLiteral(scriptPath) + "; " +
            "$source = Get-Content -LiteralPath $scriptPath -Raw; " +
            "$functionStart = $source.IndexOf('function Require-OutsideRepositoryEmptyDirectory', [System.StringComparison]::Ordinal); " +
            "$marker = '$output = Require-OutsideRepositoryEmptyDirectory $OutputDirectory'; " +
            "$prefix = $source.Substring($functionStart, $source.IndexOf($marker, [System.StringComparison]::Ordinal) - $functionStart); " +
            "Invoke-Expression $prefix; " +
            "if ((Get-ExecutionFailureClassification ([Exception]::new('frame-count oracle failed'))) -ne 'invalid-oracle') { exit 2 }; " +
            "if ((Get-ExecutionFailureClassification ([Exception]::new('ffmpeg exited'))) -ne 'execution-failed') { exit 3 }";
        var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        process!.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    [Gate0VisualEvidenceFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void LiveP2VisualEvidenceRecordsExactContractOutcomes()
    {
        var evidencePath = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_VISUAL_EVIDENCE_PATH")!;
        using var evidence = JsonDocument.Parse(File.ReadAllText(evidencePath));
        var proofs = evidence.RootElement.GetProperty("semanticProofs").EnumerateArray().ToArray();

        var composite = proofs.Single(proof => proof.GetProperty("capabilityId").GetString() == "Video.Composite.TransformAlphaAndColor");
        Assert.Equal("passed", composite.GetProperty("status").GetString());
        var compositeSelection = composite.GetProperty("details").GetProperty("componentSelection");
        Assert.Equal(["crop", "scale", "format", "overlay", "colorlevels", "hue"], compositeSelection.GetProperty("filters").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(["0:v:0", "1:v:0"], compositeSelection.GetProperty("inputStreamSelectors").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal("[out]", compositeSelection.GetProperty("outputFilterMap").GetString());
        Assert.Equal("0:v:0", compositeSelection.GetProperty("colorInputStreamSelector").GetString());
        var compositeCommands = composite.GetProperty("commands").EnumerateArray().ToArray();
        var alphaCommands = compositeCommands.Where(command => command.GetProperty("step").GetString()!.StartsWith("encode-composite-alpha", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, alphaCommands.Length);
        Assert.All(alphaCommands, command =>
        {
            var arguments = command.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains("[0:v:0]format=rgb24[base];[1:v:0]crop=80:60:0:0,scale=160:120:flags=neighbor,format=rgba[overlay];[base][overlay]overlay=x=80:y=30:format=rgb,format=rgb24[out]", arguments);
            Assert.True(HasExactMap(arguments, "[out]"));
        });
        var basicColorCommands = compositeCommands.Where(command => command.GetProperty("step").GetString()!.StartsWith("encode-basic-color-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, basicColorCommands.Length);
        Assert.All(basicColorCommands, command => Assert.True(HasExactMap(command.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).ToArray(), "0:v:0")));
        Assert.Equal(64, composite.GetProperty("details").GetProperty("alphaOverlay").GetProperty("repeatSha256").GetString()!.Length);
        Assert.Equal(64, composite.GetProperty("details").GetProperty("brightness").GetProperty("repeatSha256").GetString()!.Length);
        Assert.Equal(64, composite.GetProperty("details").GetProperty("contrast").GetProperty("repeatSha256").GetString()!.Length);
        Assert.Equal(64, composite.GetProperty("details").GetProperty("saturation").GetProperty("repeatSha256").GetString()!.Length);
        Assert.True(composite.GetProperty("details").GetProperty("saturation").GetProperty("outputChannelDelta").GetInt32() <= 3);

        var transition = proofs.Single(proof => proof.GetProperty("capabilityId").GetString() == "Video.Transition.CrossDissolveAndBlack");
        Assert.Equal("passed", transition.GetProperty("status").GetString());
        Assert.Equal(6, transition.GetProperty("details").GetProperty("crossTiming").GetProperty("FrameCount").GetInt32());
        Assert.Equal([0, 40, 80, 120, 160, 200], transition.GetProperty("details").GetProperty("crossTiming").GetProperty("PresentationTimestampsMilliseconds").EnumerateArray().Select(value => value.GetInt32()).ToArray());

        var waveform = proofs.Single(proof => proof.GetProperty("capabilityId").GetString() == "Audio.Waveform.Generate");
        Assert.Equal("passed", waveform.GetProperty("status").GetString());
        Assert.True(waveform.GetProperty("details").GetProperty("toneWavePixels").GetInt32() >= 100);
        Assert.Equal(0, waveform.GetProperty("details").GetProperty("silenceWavePixels").GetInt32());
        Assert.Equal(64, waveform.GetProperty("details").GetProperty("sha256").GetString()!.Length);
        Assert.True(evidence.RootElement.GetProperty("succeeded").GetBoolean());
        var artifacts = evidence.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
        Assert.NotEmpty(artifacts);
        Assert.All(artifacts, artifact =>
        {
            Assert.False(Path.IsPathRooted(artifact.GetProperty("path").GetString()));
            Assert.True(artifact.GetProperty("length").GetInt64() >= 0);
            Assert.Equal(64, artifact.GetProperty("sha256").GetString()!.Length);
        });
    }

    [Fact]
    public void VisualProofCapabilitiesRemainInTheAuthoritativeContract()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "semantic-proof-contract.json")));
        var capabilities = contract.RootElement.GetProperty("capabilities").EnumerateArray().ToArray();

        foreach (var id in new[]
        {
            "Video.Composite.TransformAlphaAndColor",
            "Video.Transition.CrossDissolveAndBlack",
            "Audio.Waveform.Generate"
        })
        {
            Assert.Contains(capabilities, capability => capability.GetProperty("id").GetString() == id);
            Assert.True(capabilities.Single(capability => capability.GetProperty("id").GetString() == id).GetProperty("required").GetBoolean());
        }
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private static (int ExitCode, string Output) RunFixtureValidationProbe(string fixtureRoot, string inventoryPath)
    {
        var scriptPath = RepositoryPath("eng", "gate0", "Invoke-P2VisualProof.ps1");
        var command = "$scriptPath=" + PowerShellLiteral(scriptPath) + "; " +
            "$fixtureRoot=" + PowerShellLiteral(fixtureRoot) + "; " +
            "$inventoryPath=" + PowerShellLiteral(inventoryPath) + "; " +
            "$source = Get-Content -LiteralPath $scriptPath -Raw; " +
            "$functionStart = $source.IndexOf('function Require-OutsideRepositoryEmptyDirectory', [System.StringComparison]::Ordinal); " +
            "$marker = '$output = Require-OutsideRepositoryEmptyDirectory $OutputDirectory'; " +
            "$prefix = $source.Substring($functionStart, $source.IndexOf($marker, [System.StringComparison]::Ordinal) - $functionStart); " +
            "Invoke-Expression $prefix; Test-FixtureReport $fixtureRoot $inventoryPath";
        var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        process!.WaitForExit();
        return (process.ExitCode, process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
    }

    private static string PowerShellLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static bool HasExactMap(string?[] arguments, string streamSpecifier) =>
        arguments.Zip(arguments.Skip(1)).Any(pair => pair.First == "-map" && pair.Second == streamSpecifier);
}

public sealed class Gate0VisualEvidenceFactAttribute : FactAttribute
{
    public Gate0VisualEvidenceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_VISUAL_EVIDENCE_PATH")))
        {
            Skip = "Gate 0 P2 visual-evidence assertion is opt-in and requires an explicit generated evidence path.";
        }
    }
}
