using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0P2SemanticProofScriptTests
{
    [Fact]
    public void SemanticProofRunnerKeepsTheReviewedExecutionBoundariesExplicit()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2SemanticProof.ps1"));

        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("Generate-Fixtures.ps1", script);
        Assert.Contains("PATH fallback is prohibited", script);
        Assert.Contains("OutputDirectory must be outside the repository", script);
        Assert.Contains("-c:v','ppm", script);
        Assert.Contains("-c:v','ffv1", script);
        Assert.Contains("-c:a','pcm_s16le", script);
        Assert.Contains("-c:a','flac", script);
        Assert.Contains("-f','matroska", script);
        Assert.Contains("-f','concat", script);
        Assert.Contains("'0:v:0'", script);
        Assert.Contains("'0:v:1'", script);
        Assert.Contains("'0:a:0'", script);
        Assert.Contains("'0:a:1'", script);
        Assert.Contains("Font.Licensed.UnicodeTestFont", script);
        Assert.Contains("'blocked'", script);
        Assert.Contains("IncludeLongForm", script);
        Assert.Contains("fixtureProofs", script);
        Assert.Contains("inspectionReadiness", script);
        Assert.Contains("Media.Inspect.StructureAndTiming", script);
        Assert.Contains("Assert-ExactPts", script);
        Assert.Contains("Matroska container time base is recorded separately", script);
        Assert.Contains("'30000/1001'", script);
        Assert.Contains("$vs.avg_frame_rate -ne '25/1'", script);
        Assert.Contains("capabilityVerdicts=@()", script);
        Assert.Contains("componentPresence", script);
        Assert.Contains("Project.LongForm.Integrity capability verdict", script);
        Assert.DoesNotContain("semanticProofs", script, StringComparison.Ordinal);
        Assert.DoesNotContain("colorlevels", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'-filter:v','hue", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
    }

    [Gate0RuntimeFact]
    [Trait("Category", "Gate0ExecutableProof")]
    public void SemanticProofEmitsDedicatedInspectionReadinessWithoutCapabilityPromotion()
    {
        var runtimeRoot = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(runtimeRoot));

        var output = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-SemanticProofTest", Guid.NewGuid().ToString("N"));
        try
        {
            var result = RunPowerShell(
                RepositoryPath("eng", "gate0", "Invoke-P2SemanticProof.ps1"),
                "-RuntimeRoot", runtimeRoot!,
                "-OutputDirectory", output);
            Assert.True(result.ExitCode == 0, result.AllOutput);

            using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "semantic-proof-evidence.json")));
            var root = evidence.RootElement;
            Assert.Empty(root.GetProperty("capabilityVerdicts").EnumerateArray());

            var readiness = root.GetProperty("inspectionReadiness");
            Assert.Equal("Media.Inspect.StructureAndTiming", readiness.GetProperty("readinessId").GetString());
            Assert.Equal("passed", readiness.GetProperty("status").GetString());
            Assert.True(readiness.GetProperty("executedInspectionProof").GetBoolean());
            Assert.Equal(["F1", "F7", "F8"], readiness.GetProperty("fixtureIds").EnumerateArray().Select(value => value.GetString()));

            var fixtures = readiness.GetProperty("fixtures");
            Assert.Equal([0, 40, 80], fixtures.GetProperty("F1").GetProperty("videoPresentationTimestamps").EnumerateArray().Select(value => value.GetInt32()));
            Assert.Equal([0, 96], fixtures.GetProperty("F1").GetProperty("audioPresentationTimestamps").EnumerateArray().Select(value => value.GetInt32()));
            Assert.Equal("1/1000", fixtures.GetProperty("F7").GetProperty("containerTimeBase").GetString());
            Assert.Equal([90000, 93600, 100800, 101700, 108000], fixtures.GetProperty("F7").GetProperty("authoredPresentationTimestamps").EnumerateArray().Select(value => value.GetInt32()));
            Assert.Equal([0, 40, 80, 120, 160, 200], fixtures.GetProperty("F8").GetProperty("videoZeroPresentationTimestamps").EnumerateArray().Select(value => value.GetInt32()));
            Assert.Equal([0, 96, 192], fixtures.GetProperty("F8").GetProperty("audioZeroPresentationTimestamps").EnumerateArray().Select(value => value.GetInt32()));

            var f2 = root.GetProperty("fixtureProofs").EnumerateArray().Single(proof => proof.GetProperty("fixtureId").GetString() == "F2");
            var normalization = f2.GetProperty("details").GetProperty("normalization").EnumerateArray().ToArray();
            Assert.Equal(["25", "30000/1001"], normalization.Select(item => item.GetProperty("inputFrameRate").GetString()));
            Assert.All(normalization, item => Assert.Equal("25/1", item.GetProperty("outputFrameRate").GetString()));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void SemanticProofContractDefinesTheApprovedCapabilityBoundary()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "semantic-proof-contract.json")));
        var capabilities = contract.RootElement.GetProperty("capabilities").EnumerateArray().ToArray();
        var ids = capabilities.Select(capability => capability.GetProperty("id").GetString()).ToArray();

        Assert.Equal(15, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(capabilities, capability => Assert.True(capability.GetProperty("required").GetBoolean()));
        Assert.Equal(
            "P2 is third-party LGPLv3-path proof infrastructure because the reviewed build uses --enable-version3. It is not a selected shipping runtime, public-distribution approval, or approval to use every component compiled into the archive.",
            contract.RootElement.GetProperty("runtimeScope").GetString());
        var text = capabilities.Single(capability => capability.GetProperty("id").GetString() == "Text.Render.UnicodeTitlesAndCaptions");
        Assert.Equal("approved-for-executable-proof", text.GetProperty("status").GetString());
        Assert.Equal("eng/gate0/font-proof-artifacts.json", text.GetProperty("approvedFontManifest").GetString());
        Assert.True(text.GetProperty("optionalBlockedColorEmoji").GetBoolean());
        Assert.Equal("ass", text.GetProperty("components").GetProperty("filter").GetString());
        var composite = capabilities.Single(capability => capability.GetProperty("id").GetString() == "Video.Composite.TransformAlphaAndColor");
        Assert.False(composite.TryGetProperty("status", out _));
        Assert.Equal(["F3", "F5"], composite.GetProperty("fixtures").EnumerateArray().Select(fixture => fixture.GetString()));
        Assert.Equal(["crop", "scale", "format", "overlay", "colorlevels", "hue"], composite.GetProperty("components").GetProperty("approvedFilters").EnumerateArray().Select(filter => filter.GetString()));
        Assert.DoesNotContain(
            composite.GetProperty("components").GetProperty("approvedFilters").EnumerateArray().Select(filter => filter.GetString()),
            filter => string.Equals("eq", filter, StringComparison.OrdinalIgnoreCase));
        var playback = capabilities.Single(capability => capability.GetProperty("id").GetString() == "Delivery.Validate.IndependentPlayback");
        Assert.Equal("not-run", playback.GetProperty("status").GetString());
        Assert.Equal("manual-or-separately-automated", playback.GetProperty("execution").GetString());
        Assert.Equal("Project.LongForm.Integrity", capabilities.Single(capability => capability.TryGetProperty("execution", out var execution) && execution.GetString() == "opt-in").GetProperty("id").GetString());
        Assert.Contains("not the final ReelForge default", contract.RootElement.GetProperty("deliveryPolicy").GetString());
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private static (int ExitCode, string AllOutput) RunPowerShell(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput + standardError);
    }
}
