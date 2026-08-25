using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04InputProofPolicyTests
{
    private static readonly string[] ExpectedSelectionCaseIds = ["S1-OneUsable", "S2-OneDefault", "S3-NoDefault", "S4-MultipleDefaults", "S5-AttachedPicture", "S6-UndecodableDefault", "S7-Descriptors"];
    private static readonly string[] ExpectedClassificationCaseIds = ["N1-MisleadingExtension", "N2-CorruptOrTruncated", "N3-NoUsableRequestedMedia", "N4-DecoderMissing", "N5-MultipleStreams", "N6-OutsideEnvelopeCapabilityQualified", "N6-ProtectedRejected", "N7-InvalidRuntimePair"];
    private static readonly string[] RequiredDescriptorFields = ["mediaType", "streamIndex", "codecIdentity", "defaultDisposition", "language", "title", "timing", "observedDescriptor"];

    [Fact]
    public void SyntheticPolicyProofCoversEveryApprovedSelectionAndClassificationBranchWithoutMediaInvocation()
    {
        var output = RunPolicy();
        using var result = JsonDocument.Parse(output);
        var selection = result.RootElement.GetProperty("selection").EnumerateArray().ToArray();
        var classification = result.RootElement.GetProperty("classification").EnumerateArray().ToArray();

        Assert.Equal(ExpectedSelectionCaseIds, selection.Select(item => item.GetProperty("caseId").GetString()));
        Assert.Equal(ExpectedClassificationCaseIds, classification.Select(item => item.GetProperty("caseId").GetString()));
        Assert.All(selection.Concat(classification), item =>
        {
            Assert.Equal(0, item.GetProperty("invocation").GetProperty("ffmpegInvocations").GetInt32());
            Assert.Equal(0, item.GetProperty("invocation").GetProperty("ffprobeInvocations").GetInt32());
            Assert.True(item.GetProperty("invocation").GetProperty("preflightOnly").GetBoolean());
            Assert.True(item.TryGetProperty("syntheticSnapshot", out _));
        });
        Assert.All(selection, item => Assert.Contains("synthetic", item.GetProperty("executionClaim").GetString()!, StringComparison.OrdinalIgnoreCase));
        Assert.All(selection, item => Assert.Equal("F8 explicit multi-stream semantic proof", item.GetProperty("baseExecutableEvidence").GetString()));
    }

    [Fact]
    public void SelectionPolicyExcludesAttachedPicturesAndNeverSilentlyFallsBackOrReselects()
    {
        using var result = JsonDocument.Parse(RunPolicy());
        var rows = result.RootElement.GetProperty("selection").EnumerateArray().ToDictionary(row => row.GetProperty("caseId").GetString()!);
        Assert.Equal("0:v:1", rows["S5-AttachedPicture"].GetProperty("selectedMap").GetString());
        Assert.Contains(rows["S5-AttachedPicture"].GetProperty("resolutions").EnumerateArray().Single(resolution => resolution.GetProperty("mediaType").GetString() == "video").GetProperty("ignoredAlternatives").EnumerateArray(), ignored => ignored.GetProperty("reason").GetString() == "attached-picture-excluded-from-timeline-video");
        var s6 = rows["S6-UndecodableDefault"];
        Assert.Equal("blocked", s6.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, s6.GetProperty("selectedMap").ValueKind);
        Assert.Contains(s6.GetProperty("resolutions").EnumerateArray().Single(resolution => resolution.GetProperty("mediaType").GetString() == "video").GetProperty("ignoredAlternatives").EnumerateArray(), ignored => ignored.GetProperty("reason").GetString() == "usable-alternate-not-selected-because-default-is-unusable");
        Assert.Equal("0:v:1", rows["S2-OneDefault"].GetProperty("selectedMap").GetString());
        Assert.Equal("0:v:0", rows["S3-NoDefault"].GetProperty("selectedMap").GetString());
        Assert.Equal("selected-lowest-index-default-ambiguity-reported", rows["S4-MultipleDefaults"].GetProperty("resolutions").EnumerateArray().Single(resolution => resolution.GetProperty("mediaType").GetString() == "video").GetProperty("disposition").GetString());
        var descriptor = rows["S7-Descriptors"].GetProperty("resolutions").EnumerateArray().Single(resolution => resolution.GetProperty("mediaType").GetString() == "audio").GetProperty("selected");
        foreach (var field in RequiredDescriptorFields) Assert.True(descriptor.TryGetProperty(field, out _));
    }

    [Fact]
    public void ClassificationDistinguishesRejectionBlockingAndRuntimeUnavailability()
    {
        using var result = JsonDocument.Parse(RunPolicy());
        var rows = result.RootElement.GetProperty("classification").EnumerateArray().ToDictionary(row => row.GetProperty("caseId").GetString()!);
        Assert.Equal("passed", rows["N1-MisleadingExtension"].GetProperty("status").GetString());
        Assert.Equal("rejected", rows["N2-CorruptOrTruncated"].GetProperty("status").GetString());
        Assert.Equal("retain the first 128 bytes only", rows["N2-CorruptOrTruncated"].GetProperty("syntheticSnapshot").GetProperty("truncationRule").GetString());
        Assert.Equal("rejected", rows["N3-NoUsableRequestedMedia"].GetProperty("status").GetString());
        Assert.Equal("blocked", rows["N4-DecoderMissing"].GetProperty("status").GetString());
        Assert.Equal("runtime-unavailable", rows["N7-InvalidRuntimePair"].GetProperty("status").GetString());
        Assert.Equal("rejected-before-processing-protected-or-encrypted", rows["N6-ProtectedRejected"].GetProperty("classification").GetString());
    }

    [Fact]
    public void ChangedContentBindingRequiresRevalidationBeforeASelectionCanBeReused()
    {
        using var result = JsonDocument.Parse(RunPolicy(revalidate: true));
        var s2 = result.RootElement.GetProperty("selection").EnumerateArray().Single(row => row.GetProperty("caseId").GetString() == "S2-OneDefault");
        Assert.True(s2.GetProperty("revalidationRequired").GetBoolean());
        Assert.Equal("blocked-revalidation-required", s2.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, s2.GetProperty("selectedMap").ValueKind);
    }

    [Fact]
    public void BoundCaseAndOracleMutationsFailInsteadOfProducingPolicyEvidence()
    {
        AssertPolicyFails("$c.selectionCases[1].expectedSelection.selectedMap='0:v:0'; Test-G04SelectionCases -Contract $c -Context @{} | Out-Null", "disagree");
        AssertPolicyFails("$c.selectionCases[6].expectedSelection.observedStreams[1].title='changed'; Test-G04SelectionCases -Contract $c -Context @{} | Out-Null", "disagree");
        AssertPolicyFails("$c.classificationCases[7].expectedClassification='passed'; Test-G04ClassificationCases -Contract $c -Context @{} | Out-Null", "Classification oracle failed");
    }

    [Fact]
    public void VideoAndAudioResolveIndependentlyAndMissingTypeIsExplicit()
    {
        var command = "$streams=@([pscustomobject]@{index=4;type='video';codec='h264';default=$false;usable=$true;language='en';title='v';timeBase='1/1000'},[pscustomobject]@{index=2;type='audio';codec='aac';default=$true;usable=$true;language='en';title='a';timeBase='1/48000'}); [ordered]@{video=Resolve-G04MediaType $streams 'video';audio=Resolve-G04MediaType $streams 'audio';missing=Resolve-G04MediaType $streams 'subtitle'}|ConvertTo-Json -Depth 20";
        using var result = JsonDocument.Parse(RunRaw(command));
        Assert.Equal(4, result.RootElement.GetProperty("video").GetProperty("selected").GetProperty("streamIndex").GetInt32());
        Assert.Equal(2, result.RootElement.GetProperty("audio").GetProperty("selected").GetProperty("streamIndex").GetInt32());
        Assert.Equal("rejected-no-usable-stream", result.RootElement.GetProperty("missing").GetProperty("disposition").GetString());
    }

    private static string RunPolicy(bool revalidate = false)
    {
        var repo = PathInRepo();
        var context = revalidate ? "$ctx=@{PriorSelections=@{'S2-OneDefault'=@{sourceContentSha256='AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';runtimeCapabilityFingerprint='old'}};RuntimeCapabilityFingerprint='new'};" : "$ctx=@{};";
        var script = "$ErrorActionPreference='Stop'; . '" + Path.Combine(repo, "eng", "gate0", "input-proof", "Policy.ps1").Replace("'", "''") + "'; $c=Get-Content -Raw '" + Path.Combine(repo, "eng", "gate0", "g0.4-input-proof-contract.json").Replace("'", "''") + "' | ConvertFrom-Json; " + context + " [ordered]@{selection=@(Test-G04SelectionCases -Contract $c -Context $ctx);classification=@(Test-G04ClassificationCases -Contract $c -Context $ctx)}|ConvertTo-Json -Depth 50";
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-Command", script }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output);
        return output;
    }

    private static void AssertPolicyFails(string command, string expected)
    {
        var output = RunRaw(command, out var exitCode);
        Assert.NotEqual(0, exitCode);
        Assert.Contains(expected, output, StringComparison.OrdinalIgnoreCase);
    }

    private static string RunRaw(string command) => RunRaw(command, out _);

    private static string RunRaw(string command, out int exitCode)
    {
        var repo = PathInRepo();
        var preamble = "$ErrorActionPreference='Stop'; . '" + Path.Combine(repo, "eng", "gate0", "input-proof", "Policy.ps1").Replace("'", "''") + "'; $c=Get-Content -Raw '" + Path.Combine(repo, "eng", "gate0", "g0.4-input-proof-contract.json").Replace("'", "''") + "' | ConvertFrom-Json; ";
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-Command", preamble + command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        exitCode = process.ExitCode;
        return output;
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
