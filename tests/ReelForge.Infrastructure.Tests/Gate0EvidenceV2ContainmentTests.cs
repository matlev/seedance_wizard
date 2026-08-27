using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0EvidenceV2ContainmentTests
{
    [Fact]
    public void V2RootBindsTheImmutableV1PredecessorWithoutMedia()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var v2 = File.ReadAllText(Path.Combine(root, "eng", "gate0", "evidence", "v2", "root-index.json"));
        Assert.Contains("C6D3CD9E7B0FC62E199E6FAD0A7D0FBAB6AFE1BBA6C8EFD4EAE427BBB79E30EA", v2, StringComparison.Ordinal);
        Assert.Contains("78538843", v2, StringComparison.Ordinal);
    }

    [Fact]
    public void V2WriterRefusesRuntimeRouteWithoutContinuationAuthorization()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var script = File.ReadAllText(Path.Combine(root, "eng", "gate0", "Add-Gate0EvidenceV2Shard.ps1"));
        Assert.Contains("V2 runtime-route append is blocked", script, StringComparison.Ordinal);
        Assert.Contains("future/stage2/v2/$ProofRunId", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("final-run")]
    [InlineData("final-entry")]
    public void V2RejectsAlteredImmutablePredecessor(string mutation)
    {
        var corpus = CreateCorpus();
        try
        {
            var text = File.ReadAllText(corpus.V1Root);
            text = mutation switch
            {
                "hash" => text.Replace("\"indexId\": \"Gate0.Stage2Evidence.Root.V1\"", "\"indexId\": \"tampered\""),
                "final-run" => text.Replace("stress-1080p-webm-one\"", "tampered-final\""),
                _ => text.Replace("29E21E5A56B0D82B25D914698E3CFA30EFC565F42345F436A26BCF8C1F97EB94", new string('F', 64))
            };
            File.WriteAllText(corpus.V1Root, text);
            var module = Path.Combine(Path.GetDirectoryName(corpus.V1Root)!, "Gate0EvidenceContainmentV2.psm1");
            var v2 = Path.Combine(Path.GetDirectoryName(corpus.V1Root)!, "v2", "root-index.json");
            var result = RunPs($"Import-Module '{Quote(module)}' -Force; Read-Gate0EvidenceV2RootIndex '{Quote(v2)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2ContractCarriesExactCapacityAndIsolationLimits()
    {
        var root = File.ReadAllText(Path.Combine(RepoRoot(), "eng", "gate0", "evidence", "v2", "root-index.json"));
        Assert.Contains("\"plannedContinuationCellShards\": 12", root, StringComparison.Ordinal);
        Assert.Contains("\"maxInfrastructureShards\": 2", root, StringComparison.Ordinal);
        Assert.Contains("\"globalRetentionCeilingBytes\": 805306368", root, StringComparison.Ordinal);
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "eng", "gate0", "evidence", "Gate0EvidenceContainmentV2.psm1"));
        Assert.Contains("future/stage2/v2/", module, StringComparison.Ordinal);
        Assert.Contains("prohibited machine-local", module, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("root-path", "eng/gate0/evidence/root-index.json", "eng/gate0/evidence/other.json")]
    [InlineData("root-index", "Gate0.Stage2Evidence.Root.V1", "Other.Root")]
    [InlineData("shard-cap", "\"maxShardBytes\": 65536", "\"maxShardBytes\": 65535")]
    [InlineData("compact-cap", "\"maxCompactAttemptBytes\": 262144", "\"maxCompactAttemptBytes\": 262143")]
    [InlineData("cell-cap", "\"plannedContinuationCellShards\": 12", "\"plannedContinuationCellShards\": 13")]
    public void V2RejectsMutatedRootContractFields(string _, string expected, string replacement)
    {
        var corpus = CreateCorpus();
        try
        {
            var v2 = Path.Combine(Path.GetDirectoryName(corpus.V1Root)!, "v2", "root-index.json");
            File.WriteAllText(v2, File.ReadAllText(v2).Replace(expected, replacement, StringComparison.Ordinal));
            var module = Path.Combine(Path.GetDirectoryName(corpus.V1Root)!, "Gate0EvidenceContainmentV2.psm1");
            Assert.NotEqual(0, RunPs($"Import-Module '{Quote(module)}' -Force; Read-Gate0EvidenceV2RootIndex '{Quote(v2)}' | Out-Null").ExitCode);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Theory]
    [InlineData("C:\\Users\\owner\\secret")]
    [InlineData("https://example.invalid/?sig=secret")]
    [InlineData("Authorization: Bearer secret")]
    public void V2RejectsUnsafeMetadataBeforeJournal(string provenance)
    {
        var corpus = CreateCorpus();
        try
        {
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId unsafe -EvidenceGroupId group -CellId unsafe -ContractIdentity repository:contract -Provenance '{Quote(provenance)}' -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest");
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            Assert.DoesNotContain(Directory.EnumerateFiles(corpus.Artifact, "*", SearchOption.AllDirectories)
                .SelectMany(File.ReadLines), line => line.Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2RejectsOversizeShardAndRootBeforeSchemaExpansion()
    {
        var corpus = CreateCorpus();
        try
        {
            var shard = Path.Combine(corpus.Root, "oversize.manifest.json");
            File.WriteAllText(shard, new string('x', 65_537));
            var module = Path.Combine(Path.GetDirectoryName(corpus.V1Root)!, "Gate0EvidenceContainmentV2.psm1");
            Assert.NotEqual(0, RunPs($"Import-Module '{Quote(module)}' -Force; Read-Gate0EvidenceV2Shard '{Quote(shard)}' | Out-Null").ExitCode);

            var v2 = Path.Combine(Path.GetDirectoryName(corpus.V1Root)!, "v2", "root-index.json");
            File.AppendAllText(v2, string.Concat(Enumerable.Repeat("\n", 401)));
            Assert.NotEqual(0, RunPs($"Import-Module '{Quote(module)}' -Force; Read-Gate0EvidenceV2RootIndex '{Quote(v2)}' | Out-Null").ExitCode);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2WriterUsesOnlyItsOwnNamespaceAndLeavesV1Unchanged()
    {
        var corpus = CreateCorpus();
        try
        {
            var before = File.ReadAllText(corpus.V1Root);
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId v2-run -EvidenceGroupId v2-group -CellId v2-cell -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest");
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Equal(before, File.ReadAllText(corpus.V1Root));
            Assert.True(File.Exists(Path.Combine(corpus.Artifact, "future", "stage2", "v2", "v2-run", "proof.txt")));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Theory]
    [InlineData("BeforeRemoteVerification")]
    [InlineData("AfterRemoteVerification")]
    [InlineData("AfterPayloadMove")]
    [InlineData("AfterShardMove")]
    public void V2FaultsBeforeRootPreserveJournalAndV1(string fault)
    {
        var corpus = CreateCorpus();
        try
        {
            var before = File.ReadAllText(corpus.V1Root);
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId v2-{fault} -EvidenceGroupId group -CellId cell-{fault} -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest -FaultInjection {fault}");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal(before, File.ReadAllText(corpus.V1Root));
            Assert.True(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2AcceptedRootFaultRetainsJournalUntilDedicatedRecovery()
    {
        var corpus = CreateCorpus();
        try
        {
            var command = $"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId v2-root -EvidenceGroupId group -CellId cell-root -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest -FaultInjection AfterRootReplacement";
            Assert.NotEqual(0, RunPs(command).ExitCode);
            Assert.True(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            var recovery = Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "Resolve-Gate0EvidenceV2AppendJournal.ps1");
            var result = RunPs($"& '{Quote(recovery)}' -ArtifactRoot '{Quote(corpus.Artifact)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("explicit independent remote verification", result.Output, StringComparison.Ordinal);
            Assert.True(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2PreparedJournalHasNoRemoteReceiptOrFinalRootClaims()
    {
        var corpus = CreateCorpus();
        try
        {
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId prepared -EvidenceGroupId group -CellId prepared -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest -FaultInjection BeforeRemoteVerification");
            Assert.NotEqual(0, result.ExitCode);
            var journal = JsonNode.Parse(File.ReadAllText(corpus.Artifact + ".stage2-v2-append-journal.json"))!.AsObject();
            Assert.Equal("prepared", journal["phase"]!.GetValue<string>());
            Assert.False(journal.ContainsKey("artifacts"));
            Assert.False(journal.ContainsKey("shardSha256"));
            Assert.False(journal.ContainsKey("candidateRootIndexSha256"));
            Assert.DoesNotContain("remotelyVerifiedUtc", journal.ToJsonString(), StringComparison.Ordinal);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2PreparedRecoveryRefusesAnUnindexedStagingFile()
    {
        var corpus = CreateCorpus();
        try
        {
            Assert.NotEqual(0, RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId prepared-extra -EvidenceGroupId group -CellId prepared-extra -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest -FaultInjection BeforeRemoteVerification").ExitCode);
            var journalPath = corpus.Artifact + ".stage2-v2-append-journal.json";
            var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
            var staging = Path.Combine(Path.GetDirectoryName(corpus.Artifact)!, journal["stagingDirectoryName"]!.GetValue<string>());
            File.WriteAllText(Path.Combine(staging, "unindexed.txt"), "unexpected");
            var recovery = Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "Resolve-Gate0EvidenceV2AppendJournal.ps1");
            var result = RunPs($"& '{Quote(recovery)}' -ArtifactRoot '{Quote(corpus.Artifact)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("unindexed", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(journalPath));
            Assert.True(File.Exists(Path.Combine(staging, "unindexed.txt")));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2RemoteRecoveryRefusesAJournalThatOmitsAShardArtifact()
    {
        var corpus = CreateCorpus();
        try
        {
            File.WriteAllText(Path.Combine(corpus.Source, "second.txt"), "second");
            Assert.NotEqual(0, RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId truncated -EvidenceGroupId group -CellId truncated -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest -FaultInjection AfterShardMove").ExitCode);
            var journalPath = corpus.Artifact + ".stage2-v2-append-journal.json";
            var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
            var staged = journal["stagedArtifacts"]!.AsArray();
            var receipts = journal["artifacts"]!.AsArray();
            staged.RemoveAt(staged.Count - 1);
            receipts.RemoveAt(receipts.Count - 1);
            journal["artifactCount"] = staged.Count;
            journal["artifactBytes"] = staged.Sum(node => node!["byteSize"]!.GetValue<long>());
            File.WriteAllText(journalPath, journal.ToJsonString());
            var recovery = Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "Resolve-Gate0EvidenceV2AppendJournal.ps1");
            var result = RunPs($"& '{Quote(recovery)}' -ArtifactRoot '{Quote(corpus.Artifact)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("counts differ", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(journalPath));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2WriterDoesNotDeleteAPreexistingRecoveryJournal()
    {
        var corpus = CreateCorpus();
        try
        {
            var journalPath = corpus.Artifact + ".stage2-v2-append-journal.json";
            File.WriteAllText(journalPath, "owner-review-required");
            var result = RunAppend(corpus, "existing-journal", "containment-no-media", "");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("owner-review-required", File.ReadAllText(journalPath));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2RecoveryRejectsAReparsePointLockBeforeOpeningIt()
    {
        var corpus = CreateCorpus();
        var lockPath = corpus.Artifact + ".stage2-v2-append-lock";
        try
        {
            var target = Path.Combine(corpus.Root, "lock-target"); Directory.CreateDirectory(target);
            var linked = RunPs($"New-Item -ItemType Junction -Path '{Quote(lockPath)}' -Target '{Quote(target)}' | Out-Null");
            Assert.Equal(0, linked.ExitCode);
            var recovery = Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "Resolve-Gate0EvidenceV2AppendJournal.ps1");
            var result = RunPs($"& '{Quote(recovery)}' -ArtifactRoot '{Quote(corpus.Artifact)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("lock is a reparse point", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(lockPath)) Directory.Delete(lockPath);
            Directory.Delete(corpus.Root, true);
        }
    }

    [Fact]
    public void V2RecoveryRefusesAnIntermediatePayloadJunctionWithoutDeletingBytes()
    {
        var corpus = CreateCorpus();
        var stage2 = Path.Combine(corpus.Artifact, "future", "stage2");
        var displaced = Path.Combine(corpus.Root, "displaced-stage2");
        var junctionCreated = false;
        try
        {
            Assert.NotEqual(0, RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId junction -EvidenceGroupId group -CellId junction -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest -FaultInjection AfterPayloadMove").ExitCode);
            Directory.Move(stage2, displaced);
            var linked = RunPs($"New-Item -ItemType Junction -Path '{Quote(stage2)}' -Target '{Quote(displaced)}' | Out-Null");
            Assert.Equal(0, linked.ExitCode); junctionCreated = true;
            var marker = Path.Combine(displaced, "v2", "junction", "proof.txt");
            var recovery = Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "Resolve-Gate0EvidenceV2AppendJournal.ps1");
            var result = RunPs($"& '{Quote(recovery)}' -ArtifactRoot '{Quote(corpus.Artifact)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse point", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(marker));
            Assert.True(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
        }
        finally
        {
            if (junctionCreated && Directory.Exists(stage2)) Directory.Delete(stage2);
            if (Directory.Exists(displaced) && !Directory.Exists(stage2)) Directory.Move(displaced, stage2);
            Directory.Delete(corpus.Root, true);
        }
    }

    [Theory]
    [InlineData("containment-no-media", "stage2a-continuation-cell")]
    [InlineData("p2-runtime-route", "infrastructure")]
    public void V2RejectsRootRunKindThatMisclassifiesTheBoundShard(string boundary, string forgedRunKind)
    {
        var corpus = CreateCorpus();
        try
        {
            var id = boundary == "containment-no-media" ? "kind-infra" : "kind-cell";
            var authorization = boundary == "containment-no-media" ? "" : CreateContinuationInputs(corpus, id);
            Assert.Equal(0, RunAppend(corpus, id, boundary, authorization).ExitCode);
            var gate = Path.GetDirectoryName(corpus.Writer)!;
            var module = Path.Combine(gate, "evidence", "Gate0EvidenceContainmentV2.psm1");
            var root = Path.Combine(gate, "evidence", "v2", "root-index.json");
            var command = $"Import-Module '{Quote(module)}' -Force; $p='{Quote(root)}'; $r=Get-Content -LiteralPath $p -Raw|ConvertFrom-Json -Depth 64; $r.runs[0].runKind='{forgedRunKind}'; $r.runs[0].entrySha256=Get-Gate0EvidenceV2EntryHash $r.runs[0]; [IO.File]::WriteAllText($p,(ConvertTo-Json $r -Depth 64)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false)); Read-Gate0EvidenceV2RootIndex $p|Out-Null";
            var result = RunPs(command);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("root to shard binding", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2AllowsASourceSubdirectoryOnlyWithinAnExplicitApprovedSiblingRoot()
    {
        var corpus = CreateCorpus();
        try
        {
            var nested = Path.Combine(corpus.Source, "proof-unit"); Directory.CreateDirectory(nested); File.WriteAllText(Path.Combine(nested, "nested.txt"), "nested");
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(nested)}' -ApprovedSourceRoot '{Quote(corpus.Source)}' -ProofRunId nested-source -EvidenceGroupId group -CellId nested-source -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest");
            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(corpus.Artifact, "future", "stage2", "v2", "nested-source", "nested.txt")));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2RejectsASourceOutsideTheExplicitApprovedSiblingRoot()
    {
        var corpus = CreateCorpus();
        try
        {
            var other = Path.Combine(corpus.Root, "other-source"); Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "outside.txt"), "outside");
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(other)}' -ApprovedSourceRoot '{Quote(corpus.Source)}' -ProofRunId escaped-source -EvidenceGroupId group -CellId escaped-source -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("escaped the approved source root", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("artifact")]
    public void V2RejectsRepositoryAndArtifactRootsAsApprovedSourceBoundaries(string alias)
    {
        var corpus = CreateCorpus();
        try
        {
            var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "..", ".."));
            var boundary = alias == "repository" ? repository : corpus.Artifact;
            var source = alias == "repository" ? corpus.Source : corpus.Artifact;
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(source)}' -ApprovedSourceRoot '{Quote(boundary)}' -ProofRunId forbidden-boundary -EvidenceGroupId group -CellId forbidden-boundary -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("distinct from the repository and artifact root", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            Assert.False(Directory.Exists(Path.Combine(corpus.Artifact, "future")));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2RejectsAnOversizeCandidateShardBeforeJournalOrPayloadCommit()
    {
        var corpus = CreateCorpus();
        try
        {
            for (var index = 0; index < 40; index++) File.WriteAllText(Path.Combine(corpus.Source, $"artifact-{index:D2}.txt"), index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var result = RunAppend(corpus, "oversize-candidate", "containment-no-media", "");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("before journal or remote work", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            Assert.False(Directory.Exists(Path.Combine(corpus.Artifact, "future")));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2EnforcesExactlyTwoInfrastructureAndTwelveAuthorizedContinuationCellsBeforeJournal()
    {
        var corpus = CreateCorpus();
        try
        {
            for (var i = 1; i <= 2; i++) Assert.Equal(0, RunAppend(corpus, $"infra-{i}", "containment-no-media", "").ExitCode);
            var third = RunAppend(corpus, "infra-3", "containment-no-media", "");
            Assert.NotEqual(0, third.ExitCode); Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            for (var i = 1; i <= 12; i++)
            {
                var arguments = CreateContinuationInputs(corpus, $"cell-{i}");
                Assert.Equal(0, RunAppend(corpus, $"cell-{i}", "p2-runtime-route", arguments).ExitCode);
            }
            var thirteenth = RunAppend(corpus, "cell-13", "p2-runtime-route", CreateContinuationInputs(corpus, "cell-13"));
            Assert.NotEqual(0, thirteenth.ExitCode); Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2AllowsDistinctLogicalFilesWithTheSameBytes()
    {
        var corpus = CreateCorpus();
        try
        {
            File.WriteAllText(Path.Combine(corpus.Source, "same-a.txt"), "same");
            File.WriteAllText(Path.Combine(corpus.Source, "same-b.txt"), "same");
            var result = RunAppend(corpus, "same-bytes", "containment-no-media", "");
            Assert.Equal(0, result.ExitCode);
            var root = Path.Combine(corpus.Artifact, "future", "stage2", "v2", "same-bytes");
            Assert.True(File.Exists(Path.Combine(root, "same-a.txt")));
            Assert.True(File.Exists(Path.Combine(root, "same-b.txt")));
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2LiveContinuationIsFailClosedBeforeTrackedContractsExist()
    {
        var corpus = CreateCorpus();
        try
        {
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId live -EvidenceGroupId group -CellId live -EvidenceBoundary p2-runtime-route -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("fixed tracked continuation authorization", result.Output, StringComparison.Ordinal);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Fact]
    public void V2ValidatorRejectsAnUnindexedPhysicalArtifact()
    {
        var corpus = CreateCorpus();
        try
        {
            Assert.Equal(0, RunAppend(corpus, "closure", "containment-no-media", "").ExitCode);
            var extra = Path.Combine(corpus.Artifact, "future", "stage2", "v2", "closure", "unindexed.txt");
            File.WriteAllText(extra, "unexpected");
            var validator = Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "Test-Gate0EvidenceV2Containment.ps1");
            var result = RunPs($"& '{Quote(validator)}' -ArtifactRoot '{Quote(corpus.Artifact)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("unindexed", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    private static (string Root, string V1Root, string Artifact, string Source, string Writer) CreateCorpus()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-V2-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        var gate = Path.Combine(repo, "eng", "gate0");
        Directory.CreateDirectory(Path.Combine(gate, "evidence", "v2"));
        File.WriteAllText(Path.Combine(repo, ".gate0-containment-test-sentinel"), "isolated");
        foreach (var file in new[] { "Add-Gate0EvidenceV2Shard.ps1", "Resolve-Gate0EvidenceV2AppendJournal.ps1", "Test-Gate0EvidenceV2Containment.ps1", "Gate0ArtifactTools.psm1", "Gate0ArtifactR2Client.cs" }) File.Copy(Path.Combine(RepoRoot(), "eng", "gate0", file), Path.Combine(gate, file));
        File.Copy(Path.Combine(RepoRoot(), "eng", "gate0", "evidence", "Gate0EvidenceContainmentV2.psm1"), Path.Combine(gate, "evidence", "Gate0EvidenceContainmentV2.psm1"));
        var v1 = Path.Combine(gate, "evidence", "root-index.json");
        File.Copy(Path.Combine(RepoRoot(), "eng", "gate0", "evidence", "root-index.json"), v1);
        var v2RootPath = Path.Combine(gate, "evidence", "v2", "root-index.json");
        File.Copy(Path.Combine(RepoRoot(), "eng", "gate0", "evidence", "v2", "root-index.json"), v2RootPath);
        var v2Root = JsonNode.Parse(File.ReadAllText(v2RootPath))!.AsObject();
        v2Root["runs"] = new JsonArray();
        v2Root["totals"] = new JsonObject
        {
            ["runCount"] = 0,
            ["logicalArtifactCount"] = 0,
            ["logicalArtifactBytes"] = 0
        };
        File.WriteAllText(v2RootPath, v2Root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        var artifact = Path.Combine(root, "ReelForge.Gate0Artifacts"); Directory.CreateDirectory(artifact);
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "proof.txt"), "proof");
        return (root, v1, artifact, source, Path.Combine(gate, "Add-Gate0EvidenceV2Shard.ps1"));
    }

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static (int ExitCode, string Output) RunAppend((string Root, string V1Root, string Artifact, string Source, string Writer) corpus, string id, string boundary, string authorization) =>
        RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId {id} -EvidenceGroupId group-{id} -CellId {id} -EvidenceBoundary {boundary} -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -SkipRemoteForIsolatedTest{authorization}");

    private static string CreateContinuationInputs((string Root, string V1Root, string Artifact, string Source, string Writer) corpus, string id)
    {
        var authorization = Path.Combine(corpus.Root, id + ".authorization.json");
        File.WriteAllText(authorization, $$"""{"schemaVersion":1,"authorizationId":"Gate0.Stage2Evidence.V2.ContinuationAuthorization.V1","authorizationScope":"owner-authorized-v2-continuation","continuationProofRunIds":["{{id}}"],"limitations":["isolated test only"]}""");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("proof")));
        var attempts = Path.Combine(corpus.Root, id + ".attempts.json");
        File.WriteAllText(attempts, $$"""[{"attemptId":"new-{{id}}","originalAttemptId":"old-{{id}}","phase":"warmup","ordinal":1,"retentionClass":"compact","recordPath":"future/stage2/v2/{{id}}/proof.txt","recordSha256":"{{hash}}","disposition":"passed","completeClosureReference":"closure-{{id}}"}]""");
        return $" -ContinuationAuthorizationPath '{Quote(authorization)}' -AttemptsPath '{Quote(attempts)}'";
    }

    private static (int ExitCode, string Output) RunPs(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("pwsh", $"-NoProfile -ExecutionPolicy Bypass -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }
    private static string Quote(string path) => path.Replace("'", "''");
}
