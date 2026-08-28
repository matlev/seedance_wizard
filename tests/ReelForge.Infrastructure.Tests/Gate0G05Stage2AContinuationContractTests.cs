using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AContinuationContractTests
{
    private const string V1ScheduleHash = "C16D4A65EDDEA2A6213C0A60D371BE605FD3295EE2695EF2716762EA2F85B90E";
    private static readonly string[] ExcludedGroups = ["baseline-720p", "typical-720p"];
    private static readonly string[] TestOnlyLimitations = ["test-only authorization surface"];
    private static readonly Dictionary<string, string> RequiredRoles = new()
    {
        ["owner-approval"] = "docs/gate-0-g0.5-stage2a-continuation-approval.md",
        ["v2-shard-recovery-approval"] = "docs/gate-0-g0.5-stage2a-v2-shard-approval.md",
        ["schedule"] = "eng/gate0/g0.5-stage2a-continuation-schedule.json",
        ["helper"] = "eng/gate0/G05Stage2AContinuationHelpers.psm1",
        ["runner"] = "eng/gate0/Invoke-G05Stage2AContinuation.ps1",
        ["preflight"] = "eng/gate0/Test-G05Stage2AContinuationPreflight.ps1",
        ["v2-writer-authorization"] = "eng/gate0/g0.5-stage2a-continuation-v2-writer-authorization.json",
        ["v2-writer"] = "eng/gate0/Add-Gate0EvidenceV2Shard.ps1",
        ["v2-containment"] = "eng/gate0/evidence/Gate0EvidenceContainmentV2.psm1",
        ["v2-validator"] = "eng/gate0/Test-Gate0EvidenceV2Containment.ps1",
        ["workload-contract"] = "eng/gate0/g0.5-stage2-workload-contract.json",
        ["retention-contract"] = "eng/gate0/g0.5-stage2a-retention-contract.json",
        ["v5-amendment"] = "eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json",
        ["v5-freeze"] = "eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json",
        ["v5-reevaluation-authorization"] = "eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json",
        ["v5-reevaluation-summary"] = "eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json",
        ["v5-audio-module"] = "eng/gate0/G05Stage2AV5AudioOracle.psm1",
        ["v5-freeze-validator"] = "eng/gate0/G05Stage2AV5FreezeValidation.psm1",
        ["semantic-executor"] = "eng/gate0/G05Stage2ASemanticExecutor.psm1",
        ["semantic-helper"] = "eng/gate0/G05Stage2ASemanticHelpers.psm1",
        ["smoke-helper"] = "eng/gate0/G05Stage2SmokeHelpers.psm1",
        ["marker-helper"] = "eng/gate0/G05MarkerSurvivabilityHelpers.psm1",
        ["runtime-validator"] = "eng/gate0/Validate-P2Runtime.ps1",
        ["runtime-manifest"] = "eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json",
        ["fixture-inventory"] = "eng/gate0/fixture-source-inventory.json",
        ["artifact-manifest"] = "eng/gate0/artifact-manifest.json",
        ["legacy-evidence-validator"] = "eng/gate0/Test-Gate0EvidenceContainment.ps1",
        ["artifact-retention-validator"] = "eng/gate0/Test-Gate0ArtifactRetention.ps1",
        ["artifact-manifest-validator"] = "eng/gate0/Test-Gate0ArtifactManifest.ps1",
        ["legacy-evidence-containment"] = "eng/gate0/evidence/Gate0EvidenceContainment.psm1",
        ["artifact-tools"] = "eng/gate0/Gate0ArtifactTools.psm1",
        ["r2-client-source"] = "eng/gate0/Gate0ArtifactR2Client.cs",
    };

    [Fact]
    public void ContinuationScheduleIsExactProjectionOfImmutableV1Rows37Through108()
    {
        var v1 = PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json");
        var continuation = PathInRepo("eng", "gate0", "g0.5-stage2a-continuation-schedule.json");
        Assert.Equal(V1ScheduleHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(v1))));

        var result = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AContinuationSchedule '{Escape(continuation)}' '{Escape(PathInRepo())}' | ConvertTo-Json -Depth 8 -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var output = JsonDocument.Parse(result.Output);
        Assert.Equal(12, output.RootElement.GetProperty("ProofRunIds").GetArrayLength());

        using var document = JsonDocument.Parse(File.ReadAllText(continuation));
        var root = document.RootElement;
        Assert.Equal("Gate0.G05.Stage2A.ContinuationSchedule.V2", root.GetProperty("scheduleId").GetString());
        Assert.Equal("g05-stage2a-continuation-20260827", root.GetProperty("evidenceGroupId").GetString());
        Assert.Equal(ExcludedGroups, root.GetProperty("excludedGroups").EnumerateArray().Select(x => x.GetString()));
        var attempts = root.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal(72, attempts.Length);
        Assert.Equal(Enumerable.Range(109, 72), attempts.Select(x => x.GetProperty("globalOrdinal").GetInt32()));
        Assert.Equal(Enumerable.Range(1, 72), attempts.Select(x => x.GetProperty("continuationOrdinal").GetInt32()));
        Assert.Equal(Enumerable.Range(37, 72), attempts.Select(x => x.GetProperty("originalScheduleOrdinal").GetInt32()));
        Assert.Equal(12, attempts.Select(x => x.GetProperty("cellId").GetString()).Distinct().Count());
        Assert.All(attempts.Take(6), attempt => Assert.Equal("g05-stage2a-continuation-r1-20260827-stress-720p-webm-eight", attempt.GetProperty("proofRunId").GetString()));
        Assert.All(attempts.Skip(6), attempt => Assert.Equal("g05-stage2a-continuation-20260827-" + attempt.GetProperty("cellId").GetString(), attempt.GetProperty("proofRunId").GetString()));
    }

    [Fact]
    public void ContinuationScheduleRejectsTamperingAndV1ByteChanges()
    {
        var schedule = PathInRepo("eng", "gate0", "g0.5-stage2a-continuation-schedule.json");
        using var tampered = new TempFile(File.ReadAllText(schedule).Replace("\"continuationOrdinal\": 1", "\"continuationOrdinal\": 2", StringComparison.Ordinal));
        var changed = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AContinuationSchedule '{Escape(tampered.Path)}' '{Escape(PathInRepo())}'");
        Assert.NotEqual(0, changed.ExitCode);
        Assert.Contains("ordinals", changed.Output, StringComparison.OrdinalIgnoreCase);

        using var repository = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(repository.Path, "eng", "gate0"));
        File.Copy(PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json"), Path.Combine(repository.Path, "eng", "gate0", "g0.5-stage2a-schedule.json"));
        File.AppendAllText(Path.Combine(repository.Path, "eng", "gate0", "g0.5-stage2a-schedule.json"), " ");
        var v1Changed = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AContinuationSchedule '{Escape(schedule)}' '{Escape(repository.Path)}'");
        Assert.NotEqual(0, v1Changed.ExitCode);
        Assert.Contains("immutable V1", v1Changed.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FutureAuthorizationRequiresExactScheduleProofIdsAndBoundBytes()
    {
        Assert.Equal(32, RequiredRoles.Count);
        using var repository = new TempDirectory();
        foreach (var (role, relativePath) in RequiredRoles)
        {
            var destination = Path.Combine(repository.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var source = PathInRepo(relativePath.Split('/'));
            if (File.Exists(source)) File.Copy(source, destination);
            else if (role == "v2-writer-authorization") File.WriteAllText(destination, "{\"testOnly\":true}\n");
            else throw new InvalidOperationException($"Required source is unexpectedly absent: {relativePath}");
        }
        var v1Destination = Path.Combine(repository.Path, "eng", "gate0", "g0.5-stage2a-schedule.json");
        File.Copy(PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json"), v1Destination);
        var schedule = Path.Combine(repository.Path, "eng", "gate0", "g0.5-stage2a-continuation-schedule.json");
        var scheduleHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(schedule)));
        var proofIds = ReadProofIds(schedule);
        var bindings = RequiredRoles.Select(pair => new { role = pair.Key, path = pair.Value, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository.Path, pair.Value.Replace('/', Path.DirectorySeparatorChar))))) }).ToArray();
        var authorization = JsonSerializer.Serialize(new { schemaVersion = 2, authorizationId = "Gate0.G05.Stage2A.ContinuationAuthorization.V2", authorizationScope = "owner-authorized-stage2a-single-replacement", status = "owner-authorized-single-replacement-effective", exactCellCount = 12, exactAttemptCount = 72, maximumNewCellCount = 1, scheduleBinding = new { path = "eng/gate0/g0.5-stage2a-continuation-schedule.json", sha256 = scheduleHash }, bindings, continuationProofRunIds = proofIds, limitations = TestOnlyLimitations });
        using var fixture = new TempFile(authorization);
        var valid = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AContinuationAuthorization '{Escape(fixture.Path)}' '{Escape(repository.Path)}' '{Escape(schedule)}'");
        Assert.True(valid.ExitCode == 0, valid.Output);

        var mutations = new Action<JsonObject>[]
        {
            root => ((JsonArray)root["bindings"]!).RemoveAt(0),
            root => { var extra = (JsonObject)JsonNode.Parse(((JsonArray)root["bindings"]!)[0]!.ToJsonString())!; extra["role"] = "unexpected"; ((JsonArray)root["bindings"]!).Add(extra); },
            root => ((JsonArray)root["bindings"]!).Add(JsonNode.Parse(((JsonArray)root["bindings"]!)[0]!.ToJsonString())!),
            root => { var binding = (JsonObject)((JsonArray)root["bindings"]!)[0]!; binding["sha256"] = new string('F', 64); },
            root => ((JsonArray)root["continuationProofRunIds"]!)[0] = "g05-stage2a-continuation-20260827-tampered",
            root => ((JsonArray)root["continuationProofRunIds"]!).Add(((JsonArray)root["continuationProofRunIds"]!)[0]!.DeepClone()),
        };
        foreach (var mutate in mutations)
        {
            var changed = JsonNode.Parse(authorization)!.AsObject();
            mutate(changed);
            File.WriteAllText(fixture.Path, changed.ToJsonString());
            var tampered = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AContinuationAuthorization '{Escape(fixture.Path)}' '{Escape(repository.Path)}' '{Escape(schedule)}'");
            Assert.NotEqual(0, tampered.ExitCode);
        }
    }

    private static string[] ReadProofIds(string schedule)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(schedule));
        return document.RootElement.GetProperty("attempts").EnumerateArray()
            .Select(x => x.GetProperty("proofRunId").GetString()!).Distinct().Order().ToArray();
    }

    private static (int ExitCode, string Output) RunPwsh(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string ModulePath() => PathInRepo("eng", "gate0", "G05Stage2AContinuationHelpers.psm1").Replace("'", "''", StringComparison.Ordinal);
    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string content) { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReelForge-Continuation-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(Path, content); }
        public string Path { get; }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReelForge-Continuation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
