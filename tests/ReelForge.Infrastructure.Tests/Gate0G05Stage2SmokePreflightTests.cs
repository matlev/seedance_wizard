using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2SmokePreflightTests
{
    [Fact]
    public void ContractDefinesOnlyTheApprovedBoundedSmokePrerequisite()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2-smoke-preflight-contract.json")));
        var root = document.RootElement;
        Assert.Equal("Gate0.G05.Stage2.SmokePreflight.V1", root.GetProperty("contractId").GetString());
        Assert.False(root.GetProperty("scope").GetProperty("mediaExecutionPermitted").GetBoolean());
        Assert.False(root.GetProperty("scope").GetProperty("preflightExecutionPermitted").GetBoolean());
        Assert.False(root.GetProperty("scope").GetProperty("smokeAuthorizationClaimPermitted").GetBoolean());
        Assert.Equal(3, root.GetProperty("scope").GetProperty("candidates").GetArrayLength());
        Assert.Equal(16, root.GetProperty("host").GetProperty("requiredLogicalProcessorCount").GetInt32());
        Assert.Equal(30L * 1024 * 1024 * 1024, root.GetProperty("host").GetProperty("minimumTotalPhysicalMemoryBytes").GetInt64());
        Assert.Equal(8L * 1024 * 1024 * 1024, root.GetProperty("host").GetProperty("minimumAvailablePhysicalMemoryBytes").GetInt64());
        Assert.Equal(3758096384, root.GetProperty("storage").GetProperty("sameVolumeRequiredFreeBytes").GetInt64());
    }

    [Fact]
    public void RunnerPreservesOfflineNoMediaSafetyAndClosedEvidenceRequirements()
    {
        var path = PathInRepo("eng", "gate0", "Test-G05Stage2SmokePreflight.ps1");
        var parse = Run("pwsh", ["-NoProfile", "-Command", "$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('" + path.Replace("'", "''", StringComparison.Ordinal) + "',[ref]$tokens,[ref]$errors)|Out-Null;$errors|% Message;if($errors.Count){exit 1}"]);
        Assert.Equal(0, parse.ExitCode);
        var script = File.ReadAllText(path);
        foreach (var expected in new[] { "owner-approved-execution-authorized", "preflightExecutionPermitted", "Test-ClosedPreflightPolicy", "Gate0.G05.Stage2.Workloads.V1.OwnerApproved.20260826", "attemptsPerAdmittedRouteThreadCandidate", "retainEveryAttempt", "failFastPerRoute", "threadPolicies", "Test-Gate0ArtifactRetention.ps1", "Test-Gate0ArtifactManifest.ps1", "GlobalMemoryStatusEx", "GetDiskFreeSpaceExW", "Get-Process", "ffmpeg", "ffprobe", "OutputDirectory must be a direct child of StagingRoot", "OutputDirectory must be new", "reparse", "noMediaInvoked", "absolutePathsExcluded", "g0.5-stage2-smoke-preflight-evidence.json", "sameVolumeRequiredFreeBytes", "REELFORGE_GATE0_TEST_INJECTION", ".gate0-test-repository-marker", "GetTempPath" }) Assert.Contains(expected, script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Move-Item", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BindingHashesAndStorageArithmeticRemainExact()
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2-smoke-preflight-contract.json")));
        var root = contract.RootElement;
        var workload = PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json");
        Assert.Equal(root.GetProperty("bindings").GetProperty("workloadContract").GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workload))));
        var storage = root.GetProperty("storage");
        Assert.Equal(storage.GetProperty("fixedFreeSpaceReserveBytes").GetInt64() + storage.GetProperty("smokeRetainedGroupCeilingBytes").GetInt64() + storage.GetProperty("smokeScratchCeilingBytes").GetInt64(), storage.GetProperty("sameVolumeRequiredFreeBytes").GetInt64());
    }

    [Fact]
    public void OwnerEnabledCopiedRepositoryCanRetainAPassedOfflinePreflight()
    {
        if (Environment.ProcessorCount != 16) return;
        using var fixture = new SmokeFixture();
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, result.Output);
        using var evidence = fixture.ReadEvidence();
        Assert.Equal("passed", evidence.RootElement.GetProperty("status").GetString());
        Assert.True(evidence.RootElement.GetProperty("noMediaInvoked").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("observations").GetProperty("testInjectionUsed").GetBoolean());
        Assert.Equal(3758096384, evidence.RootElement.GetProperty("observations").GetProperty("storage").GetProperty("requiredFreeSpaceBytes").GetInt64());
    }

    [Fact]
    public void RouteSpecificThreadPolicyDriftBlocksAndRetainsSanitizedEvidence()
    {
        using var fixture = new SmokeFixture();
        fixture.MutateWorkload(node => node["routes"]!.AsArray()[1]! ["threadPolicies"] = new JsonArray("one"));
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        using var evidence = fixture.ReadEvidence();
        Assert.Equal("blocked", evidence.RootElement.GetProperty("status").GetString());
        Assert.Contains(evidence.RootElement.GetProperty("failures").EnumerateArray().Select(item => item.GetString()), item => item!.Contains("route-specific thread policy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DisabledContractBlocksAfterCreatingClosedEvidence()
    {
        using var fixture = new SmokeFixture(enableExecution: false);
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        using var evidence = fixture.ReadEvidence();
        Assert.Equal("blocked", evidence.RootElement.GetProperty("status").GetString());
        Assert.Contains(evidence.RootElement.GetProperty("failures").EnumerateArray().Select(item => item.GetString()), item => item!.Contains("execution disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AbsolutePathsInValidatorFailuresAreSanitizedInRetainedEvidence()
    {
        using var fixture = new SmokeFixture(retentionFailure: true);
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        var content = File.ReadAllText(fixture.EvidencePath);
        Assert.DoesNotContain("C:\\forbidden", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<absolute-path>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAbsoluteValuedContractPropertyBlocksWithoutEnteringEvidence()
    {
        using var fixture = new SmokeFixture();
        fixture.MutateContract(node => node["scope"]!["somePath"] = "C:\\forbidden\\scope.bin");
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        var content = File.ReadAllText(fixture.EvidencePath);
        Assert.DoesNotContain("C:\\forbidden", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("closed schema", content, StringComparison.OrdinalIgnoreCase);
    }

    private static (int ExitCode, string Output) Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private sealed class SmokeFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "ReelForge-G05-Preflight-" + Guid.NewGuid().ToString("N"));
        private readonly string repository;
        private readonly string gate0;
        private readonly string artifacts;
        private readonly string staging;
        private readonly string output;

        public SmokeFixture(bool enableExecution = true, bool retentionFailure = false)
        {
            repository = Path.Combine(root, "ReelForge"); gate0 = Path.Combine(repository, "eng", "gate0");
            artifacts = Path.Combine(root, "ReelForge.Gate0Artifacts"); staging = Path.Combine(root, "ReelForge.Gate0Staging"); output = Path.Combine(staging, "attempt");
            Directory.CreateDirectory(gate0); Directory.CreateDirectory(artifacts); Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(repository, ".gate0-test-repository-marker"), "test-only\n");
            File.Copy(PathInRepo("eng", "gate0", "Test-G05Stage2SmokePreflight.ps1"), Path.Combine(gate0, "Test-G05Stage2SmokePreflight.ps1"));
            File.Copy(PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json"), Path.Combine(gate0, "g0.5-stage2-workload-contract.json"));
            File.Copy(PathInRepo("eng", "gate0", "g0.5-stage2-preparation-result-summary.json"), Path.Combine(gate0, "g0.5-stage2-preparation-result-summary.json"));
            File.WriteAllText(Path.Combine(gate0, "Test-Gate0ArtifactRetention.ps1"), retentionFailure ? "param([string]$ArtifactRoot) throw 'C:\\forbidden\\retention-source.bin failed'" : "param([string]$ArtifactRoot) [pscustomobject]@{ artifactSetId='TEST'; status='verified'; groupCount=1; fileCount=1; totalBytes=1; manifestSha256=('A'*64); secondCopyVerified=$false; twoCopyRetentionCondition='incomplete' }");
            File.WriteAllText(Path.Combine(gate0, "Test-Gate0ArtifactManifest.ps1"), "[pscustomobject]@{ manifestId='TEST'; artifactSetId='TEST'; sourceManifestSha256=('B'*64); selectedLogicalArtifactCount=1; selectedLogicalArtifactBytes=1; localByteVerificationPerformed=$false; remoteByteVerificationPerformed=$false; recordedRemoteVerifiedLogicalArtifacts=0; requiredLogicalArtifactCount=1; secondPrivateCopyVerified=$false; retentionCondition='incomplete' }");
            var workloadPath = Path.Combine(gate0, "g0.5-stage2-workload-contract.json");
            var contract = JsonNode.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2-smoke-preflight-contract.json")))!.AsObject();
            contract["status"] = enableExecution ? "owner-approved-execution-authorized" : "owner-review-required-no-execution";
            contract["scope"]!["preflightExecutionPermitted"] = enableExecution;
            contract["bindings"]!["workloadContract"]!["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(workloadPath)));
            File.WriteAllText(Path.Combine(gate0, "g0.5-stage2-smoke-preflight-contract.json"), contract.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(root, "observations.json"), "{\"totalPhysicalMemoryBytes\":32212254720,\"availablePhysicalMemoryBytes\":8589934592,\"currentCpuUtilizationPercent\":12.5,\"activeMediaProcesses\":[],\"availableFreeSpaceBytes\":3758096384}");
        }

        public string EvidencePath => Path.Combine(output, "g0.5-stage2-smoke-preflight-evidence.json");
        public void MutateWorkload(Action<JsonObject> mutation)
        {
            var path = Path.Combine(gate0, "g0.5-stage2-workload-contract.json"); var workload = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); mutation(workload); File.WriteAllText(path, workload.ToJsonString());
            var contractPath = Path.Combine(gate0, "g0.5-stage2-smoke-preflight-contract.json"); var contract = JsonNode.Parse(File.ReadAllText(contractPath))!.AsObject(); contract["bindings"]!["workloadContract"]!["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); File.WriteAllText(contractPath, contract.ToJsonString());
        }
        public void MutateContract(Action<JsonObject> mutation)
        {
            var path = Path.Combine(gate0, "g0.5-stage2-smoke-preflight-contract.json");
            var contract = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); mutation(contract); File.WriteAllText(path, contract.ToJsonString());
        }
        public (int ExitCode, string Output) Run()
        {
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["REELFORGE_GATE0_TEST_INJECTION"] = "1";
            foreach (var value in new[] { "-NoProfile", "-File", Path.Combine(gate0, "Test-G05Stage2SmokePreflight.ps1"), "-ArtifactRoot", artifacts, "-StagingRoot", staging, "-OutputDirectory", output, "-EnableTestInjection", "-TestObservationPath", Path.Combine(root, "observations.json") }) start.ArgumentList.Add(value);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell."); var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, text);
        }
        public JsonDocument ReadEvidence() => JsonDocument.Parse(File.ReadAllText(EvidencePath));
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
