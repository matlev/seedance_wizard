using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0EvidenceContainmentTests
{
    [Fact]
    public void RootIndexHasClosedSchemaApprovedSealAndNoPerArtifactArray()
    {
        var root = CopyRootIndex();
        var result = RunPs($"Import-Module '{PsQuote(ModulePath())}' -Force; $r=Read-Gate0EvidenceRootIndex '{PsQuote(root)}' -AllowMissingShards; $r.Index | ConvertTo-Json -Depth 20");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var document = json.RootElement;
        Assert.Equal(0, document.GetProperty("runs").GetArrayLength());
        Assert.False(document.TryGetProperty("artifacts", out _));
        Assert.Equal("AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0", document.GetProperty("legacySeal").GetProperty("sourceManifestSha256").GetString());
    }

    [Theory]
    [InlineData("legacySeal.sourceManifestSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("legacySeal.durableManifestSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("legacySeal.logicalArtifactCount", "1")]
    public void RootIndexRejectsChangedLegacySeal(string property, string value)
    {
        var root = CopyRootIndex();
        var node = JsonNode.Parse(File.ReadAllText(root))!.AsObject();
        object replacement = int.TryParse(value, out var number) ? number : value;
        Set(node, property, JsonValue.Create(replacement));
        File.WriteAllText(root, node.ToJsonString());
        AssertRejected(root, "Evidence root index changed the approved legacy seal");
    }

    [Fact]
    public void RootIndexRejectsExtraPropertyAndRetentionCapChange()
    {
        var root = CopyRootIndex();
        var node = JsonNode.Parse(File.ReadAllText(root))!.AsObject();
        node["unexpected"] = true;
        File.WriteAllText(root, node.ToJsonString());
        AssertRejected(root, "closed evidence schema");

        node.Remove("unexpected");
        node["limits"]!["maxShardBytes"] = 65535;
        File.WriteAllText(root, node.ToJsonString());
        AssertRejected(root, "approved retention limit");
    }

    [Fact]
    public void RootIndexRejectsDuplicateOrReorderedRuns()
    {
        var root = CopyRootIndex();
        var node = JsonNode.Parse(File.ReadAllText(root))!.AsObject();
        node["runs"] = new JsonArray(RunEntry("run-a", "cell-a", null, null), RunEntry("run-a", "cell-a", null, null));
        node["totals"]!["runCount"] = 2;
        File.WriteAllText(root, node.ToJsonString());
        AssertRejected(root, "entry hash");
    }

    [Theory]
    [InlineData("stage2/../escape.manifest.json")]
    [InlineData("stage2\\escape.manifest.json")]
    [InlineData("C:/escape.manifest.json")]
    public void RootIndexRejectsUnsafeShardPaths(string shardPath)
    {
        var root = CopyRootIndex();
        var node = JsonNode.Parse(File.ReadAllText(root))!.AsObject();
        node["runs"] = new JsonArray(RunEntry("run-a", "cell-a", shardPath, null));
        node["totals"]!["runCount"] = 1;
        File.WriteAllText(root, node.ToJsonString());
        AssertRejected(root, "");
    }

    [Fact]
    public void ShardAcceptsOneBoundArtifactAndRejectsBadHashBindingAndUnsafeMetadata()
    {
        var shard = WriteValidShard();
        Assert.Equal(0, RunPs($"Import-Module '{PsQuote(ModulePath())}' -Force; Read-Gate0EvidenceShard '{PsQuote(shard)}' | Out-Null").ExitCode);

        var node = JsonNode.Parse(File.ReadAllText(shard))!.AsObject();
        node["artifacts"]![0]! ["r2ObjectKey"] = "objects/sha256/ff/" + new string('f', 64);
        File.WriteAllText(shard, node.ToJsonString());
        AssertRejectedShard(shard, "object key");

        node = JsonNode.Parse(File.ReadAllText(WriteValidShard()))!.AsObject();
        node["limitations"] = new JsonArray("https://signed.example/?sig=secret");
        File.WriteAllText(shard, node.ToJsonString());
        AssertRejectedShard(shard, "prohibited");
    }

    [Fact]
    public void ShardRejectsDuplicateArtifactsAndExcessiveCompactRecord()
    {
        var shard = WriteValidShard();
        var node = JsonNode.Parse(File.ReadAllText(shard))!.AsObject();
        var artifact = node["artifacts"]![0]!.DeepClone();
        node["artifacts"]!.AsArray().Add(artifact);
        node["totals"]!["logicalArtifactCount"] = 2;
        node["totals"]!["logicalArtifactBytes"] = 2;
        File.WriteAllText(shard, node.ToJsonString());
        AssertRejectedShard(shard, "duplicate");

        var containmentScript = File.ReadAllText(PathInRepo("eng", "gate0", "Test-Gate0EvidenceContainment.ps1"));
        Assert.Contains("262144", containmentScript, StringComparison.Ordinal);
        Assert.Contains("compact-attempt", containmentScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSealIsEffectiveAndFaultInjectionRemainsIsolated()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Add-Gate0EvidenceShard.ps1"));
        Assert.Contains("Assert-Gate0LegacyEvidenceSeal $repositoryRoot -RequireEffective", script, StringComparison.Ordinal);
        Assert.Contains("FaultInjection", script, StringComparison.Ordinal);
        Assert.Contains("262144", File.ReadAllText(PathInRepo("eng", "gate0", "Test-Gate0EvidenceContainment.ps1")), StringComparison.Ordinal);
        var result = RunPs($"Import-Module '{PsQuote(ModulePath())}' -Force; Assert-Gate0LegacyEvidenceSeal '{PsQuote(RepositoryRoot())}' -RequireEffective | Out-Null");
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void IsolatedWriterFailureLeavesRootAndPayloadUnchangedThenValidAppendSucceeds()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var before = File.ReadAllText(corpus.RootIndex);
            var failed = RunPs(corpus.WriterCommand + " -FaultInjection AfterShardMove");
            Assert.NotEqual(0, failed.ExitCode);
            Assert.Equal(before, File.ReadAllText(corpus.RootIndex));
            Assert.False(Directory.Exists(corpus.Destination));
            Assert.False(File.Exists(corpus.Shard));

            var passed = RunPs(corpus.WriterCommand);
            Assert.True(passed.ExitCode == 0, passed.Output);
            Assert.NotEqual(before, File.ReadAllText(corpus.RootIndex));
            Assert.True(Directory.Exists(corpus.Destination));
            Assert.True(File.Exists(corpus.Shard));
            var validation = RunPs($"Import-Module '{PsQuote(Path.Combine(corpus.Gate0, "evidence", "Gate0EvidenceContainment.psm1"))}' -Force; Read-Gate0EvidenceRootIndex '{PsQuote(corpus.RootIndex)}' | Out-Null");
            Assert.True(validation.ExitCode == 0, validation.Output);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void EffectiveSealDisablesTheLegacyAppendScript()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            File.Copy(PathInRepo("eng", "gate0", "Add-Gate0RetainedProof.ps1"), Path.Combine(corpus.Gate0, "Add-Gate0RetainedProof.ps1"));
            var command = $"& '{PsQuote(Path.Combine(corpus.Gate0, "Add-Gate0RetainedProof.ps1"))}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -SourceRoot '{PsQuote(corpus.Source)}' -GroupId old -DestinationName old/path -Provenance old -ProofRunIdentity artifact:old/path/proof.txt";
            var result = RunPs(command);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Legacy retained-proof append is disabled", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, JsonNode.Parse(File.ReadAllText(corpus.RootIndex))!["totals"]!["runCount"]!.GetValue<int>());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void RootValidatorRejectsCumulativeBytesAboveTheApprovedCeiling()
    {
        var root = CopyRootIndex();
        var command = $"Import-Module '{PsQuote(ModulePath())}' -Force; $p='{PsQuote(root)}'; $r=Get-Content $p -Raw|ConvertFrom-Json; $e=[pscustomobject]@{{ordinal=1;runKind='infrastructure';proofRunId='run-a';evidenceGroupId='group-a';cellId='cell-a';shardPath='stage2/run-a.manifest.json';shardSha256=('A'*64);entrySha256='';previousRunId=$null;previousRunEntrySha256=$null;disposition='passed';logicalArtifactCount=0;logicalArtifactBytes=805306369;localRetention='verified';r2Retention='independently-retrieved-and-verified'}};$e.entrySha256=Get-Gate0EvidenceEntryHash $e;$r.runs=@($e);$r.totals.runCount=1;$r.totals.logicalArtifactCount=0;$r.totals.logicalArtifactBytes=805306369;$r|ConvertTo-Json -Depth 20|Set-Content $p;Read-Gate0EvidenceRootIndex $p -AllowMissingShards|Out-Null";
        var result = RunPs(command);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("retention ceiling", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShardValidatorRejectsAnOversizeBoundCompactAttempt()
    {
        var shard = WriteValidShard();
        var node = JsonNode.Parse(File.ReadAllText(shard))!.AsObject();
        node["artifacts"]![0]!["byteSize"] = 262145;
        node["totals"]!["logicalArtifactBytes"] = 262145;
        var sha = node["artifacts"]![0]!["sha256"]!.GetValue<string>();
        node["attempts"] = new JsonArray(new JsonObject
        {
            ["attemptId"] = "attempt-a", ["phase"] = "measured", ["ordinal"] = 1, ["retentionClass"] = "compact",
            ["recordPath"] = "run-a/proof.txt", ["recordSha256"] = sha, ["disposition"] = "passed", ["completeClosureReference"] = "closure-a"
        });
        File.WriteAllText(shard, node.ToJsonString());
        AssertRejectedShard(shard, "256 KiB cap");
    }

    [Fact]
    public void FailedPostRemoteBoundaryPreservesJournalAndStagingForOwnerReview()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var before = File.ReadAllText(corpus.RootIndex);
            var result = RunPs(corpus.WriterCommand + " -FaultInjection AfterRemoteVerification");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal(before, File.ReadAllText(corpus.RootIndex));
            Assert.True(File.Exists(corpus.ArtifactRoot + ".stage2-append-journal.json"));
            Assert.NotEmpty(Directory.GetDirectories(corpus.TestRoot, "ReelForge.Gate0Artifacts.stage2-staging-*"));
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void OwnerReviewedJournalDispositionCreatesAnIndexedBlockedShard()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            Assert.NotEqual(0, RunPs(corpus.WriterCommand + " -FaultInjection AfterRemoteVerification").ExitCode);
            var resolver = Path.Combine(corpus.Gate0, "Resolve-Gate0EvidenceAppendJournal.ps1");
            var result = RunPs($"& '{PsQuote(resolver)}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -Disposition blocked -OwnerReviewIdentity repository:owner-review -SkipRemoteForIsolatedTest");
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.False(File.Exists(corpus.ArtifactRoot + ".stage2-append-journal.json"));
            using var root = JsonDocument.Parse(File.ReadAllText(corpus.RootIndex));
            var run = root.RootElement.GetProperty("runs").EnumerateArray().Single();
            Assert.Equal("blocked", run.GetProperty("disposition").GetString());
            Assert.True(File.Exists(corpus.Shard));
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void JournalDispositionRejectsSameLengthStagingTampering()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            Assert.NotEqual(0, RunPs(corpus.WriterCommand + " -FaultInjection AfterRemoteVerification").ExitCode);
            var staging = Directory.GetDirectories(corpus.TestRoot, "ReelForge.Gate0Artifacts.stage2-staging-*").Single();
            File.WriteAllText(Path.Combine(staging, "proof.txt"), "troof");
            var resolver = Path.Combine(corpus.Gate0, "Resolve-Gate0EvidenceAppendJournal.ps1");
            var result = RunPs($"& '{PsQuote(resolver)}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -Disposition blocked -OwnerReviewIdentity repository:owner-review -SkipRemoteForIsolatedTest");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("differs", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(corpus.ArtifactRoot + ".stage2-append-journal.json"));
            using var root = JsonDocument.Parse(File.ReadAllText(corpus.RootIndex));
            Assert.Equal(0, root.RootElement.GetProperty("runs").GetArrayLength());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void LocalValidatorRejectsUnindexedFutureEvidence()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var passed = RunPs(corpus.WriterCommand);
            Assert.True(passed.ExitCode == 0, passed.Output);
            File.WriteAllText(Path.Combine(corpus.Destination, "stray.txt"), "stray");
            var validator = Path.Combine(corpus.Gate0, "Test-Gate0EvidenceContainment.ps1");
            var result = RunPs($"& '{PsQuote(validator)}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -RequireEffectiveSeal");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("unindexed", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void EffectiveSealBlocksDirectDefaultDurableLedgerMutation()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var module = Path.Combine(corpus.Gate0, "Gate0ArtifactTools.psm1");
            var manifest = Path.Combine(corpus.Gate0, "artifact-manifest.json");
            var result = RunPs($"Import-Module '{PsQuote(module)}' -Force; Save-Gate0RemoteVerifiedReceipt $null '{PsQuote(manifest)}' $null x x");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("sealed", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void TwoConcurrentWritersProduceExactlyOneOrderedAppend()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            using var first = StartPowerShell(corpus.WriterCommand);
            using var second = StartPowerShell(corpus.WriterCommand);
            var firstOutput = first.StandardOutput.ReadToEnd() + first.StandardError.ReadToEnd();
            var secondOutput = second.StandardOutput.ReadToEnd() + second.StandardError.ReadToEnd();
            first.WaitForExit(); second.WaitForExit();
            Assert.Equal(1, new[] { first.ExitCode, second.ExitCode }.Count(code => code == 0));
            using var root = JsonDocument.Parse(File.ReadAllText(corpus.RootIndex));
            Assert.Equal(1, root.RootElement.GetProperty("runs").GetArrayLength());
            Assert.True(first.ExitCode == 0 || firstOutput.Contains("active", StringComparison.OrdinalIgnoreCase) || firstOutput.Contains("already", StringComparison.OrdinalIgnoreCase));
            Assert.True(second.ExitCode == 0 || secondOutput.Contains("active", StringComparison.OrdinalIgnoreCase) || secondOutput.Contains("already", StringComparison.OrdinalIgnoreCase));
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void RootValidatorKeepsTwoInfrastructureShardsSeparateFromEighteenCellShards()
    {
        var root = CopyRootIndex();
        var command = $"Import-Module '{PsQuote(ModulePath())}' -Force;$p='{PsQuote(root)}';$r=Get-Content $p -Raw|ConvertFrom-Json;$runs=@();$prev=$null;1..3|%{{$e=[pscustomobject]@{{ordinal=$_;runKind='infrastructure';proofRunId=('infra-'+$_);evidenceGroupId=('group-'+$_);cellId=('cell-'+$_);shardPath=('stage2/infra-'+$_.ToString()+'.manifest.json');shardSha256=('A'*64);entrySha256='';previousRunId=if($prev){{$prev.proofRunId}}else{{$null}};previousRunEntrySha256=if($prev){{$prev.entrySha256}}else{{$null}};disposition='passed';logicalArtifactCount=0;logicalArtifactBytes=0;localRetention='verified';r2Retention='independently-retrieved-and-verified'}};$e.entrySha256=Get-Gate0EvidenceEntryHash $e;$runs+=@($e);$prev=$e}};$r.runs=$runs;$r.totals.runCount=3;$r|ConvertTo-Json -Depth 20|Set-Content $p;Read-Gate0EvidenceRootIndex $p -AllowMissingShards|Out-Null";
        var result = RunPs(command);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("shard count", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalValidatorRejectsAnEmptyReparseDirectoryWhenHostPrivilegesPermitCreation()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var future = Path.Combine(corpus.ArtifactRoot, "future", "stage2");
            var target = Path.Combine(corpus.TestRoot, "reparse-target");
            Directory.CreateDirectory(future); Directory.CreateDirectory(target);
            var link = Path.Combine(future, "empty-link");
            try { Directory.CreateSymbolicLink(link, target); }
            catch (UnauthorizedAccessException)
            {
                Assert.Contains("ReparsePoint", File.ReadAllText(Path.Combine(corpus.Gate0, "Test-Gate0EvidenceContainment.ps1")), StringComparison.Ordinal);
                return;
            }
            catch (IOException)
            {
                Assert.Contains("ReparsePoint", File.ReadAllText(Path.Combine(corpus.Gate0, "Test-Gate0EvidenceContainment.ps1")), StringComparison.Ordinal);
                return;
            }
            var validator = Path.Combine(corpus.Gate0, "Test-Gate0EvidenceContainment.ps1");
            var result = RunPs($"& '{PsQuote(validator)}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -RequireEffectiveSeal");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    private static string CopyRootIndex()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-containment-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(directory, "root-index.json");
        File.Copy(PathInRepo("eng", "gate0", "evidence", "root-index.json"), path);
        return path;
    }

    private static string WriteValidShard()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-shard-" + Guid.NewGuid().ToString("N"))).FullName;
        var payload = Path.Combine(directory, "proof.txt");
        File.WriteAllText(payload, "proof");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(payload)));
        var shard = new
        {
            schemaVersion = 1, shardId = "Gate0.Stage2Evidence.Shard.V1", proofRunId = "run-a", evidenceGroupId = "group-a", cellId = "cell-a",
            evidenceBoundary = "containment-no-media", createdUtc = DateTimeOffset.UtcNow.ToString("O"), contractIdentity = new[] { "repository:contract" },
            provenance = "isolated test", producerRuntimeIdentity = new[] { "repository:producer" }, licenseRecords = Array.Empty<string>(),
            artifacts = new[] { new { artifactId = "artifact-a", relativePath = "run-a/proof.txt", byteSize = 5, sha256 = sha, r2ObjectKey = $"objects/sha256/{sha[..2].ToLowerInvariant()}/{sha.ToLowerInvariant()}", purpose = "test", retentionStatus = "remote-verified", transferDisposition = "independently-retrieved-and-verified", remotelyVerifiedUtc = DateTimeOffset.UtcNow.ToString("O") } },
            attempts = Array.Empty<string>(), disposition = "passed", localRetention = "verified", r2Retention = "independently-retrieved-and-verified", totals = new { logicalArtifactCount = 1, logicalArtifactBytes = 5 }, limitations = new[] { "test" }
        };
        var path = Path.Combine(directory, "shard.json");
        File.WriteAllText(path, JsonSerializer.Serialize(shard));
        return path;
    }

    private static (string TestRoot, string Gate0, string ArtifactRoot, string Source, string RootIndex, string Destination, string Shard, string WriterCommand) CreateIsolatedContainmentCorpus()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-containment-writer-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(testRoot, "repo");
        var gate0 = Path.Combine(repo, "eng", "gate0");
        var evidence = Path.Combine(gate0, "evidence");
        Directory.CreateDirectory(evidence);
        File.WriteAllText(Path.Combine(repo, ".gate0-containment-test-sentinel"), "isolated\n");
        foreach (var file in new[] { "artifact-retention-manifest.json", "artifact-manifest.json", "Gate0ArtifactTools.psm1", "Gate0ArtifactR2Client.cs", "Add-Gate0EvidenceShard.ps1", "Resolve-Gate0EvidenceAppendJournal.ps1", "Test-Gate0EvidenceContainment.ps1" })
            File.Copy(PathInRepo("eng", "gate0", file), Path.Combine(gate0, file));
        File.Copy(ModulePath(), Path.Combine(evidence, "Gate0EvidenceContainment.psm1"));
        var rootIndex = Path.Combine(evidence, "root-index.json");
        File.Copy(PathInRepo("eng", "gate0", "evidence", "root-index.json"), rootIndex);
        var rootHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(rootIndex)));
        var seal = new
        {
            schemaVersion = 1,
            sealId = "Gate0.LegacyEvidenceSeal.20260827",
            effectiveUtc = DateTimeOffset.UtcNow.ToString("O"),
            sourceManifestPath = "eng/gate0/artifact-retention-manifest.json",
            sourceManifestSha256 = "AE088727059D3686930C4422237A02E6691580D93C85E3862489C8F65FCDD0A0",
            durableManifestPath = "eng/gate0/artifact-manifest.json",
            durableManifestSha256 = "AF9B368D44FDE3EFD2C45E2D847CB989D38E52066607A0D3E61384588D23C113",
            logicalArtifactCount = 4101,
            logicalArtifactBytes = 1121540509L,
            rootIndexPath = "eng/gate0/evidence/root-index.json",
            initialRootIndexSha256 = rootHash,
            retentionCondition = "complete-and-independently-byte-verified",
            limitations = new[] { "isolated test" }
        };
        File.WriteAllText(Path.Combine(evidence, "legacy-seal.json"), JsonSerializer.Serialize(seal));
        var artifactRoot = Path.Combine(testRoot, "ReelForge.Gate0Artifacts");
        Directory.CreateDirectory(artifactRoot);
        var source = Path.Combine(testRoot, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "proof.txt"), "proof");
        var destination = Path.Combine(artifactRoot, "future", "stage2", "run-a");
        var shard = Path.Combine(evidence, "stage2", "run-a.manifest.json");
        var writer = Path.Combine(gate0, "Add-Gate0EvidenceShard.ps1");
        var command = $"& '{PsQuote(writer)}' -ArtifactRoot '{PsQuote(artifactRoot)}' -SourceRoot '{PsQuote(source)}' -ProofRunId run-a -EvidenceGroupId group-a -CellId cell-a -DestinationName future/stage2/run-a -EvidenceBoundary containment-no-media -Disposition passed -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest";
        return (testRoot, gate0, artifactRoot, source, rootIndex, destination, shard, command);
    }

    private static JsonObject RunEntry(string id, string cell, string? shardPath, string? entryHash)
    {
        return new JsonObject { ["ordinal"] = 1, ["runKind"] = "infrastructure", ["proofRunId"] = id, ["evidenceGroupId"] = "group-a", ["cellId"] = cell, ["shardPath"] = shardPath ?? "stage2/run-a.manifest.json", ["shardSha256"] = new string('A', 64), ["entrySha256"] = entryHash ?? new string('B', 64), ["previousRunId"] = null, ["previousRunEntrySha256"] = null, ["disposition"] = "passed", ["logicalArtifactCount"] = 0, ["logicalArtifactBytes"] = 0, ["localRetention"] = "verified", ["r2Retention"] = "independently-retrieved-and-verified" };
    }

    private static void Set(JsonObject node, string path, JsonNode? value)
    {
        var parts = path.Split('.');
        var target = node;
        for (var i = 0; i < parts.Length - 1; i++) target = target[parts[i]]!.AsObject();
        target[parts[^1]] = value;
    }

    private static void AssertRejected(string path, string expected)
    {
        var result = RunPs($"Import-Module '{PsQuote(ModulePath())}' -Force; Read-Gate0EvidenceRootIndex '{PsQuote(path)}' -AllowMissingShards | Out-Null");
        Assert.NotEqual(0, result.ExitCode);
        if (!string.IsNullOrEmpty(expected)) Assert.Contains(expected, result.Output, StringComparison.OrdinalIgnoreCase);
    }
    private static void AssertRejectedShard(string path, string expected)
    {
        var result = RunPs($"Import-Module '{PsQuote(ModulePath())}' -Force; Read-Gate0EvidenceShard '{PsQuote(path)}' | Out-Null");
        Assert.NotEqual(0, result.ExitCode);
        if (!string.IsNullOrEmpty(expected)) Assert.Contains(expected, result.Output, StringComparison.OrdinalIgnoreCase);
    }
    private static string ModulePath() => PathInRepo("eng", "gate0", "evidence", "Gate0EvidenceContainment.psm1");
    private static string RepositoryRoot() => PathInRepo();
    private static string PsQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static (int ExitCode, string Output) RunPs(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(command);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }
    private static Process StartPowerShell(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add(command);
        return Process.Start(start)!;
    }
    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory); return Path.Combine([directory!.FullName, .. parts]);
    }
}
