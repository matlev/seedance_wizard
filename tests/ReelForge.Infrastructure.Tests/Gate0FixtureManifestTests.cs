using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ReelForge.Tests;

public sealed class WindowsReparsePointFactAttribute : FactAttribute
{
    public WindowsReparsePointFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This behavioral regression covers Windows reparse points.";
            return;
        }

        var probePath = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-ReparseProbe", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(probePath)!);
            Directory.CreateSymbolicLink(probePath, Path.GetTempPath());
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Skip = $"Windows cannot create the reparse-point test link: {exception.Message}";
        }
        finally
        {
            if (Directory.Exists(probePath)) Directory.Delete(probePath);
        }
    }
}

public sealed class Gate0FixtureManifestTests
{
    [Fact]
    public void FixtureManifestDefinesEveryApprovedFixtureWithExplicitConcreteComponents()
    {
        using var manifest = OpenRepositoryJson("eng", "gate0", "fixture-manifest.json");

        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("generatorVersion").GetInt32());
        Assert.Equal("P2.BtbnLgplShared.WindowsX64.20260820", manifest.RootElement.GetProperty("profileId").GetString());

        var fixtures = manifest.RootElement.GetProperty("fixtures").EnumerateArray().ToArray();
        Assert.Equal(["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8"], fixtures.Select(fixture => fixture.GetProperty("id").GetString()));

        foreach (var fixture in fixtures)
        {
            var packaging = fixture.GetProperty("plannedPackaging");
            Assert.True(packaging.GetProperty("explicitSelectionsRequired").GetBoolean());
            Assert.NotEmpty(packaging.GetProperty("decoders").EnumerateArray().ToArray());
            Assert.NotEmpty(packaging.GetProperty("encoders").EnumerateArray().ToArray());
            Assert.NotEmpty(packaging.GetProperty("muxers").EnumerateArray().ToArray());
            Assert.NotEmpty(packaging.GetProperty("filters").EnumerateArray().ToArray());
        }

