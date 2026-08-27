using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AV5AudioOracleTests
{
    private static readonly string[] ReplacedV3Checks = [
        "qualityThresholds.minimumActiveChannelRmsFullScale",
        "qualityThresholds.minimumActiveReferenceWindowOutputRmsFullScale"
    ];

    [Fact]
    public void V5IsAnExactStressOnlyV3OverlayWithoutMediaExecutionSeams()
    {
        using var v3 = ReadJson("eng", "gate0", "g0.5-lossy-audio-oracle-contract.json");
        using var v4 = ReadJson("eng", "gate0", "g0.5-lossy-audio-oracle-amendment-v4.json");
        using var v5 = ReadJson("eng", "gate0", "g0.5-lossy-audio-oracle-amendment-v5.json");
        var root = v5.RootElement;
        Assert.Equal("Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.20260827", root.GetProperty("amendmentId").GetString());
        Assert.Equal("119A4C179BFA010F3202DBF6AA368E42EDE5FD0FC23EF2781AA9C7F63540CBE4", root.GetProperty("extends").GetProperty("sha256").GetString());
        Assert.Equal(v3.RootElement.GetProperty("contractId").GetString(), root.GetProperty("extends").GetProperty("contractId").GetString());
        var scopedDescriptors = root.GetProperty("scope").GetProperty("referenceDescriptorIds").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Single(scopedDescriptors);
        Assert.Equal("stress-4v8a-30s", scopedDescriptors[0]);
        Assert.False(root.GetProperty("scope").GetProperty("routeOutputsMayBeReadBeforeControlsPass").GetBoolean());
        Assert.False(root.GetProperty("scope").GetProperty("routeReencodeAuthorized").GetBoolean());
        Assert.True(root.GetProperty("scope").GetProperty("otherReferenceDescriptorsRemainExactV3").GetBoolean());
        Assert.True(root.GetProperty("scope").GetProperty("v4RemainsUntouched").GetBoolean());
        var overlay = Assert.Single(root.GetProperty("descriptorOverlays").EnumerateArray());
        Assert.Equal("stress-4v8a-30s", overlay.GetProperty("referenceDescriptorId").GetString());
        Assert.Equal("reference-relative-active-windows-v1", overlay.GetProperty("mode").GetString());
        Assert.Equal(960, overlay.GetProperty("windowSamples").GetInt32());
        Assert.Equal(0.90, overlay.GetProperty("minimumOutputToReferenceRmsRatio").GetDouble());
        Assert.Equal(1.10, overlay.GetProperty("maximumOutputToReferenceRmsRatio").GetDouble());
        Assert.Equal(ReplacedV3Checks, overlay.GetProperty("replacesV3Checks").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("independently authored stress reference PCM", overlay.GetProperty("activeClassification").GetString());
        Assert.Contains("RMS(output window) / RMS(reference window)", overlay.GetProperty("formula").GetString());
        Assert.Contains("no eligible active windows fails", overlay.GetProperty("failClosed").GetString());
        Assert.Equal("21ECAFCD94F71E58AA43955079EF9959C135DB12530D015E8380CFD09B5E9FBC", Hash("eng", "gate0", "g0.5-lossy-audio-oracle-amendment-v4.json"));
    }

    [Fact]
    public void V5ImplementationUsesTheFrozenAudioApiWithoutMediaTooling()
    {
        var module = File.ReadAllText(PathInRepo("eng", "gate0", "G05Stage2AV5AudioOracle.psm1"));
        var runner = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05Stage2AV5AudioOracleControls.ps1"));
        Assert.Contains("Test-G05SmokeAudio", module);
        Assert.Contains("New-G05Stage2AAudioTruth", runner);
        Assert.Contains("stress-right-low-level-960-sample-dropout", runner);
        Assert.Contains("reference-relative-window-rms-ratio", runner);
        Assert.Contains("Exact V3 legacy control changed", runner);
        Assert.Contains("Exact V4 structured control changed", runner);
        Assert.Contains("retainedMp4OrWebmOutputsRead=$false", runner);
        Assert.DoesNotContain("ffmpeg.exe", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffprobe.exe", module, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg.exe", runner, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffprobe.exe", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V5ControlRunnerPreservesAllBoundControlsWithoutMediaToolsWhenTrustedFixturesAreAvailable()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(PathInRepo(), "..", "ReelForge.Gate0Artifacts", "fixtures"));
        if (!File.Exists(Path.Combine(fixtureRoot, "F1", "f1-sync-440hz-880hz-48000-stereo.pcm"))) return;

        var output = Path.Combine(Path.GetTempPath(), "ReelForge-G0-V5-controls-" + Guid.NewGuid().ToString("N"));
        try
        {
            var runner = PathInRepo("eng", "gate0", "Invoke-G05Stage2AV5AudioOracleControls.ps1");
            var v4Report = Path.GetFullPath(Path.Combine(PathInRepo(), "..", "ReelForge.Gate0Artifacts", "g0.5", "v4-structured-audio-controls", "20260827-001", "g0.5-structured-audio-oracle-control-results.json"));
            Assert.True(File.Exists(v4Report), "The trusted V4 control report is required for this local integration test.");
            var result = RunProcess("pwsh", ["-NoProfile", "-File", runner, "-FixtureRoot", fixtureRoot, "-OutputDirectory", output, "-V4AuthoritativeControlReportPath", v4Report]);
            Assert.True(result.ExitCode == 0, result.Output);

            var reportPath = Path.Combine(output, "g0.5-v5-stress-audio-oracle-control-results.json");
            var candidatePath = Path.Combine(output, "g0.5-v5-stress-audio-oracle-freeze-candidate.json");
            using var report = ReadJsonFrom(reportPath);
            Assert.Equal("passed-controls-only-freeze-candidate-pending", report.RootElement.GetProperty("status").GetString());
            Assert.False(report.RootElement.GetProperty("retainedMp4OrWebmOutputsRead").GetBoolean());
            Assert.False(report.RootElement.GetProperty("executionBoundary").GetProperty("ffmpegInvoked").GetBoolean());
            Assert.False(report.RootElement.GetProperty("executionBoundary").GetProperty("ffprobeInvoked").GetBoolean());
            Assert.False(report.RootElement.GetProperty("executionBoundary").GetProperty("mediaProcessesStarted").GetBoolean());
            Assert.Equal(5, report.RootElement.GetProperty("structuredControls").GetArrayLength());
            Assert.All(report.RootElement.GetProperty("structuredControls").EnumerateArray(), control => Assert.Equal(control.GetProperty("expectedPass").GetBoolean(), control.GetProperty("actualPass").GetBoolean()));
            Assert.True(report.RootElement.GetProperty("legacyV3Controls").GetProperty("exactBidirectionalIdSetPreserved").GetBoolean());
            Assert.True(report.RootElement.GetProperty("legacyV3Controls").GetProperty("allFrozenDispositionsAndHashesPreserved").GetBoolean());
            Assert.True(report.RootElement.GetProperty("legacyV4Controls").GetProperty("exactBidirectionalIdSetPreserved").GetBoolean());
            Assert.True(report.RootElement.GetProperty("legacyV4Controls").GetProperty("allFrozenDispositionsAndHashesPreserved").GetBoolean());
            using var candidate = ReadJsonFrom(Path.Combine(output, "g0.5-v5-stress-audio-oracle-freeze-candidate.json"));
            Assert.Equal("controls-passed-freeze-candidate-not-frozen", candidate.RootElement.GetProperty("status").GetString());
            Assert.False(candidate.RootElement.GetProperty("retainedOutputEvaluationAuthorized").GetBoolean());

            var freezePath = Path.Combine(output, "g0.5-v5-stress-audio-oracle-freeze.json");
            var freezer = PathInRepo("eng", "gate0", "New-G05Stage2AV5AudioOracleFreeze.ps1");
            var freezeResult = RunProcess("pwsh", ["-NoProfile", "-File", freezer, "-ControlReportPath", Path.Combine(output, "g0.5-v5-stress-audio-oracle-control-results.json"), "-FreezeCandidatePath", Path.Combine(output, "g0.5-v5-stress-audio-oracle-freeze-candidate.json"), "-V4AuthoritativeControlReportPath", v4Report, "-OutputPath", freezePath]);
            Assert.True(freezeResult.ExitCode == 0, freezeResult.Output);
            using var freeze = ReadJsonFrom(freezePath);
            Assert.Equal("frozen-after-controls-passed-before-retained-output-reevaluation", freeze.RootElement.GetProperty("status").GetString());
            Assert.False(freeze.RootElement.GetProperty("retainedOutputEvaluationAuthorized").GetBoolean());
            AssertPortable(File.ReadAllText(candidatePath));
            AssertPortable(File.ReadAllText(freezePath));

            var authorizationPath = Path.Combine(output, "g0.5-v5-retained-output-reevaluation-authorization.json");
            var authorizer = PathInRepo("eng", "gate0", "New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1");
            var authorizationResult = RunProcess("pwsh", ["-NoProfile", "-File", authorizer, "-FinalFreezePath", freezePath, "-ControlReportPath", reportPath, "-FreezeCandidatePath", candidatePath, "-V4AuthoritativeControlReportPath", v4Report, "-OutputPath", authorizationPath]);
            Assert.True(authorizationResult.ExitCode == 0, authorizationResult.Output);
            var evaluator = PathInRepo("eng", "gate0", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1");
            var noReadOutput = Path.Combine(output, "no-retained-read-result");
            var noReadResult = RunProcess("pwsh", ["-NoProfile", "-File", evaluator, "-FinalFreezePath", freezePath, "-ControlReportPath", reportPath, "-FreezeCandidatePath", candidatePath, "-V4AuthoritativeControlReportPath", v4Report, "-AuthorizationPath", authorizationPath,
                "-StressReferencePcmPath", Path.Combine(output, "missing-truth.pcm"), "-WebmPcmPath", Path.Combine(output, "missing-webm.pcm"), "-WebmOriginalSummaryPath", Path.Combine(output, "missing-webm.json"), "-Mp4PcmPath", Path.Combine(output, "missing-mp4.pcm"), "-Mp4OriginalSummaryPath", Path.Combine(output, "missing-mp4.json"), "-OutputDirectory", noReadOutput]);
            Assert.NotEqual(0, noReadResult.ExitCode);
            Assert.True(noReadResult.Output.Contains("StressReferencePcmPath must be an existing absolute file", StringComparison.Ordinal), noReadResult.Output);
            Assert.DoesNotContain("call depth overflow", noReadResult.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(noReadOutput));

            var baseReport = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
            var baseCandidate = JsonNode.Parse(File.ReadAllText(candidatePath))!.AsObject();

            var badV4Freeze = baseCandidate.DeepClone().AsObject();
            badV4Freeze["frozenInputs"]!.AsArray().Single(item => item!["path"]!.GetValue<string>() == "eng/gate0/g0.5-lossy-audio-oracle-amendment-v4-freeze.json")!["sha256"] = "00";
            AssertFreezeRejected(output, freezer, baseReport.DeepClone().AsObject(), badV4Freeze, "bad-v4-freeze");

            var missingFreezer = baseCandidate.DeepClone().AsObject();
            var frozenInputs = missingFreezer["frozenInputs"]!.AsArray();
            frozenInputs.RemoveAt(frozenInputs.Select((item, index) => (item, index)).Single(pair => pair.item!["path"]!.GetValue<string>() == "eng/gate0/New-G05Stage2AV5AudioOracleFreeze.ps1").index);
            AssertFreezeRejected(output, freezer, baseReport.DeepClone().AsObject(), missingFreezer, "missing-freezer");

            var badReport = baseReport.DeepClone().AsObject();
            badReport["executionBoundary"]!["ffmpegInvoked"] = true;
            var candidateForBadReport = baseCandidate.DeepClone().AsObject();
            AssertFreezeRejected(output, freezer, badReport, candidateForBadReport, "widened-boundary", updateCandidateReportHash: true);

            var badDropoutReport = baseReport.DeepClone().AsObject();
            var dropout = badDropoutReport["structuredControls"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "stress-right-low-level-960-sample-dropout")!.AsObject();
            dropout["actualPass"] = true;
            var candidateForBadDropout = baseCandidate.DeepClone().AsObject();
            AssertFreezeRejected(output, freezer, badDropoutReport, candidateForBadDropout, "bad-dropout-disposition", updateCandidateReportHash: true);

            var badV4ReportIdentity = baseCandidate.DeepClone().AsObject();
            badV4ReportIdentity["authoritativeV4ControlReport"]!["filename"] = "wrong.json";
            AssertFreezeRejected(output, freezer, baseReport.DeepClone().AsObject(), badV4ReportIdentity, "bad-v4-report-identity", v4Report);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    private static JsonDocument ReadJson(params string[] parts) => JsonDocument.Parse(File.ReadAllText(PathInRepo(parts)));
    private static JsonDocument ReadJsonFrom(string path) => JsonDocument.Parse(File.ReadAllText(path));
    private static string Hash(params string[] parts) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PathInRepo(parts))));
    private static string PathInRepo(params string[] parts) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts));

    private static (int ExitCode, string Output) RunProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static void AssertFreezeRejected(string directory, string freezer, JsonObject report, JsonObject candidate, string label, string? v4ReportPath = null, bool updateCandidateReportHash = false)
    {
        var reportPath = Path.Combine(directory, label + "-report.json");
        var candidatePath = Path.Combine(directory, label + "-candidate.json");
        File.WriteAllText(reportPath, report.ToJsonString());
        if (updateCandidateReportHash)
            candidate["controlReport"]!["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath)));
        File.WriteAllText(candidatePath, candidate.ToJsonString());
        v4ReportPath ??= Path.GetFullPath(Path.Combine(PathInRepo(), "..", "ReelForge.Gate0Artifacts", "g0.5", "v4-structured-audio-controls", "20260827-001", "g0.5-structured-audio-oracle-control-results.json"));
        var result = RunProcess("pwsh", ["-NoProfile", "-File", freezer, "-ControlReportPath", reportPath, "-FreezeCandidatePath", candidatePath, "-V4AuthoritativeControlReportPath", v4ReportPath, "-OutputPath", Path.Combine(directory, label + "-freeze.json")]);
        Assert.NotEqual(0, result.ExitCode);
    }

    private static void AssertPortable(string serialized)
    {
        foreach (var forbidden in new[] { "C:\\", "Users\\", "OneDrive", "AppData", "://", "credential", "secret", "accesskey" })
            Assert.DoesNotContain(forbidden, serialized, StringComparison.OrdinalIgnoreCase);
    }
}
