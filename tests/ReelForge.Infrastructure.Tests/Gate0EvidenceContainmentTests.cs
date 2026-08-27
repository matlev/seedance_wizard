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
        Assert.Equal(document.GetProperty("runs").GetArrayLength(), document.GetProperty("totals").GetProperty("runCount").GetInt32());
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
    public void LocalValidatorRejectsUnindexedTrackedStage2File()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var passed = RunPs(corpus.WriterCommand);
            Assert.True(passed.ExitCode == 0, passed.Output);

            var trackedStage2 = Path.Combine(corpus.Gate0, "evidence", "stage2");
            File.WriteAllText(Path.Combine(trackedStage2, "notes.txt"), "unindexed");
            var validator = Path.Combine(corpus.Gate0, "Test-Gate0EvidenceContainment.ps1");
            var result = RunPs($"& '{PsQuote(validator)}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -RequireEffectiveSeal");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("unindexed", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void LocalValidatorRejectsUnexpectedTrackedStage2Directory()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var passed = RunPs(corpus.WriterCommand);
            Assert.True(passed.ExitCode == 0, passed.Output);

            var trackedStage2 = Path.Combine(corpus.Gate0, "evidence", "stage2");
            var nested = Directory.CreateDirectory(Path.Combine(trackedStage2, "unexpected")).FullName;
            File.WriteAllText(Path.Combine(nested, "nested.txt"), "unindexed");

            var validator = Path.Combine(corpus.Gate0, "Test-Gate0EvidenceContainment.ps1");
            var result = RunPs($"& '{PsQuote(validator)}' -ArtifactRoot '{PsQuote(corpus.ArtifactRoot)}' -RequireEffectiveSeal");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("tracked evidence shard tree", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void LegacySealRejectsChangedWellFormedInitialRootIndexHash()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var baseline = RunPs($"Import-Module '{PsQuote(Path.Combine(corpus.Gate0, "evidence", "Gate0EvidenceContainment.psm1"))}' -Force; Assert-Gate0LegacyEvidenceSeal '{PsQuote(IsolatedRepoRoot(corpus))}' -RequireEffective | Out-Null");
            Assert.True(baseline.ExitCode == 0, baseline.Output);

            var sealPath = Path.Combine(corpus.Gate0, "evidence", "legacy-seal.json");
            var seal = JsonNode.Parse(File.ReadAllText(sealPath))!.AsObject();
            seal["initialRootIndexSha256"] = new string('F', 64);
            File.WriteAllText(sealPath, seal.ToJsonString());

            var result = RunPs($"Import-Module '{PsQuote(Path.Combine(corpus.Gate0, "evidence", "Gate0EvidenceContainment.psm1"))}' -Force; Assert-Gate0LegacyEvidenceSeal '{PsQuote(IsolatedRepoRoot(corpus))}' -RequireEffective | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Legacy evidence seal", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void P2RuntimeRouteAppendIsBlockedWithoutAnExactStage2AScheduleAndRunnerAuthorization()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var p2Command = corpus.WriterCommand.Replace("-EvidenceBoundary containment-no-media", "-EvidenceBoundary p2-runtime-route", StringComparison.Ordinal);
            var result = RunPs(p2Command);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Stage 2A evidence append is blocked", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, JsonNode.Parse(File.ReadAllText(corpus.RootIndex))!["totals"]!["runCount"]!.GetValue<int>());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void WriterRetainsARealisticFailedCellWithinTheImmutableShardCap()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            PrepareRealisticCellSource(corpus);
            var attemptBindings = CreateRealisticStage2AAttemptBindings(corpus, projectToRetainedNamespace: true);
            var command = corpus.WriterCommand.Replace("-Disposition passed", "-Disposition failed", StringComparison.Ordinal)
                + $" -AttemptBindingsPath '{PsQuote(attemptBindings)}'";
            var result = RunPs(command);
            Assert.True(result.ExitCode == 0, result.Output);

            var shardInfo = new FileInfo(corpus.Shard);
            Assert.True(shardInfo.Length <= 65536, $"Shard is {shardInfo.Length} bytes.");
            Assert.True(File.ReadLines(corpus.Shard).Count() <= 300);
            var shard = JsonNode.Parse(File.ReadAllText(corpus.Shard))!.AsObject();
            Assert.Equal("failed", shard["disposition"]!.GetValue<string>());
            Assert.Equal(24, shard["artifacts"]!.AsArray().Count);
            Assert.Equal(6, shard["attempts"]!.AsArray().Count);
            var ids = shard["artifacts"]!.AsArray().Select(node => node!["artifactId"]!.GetValue<string>()).ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
            var firstAttemptPath = "future/stage2/run-a/attempts/attempt-1.json";
            var expectedId = "artifact-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(firstAttemptPath))).ToLowerInvariant();
            Assert.Contains(expectedId, ids);
            using var root = JsonDocument.Parse(File.ReadAllText(corpus.RootIndex));
            Assert.Equal("failed", root.RootElement.GetProperty("runs")[0].GetProperty("disposition").GetString());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void P2RuntimeRouteAppendRejectsAnOtherwiseExactPendingStage2AAuthorization()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            ConfigureExactStage2AAuthorization(corpus, "owner-authorized-execution-implementation-pending");
            var result = RunPs(P2WriterCommand(corpus));
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("authorization is effective", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, JsonNode.Parse(File.ReadAllText(corpus.RootIndex))!["totals"]!["runCount"]!.GetValue<int>());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void P2RuntimeRouteAppendAcceptsOnlyTheExactExpandedEffectiveStage2AAuthorization()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            ConfigureExactStage2AAuthorization(corpus, "owner-authorized-and-prerequisites-verified");
            var result = RunPs(P2WriterCommand(corpus));
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.DoesNotContain("unexpected or duplicate binding role", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, JsonNode.Parse(File.ReadAllText(corpus.RootIndex))!["totals"]!["runCount"]!.GetValue<int>());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void P2RuntimeRouteAppendAcceptsAllSixProjectedAttemptBindingsBeforeRemoteRetention()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            ConfigureExactStage2AAuthorization(corpus, "owner-authorized-and-prerequisites-verified");
            PrepareRealisticCellSource(corpus);
            var attemptBindings = CreateRealisticStage2AAttemptBindings(corpus, projectToRetainedNamespace: true);
            var result = RunPs(P2WriterCommand(corpus).Replace("-Disposition passed", "-Disposition failed", StringComparison.Ordinal)
                + $" -AttemptBindingsPath '{PsQuote(attemptBindings)}'");

            Assert.True(result.ExitCode == 0, result.Output);
            var shard = JsonNode.Parse(File.ReadAllText(corpus.Shard))!.AsObject();
            Assert.Equal(6, shard["attempts"]!.AsArray().Count);
            Assert.All(shard["attempts"]!.AsArray(), attempt =>
                Assert.StartsWith("future/stage2/run-a/", attempt!["recordPath"]!.GetValue<string>(), StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Fact]
    public void P2RuntimeRouteAppendRejectsUnprojectedAttemptBindingsBeforeRemoteRetention()
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            ConfigureExactStage2AAuthorization(corpus, "owner-authorized-and-prerequisites-verified");
            PrepareRealisticCellSource(corpus);
            var attemptBindings = CreateRealisticStage2AAttemptBindings(corpus, projectToRetainedNamespace: false);
            var result = RunPs(P2WriterCommand(corpus).Replace("-Disposition passed", "-Disposition failed", StringComparison.Ordinal)
                + $" -AttemptBindingsPath '{PsQuote(attemptBindings)}'");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("does not reference one exact retained artifact", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(corpus.Destination));
            Assert.False(File.Exists(corpus.Shard));
            Assert.Equal(0, JsonNode.Parse(File.ReadAllText(corpus.RootIndex))!["totals"]!["runCount"]!.GetValue<int>());
        }
        finally { if (Directory.Exists(corpus.TestRoot)) Directory.Delete(corpus.TestRoot, recursive: true); }
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("tamper")]
    [InlineData("extra")]
    [InlineData("missing")]
    public void P2RuntimeRouteAppendRejectsAnyNonExactStage2AAuthorizationBindingSet(string mutation)
    {
        var corpus = CreateIsolatedContainmentCorpus();
        try
        {
            var authorizationPath = ConfigureExactStage2AAuthorization(corpus, "owner-authorized-and-prerequisites-verified");
            var authorization = JsonNode.Parse(File.ReadAllText(authorizationPath))!.AsObject();
            var bindings = authorization["bindings"]!.AsArray();
            switch (mutation)
            {
                case "duplicate": bindings.Add(bindings[0]!.DeepClone()); break;
                case "tamper": bindings[0]!["sha256"] = new string('0', 64); break;
                case "extra": bindings.Add(new JsonObject { ["role"] = "unapproved", ["path"] = "eng/gate0/unapproved.txt", ["sha256"] = new string('A', 64) }); break;
                case "missing": bindings.RemoveAt(bindings.Count - 1); break;
                default: throw new InvalidOperationException(mutation);
            }
            File.WriteAllText(authorizationPath, authorization.ToJsonString());

            var result = RunPs(P2WriterCommand(corpus));
            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("unexpected or duplicate binding role", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, JsonNode.Parse(File.ReadAllText(corpus.RootIndex))!["totals"]!["runCount"]!.GetValue<int>());
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
        ResetFutureRuns(path);
        return path;
    }

    private static void ResetFutureRuns(string rootIndex)
    {
        var root = JsonNode.Parse(File.ReadAllText(rootIndex))!.AsObject();
        root["runs"] = new JsonArray();
        root["totals"]!["runCount"] = 0;
        root["totals"]!["logicalArtifactCount"] = 0;
        root["totals"]!["logicalArtifactBytes"] = 0;
        File.WriteAllText(rootIndex, root.ToJsonString());
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
        foreach (var file in new[] { "artifact-retention-manifest.json", "artifact-manifest.json", "Gate0ArtifactTools.psm1", "Gate0ArtifactR2Client.cs", "Add-Gate0EvidenceShard.ps1", "G05Stage2AMatrixHelpers.psm1", "G05Stage2SmokeHelpers.psm1", "G05Stage2ASemanticExecutor.psm1", "Resolve-Gate0EvidenceAppendJournal.ps1", "Test-Gate0EvidenceContainment.ps1" })
            File.Copy(PathInRepo("eng", "gate0", file), Path.Combine(gate0, file));
        File.Copy(ModulePath(), Path.Combine(evidence, "Gate0EvidenceContainment.psm1"));
        var rootIndex = Path.Combine(evidence, "root-index.json");
        File.Copy(PathInRepo("eng", "gate0", "evidence", "root-index.json"), rootIndex);
        ResetFutureRuns(rootIndex);
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
            initialRootIndexSha256 = "146936D12F54D0DC6D324F51330445E1B9F07C2C0DF13575F4EA0EB7C8643126",
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

    private static string ConfigureExactStage2AAuthorization((string TestRoot, string Gate0, string ArtifactRoot, string Source, string RootIndex, string Destination, string Shard, string WriterCommand) corpus, string status)
    {
        var repo = IsolatedRepoRoot(corpus);
        var expected = new (string Role, string Path)[]
        {
            ("owner-decision", "docs/gate-0-g0.5-stage2a-owner-decisions.md"),
            ("execution-owner-approval", "docs/gate-0-g0.5-stage2a-execution-approval.md"),
            ("replacement-warmup-approval", "docs/gate-0-g0.5-stage2a-replacement-warmup-approval.md"),
            ("replacement-activation-summary", "eng/gate0/g0.5-stage2a-replacement-activation-summary.json"),
            ("replacement-execution-block", "docs/gate-0-g0.5-stage2a-replacement-execution-block.md"),
            ("retained-path-repair-approval", "docs/gate-0-g0.5-stage2a-retained-path-repair-approval.md"),
            ("retained-path-restart-activation", "eng/gate0/g0.5-stage2a-retained-path-restart-activation-summary.json"),
            ("schedule", "eng/gate0/g0.5-stage2a-schedule.json"),
            ("runner", "eng/gate0/Invoke-G05Stage2AMatrix.ps1"),
            ("preflight", "eng/gate0/Test-G05Stage2AMatrixPreflight.ps1"),
            ("legacy-retention-validator", "eng/gate0/Test-Gate0ArtifactRetention.ps1"),
            ("helper", "eng/gate0/G05Stage2AMatrixHelpers.psm1"),
            ("semantic-executor", "eng/gate0/G05Stage2ASemanticExecutor.psm1"),
            ("semantic-helper", "eng/gate0/G05Stage2ASemanticHelpers.psm1"),
            ("smoke-helper", "eng/gate0/G05Stage2SmokeHelpers.psm1"),
            ("marker-helper", "eng/gate0/G05MarkerSurvivabilityHelpers.psm1"),
            ("runtime-validator", "eng/gate0/Validate-P2Runtime.ps1"),
            ("runtime-manifest", "eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json"),
            ("workload-contract", "eng/gate0/g0.5-stage2-workload-contract.json"),
            ("containment-contract", "eng/gate0/g0.5-stage2-containment-dry-run-contract.json"),
            ("audio-oracle-contract", "eng/gate0/g0.5-lossy-audio-oracle-contract.json"),
            ("audio-oracle-amendment", "eng/gate0/g0.5-lossy-audio-oracle-amendment-v4.json"),
            ("audio-oracle-amendment-freeze", "eng/gate0/g0.5-lossy-audio-oracle-amendment-v4-freeze.json"),
            ("structured-audio-control-summary", "eng/gate0/g0.5-structured-audio-control-result-summary.json"),
            ("structured-audio-control-retention-summary", "eng/gate0/g0.5-structured-audio-control-retention-result-summary.json"),
            ("replacement-smoke-authorization", "eng/gate0/g0.5-stage2-replacement-smoke-authorization-summary.json"),
            ("replacement-smoke-result", "eng/gate0/g0.5-stage2-replacement-smoke-result-summary.json"),
            ("retention-contract", "eng/gate0/g0.5-stage2a-retention-contract.json"),
            ("evidence-writer", "eng/gate0/Add-Gate0EvidenceShard.ps1"),
            ("evidence-containment", "eng/gate0/evidence/Gate0EvidenceContainment.psm1")
        };
        var bindings = new JsonArray();
        foreach (var (role, relativePath) in expected)
        {
            var source = PathInRepo(relativePath.Split('/'));
            var destination = Path.Combine(repo, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!File.Exists(destination)) File.Copy(source, destination);
            bindings.Add(new JsonObject { ["role"] = role, ["path"] = relativePath, ["sha256"] = Sha256(destination) });
        }
        var authorization = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["authorizationId"] = "Gate0.G05.Stage2A.ExecutionAuthorization.V1",
            ["status"] = status,
            ["exactCellCount"] = 18,
            ["exactAttemptCount"] = 108,
            ["bindings"] = bindings,
            ["limitations"] = new JsonArray("isolated no-media test")
        };
        var path = Path.Combine(corpus.Gate0, "g0.5-stage2a-execution-authorization.json");
        File.WriteAllText(path, authorization.ToJsonString());
        return path;
    }

    private static void PrepareRealisticCellSource((string TestRoot, string Gate0, string ArtifactRoot, string Source, string RootIndex, string Destination, string Shard, string WriterCommand) corpus)
    {
        Directory.Delete(corpus.Source, recursive: true);
        Directory.CreateDirectory(corpus.Source);
        Directory.CreateDirectory(Path.Combine(corpus.Source, "attempts"));
        for (var ordinal = 1; ordinal <= 17; ordinal++)
        {
            var path = Path.Combine(corpus.Source, "observations", $"observation-{ordinal}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, $"{{\"observation\":{ordinal},\"value\":\"synthetic\"}}");
        }
    }

    private static string CreateRealisticStage2AAttemptBindings((string TestRoot, string Gate0, string ArtifactRoot, string Source, string RootIndex, string Destination, string Shard, string WriterCommand) corpus, bool projectToRetainedNamespace)
    {
        var module = Path.Combine(corpus.Gate0, "G05Stage2ASemanticExecutor.psm1");
        var smokeModule = Path.Combine(corpus.Gate0, "G05Stage2SmokeHelpers.psm1");
        var bindingsPath = Path.Combine(corpus.Source, "attempt-bindings.json");
        var projection = projectToRetainedNamespace ? "$records=ConvertTo-G05Stage2ARetainedAttemptBindings @($records) 'future/stage2/run-a';" : string.Empty;
        var command = $"Import-Module '{PsQuote(smokeModule)}' -Force;Import-Module '{PsQuote(module)}' -Force;$source='{PsQuote(corpus.Source)}';$records=@();1..6|%{{$ordinal=$_;$attempt=[pscustomobject]@{{globalOrdinal=$ordinal;phase=if($ordinal-eq1){{'warmup'}}else{{'measured'}}}};$summary=[pscustomobject]@{{disposition='failed'}};$path=Join-Path $source ('attempts/attempt-'+$ordinal+'.json');$records+=,(New-G05Stage2AAttemptBinding $attempt $summary $path $source 'complete')}};{projection}$records|ConvertTo-Json -Depth 16|Set-Content -LiteralPath '{PsQuote(bindingsPath)}' -NoNewline";
        var result = RunPs(command);
        Assert.True(result.ExitCode == 0, result.Output);
        return bindingsPath;
    }

    private static string P2WriterCommand((string TestRoot, string Gate0, string ArtifactRoot, string Source, string RootIndex, string Destination, string Shard, string WriterCommand) corpus)
    {
        const string authorizationRelativePath = "eng/gate0/g0.5-stage2a-execution-authorization.json";
        var authorization = Path.Combine(IsolatedRepoRoot(corpus), authorizationRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var authorizationSha = Sha256(authorization);
        return corpus.WriterCommand.Replace("-EvidenceBoundary containment-no-media", "-EvidenceBoundary p2-runtime-route", StringComparison.Ordinal)
            .Replace("-ContractIdentity repository:contract", $"-ContractIdentity @('repository:contract','repository:{authorizationRelativePath}','sha256:{authorizationSha}')", StringComparison.Ordinal);
    }

    private static string Sha256(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static string IsolatedRepoRoot((string TestRoot, string Gate0, string ArtifactRoot, string Source, string RootIndex, string Destination, string Shard, string WriterCommand) corpus)
        => Directory.GetParent(Directory.GetParent(corpus.Gate0)!.FullName)!.FullName;

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
