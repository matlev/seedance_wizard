using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0EvidenceV2ContainmentTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly string[] TestOnlyLimitations = ["test-only authorization"];

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

    [Theory]
    [InlineData("missing-writer-authorization")]
    [InlineData("tampered-writer-authorization")]
    [InlineData("wrong-writer-proof")]
    [InlineData("missing-writer-proof")]
    [InlineData("extra-writer-proof")]
    [InlineData("duplicate-writer-proof")]
    [InlineData("wrong-full-status")]
    [InlineData("tampered-schedule")]
    public void V2LiveContinuationAuthorizationRejectsInvalidTrackedBindingsBeforeRemoteOrMedia(string mutation)
    {
        var corpus = CreateLiveAuthorizationCorpus();
        try
        {
            var proof = corpus.ProofIds[0];
            var cellId = corpus.CellsByProof[proof];
            switch (mutation)
            {
                case "missing-writer-authorization":
                    File.Delete(corpus.WriterAuthorization);
                    break;
                case "tampered-writer-authorization":
                    File.AppendAllText(corpus.WriterAuthorization, " ");
                    break;
                case "wrong-writer-proof":
                    WriteWriterAuthorization(corpus.WriterAuthorization, corpus.ProofIds.Skip(1).Append("not-the-requested-proof"));
                    WriteFullAuthorization(corpus);
                    break;
                case "missing-writer-proof":
                    WriteWriterAuthorization(corpus.WriterAuthorization, corpus.ProofIds.Take(11));
                    WriteFullAuthorization(corpus);
                    break;
                case "extra-writer-proof":
                    WriteWriterAuthorization(corpus.WriterAuthorization, corpus.ProofIds.Append("not-an-approved-proof"));
                    WriteFullAuthorization(corpus);
                    break;
                case "duplicate-writer-proof":
                    WriteWriterAuthorization(corpus.WriterAuthorization, corpus.ProofIds.Take(11).Append(corpus.ProofIds[0]));
                    WriteFullAuthorization(corpus);
                    break;
                case "wrong-full-status":
                    var authorization = JsonNode.Parse(File.ReadAllText(corpus.FullAuthorization))!.AsObject();
                    authorization["status"] = "owner-authorized-continuation-pending";
                    File.WriteAllText(corpus.FullAuthorization, authorization.ToJsonString(IndentedJson) + "\n");
                    break;
                case "tampered-schedule":
                    File.AppendAllText(corpus.Schedule, " ");
                    break;
            }

            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId '{proof}' -EvidenceGroupId g05-stage2a-continuation-20260827 -CellId '{cellId}' -EvidenceBoundary p2-runtime-route -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer");
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            Assert.False(Directory.Exists(Path.Combine(corpus.Artifact, "future")));
            Assert.DoesNotContain("ffmpeg", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ReelForge.Engineering.R2", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(corpus.Root, true); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("reordered")]
    [InlineData("duplicate")]
    [InlineData("mismatched")]
    public void V2LiveContinuationRejectsNonExactScheduledAttemptBindingsBeforeStagingOrRemote(string mutation)
    {
        var corpus = CreateLiveAuthorizationCorpus();
        try
        {
            var proof = corpus.ProofIds[0];
            var attempts = Path.Combine(corpus.Root, "attempts.json");
            WriteLiveAttempts(corpus, proof, attempts, mutation);
            var result = RunPs($"& '{Quote(corpus.Writer)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -SourceRoot '{Quote(corpus.Source)}' -ProofRunId '{proof}' -EvidenceGroupId g05-stage2a-continuation-20260827 -CellId '{corpus.CellsByProof[proof]}' -EvidenceBoundary p2-runtime-route -ContractIdentity repository:contract -Provenance test -ProducerRuntimeIdentity repository:producer -AttemptsPath '{Quote(attempts)}'");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Live V2 continuation attempt bindings", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(corpus.Artifact + ".stage2-v2-append-journal.json"));
            Assert.False(Directory.Exists(Path.Combine(corpus.Artifact, "future")));
            Assert.DoesNotContain("ffmpeg", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ReelForge.Engineering.R2", result.Output, StringComparison.OrdinalIgnoreCase);
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

    private static (string Root, string Artifact, string Source, string Writer, string WriterAuthorization, string FullAuthorization, string Schedule, string[] ProofIds, Dictionary<string, string> CellsByProof) CreateLiveAuthorizationCorpus()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-V2-live-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repo");
        var gate0 = Path.Combine(repository, "eng", "gate0");
        Directory.CreateDirectory(gate0);
        foreach (var path in new[]
        {
            "docs/gate-0-g0.5-stage2a-continuation-approval.md",
            "eng/gate0/g0.5-stage2a-continuation-schedule.json",
            "eng/gate0/G05Stage2AContinuationHelpers.psm1",
            "eng/gate0/Invoke-G05Stage2AContinuation.ps1",
            "eng/gate0/Test-G05Stage2AContinuationPreflight.ps1",
            "eng/gate0/Add-Gate0EvidenceV2Shard.ps1",
            "eng/gate0/Gate0ArtifactTools.psm1",
            "eng/gate0/Gate0ArtifactR2Client.cs",
            "eng/gate0/evidence/Gate0EvidenceContainmentV2.psm1",
            "eng/gate0/Test-Gate0EvidenceV2Containment.ps1",
            "eng/gate0/g0.5-stage2-workload-contract.json",
            "eng/gate0/g0.5-stage2a-retention-contract.json",
            "eng/gate0/g0.5-lossy-audio-oracle-amendment-v5.json",
            "eng/gate0/g0.5-lossy-audio-oracle-amendment-v5-freeze.json",
            "eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-authorization.json",
            "eng/gate0/g0.5-stage2a-v5-retained-output-reevaluation-result-summary.json",
            "eng/gate0/G05Stage2AV5AudioOracle.psm1",
            "eng/gate0/G05Stage2AV5FreezeValidation.psm1",
            "eng/gate0/G05Stage2ASemanticExecutor.psm1",
            "eng/gate0/G05Stage2ASemanticHelpers.psm1",
            "eng/gate0/G05Stage2SmokeHelpers.psm1",
            "eng/gate0/G05MarkerSurvivabilityHelpers.psm1",
            "eng/gate0/Validate-P2Runtime.ps1",
            "eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json",
            "eng/gate0/fixture-source-inventory.json",
            "eng/gate0/artifact-manifest.json",
            "eng/gate0/g0.5-stage2a-schedule.json",
            "eng/gate0/Test-Gate0EvidenceContainment.ps1",
            "eng/gate0/Test-Gate0ArtifactRetention.ps1",
            "eng/gate0/Test-Gate0ArtifactManifest.ps1",
            "eng/gate0/evidence/Gate0EvidenceContainment.psm1"
        })
        {
            var source = Path.Combine(RepoRoot(), path.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(repository, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }

        var schedule = Path.Combine(gate0, "g0.5-stage2a-continuation-schedule.json");
        using var scheduleDocument = JsonDocument.Parse(File.ReadAllText(schedule));
        var attempts = scheduleDocument.RootElement.GetProperty("attempts").EnumerateArray().ToArray();
        var cellsByProof = attempts.GroupBy(static attempt => attempt.GetProperty("proofRunId").GetString()!).ToDictionary(
            static group => group.Key,
            static group => group.First().GetProperty("cellId").GetString()!);
        var proofIds = cellsByProof.Keys.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var writerAuthorization = Path.Combine(gate0, "g0.5-stage2a-continuation-v2-writer-authorization.json");
        var fullAuthorization = Path.Combine(gate0, "g0.5-stage2a-continuation-authorization.json");
        WriteWriterAuthorization(writerAuthorization, proofIds);
        var corpus = (root, Path.Combine(root, "ReelForge.Gate0Artifacts"), Path.Combine(root, "source"), Path.Combine(gate0, "Add-Gate0EvidenceV2Shard.ps1"), writerAuthorization, fullAuthorization, schedule, proofIds, cellsByProof);
        Directory.CreateDirectory(corpus.Item2);
        Directory.CreateDirectory(corpus.Item3);
        WriteFullAuthorization(corpus);
        return corpus;
    }

    private static void WriteWriterAuthorization(string path, IEnumerable<string> proofIds) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            authorizationId = "Gate0.Stage2Evidence.V2.ContinuationAuthorization.V1",
            authorizationScope = "owner-authorized-v2-continuation",
            continuationProofRunIds = proofIds.ToArray(),
            limitations = TestOnlyLimitations
        }, IndentedJson) + "\n");

    private static void WriteFullAuthorization((string Root, string Artifact, string Source, string Writer, string WriterAuthorization, string FullAuthorization, string Schedule, string[] ProofIds, Dictionary<string, string> CellsByProof) corpus)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner-approval"] = "docs/gate-0-g0.5-stage2a-continuation-approval.md",
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
            ["r2-client-source"] = "eng/gate0/Gate0ArtifactR2Client.cs"
        };
        var repository = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(corpus.Writer)!, "..", ".."));
        var bindings = paths.Select(pair => new { role = pair.Key, path = pair.Value, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository, pair.Value.Replace('/', Path.DirectorySeparatorChar))))) }).ToArray();
        File.WriteAllText(corpus.FullAuthorization, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            authorizationId = "Gate0.G05.Stage2A.ContinuationAuthorization.V1",
            authorizationScope = "owner-authorized-stage2a-continuation",
            status = "owner-authorized-continuation-effective",
            exactCellCount = 12,
            exactAttemptCount = 72,
            scheduleBinding = new { path = "eng/gate0/g0.5-stage2a-continuation-schedule.json", sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corpus.Schedule))) },
            bindings,
            continuationProofRunIds = corpus.ProofIds,
            limitations = TestOnlyLimitations
        }, IndentedJson) + "\n");
    }

    private static void WriteLiveAttempts((string Root, string Artifact, string Source, string Writer, string WriterAuthorization, string FullAuthorization, string Schedule, string[] ProofIds, Dictionary<string, string> CellsByProof) corpus, string proofRunId, string path, string mutation)
    {
        using var schedule = JsonDocument.Parse(File.ReadAllText(corpus.Schedule));
        var attempts = schedule.RootElement.GetProperty("attempts").EnumerateArray()
            .Where(attempt => attempt.GetProperty("proofRunId").GetString() == proofRunId)
            .OrderBy(attempt => attempt.GetProperty("continuationOrdinal").GetInt32())
            .Select(attempt => new JsonObject
            {
                ["attemptId"] = "stage2a-continuation-" + attempt.GetProperty("globalOrdinal").GetInt32(),
                ["originalAttemptId"] = "stage2a-" + attempt.GetProperty("originalScheduleOrdinal").GetInt32(),
                ["phase"] = attempt.GetProperty("phase").GetString(),
                ["ordinal"] = attempt.GetProperty("globalOrdinal").GetInt32(),
                ["retentionClass"] = "complete",
                ["recordPath"] = "unreached-attempt.json",
                ["recordSha256"] = new string('0', 64),
                ["disposition"] = "passed",
                ["completeClosureReference"] = null
            })
            .ToList();
        switch (mutation)
        {
            case "missing": attempts.RemoveAt(attempts.Count - 1); break;
            case "extra": attempts.Add(attempts[0].DeepClone().AsObject()); break;
            case "reordered": attempts.Reverse(); break;
            case "duplicate": attempts[1]["attemptId"] = attempts[0]["attemptId"]!.GetValue<string>(); break;
            case "mismatched": attempts[0]["originalAttemptId"] = "stage2a-not-the-frozen-row"; break;
        }
        File.WriteAllText(path, new JsonArray(attempts.Select(static attempt => (JsonNode?)attempt).ToArray()).ToJsonString(IndentedJson) + "\n");
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
