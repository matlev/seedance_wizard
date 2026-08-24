using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace ReelForge.Tests;

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
    public void ManifestPreservesIndependentProvenanceAndBlockedFontRequirement()
    {
        using var manifest = OpenRepositoryJson("eng", "gate0", "fixture-manifest.json");
        var provenance = manifest.RootElement.GetProperty("provenance");

        Assert.False(provenance.GetProperty("generatedMediaCommitted").GetBoolean());
        Assert.Contains("PATH fallback prohibited", provenance.GetProperty("toolPathPolicy").GetString());
        Assert.Contains("System-font fallback is prohibited", provenance.GetProperty("fontPolicy").GetString());

        var f3 = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(fixture => fixture.GetProperty("id").GetString() == "F3");
        var fontRequirement = f3.GetProperty("blockedPrerequisites")[0];
        Assert.Equal("Font.Licensed.UnicodeTestFont", fontRequirement.GetProperty("id").GetString());
        Assert.Equal("blocked", fontRequirement.GetProperty("status").GetString());
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
    public void GeneratorProducesHashedPrimitivesAndRejectsRepositoryOutput()
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
            foreach (var file in report.RootElement.GetProperty("sourceFiles").EnumerateArray())
            {
                var path = Path.Combine(outputRoot, file.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(file.GetProperty("length").GetInt64(), new FileInfo(path).Length);
                Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            }

            var rejected = RunGenerator(ffmpeg, ffprobe, runtimeRoot, RepositoryPath());
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("outside the repository", rejected.StandardError, StringComparison.OrdinalIgnoreCase);

            var contaminatedOutput = Path.Combine(temporaryRoot, "contaminated-output");
            Directory.CreateDirectory(contaminatedOutput);
            File.WriteAllText(Path.Combine(contaminatedOutput, "stale.txt"), "must-not-enter-evidence");
            var contaminated = RunGenerator(ffmpeg, ffprobe, runtimeRoot, contaminatedOutput);
            Assert.NotEqual(0, contaminated.ExitCode);
            Assert.Contains("new or empty", contaminated.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static ProcessResult RunGenerator(string ffmpeg, string ffprobe, string runtimeRoot, string outputRoot)
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