        var f8 = fixtures.Single(fixture => fixture.GetProperty("id").GetString() == "F8");
        Assert.Equal(
            ["0:v:0", "1:v:0", "2:a:0", "3:a:0"],
            f8.GetProperty("plannedPackaging").GetProperty("requiredSourceBuildInputMaps").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(
            ["0:v:0", "0:v:1", "0:a:0", "0:a:1"],
            f8.GetProperty("plannedPackaging").GetProperty("requiredPackagedStreamSelectors").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void ExpectedTruthsCoverExactTimingAndDistinguishableStreams()
    {
        using var truths = OpenRepositoryJson("eng", "gate0", "expected-truths.json");
        var fixtures = truths.RootElement.GetProperty("fixtures");

        var f7 = fixtures.GetProperty("F7");
        Assert.True(f7.GetProperty("video").GetProperty("variableFrameRate").GetBoolean());
        Assert.Equal("1/90000", f7.GetProperty("video").GetProperty("timeBase").GetString());

        var presentationFrames = f7.GetProperty("presentationFrames").EnumerateArray().ToArray();
        Assert.Equal(5, presentationFrames.Length);
        Assert.Equal(90000, presentationFrames[0].GetProperty("presentationTimestamp").GetInt32());
        Assert.True(presentationFrames.Zip(presentationFrames.Skip(1)).All(pair =>
            pair.First.GetProperty("presentationTimestamp").GetInt32() < pair.Second.GetProperty("presentationTimestamp").GetInt32()));
        Assert.Equal(["f7-red", "f7-green", "f7-blue", "f7-white", "f7-black"], presentationFrames.Select(frame => frame.GetProperty("frameId").GetString()));

        var f8 = fixtures.GetProperty("F8");
        Assert.Equal(["0:v:0", "0:v:1"], f8.GetProperty("videoStreams").EnumerateArray().Select(stream => stream.GetProperty("expectedStreamSpecifier").GetString()));
        Assert.Equal([440, 880], f8.GetProperty("audioStreams").EnumerateArray().Select(stream => stream.GetProperty("toneHz").GetInt32()));

        var f5Variants = fixtures.GetProperty("F5").GetProperty("variants").EnumerateArray().ToArray();
        Assert.Equal([0, 1], f5Variants.Select(variant => variant.GetProperty("expectedAudioStreams").GetInt32()));
        Assert.Equal(0, f5Variants[1].GetProperty("expectedPeak").GetInt32());

        var f6 = fixtures.GetProperty("F6");
        Assert.Equal(3600, f6.GetProperty("durationSeconds").GetInt32());
        Assert.Equal(30000, f6.GetProperty("segmentCount").GetInt32());
        Assert.Equal(120, f6.GetProperty("segmentDurationMilliseconds").GetInt32());
        Assert.Equal(3599880, f6.GetProperty("expectedTimestampRange").GetProperty("lastMilliseconds").GetInt32());

        var f1 = fixtures.GetProperty("F1");
        Assert.Equal(2, f1.GetProperty("audio").GetProperty("channels").GetInt32());
        Assert.Equal(10, f1.GetProperty("visualMarkers").GetProperty("safeAreaInsetPercent").GetInt32());
        Assert.Equal([0, 1, 2], f1.GetProperty("frames").EnumerateArray().Select(frame => frame.GetProperty("frameNumber").GetInt32()));

        var f4 = fixtures.GetProperty("F4");
        Assert.Equal([32000, 44100, 48000], f4.GetProperty("variants").EnumerateArray().Select(variant => variant.GetProperty("sampleRate").GetInt32()));
        Assert.Equal(["wav", "flac"], f4.GetProperty("losslessOutputs").EnumerateArray().Select(output => output.GetProperty("muxer").GetString()));
        Assert.Equal(180, f4.GetProperty("variants")[2].GetProperty("expectedPhaseRelationshipDegrees").GetInt32());
    }

    [Fact]
    public void ManifestPreservesIndependentProvenanceAndApprovedFontArtifactRequirement()
    {
        using var manifest = OpenRepositoryJson("eng", "gate0", "fixture-manifest.json");
        var provenance = manifest.RootElement.GetProperty("provenance");

        Assert.False(provenance.GetProperty("generatedMediaCommitted").GetBoolean());
        Assert.Contains("PATH fallback prohibited", provenance.GetProperty("toolPathPolicy").GetString());
        Assert.Contains("System-font fallback is prohibited", provenance.GetProperty("fontPolicy").GetString());

        var f3 = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(fixture => fixture.GetProperty("id").GetString() == "F3");
        Assert.Contains("ppm-basic-color-oracle", f3.GetProperty("sourcePrimitives").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["crop", "scale", "format", "overlay", "colorlevels", "hue", "ass"], f3.GetProperty("plannedPackaging").GetProperty("filters").EnumerateArray().Select(value => value.GetString()));
        var fontRequirement = f3.GetProperty("blockedPrerequisites")[0];
        Assert.Equal("Font.Licensed.UnicodeTestFont", fontRequirement.GetProperty("id").GetString());
        Assert.Equal("approved-artifacts-ready-for-proof", fontRequirement.GetProperty("status").GetString());
    }

    [Fact]
    public void GeneratorContainsNoPathDiscoveryOrMediaProofInvocation()
    {
        var generator = File.ReadAllText(RepositoryPath("eng", "gate0", "Generate-Fixtures.ps1"));

        Assert.Contains("[string]$FfmpegPath", generator);
        Assert.Contains("[string]$FfprobePath", generator);
        Assert.Contains("PATH fallback is prohibited", generator);
        Assert.DoesNotContain("Get-Command", generator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("& $FfmpegPath", generator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("& $FfprobePath", generator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$outputEqualsRepository = $resolvedOutput.Equals($repositoryRoot", generator);
    }

    [Fact]
    public void GeneratorProducesApprovedPrimitivesAndRejectsUntrustedProvenance()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-FixtureTest", Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(temporaryRoot, "runtime");
        var outputRoot = Path.Combine(temporaryRoot, "output");
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "bin"));
        var ffmpeg = Path.Combine(runtimeRoot, "bin", "ffmpeg.exe");
        var ffprobe = Path.Combine(runtimeRoot, "bin", "ffprobe.exe");
        File.WriteAllText(ffmpeg, "fixture-generator-path-boundary-only");
        File.WriteAllText(ffprobe, "fixture-generator-path-boundary-only");

        try
        {
            var success = RunGenerator(ffmpeg, ffprobe, runtimeRoot, outputRoot);
            Assert.Equal(0, success.ExitCode);
            Assert.Equal(320 * 180 * 3 + "P6\n320 180\n255\n".Length, new FileInfo(Path.Combine(outputRoot, "F1", "f1-pattern-000.ppm")).Length);
            Assert.Equal(23040, new FileInfo(Path.Combine(outputRoot, "F1", "f1-sync-440hz-880hz-48000-stereo.pcm")).Length);
            Assert.Equal(32000, new FileInfo(Path.Combine(outputRoot, "F4", "f4-mono-32000-1000hz.pcm")).Length);
            Assert.Equal(96000, new FileInfo(Path.Combine(outputRoot, "F4", "f4-stereo-48000-1000hz-opposed.pcm")).Length);

            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "generated-fixture-report.json")));
            Assert.False(report.RootElement.GetProperty("externalMediaCommandsExecuted").GetBoolean());
            var approvedInventory = JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "fixture-source-inventory.json")));
            Assert.Equal(1, report.RootElement.GetProperty("approvedInventory").GetProperty("schemaVersion").GetInt32());
            Assert.Equal(1, report.RootElement.GetProperty("approvedInventory").GetProperty("inventoryVersion").GetInt32());
            Assert.Equal("checked-in approved inventory", report.RootElement.GetProperty("approvedInventory").GetProperty("approvalStatus").GetString());
            Assert.False(report.RootElement.GetProperty("approvedInventory").GetProperty("testOnlyOverride").GetBoolean());
            Assert.Equal("eng/gate0/fixture-source-inventory.json", report.RootElement.GetProperty("approvedInventory").GetProperty("path").GetString());
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(RepositoryPath("eng", "gate0", "fixture-source-inventory.json")))),
                report.RootElement.GetProperty("approvedInventory").GetProperty("sha256").GetString());

            var expectedInventory = approvedInventory.RootElement.GetProperty("files").EnumerateArray()
                .ToDictionary(file => file.GetProperty("path").GetString()!, StringComparer.Ordinal);
            var reportedFiles = report.RootElement.GetProperty("sourceFiles").EnumerateArray().ToArray();
            Assert.Equal(expectedInventory.Keys.Order(), reportedFiles.Select(file => file.GetProperty("path").GetString()).Order());
            Assert.DoesNotContain(reportedFiles, file => file.GetProperty("path").GetString() == "generated-fixture-report.json");
            foreach (var file in reportedFiles)
            {
                var relativePath = file.GetProperty("path").GetString()!;
                Assert.DoesNotContain('\\', relativePath);
                Assert.False(Path.IsPathRooted(relativePath));
                Assert.DoesNotContain("..", relativePath.Split('/'));
                var path = Path.Combine(outputRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(file.GetProperty("length").GetInt64(), new FileInfo(path).Length);
                Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                var expected = expectedInventory[relativePath];
                Assert.Equal(expected.GetProperty("length").GetInt64(), file.GetProperty("length").GetInt64());
                Assert.Equal(expected.GetProperty("sha256").GetString(), file.GetProperty("sha256").GetString());
            }

            var generatorSources = report.RootElement.GetProperty("generatorSourceSet").EnumerateArray().ToArray();
            Assert.Equal(4, generatorSources.Length);
            Assert.All(generatorSources, source =>
            {
                var sourcePath = source.GetProperty("path").GetString()!;
                Assert.StartsWith("eng/gate0/", sourcePath, StringComparison.Ordinal);
                Assert.Equal(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(RepositoryPath(sourcePath.Split('/'))))),
                    source.GetProperty("sha256").GetString());
            });

            var rejected = RunGenerator(ffmpeg, ffprobe, runtimeRoot, RepositoryPath());
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("outside the repository", rejected.StandardError, StringComparison.OrdinalIgnoreCase);

            var contaminatedOutput = Path.Combine(temporaryRoot, "contaminated-output");
            Directory.CreateDirectory(contaminatedOutput);
            File.WriteAllText(Path.Combine(contaminatedOutput, "stale.txt"), "must-not-enter-evidence");
            var contaminated = RunGenerator(ffmpeg, ffprobe, runtimeRoot, contaminatedOutput);
            Assert.NotEqual(0, contaminated.ExitCode);
            Assert.Contains("new or empty", contaminated.StandardError, StringComparison.OrdinalIgnoreCase);

            var tamperedInventory = Path.Combine(temporaryRoot, "tampered-inventory.json");
            File.WriteAllText(
                tamperedInventory,
                File.ReadAllText(RepositoryPath("eng", "gate0", "fixture-source-inventory.json"))
                    .Replace("2F0A75C882C49BABF9A2B29B34DDECDCDABFFF05EF5097FF43BD91E96BC6D173", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", StringComparison.Ordinal));
            var tampered = RunGenerator(ffmpeg, ffprobe, runtimeRoot, Path.Combine(temporaryRoot, "tampered-output"), tamperedInventory);
            Assert.NotEqual(0, tampered.ExitCode);
            Assert.Contains("does not match the approved inventory", tampered.StandardError, StringComparison.OrdinalIgnoreCase);

            var testOnlyInventory = Path.Combine(temporaryRoot, "test-only-inventory.json");
            File.Copy(RepositoryPath("eng", "gate0", "fixture-source-inventory.json"), testOnlyInventory);
            var testOnly = RunGenerator(ffmpeg, ffprobe, runtimeRoot, Path.Combine(temporaryRoot, "test-only-output"), testOnlyInventory);
            Assert.Equal(0, testOnly.ExitCode);
            using var testOnlyReport = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporaryRoot, "test-only-output", "generated-fixture-report.json")));
            Assert.True(testOnlyReport.RootElement.GetProperty("approvedInventory").GetProperty("testOnlyOverride").GetBoolean());
            Assert.Equal("test-only override; not approved for Gate 0 proof", testOnlyReport.RootElement.GetProperty("approvedInventory").GetProperty("approvalStatus").GetString());

            var unsafeInventory = Path.Combine(temporaryRoot, "unsafe-inventory.json");
            File.WriteAllText(
                unsafeInventory,
                File.ReadAllText(RepositoryPath("eng", "gate0", "fixture-source-inventory.json"))
                    .Replace("\"expected-truths.json\"", "\"../escaped.json\"", StringComparison.Ordinal));
            var unsafeInventoryResult = RunGenerator(ffmpeg, ffprobe, runtimeRoot, Path.Combine(temporaryRoot, "unsafe-output"), unsafeInventory);
            Assert.NotEqual(0, unsafeInventoryResult.ExitCode);
            Assert.Contains("unsafe path", unsafeInventoryResult.StandardError, StringComparison.OrdinalIgnoreCase);

            var missingInventory = RunGenerator(ffmpeg, ffprobe, runtimeRoot, Path.Combine(temporaryRoot, "missing-inventory-output"), Path.Combine(temporaryRoot, "does-not-exist.json"));
            Assert.NotEqual(0, missingInventory.ExitCode);
            Assert.Contains("existing explicit rooted file", missingInventory.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [WindowsReparsePointFact]
    public void GeneratorRejectsOutputUnderExistingRepositorySymlinkBeforeWriting()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-FixtureLinkTest", Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(temporaryRoot, "runtime");
        var linkPath = Path.Combine(temporaryRoot, "repository-link");
        Directory.CreateDirectory(Path.Combine(runtimeRoot, "bin"));
        var ffmpeg = Path.Combine(runtimeRoot, "bin", "ffmpeg.exe");
        var ffprobe = Path.Combine(runtimeRoot, "bin", "ffprobe.exe");
        File.WriteAllText(ffmpeg, "fixture-generator-path-boundary-only");
        File.WriteAllText(ffprobe, "fixture-generator-path-boundary-only");

        try
        {
            Directory.CreateSymbolicLink(linkPath, RepositoryPath());

            var escapedOutputName = $"fixture-output-{Guid.NewGuid():N}";
            var result = RunGenerator(ffmpeg, ffprobe, runtimeRoot, Path.Combine(linkPath, escapedOutputName));
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse point", result.StandardError, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(RepositoryPath(), escapedOutputName)));
        }
        finally
        {
            if (Directory.Exists(linkPath)) Directory.Delete(linkPath);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static ProcessResult RunGenerator(string ffmpeg, string ffprobe, string runtimeRoot, string outputRoot, string? inventoryPath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-File", RepositoryPath("eng", "gate0", "Generate-Fixtures.ps1"),
            "-FfmpegPath", ffmpeg,
            "-FfprobePath", ffprobe,
            "-ApprovedRuntimeRoot", runtimeRoot,
            "-OutputDirectory", outputRoot
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (inventoryPath is not null)
        {
            startInfo.ArgumentList.Add("-FixtureSourceInventoryPath");
            startInfo.ArgumentList.Add(inventoryPath);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell fixture generator.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static JsonDocument OpenRepositoryJson(params string[] segments) =>
        JsonDocument.Parse(File.ReadAllText(RepositoryPath(segments)));

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
