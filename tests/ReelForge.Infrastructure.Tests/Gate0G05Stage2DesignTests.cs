using System.Security.Cryptography;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2DesignTests
{
    [Fact]
    public void LossyAudioProposalLocksThresholdSelectionBeforeRouteEvaluation()
    {
        using var contract = ReadJson("eng", "gate0", "g0.5-lossy-audio-oracle-contract.json");
        var root = contract.RootElement;

        Assert.Equal("Gate0.G05.LossyAudioOracle.V3.Frozen.20260826", root.GetProperty("contractId").GetString());
        Assert.Equal("owner-approved-frozen-controls-passed-retained-route-evaluation-authorized", root.GetProperty("status").GetString());
        Assert.Equal(2, root.GetProperty("appliesTo").GetArrayLength());

        var execution = root.GetProperty("executionBoundary");
        Assert.True(execution.GetProperty("syntheticControlExecutionAuthorized").GetBoolean());
        Assert.True(execution.GetProperty("retainedAacOrOpusEvaluationAuthorized").GetBoolean());
        Assert.False(execution.GetProperty("routeReencodeAuthorized").GetBoolean());
        Assert.False(execution.GetProperty("thresholdSelectionMayReadCodecRouteOutcomes").GetBoolean());

        var timing = root.GetProperty("rawAndNormalizedTiming");
        Assert.Equal("referenceDescriptor.samplesPerChannel", timing.GetProperty("contentNormalizedExpectedSamplesPerChannel").GetString());
        Assert.Equal(0, timing.GetProperty("contentNormalizedMaximumSampleCountDelta").GetInt32());
        Assert.True(timing.GetProperty("timingVerdictIsIndependentFromQuality").GetBoolean());

        var region = root.GetProperty("qualityRegion");
        Assert.Equal("none-zero-lag-only", region.GetProperty("alignmentMode").GetString());
        Assert.False(region.GetProperty("gainFittingPermitted").GetBoolean());
        Assert.False(region.GetProperty("absoluteCorrelationPermitted").GetBoolean());

        var quality = root.GetProperty("qualityThresholds");
        Assert.Equal(0.995, quality.GetProperty("minimumSignedNormalizedCrossCorrelationPerChannel").GetDouble());
        Assert.Equal(0.10, quality.GetProperty("maximumNormalizedRmsErrorPerChannel").GetDouble());
        Assert.Equal(20.0, quality.GetProperty("minimumSnrDbPerChannel").GetDouble());
        Assert.Equal(0.90, quality.GetProperty("minimumOutputToReferenceRmsRatioPerChannel").GetDouble());
        Assert.Equal(1.10, quality.GetProperty("maximumOutputToReferenceRmsRatioPerChannel").GetDouble());
        Assert.Equal(0.90, quality.GetProperty("minimumExpectedToneOutputToReferenceAmplitudeRatio").GetDouble());
        Assert.Equal(1.10, quality.GetProperty("maximumExpectedToneOutputToReferenceAmplitudeRatio").GetDouble());
        Assert.False(quality.TryGetProperty("minimumExpectedToneOutputToReferencePowerRatio", out _));
        Assert.Contains("sqrt(outputTonePower/referenceTonePower)", root.GetProperty("metricDefinitions").GetProperty("expectedToneOutputToReferenceAmplitudeRatio").GetString());

        var descriptors = root.GetProperty("referenceDescriptors").EnumerateArray().ToArray();
        Assert.Equal(5, descriptors.Length);
        Assert.All(descriptors, descriptor =>
        {
            Assert.Matches("^[A-F0-9]{64}$", descriptor.GetProperty("referencePcmSha256").GetString()!);
            Assert.Equal(48_000, descriptor.GetProperty("sampleRate").GetInt32());
            Assert.Equal(2, descriptor.GetProperty("channels").GetInt32());
        });
        foreach (var descriptor in descriptors.Where(item => item.TryGetProperty("trackOnsetWindows", out _)))
        {
            Assert.All(descriptor.GetProperty("trackOnsetWindows").EnumerateArray(), window =>
            {
                Assert.False(string.IsNullOrWhiteSpace(window.GetProperty("newTrackId").GetString()));
                Assert.True(window.GetProperty("endSampleExclusive").GetInt32() > window.GetProperty("startSample").GetInt32());
                Assert.NotEmpty(window.GetProperty("activeTrackIds").EnumerateArray());
                Assert.Equal(2, window.GetProperty("expectedFrequenciesHzByChannel").GetArrayLength());
            });
        }

        var vectors = root.GetProperty("syntheticControlEvidence").GetProperty("vectors").EnumerateArray().ToArray();
        Assert.Equal(12, vectors.Length);
        Assert.Equal(4, vectors.Count(vector => vector.GetProperty("expectedPass").GetBoolean()));
        Assert.Equal(8, vectors.Count(vector => !vector.GetProperty("expectedPass").GetBoolean()));
        var dropout = Assert.Single(vectors, vector => vector.GetProperty("id").GetString() == "midstream-960-sample-dropout");
        Assert.False(dropout.GetProperty("expectedPass").GetBoolean());

        using var summary = ReadJson("eng", "gate0", "g0.5-lossy-audio-control-result-summary.json");
        Assert.Equal("Gate0.G05.LossyAudioOracle.Controls.V2.AmplitudeRatio", summary.RootElement.GetProperty("controlSetId").GetString());
        Assert.False(summary.RootElement.GetProperty("routeOutputsEvaluated").GetBoolean());
        Assert.False(summary.RootElement.GetProperty("routeReencodePerformed").GetBoolean());
        var metricCorrection = summary.RootElement.GetProperty("metricCorrection");
        Assert.Equal("sqrt(outputTonePower/referenceTonePower)", metricCorrection.GetProperty("formula").GetString());
        Assert.Equal(0.90, metricCorrection.GetProperty("minimum").GetDouble());
        Assert.Equal(1.10, metricCorrection.GetProperty("maximum").GetDouble());
        Assert.True(metricCorrection.GetProperty("controlDispositionsPreserved").GetBoolean());
        var summaryVectors = summary.RootElement.GetProperty("accepted").EnumerateArray()
            .Concat(summary.RootElement.GetProperty("rejected").EnumerateArray())
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("sha256").GetString());
        Assert.All(vectors, vector => Assert.Equal(
            vector.GetProperty("sha256").GetString(),
            summaryVectors[vector.GetProperty("id").GetString()!]));
        Assert.Equal(0.0, root.GetProperty("syntheticControlEvidence").GetProperty("measuredSeparation").GetProperty("rejectedDropoutMinimumActiveWindowRmsFullScale").GetDouble());

        var text = root.GetRawText();
        Assert.DoesNotContain("9015", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1859", text, StringComparison.Ordinal);

        using var freeze = ReadJson("eng", "gate0", "g0.5-lossy-audio-oracle-freeze.json");
        Assert.Equal("Gate0.G05.LossyAudioOracle.Freeze.20260826", freeze.RootElement.GetProperty("freezeId").GetString());
        Assert.False(freeze.RootElement.GetProperty("guards").GetProperty("retainedAacOrOpusReadBeforeFreeze").GetBoolean());
        Assert.False(freeze.RootElement.GetProperty("guards").GetProperty("routeReencodePerformed").GetBoolean());
        Assert.Equal(12, freeze.RootElement.GetProperty("controlEvidence").GetProperty("expectedDispositionCount").GetInt32());
        foreach (var record in freeze.RootElement.GetProperty("frozenFiles").EnumerateArray())
        {
            var path = record.GetProperty("path").GetString()!;
            using var stream = File.OpenRead(PathInRepo(path.Split('/')));
            Assert.Equal(record.GetProperty("size").GetInt64(), stream.Length);
            Assert.Equal(record.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(stream)));
        }
    }

    [Fact]
    public void StageTwoProposalDefinesExactBoundariesWorkloadsAndConditionalExecution()
    {
        using var contract = ReadJson("eng", "gate0", "g0.5-stage2-workload-contract.json");
        var root = contract.RootElement;

        Assert.Equal("Gate0.G05.Stage2.Workloads.V1.OwnerApproved.20260826", root.GetProperty("contractId").GetString());
        Assert.Equal("owner-approved-prerequisite-execution-authorized-full-matrix-blocked", root.GetProperty("status").GetString());
        var execution = root.GetProperty("currentExecution");
        Assert.False(execution.GetProperty("preMatrixSmokeAuthorizedNow").GetBoolean());
        Assert.False(execution.GetProperty("full2AAuthorizedNow").GetBoolean());
        Assert.False(execution.GetProperty("applicationHost2BAuthorizedNow").GetBoolean());
        Assert.False(execution.GetProperty("longForm2CAuthorizedNow").GetBoolean());
        Assert.True(execution.GetProperty("markerAtlasGenerationAuthorizedNow").GetBoolean());
        Assert.True(execution.GetProperty("markerSurvivabilityQualificationAuthorizedNow").GetBoolean());
        AssertNoTemplateProperties(root);

        var boundaryIds = root.GetProperty("evidenceBoundaries").EnumerateArray()
            .Select(boundary => boundary.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var profileIds = root.GetProperty("inputProfiles").EnumerateArray()
            .Select(profile => profile.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var artifactIds = root.GetProperty("artifactCatalog").EnumerateArray()
            .Concat(root.GetProperty("proposedDerivedArtifacts").EnumerateArray())
            .Select(artifact => artifact.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        using var audioContract = ReadJson("eng", "gate0", "g0.5-lossy-audio-oracle-contract.json");
        var descriptorIds = audioContract.RootElement.GetProperty("referenceDescriptors").EnumerateArray()
            .Select(descriptor => descriptor.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var routes = root.GetProperty("routes").EnumerateArray().ToDictionary(route => route.GetProperty("id").GetString()!);
        Assert.Equal(["mp4-openh264-aac", "webm-vp9-opus"], routes.Keys);
        Assert.Equal("libopenh264", routes["mp4-openh264-aac"].GetProperty("videoEncoder").GetString());
        Assert.Equal("openh264-constrained-baseline-2m-aaclc-192k", routes["mp4-openh264-aac"].GetProperty("qualityProfileId").GetString());
        Assert.Equal("aac", routes["mp4-openh264-aac"].GetProperty("audioEncoder").GetString());
        Assert.Equal("mp4", routes["mp4-openh264-aac"].GetProperty("muxer").GetString());
        Assert.Equal("libvpx-vp9", routes["webm-vp9-opus"].GetProperty("videoEncoder").GetString());
        Assert.Equal("vp9-crf32-cpu2-opus-128k-cbr", routes["webm-vp9-opus"].GetProperty("qualityProfileId").GetString());
        Assert.Equal("libopus", routes["webm-vp9-opus"].GetProperty("audioEncoder").GetString());
        Assert.Equal("webm", routes["webm-vp9-opus"].GetProperty("muxer").GetString());
        Assert.All(routes.Values, route =>
        {
            Assert.Equal(["[vout]", "[aout]"], route.GetProperty("maps").EnumerateArray().Select(value => value.GetString()));
            Assert.NotEqual("libx264", route.GetProperty("videoEncoder").GetString());
            Assert.NotEmpty(route.GetProperty("videoOptions").EnumerateArray());
            Assert.NotEmpty(route.GetProperty("audioOptions").EnumerateArray());
            Assert.NotEmpty(route.GetProperty("outputDecoders").EnumerateArray());
        });

        var policies = root.GetProperty("threadPolicies").EnumerateArray().ToArray();
        Assert.Equal(["one", "half-logical"], policies.Select(policy => policy.GetProperty("id").GetString()));
        Assert.All(policies, policy =>
        {
            var scopes = policy.GetProperty("controls").EnumerateArray().Select(control => control.GetProperty("scope").GetString()).ToArray();
            Assert.Contains("each input video decoder", scopes);
            Assert.Contains("each input audio decoder", scopes);
            Assert.Contains("ordinary filter pipelines", scopes);
            Assert.Contains("complex filter graph", scopes);
            Assert.Contains("selected output video encoder stream", scopes);
            Assert.Contains("selected output audio encoder stream", scopes);
            Assert.Contains("not a process-wide CPU cap", policy.GetProperty("claim").GetString());
        });

        AssertArtifactCatalogMatchesRetentionManifest(root.GetProperty("artifactCatalog"));
        var marker = Assert.Single(root.GetProperty("proposedDerivedArtifacts").EnumerateArray());
        Assert.Equal("g05-long-frame-index-atlas", marker.GetProperty("id").GetString());
        Assert.Equal(4_590_016, marker.GetProperty("size").GetInt64());
        Assert.Equal("BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97", marker.GetProperty("sha256").GetString());
        Assert.All(root.GetProperty("inputProfiles").EnumerateArray(), profile =>
            Assert.All(profile.GetProperty("artifacts").EnumerateArray(), artifact => Assert.Contains(artifact.GetString()!, artifactIds)));

        var workloads = root.GetProperty("workloads").EnumerateArray().ToDictionary(workload => workload.GetProperty("id").GetString()!);
        Assert.Equal(4, workloads.Count);
        Assert.Equal(1, workloads["baseline-1v1a"].GetProperty("videoLayers").GetArrayLength());
        Assert.Equal(1, workloads["baseline-1v1a"].GetProperty("audioTracks").GetArrayLength());
        Assert.Equal(2, workloads["typical-2v4a"].GetProperty("videoLayers").GetArrayLength());
        Assert.Equal(4, workloads["typical-2v4a"].GetProperty("audioTracks").GetArrayLength());
        Assert.Equal(4, workloads["stress-4v8a"].GetProperty("videoLayers").GetArrayLength());
        Assert.Equal(8, workloads["stress-4v8a"].GetProperty("audioTracks").GetArrayLength());
        Assert.Equal(3_600, workloads["long-form-adapter-1v1a-60m"].GetProperty("durationSeconds").GetInt32());
        Assert.Equal("p2-windows-wpf-measurement-adapter", workloads["long-form-adapter-1v1a-60m"].GetProperty("evidenceBoundary").GetString());
        Assert.Contains("unique 17-bit", workloads["long-form-adapter-1v1a-60m"].GetProperty("markerTruth").GetString());

        foreach (var workload in workloads.Values)
        {
            Assert.Contains(workload.GetProperty("evidenceBoundary").GetString()!, boundaryIds);
            Assert.False(string.IsNullOrWhiteSpace(workload.GetProperty("videoFilterGraph").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(workload.GetProperty("audioFilterGraph").GetString()));
            Assert.Contains(workload.GetProperty("audioReferenceDescriptor").GetString()!, descriptorIds);
            Assert.All(workload.GetProperty("inputs").EnumerateArray(), input =>
                Assert.Contains(input.GetProperty("profile").GetString()!, profileIds));
        }

        foreach (var workload in workloads.Values.Where(item => item.GetProperty("durationSeconds").GetInt32() == 30))
        {
            var variants = workload.GetProperty("resolutionVariants").EnumerateArray().ToDictionary(variant => variant.GetProperty("id").GetString()!);
            Assert.Equal(1_280, variants["720p"].GetProperty("width").GetInt32());
            Assert.Equal(720, variants["720p"].GetProperty("height").GetInt32());
            Assert.Equal(1_920, variants["1080p"].GetProperty("width").GetInt32());
            Assert.Equal(1_080, variants["1080p"].GetProperty("height").GetInt32());
            Assert.Contains("{variant.width}:{variant.height}", workload.GetProperty("videoLayers")[0].GetProperty("scale").GetString());
        }

        var matrix = root.GetProperty("matrix");
        Assert.Equal(3, matrix.GetProperty("preMatrixSmoke").GetProperty("maximumCandidates").GetInt32());
        Assert.True(matrix.GetProperty("preMatrixSmoke").GetProperty("failFastPerRoute").GetBoolean());
        Assert.Equal(72, matrix.GetProperty("full2A").GetProperty("webmAttempts").GetInt32());
        Assert.Equal(36, matrix.GetProperty("full2A").GetProperty("conditionalMp4Attempts").GetInt32());
        Assert.Equal(805_306_368, matrix.GetProperty("full2A").GetProperty("retentionCeilingBytes").GetInt64());
        Assert.Equal(["proof-adapter-cold", "proof-adapter-warm"], matrix.GetProperty("applicationHost2B").GetProperty("cacheStates").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("p2-windows-wpf-measurement-adapter", matrix.GetProperty("applicationHost2B").GetProperty("evidenceBoundaryOverride").GetString());
        Assert.Equal(6, matrix.GetProperty("applicationHost2B").GetProperty("scenarioSequencePerRouteConcurrencyCell").GetArrayLength());
        Assert.Contains("lowest median wall time", matrix.GetProperty("applicationHost2B").GetProperty("threadPolicySelection").GetString());
        Assert.False(matrix.GetProperty("applicationHost2B").GetProperty("concurrencyFourAuthorized").GetBoolean());
        Assert.False(matrix.GetProperty("longForm2C").GetProperty("duration120MinutesAuthorized").GetBoolean());

        var markerQualification = root.GetProperty("markerQualification");
        Assert.Equal(750, markerQualification.GetProperty("expectedFrameCount").GetInt32());
        Assert.Equal(2, markerQualification.GetProperty("requiredRouteQualityProfiles").GetArrayLength());
        Assert.Equal([0, 749, 1], new[]
        {
            markerQualification.GetProperty("exercisedMarkerIds").GetProperty("first").GetInt32(),
            markerQualification.GetProperty("exercisedMarkerIds").GetProperty("lastInclusive").GetInt32(),
            markerQualification.GetProperty("exercisedMarkerIds").GetProperty("step").GetInt32(),
        });
        var bitOracle = markerQualification.GetProperty("bitOracle");
        Assert.Equal(17, bitOracle.GetProperty("cells").GetInt32());
        Assert.Equal([64, 192], bitOracle.GetProperty("inclusiveAmbiguousRange").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Contains(markerQualification.GetProperty("acceptance").EnumerateArray(),
            value => value.GetString()!.Contains("zero duplicate", StringComparison.Ordinal));
        Assert.Equal(workloads["long-form-adapter-1v1a-60m"].GetProperty("videoFilterGraph").GetString(),
            markerQualification.GetProperty("videoFilterGraph").GetString());
        Assert.Equal(workloads["long-form-adapter-1v1a-60m"].GetProperty("audioFilterGraph").GetString(),
            markerQualification.GetProperty("audioFilterGraph").GetString());
        Assert.Equal(1_440_000, markerQualification.GetProperty("decodedAudio").GetProperty("expectedPresentationSamplesPerChannel").GetInt32());
        Assert.Equal(1_024, markerQualification.GetProperty("decodedAudio").GetProperty("maximumRawDecoderTailSamples").GetInt32());
        Assert.Contains("no signal-derived", markerQualification.GetProperty("decodedAudio").GetProperty("normalization").GetString());
        var markerProfiles = markerQualification.GetProperty("requiredRouteQualityProfiles").EnumerateArray().ToArray();
        Assert.Equal("Constrained Baseline", markerProfiles[0].GetProperty("observedDescriptor").GetProperty("videoProfile").GetString());
        Assert.Equal("Profile 0", markerProfiles[1].GetProperty("observedDescriptor").GetProperty("videoProfile").GetString());

        var adapter = root.GetProperty("applicationHostBoundary");
        Assert.Equal("p2-windows-wpf-measurement-adapter", adapter.GetProperty("id").GetString());
        Assert.Empty(adapter.GetProperty("productAssemblyReferences").EnumerateArray());
        Assert.Contains("no PATH discovery", adapter.GetProperty("runtime").GetString());
        Assert.Equal("System.Windows.Threading.DispatcherPriority.Normal", adapter.GetProperty("dispatcherProbe").GetProperty("priority").GetString());
        Assert.Equal(1_800, adapter.GetProperty("dispatcherProbe").GetProperty("minimumCompletedScenarioSamples").GetInt32());
        Assert.Contains("960x540", adapter.GetProperty("preview").GetProperty("cold").GetString());
        Assert.Contains("start no media child", adapter.GetProperty("preview").GetProperty("warm").GetString());
        Assert.Contains("may overlap", adapter.GetProperty("preview").GetProperty("overlap").GetString());
        Assert.Equal(750, adapter.GetProperty("cancellation").GetProperty("gracePeriodMilliseconds").GetInt32());
        Assert.Contains("q followed by newline", adapter.GetProperty("cancellation").GetProperty("gracefulRequestWritten").GetString());
        Assert.Contains("next dispatcher turn", adapter.GetProperty("cancellation").GetProperty("uiAcknowledged").GetString());
        Assert.Contains("Kill(entireProcessTree:true)", adapter.GetProperty("cancellation").GetProperty("forcedFallback").GetString());
        Assert.Equal("empty staging root created for the scenario; only contract-hashed retained source closure may be mounted read-only outside it", adapter.GetProperty("cache").GetProperty("proof-adapter-cold").GetProperty("precondition").GetString());
        Assert.Contains("libx264", adapter.GetProperty("prohibited").EnumerateArray().Select(value => value.GetString()));
        Assert.False(root.GetProperty("ci").GetProperty("hostedCiMayAcquireP2OrExecuteMedia").GetBoolean());
    }

    [Fact]
    public void PreparationScriptsRemainDeterministicAndMediaEngineFree()
    {
        var controls = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G05LossyAudioOracleControls.ps1"));
        var truth = File.ReadAllText(PathInRepo("eng", "gate0", "Generate-G05Stage2AudioTruth.ps1"));

        Assert.Contains("xorshift32", controls);
        Assert.Contains("MidpointRounding.ToEven", controls);
        Assert.Contains("Synthetic control verdict mismatch", controls);
        Assert.Contains("MidpointRounding.ToEven", truth);
        Assert.Contains("Fixture source hash mismatch", truth);
        Assert.Contains("Get-WorkloadTracks", truth);
        Assert.Contains("ContractPath", truth);
        var marker = File.ReadAllText(PathInRepo("eng", "gate0", "Generate-G05Stage2MarkerAtlas.ps1"));
        Assert.Contains("MarkerCount = 90000", marker);
        Assert.Contains("BitsPerMarker = 17", marker);
        Assert.DoesNotContain("ffmpeg", controls, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", truth, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", marker, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreparationSummaryPinsEveryProposedContractAndScript()
    {
        using var summary = ReadJson("eng", "gate0", "g0.5-stage2-preparation-result-summary.json");
        var root = summary.RootElement;
        Assert.Equal("audio-routes-admitted-marker-and-adapter-prerequisites-pending", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("syntheticAudioControls").GetProperty("routeOutputsEvaluated").GetBoolean());
        Assert.False(root.GetProperty("syntheticAudioControls").GetProperty("routeReencodePerformed").GetBoolean());
        Assert.True(root.GetProperty("syntheticAudioControls").GetProperty("frozen").GetBoolean());

        foreach (var record in root.GetProperty("contracts").EnumerateArray().Concat(root.GetProperty("preparationScripts").EnumerateArray()))
        {
            var path = record.GetProperty("path").GetString()!;
            var expected = record.GetProperty("sha256").GetString();
            using var stream = File.OpenRead(PathInRepo(path.Split('/')));
            Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(stream)));
        }
    }

    private static void AssertArtifactCatalogMatchesRetentionManifest(JsonElement catalog)
    {
        using var manifest = ReadJson("eng", "gate0", "artifact-retention-manifest.json");
        var retained = manifest.RootElement.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("files").EnumerateArray())
            .GroupBy(file => file.GetProperty("filename").GetString()!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var artifact in catalog.EnumerateArray())
        {
            var path = artifact.GetProperty("path").GetString()!;
            Assert.False(Path.IsPathRooted(path));
            Assert.True(retained.TryGetValue(path, out var retainedArtifact), $"Artifact is not retained: {path}");
            Assert.Equal(artifact.GetProperty("size").GetInt64(), retainedArtifact.GetProperty("size").GetInt64());
            Assert.Equal(artifact.GetProperty("sha256").GetString(), retainedArtifact.GetProperty("sha256").GetString());
        }
    }

    private static void AssertNoTemplateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.DoesNotContain("template", property.Name, StringComparison.OrdinalIgnoreCase);
                AssertNoTemplateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) AssertNoTemplateProperties(item);
        }
    }

    private static JsonDocument ReadJson(params string[] parts) =>
        JsonDocument.Parse(File.ReadAllText(PathInRepo(parts)));

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
