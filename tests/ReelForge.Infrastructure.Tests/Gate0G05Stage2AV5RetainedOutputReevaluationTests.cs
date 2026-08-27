using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AV5RetainedOutputReevaluationTests
{
    [Fact]
    public void ReassessmentSurfaceBindsOnlyTheApprovedFrozenV5AndRetainedInputHashes()
    {
        var evaluator = File.ReadAllText(InRepo("eng", "gate0", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1"));
        var authorizer = File.ReadAllText(InRepo("eng", "gate0", "New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1"));
        var controls = File.ReadAllText(InRepo("eng", "gate0", "Invoke-G05Stage2AV5AudioOracleControls.ps1"));
        var freezer = File.ReadAllText(InRepo("eng", "gate0", "New-G05Stage2AV5AudioOracleFreeze.ps1"));

        foreach (var hash in new[]
        {
            "299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5",
            "B59110445A1A45F31E5DDAF117184F4F40F9AD67D036DE697BD98CECE512D7B6",
            "1CF498BE47FFA394B9A5F6B0BFB2A4A9DEAE615A03F8F999B76B9375CFB96E9A",
            "C1177ED32A9E17CB118FFBAE16504A1BFCD08815041B6A18E0C59D9E7E6D36B9",
            "4E4BACBC4BA0DB258215F93D41F411DE872DD69691B60949C5088824572DED97"
        })
        {
            Assert.Contains(hash, evaluator);
            Assert.Contains(hash, authorizer);
        }

        Assert.Contains("Test-G05Stage2AV5StressAudio", evaluator);
        Assert.Contains("stop-before-media-v5-route-failure", evaluator);
        Assert.Contains("originalV3RecordsModified=$false", evaluator);
        Assert.Contains("Assert-PortableJsonValue", evaluator);
        Assert.Contains("$Value -is [ValueType]", evaluator);
        Assert.Contains("ConvertFrom-Json -Depth 64 -DateKind String", evaluator);
        Assert.Contains("ConvertFrom-Json -Depth 128 -DateKind String", evaluator);
        Assert.Contains("Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1", authorizer);
        Assert.Contains("Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1", controls);
        Assert.Contains("Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1", freezer);
        Assert.Contains("New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1", controls);
        Assert.Contains("New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1", freezer);
    }

    [Fact]
    public void ReassessmentRejectsRelativeAndTamperedInputsBeforeAnyRetainedRouteCanBeRead()
    {
        var script = InRepo("eng", "gate0", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1");
        var temporary = Path.Combine(Path.GetTempPath(), "ReelForge-G0-V5-reevaluation-negative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var result = Run("pwsh", ["-NoProfile", "-File", script,
                "-FinalFreezePath", "relative-freeze.json",
                "-ControlReportPath", "relative-control.json",
                "-FreezeCandidatePath", "relative-candidate.json",
                "-V4AuthoritativeControlReportPath", "relative-v4.json",
                "-AuthorizationPath", "relative-authorization.json",
                "-StressReferencePcmPath", "relative-truth.pcm",
                "-WebmPcmPath", "relative-webm.pcm",
                "-WebmOriginalSummaryPath", "relative-webm.json",
                "-Mp4PcmPath", "relative-mp4.pcm",
                "-Mp4OriginalSummaryPath", "relative-mp4.json",
                "-OutputDirectory", Path.Combine(temporary, "result")]);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("FinalFreezePath must be an existing absolute file.", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(temporary, "result")));
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }

    [Fact]
    public void ReassessmentRejectsAHashTamperedSyntheticInputWithNoOutput()
    {
        var script = InRepo("eng", "gate0", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1");
        var temporary = Path.Combine(Path.GetTempPath(), "ReelForge-G0-V5-reevaluation-tamper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var freezePath = Path.Combine(temporary, "freeze.json");
            File.WriteAllText(freezePath, "{\"status\":\"frozen-after-controls-passed-before-retained-output-reevaluation\",\"retainedOutputEvaluationAuthorized\":false,\"routeReencodeAuthorized\":false}");
            var evaluatorHash = HashFile(script);
            var moduleHash = HashFile(InRepo("eng", "gate0", "G05Stage2AV5AudioOracle.psm1"));
            var amendmentHash = HashFile(InRepo("eng", "gate0", "g0.5-lossy-audio-oracle-amendment-v5.json"));
            var authorizationPath = Path.Combine(temporary, "authorization.json");
            var authorization = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["authorizationId"] = "Gate0.G05.Stage2A.V5RetainedOutputReevaluation.20260827",
                ["status"] = "owner-approved-after-final-v5-freeze",
                ["createdUtc"] = "2026-08-27T00:00:00Z",
                ["finalFreeze"] = new JsonObject { ["filename"] = "freeze.json", ["sha256"] = HashFile(freezePath), ["freezeId"] = "Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.Frozen.20260827" },
                ["evaluator"] = new JsonObject { ["path"] = "eng/gate0/Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1", ["sha256"] = evaluatorHash },
                ["v5"] = new JsonObject { ["amendmentPath"] = "eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json", ["amendmentSha256"] = amendmentHash, ["modulePath"] = "eng/gate0/G05Stage2AV5AudioOracle.psm1", ["moduleSha256"] = moduleHash, ["referenceDescriptorId"] = "stress-4v8a-30s" },
                ["inputs"] = new JsonObject { ["stressReferencePcmSha256"] = "299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5", ["webm"] = new JsonObject { ["routeId"] = "webm-vp9-opus", ["pcmSha256"] = "B59110445A1A45F31E5DDAF117184F4F40F9AD67D036DE697BD98CECE512D7B6", ["originalV3SummarySha256"] = "1CF498BE47FFA394B9A5F6B0BFB2A4A9DEAE615A03F8F999B76B9375CFB96E9A" }, ["mp4"] = new JsonObject { ["routeId"] = "mp4-h264-aac", ["pcmSha256"] = "C1177ED32A9E17CB118FFBAE16504A1BFCD08815041B6A18E0C59D9E7E6D36B9", ["originalV3SummarySha256"] = "4E4BACBC4BA0DB258215F93D41F411DE872DD69691B60949C5088824572DED97" } },
                ["executionBoundary"] = new JsonObject { ["reencodeAuthorized"] = false, ["ffmpegAuthorized"] = false, ["ffprobeAuthorized"] = false, ["mediaProcessAuthorized"] = false, ["retainedPcmReadAuthorized"] = true }
            };
            File.WriteAllText(authorizationPath, authorization.ToJsonString());
            var fakeFiles = new[] { "truth.pcm", "webm.pcm", "webm.json", "mp4.pcm", "mp4.json" };
            foreach (var file in fakeFiles) File.WriteAllText(Path.Combine(temporary, file), "synthetic test sentinel only");

            var output = Path.Combine(temporary, "result");
            var result = Run("pwsh", ["-NoProfile", "-File", script,
                "-FinalFreezePath", freezePath, "-AuthorizationPath", authorizationPath,
                "-ControlReportPath", freezePath, "-FreezeCandidatePath", freezePath, "-V4AuthoritativeControlReportPath", freezePath,
                "-StressReferencePcmPath", Path.Combine(temporary, "truth.pcm"),
                "-WebmPcmPath", Path.Combine(temporary, "webm.pcm"), "-WebmOriginalSummaryPath", Path.Combine(temporary, "webm.json"),
                "-Mp4PcmPath", Path.Combine(temporary, "mp4.pcm"), "-Mp4OriginalSummaryPath", Path.Combine(temporary, "mp4.json"),
                "-OutputDirectory", output]);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Final freeze schema is not closed", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }

    [Fact]
    public void ReassessmentAuthorizationMustBePortableAndClosed()
    {
        var authorizer = File.ReadAllText(InRepo("eng", "gate0", "New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1"));
        var evaluator = File.ReadAllText(InRepo("eng", "gate0", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1"));
        Assert.Contains("schema is not closed", evaluator);
        Assert.Contains("Non-portable or sensitive value", evaluator);
        Assert.Contains("reencodeAuthorized = $false", authorizer);
        Assert.Contains("ffmpegAuthorized = $false", authorizer);
        Assert.Contains("ffprobeAuthorized = $false", authorizer);
        Assert.Contains("mediaProcessAuthorized = $false", authorizer);
        Assert.Contains("[DateTime]::UtcNow.ToString('O'", authorizer);
        Assert.DoesNotContain("Start-Process", evaluator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("& pwsh", evaluator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReassessmentRejectsSchemaCompleteForgedControlClosureBeforeRetainedPathResolution()
    {
        var script = InRepo("eng", "gate0", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1");
        var temporary = Path.Combine(Path.GetTempPath(), "ReelForge-G0-V5-forged-closure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var reportPath = Path.Combine(temporary, "report.json");
            var report = new JsonObject
            {
                ["schemaVersion"] = 1, ["controlSetId"] = "Gate0.G05.LossyAudioOracle.Controls.V5.ReferenceRelativeStress", ["status"] = "passed-controls-only-freeze-candidate-pending", ["amendmentId"] = "Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.20260827",
                ["v3Contract"] = new JsonObject { ["path"] = "eng/gate0/g0.5-lossy-audio-oracle-contract.json", ["sha256"] = new string('A', 64), ["contractId"] = "Gate0.G05.LossyAudioOracle.V3.Frozen.20260826" },
                ["routeOutputsEvaluated"] = false, ["routeReencodePerformed"] = false, ["retainedMp4OrWebmOutputsRead"] = false,
                ["stressTruth"] = new JsonObject { ["sha256"] = "299846E21A0AF6F1416CCA7BF1BF8ACAC4A5EDDA78EFF9BEB392CC7B992B8CF5", ["size"] = 5760000 },
                ["structuredControls"] = new JsonArray(
                    Control("stress-identity", true), Control("stress-gain-95-percent", true), Control("stress-right-low-level-960-sample-dropout", false, "reference-relative-window-rms-ratio"), Control("stress-gain-75-percent", false), Control("stress-gain-125-percent", false)),
                ["legacyV3Controls"] = new JsonObject { ["count"] = 12, ["exactBidirectionalIdSetPreserved"] = true, ["allFrozenDispositionsAndHashesPreserved"] = true },
                ["legacyV4Controls"] = new JsonObject { ["authoritativeRetentionGroupId"] = "G05-V4-Structured-Audio-Controls-20260827-001", ["retentionManifestSha256"] = HashFile(InRepo("eng", "gate0", "artifact-retention-manifest.json")), ["count"] = 5, ["exactBidirectionalIdSetPreserved"] = true, ["allFrozenDispositionsAndHashesPreserved"] = true },
                ["executionBoundary"] = new JsonObject { ["ffmpegInvoked"] = false, ["ffprobeInvoked"] = false, ["mediaProcessesStarted"] = false, ["retainedCodecOutputsRead"] = false }
            };
            File.WriteAllText(reportPath, report.ToJsonString());
            var inputs = FrozenInputs();
            var candidatePath = Path.Combine(temporary, "candidate.json");
            var candidate = new JsonObject
            {
                ["schemaVersion"] = 1, ["candidateId"] = "Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.FreezeCandidate.20260827", ["status"] = "controls-passed-freeze-candidate-not-frozen",
                ["amendment"] = new JsonObject { ["amendmentId"] = "Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.20260827", ["status"] = "owner-approved-controls-required-before-retained-output-reevaluation", ["referenceDescriptorIds"] = new JsonArray("stress-4v8a-30s"), ["overlay"] = new JsonObject() },
                ["authoritativeV4ControlReport"] = new JsonObject { ["groupId"] = "G05-V4-Structured-Audio-Controls-20260827-001", ["artifactId"] = "G05-V4-Structured-Audio-Controls-20260827-001/g0.5-structured-audio-oracle-control-results.json", ["filename"] = "v4.json", ["sha256"] = "2CAEE1C652F292BBF7E9DB6E1DAA0DD7C5E68788C3E5D74F63997DC3775F2AF6", ["size"] = 1 },
                ["controlReport"] = new JsonObject { ["path"] = "report.json", ["sha256"] = HashFile(reportPath), ["size"] = new FileInfo(reportPath).Length }, ["frozenInputs"] = inputs.DeepClone(),
                ["requiredControlVerdicts"] = new JsonObject { ["v5"] = 5, ["v3"] = 12, ["v4"] = 5, ["allPassed"] = true }, ["retainedOutputEvaluationAuthorized"] = false
            };
            File.WriteAllText(candidatePath, candidate.ToJsonString());
            var freezePath = Path.Combine(temporary, "freeze.json");
            var freeze = new JsonObject
            {
                ["schemaVersion"] = 1, ["freezeId"] = "Gate0.G05.LossyAudioOracle.V5.ReferenceRelativeStress.Frozen.20260827", ["status"] = "frozen-after-controls-passed-before-retained-output-reevaluation", ["frozenUtc"] = "2026-08-27T00:00:00Z",
                ["controlReport"] = new JsonObject { ["path"] = "report.json", ["sha256"] = HashFile(reportPath), ["size"] = new FileInfo(reportPath).Length }, ["freezeCandidate"] = new JsonObject { ["path"] = "candidate.json", ["sha256"] = HashFile(candidatePath), ["size"] = new FileInfo(candidatePath).Length },
                ["frozenInputs"] = inputs, ["authoritativeV4ControlReport"] = candidate["authoritativeV4ControlReport"]!.DeepClone(), ["retainedOutputEvaluationAuthorized"] = false, ["routeReencodeAuthorized"] = false
            };
            File.WriteAllText(freezePath, freeze.ToJsonString());
            var v4 = Path.Combine(temporary, "v4.json"); File.WriteAllText(v4, "x");
            var authorization = Path.Combine(temporary, "authorization.json"); File.WriteAllText(authorization, "{}");
            var output = Path.Combine(temporary, "result");
            var result = Run("pwsh", ["-NoProfile", "-File", script, "-FinalFreezePath", freezePath, "-ControlReportPath", reportPath, "-FreezeCandidatePath", candidatePath, "-V4AuthoritativeControlReportPath", v4, "-AuthorizationPath", authorization,
                "-StressReferencePcmPath", Path.Combine(temporary, "does-not-exist-truth.pcm"), "-WebmPcmPath", Path.Combine(temporary, "does-not-exist-webm.pcm"), "-WebmOriginalSummaryPath", Path.Combine(temporary, "does-not-exist-webm.json"), "-Mp4PcmPath", Path.Combine(temporary, "does-not-exist-mp4.pcm"), "-Mp4OriginalSummaryPath", Path.Combine(temporary, "does-not-exist-mp4.json"), "-OutputDirectory", output]);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("V5 control report V3 identity changed", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("StressReferencePcmPath", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static JsonObject Control(string id, bool passed, string? requiredFailure = null)
    {
        var result = new JsonObject { ["failures"] = requiredFailure is null ? new JsonArray() : new JsonArray(requiredFailure) };
        return new JsonObject { ["id"] = id, ["expectedPass"] = passed, ["actualPass"] = passed, ["result"] = result };
    }

    private static JsonArray FrozenInputs()
    {
        var paths = new[] { "g0.5-lossy-audio-oracle-contract.json", "g0.5-lossy-audio-oracle-amendment-v4.json", "g0.5-lossy-audio-oracle-amendment-v4-freeze.json", "g0.5-lossy-audio-oracle-amendment-v5.json", "g0.5-stage2-workload-contract.json", "G05Stage2SmokeHelpers.psm1", "G05Stage2ASemanticHelpers.psm1", "G05Stage2AV5AudioOracle.psm1", "Invoke-G05LossyAudioOracleControls.ps1", "Invoke-G05StructuredAudioOracleControls.ps1", "g0.5-structured-audio-control-result-summary.json", "Invoke-G05Stage2AV5AudioOracleControls.ps1", "New-G05Stage2AV5AudioOracleFreeze.ps1", "Invoke-G05Stage2AV5RetainedOutputReevaluation.ps1", "New-G05Stage2AV5RetainedOutputReevaluationAuthorization.ps1", "G05Stage2AV5FreezeValidation.psm1", "artifact-retention-manifest.json" };
        var result = new JsonArray();
        foreach (var path in paths) result.Add(new JsonObject { ["path"] = "eng/gate0/" + path, ["sha256"] = HashFile(InRepo("eng", "gate0", path)) });
        return result;
    }

    private static string InRepo(params string[] parts) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", Path.Combine(parts));
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static (int ExitCode, string Output) Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        if (!process.WaitForExit(20_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The negative PowerShell validation did not complete within 20 seconds.");
        }
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        return (process.ExitCode, output);
    }
}
