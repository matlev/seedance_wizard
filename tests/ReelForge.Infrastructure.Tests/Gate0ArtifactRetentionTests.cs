using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0ArtifactRetentionTests
{
    [Fact]
    public void ManifestDefinesTheVerifiedInterimCorpusWithoutMachinePaths()
    {
        var manifestPath = PathInRepo("eng", "gate0", "artifact-retention-manifest.json");
        var text = File.ReadAllText(manifestPath);
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Gate0.InterimCorpus.20260825", root.GetProperty("artifactSetId").GetString());
        Assert.DoesNotMatch(@"[A-Za-z]:\\", text);
        Assert.DoesNotContain("AppData", text, StringComparison.OrdinalIgnoreCase);

        var storage = root.GetProperty("storage");
        Assert.Equal("ReelForge.Gate0Artifacts", storage.GetProperty("rootName").GetString());
        Assert.Equal("local-working-copy-r2-retention-pending", storage.GetProperty("classification").GetString());
        Assert.False(storage.GetProperty("productionArtifactRepository").GetBoolean());
        Assert.False(storage.GetProperty("hostedCiEligible").GetBoolean());
        Assert.False(storage.GetProperty("separatelyBackedUpPrivateCopyVerified").GetBoolean());
        Assert.Equal("incomplete", storage.GetProperty("twoCopyRetentionCondition").GetString());
        Assert.False(storage.GetProperty("temporaryProviderR2Permitted").GetBoolean());

        var groups = root.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(34, groups.Length);
        Assert.Equal(
            [
                "P2.BtbnLgplShared.WindowsX64.20260820",
                "Gate0.Fixtures.F1-F8.20260824",
                "Gate0.G04.Input.Corrected.20260825",
                "P3.LibjpegTurboCjpeg.WindowsX64.3.2.0",
                "Gate0.RepositoryContracts.20260825",
                "Gate0.G04.F7.Setts.20260825",
                "Gate0.G04.P3.JpegInput.20260825",
                "Gate0.G05.Calibration.20260826T000415244Z.4ABB046B",
                "Gate0.G05.Calibration.20260826T000915709Z.AC4E549E",
                "Gate0.G05.Calibration.20260826T001250746Z.1F7B911B",
                "Gate0.G05.Calibration.20260826T001904417Z.AC1E2691",
                "Gate0.G05.Calibration.20260826T004152697Z.FDABDAA2",
                "Gate0.G05.LossyAudioOracle.V3.20260826",
                "Gate0.G05.RetainedAudioOracle.20260826.Preflight",
                "Gate0.G05.RetainedAudioOracle.20260826.ControlsPreflight",
                "Gate0.G05.RetainedAudioOracle.20260826.EmptyArguments",
                "Gate0.G05.RetainedAudioOracle.20260826.CombinedParser",
                "Gate0.G05.RetainedAudioOracle.20260826.PtsEndpoint",
                "Gate0.G05.RetainedAudioOracle.20260826.Final",
                "Gate0.G05.MarkerAtlas.20260826",
                "Gate0.G05.MarkerSurvivability.20260826.ExpectationPreflight",
                "Gate0.G05.MarkerSurvivability.20260826.FreeSpacePreflight",
                "Gate0.G05.MarkerSurvivability.20260826.AudioTimingSuperseded",
                "Gate0.G05.MarkerSurvivability.20260826.Final",
                "Gate0.G05.WpfMeasurementAdapter.NoMedia.20260826",
                "Gate0.G05.WpfMeasurementAdapter.NoMedia.20260826.Final",
                "Gate0.G05.Stage2SmokePreflight.20260826.NativeProbeBlocked",
                "Gate0.G05.Stage2SmokePreflight.20260826.AuthoritativePass",
                "Gate0.G05.Stage2SmokePreflight.20260826.StaleDurableBindingBlocked",
                "Gate0.G05.Stage2SmokePreflight.20260826.NullCpuSuperseded",
                "Gate0.G05.Stage2PreMatrixSmoke.20260827T002126507Z.1BD6749C",
                "G05-V4-Structured-Audio-Controls-20260827-001",
                "Gate0.G05.Stage2ReplacementSmokePreflight.20260827.AuthoritativePass",
                "Gate0.G05.Stage2PreMatrixSmoke.20260827T073620244Z.F2D80483",
            ],
            groups.Select(group => group.GetProperty("groupId").GetString()));

        var files = groups.SelectMany(group => group.GetProperty("files").EnumerateArray()).ToArray();
        Assert.Equal(4101, files.Length);
        Assert.Equal(1121540509, files.Sum(file => file.GetProperty("size").GetInt64()));
        Assert.Equal(files.Length, files.Select(file => file.GetProperty("artifactId").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(files.Length, files.Select(file => file.GetProperty("filename").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(files, file =>
        {
            var filename = file.GetProperty("filename").GetString()!;
            Assert.False(Path.IsPathRooted(filename));
            Assert.DoesNotContain('\\', filename);
            Assert.DoesNotContain("..", filename.Split('/'));
            Assert.True(file.GetProperty("size").GetInt64() >= 0);
            Assert.Matches("^[A-F0-9]{64}$", file.GetProperty("sha256").GetString()!);
        });

        var totals = root.GetProperty("totals");
        Assert.Equal(groups.Length, totals.GetProperty("groupCount").GetInt32());
        Assert.Equal(files.Length, totals.GetProperty("fileCount").GetInt32());
        Assert.Equal(files.Sum(file => file.GetProperty("size").GetInt64()), totals.GetProperty("totalBytes").GetInt64());

        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "p2/ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-shared-8.1.zip" &&
            file.GetProperty("sha256").GetString() == "D311C8C7B86E06B54588E442652F963BAE165BD4D8393E73CC9EBB445B025547");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.4-input-corrected/g0.4-input-proof-evidence.json" &&
            file.GetProperty("sha256").GetString() == "F9D0A742F011BA19D1B7A30B547555D7DE7CC7A64B97F8294DD3CE828FFFD969");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "p3/libjpeg-turbo-3.2.0/libjpeg-turbo-3.2.0-vc-x64.exe" &&
            file.GetProperty("sha256").GetString() == "662761D8BA8DAE04AEC74023EBAECEB856C2B56B9B59CFD180759D26300DDA42");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/f7-setts-20260825/corrected/f7-setts-experiment-evidence.json" &&
            file.GetProperty("sha256").GetString() == "1835D0D38993141539197AD34B2ACF138E63B61EE3F109837C36A5D11C43A13E");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/f7-setts-20260825/superseded-preexecution/f7-setts-experiment-evidence.json" &&
            file.GetProperty("sha256").GetString() == "503BF3DDC5C6A18135AC28596B9E9913A700079949C39770B4A6A3AE12ABB37B");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/p3-jpeg-20260825/validated/p3-jpeg-input-proof-evidence.json" &&
            file.GetProperty("sha256").GetString() == "679F5C79CFBA9C5FEBC3DB70714D867D7E9FBD8305F3129663C13A1E2FD88F45");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.5-marker-survivability-20260826-final/g0.5-marker-survivability-report.json" &&
            file.GetProperty("sha256").GetString() == "48611C1D670AEA59CA7192537B36237FE769B7F52BC074A5F9387B666FDEBFA9");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.5-wpf-no-media-20260826-f4d39ac/evidence.json" &&
            file.GetProperty("sha256").GetString() == "63B2895046E000AD9B6B168AC5A28491BE193BA45C8DEE10B0509C86F09AD186");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.5-wpf-no-media-20260826-f4d39ac-final/evidence.json" &&
            file.GetProperty("sha256").GetString() == "237C38B7EA8A99DC3BE865F4EB25BE0D6BE1C24CCEB1CA45D6236C7DD549000D");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/p3-jpeg-20260825/validated/media/progressive-420.jpg" &&
            file.GetProperty("sha256").GetString() == "F9F34A9F0651066BFAD6646AB1E601FAFBFFBDDEBDB6A9FED45725857B5035F2");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/p3-jpeg-20260825/validated/media/baseline-420-orientation-6.jpg" &&
            file.GetProperty("sha256").GetString() == "A020131E12BD3E7A2210916FB8F24B2B261687320A52F6A5A75813CF82138CD7");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g05-calibration-20260826T000415184Z-02881551/g0.5-calibration-evidence.json" &&
            file.GetProperty("sha256").GetString() == "72680C5EFD7148779E07054BDE462752AFAF255CC4E1E8892C0A0170787CE5C2");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.5-retained-audio-oracle-20260826-final/g0.5-retained-audio-oracle-report.json" &&
            file.GetProperty("sha256").GetString() == "387BEF57A359C43479ECBF1B85C20DAB927378F7B8AFD1C58FF7CDEC87F56A0A");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "fixtures/G05/g0.5-frame-index-atlas-90000x17bit.ppm" &&
            file.GetProperty("sha256").GetString() == "BB158EA61BFD6FE99BA7ED82C6A280AE4AABE2216E87028F35002FB9EC2DFC97");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g05-calibration-20260826T000911853Z-71c3c6b3/g0.5-calibration-evidence.json" &&
            file.GetProperty("sha256").GetString() == "3D8E0CBA16381FD11F9228AB30A386DD864B520EFA37C00532790FF4E07D1951");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g05-calibration-20260826T001236270Z-5c7a1bc4/g0.5-calibration-evidence.json" &&
            file.GetProperty("sha256").GetString() == "6141B50D5DE53F404F88DBC931979F11E89B270E312F1B099D8AB4CF574815D9");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g05-calibration-20260826T001825026Z-564182f4/g0.5-calibration-evidence.json" &&
            file.GetProperty("sha256").GetString() == "F3A7EE69F53A7E43E5B5441A380705C22EF6B1F1F89DE876F18BD6B9AC5363B1");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g05-calibration-20260826T002913397Z-189786ac/g0.5-calibration-evidence.json" &&
            file.GetProperty("size").GetInt64() == 497033 &&
            file.GetProperty("sha256").GetString() == "A95979C4211BD2CB844C44A2F1A1F55B9A7341EAC8C81A5B356AF7121468AC15");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.5-audio-oracle-v3-20260826/g0.5-lossy-audio-control-results.json" &&
            file.GetProperty("size").GetInt64() == 26055 &&
            file.GetProperty("sha256").GetString() == "C30734212AD29DF3723D94280BA3BBFCE20E235F8F6843907D3C4E6D85533DEF");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "proofs/g0.5-audio-oracle-v3-20260826/repository-snapshots/eng/gate0/g0.5-lossy-audio-oracle-freeze.json" &&
            file.GetProperty("sha256").GetString() == "895BE152017E56F4CF634BFDC8D13FC57D4F8A0A227B187E3779A3B537298822");
        Assert.Contains(files, file => file.GetProperty("filename").GetString() == "contracts/g0.4-input-proof-contract.json");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "contracts/artifacts/fonts/NotoSans-Regular.ttf" &&
            file.GetProperty("sha256").GetString() == "478C558EA716033CD60C03438F628DFA75694DCF6B5F6D505A2F05FD2B4F3823");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "contracts/artifacts/fonts/NotoSansArabic-Regular.ttf" &&
            file.GetProperty("sha256").GetString() == "BDFF3E5659D67E67DEF05B33F749683B9376AE819D65D3DD62AC4640B3AAEF48");
        Assert.Contains(files, file =>
            file.GetProperty("filename").GetString() == "contracts/artifacts/fonts/NotoSansCJKsc-Regular.otf" &&
            file.GetProperty("sha256").GetString() == "2C76254F6FC379FDDFCE0A7E84FB5385BB135D3E399294F6EEB6680D0365B74B");
        Assert.Equal(3, files.Count(file => file.GetProperty("filename").GetString()!.StartsWith("contracts/artifacts/fonts/licenses/", StringComparison.Ordinal)));

        Assert.All(groups, group =>
        {
            var proofReferences = group.GetProperty("proofRunIdentity").EnumerateArray().Select(item => item.GetString()).ToArray();
            Assert.NotEmpty(proofReferences);
            Assert.All(proofReferences, reference => Assert.True(
                reference!.StartsWith("artifact:", StringComparison.Ordinal) ||
                reference.StartsWith("manifest:", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void PreservationAndValidationScriptsRetainTheApprovedBoundariesAndParse()
    {
        var preservationPath = PathInRepo("eng", "gate0", "Preserve-Gate0Artifacts.ps1");
        var validationPath = PathInRepo("eng", "gate0", "Test-Gate0ArtifactRetention.ps1");
        var preservation = File.ReadAllText(preservationPath);
        var validation = File.ReadAllText(validationPath);

        foreach (var required in new[]
        {
            "The artifact root must be the approved repository sibling",
            "requires a new artifact root",
            "Assert-NoReparsePoints",
            "Copy-VerifiedGroup",
            "[IO.File]::Move($temporaryManifestPath, $resolvedManifestPath, $true)",
            "separatelyBackedUpPrivateCopyVerified = $false",
            "hostedCiEligible = $false",
            "temporaryProviderR2Permitted = $false",
        }) Assert.Contains(required, preservation);

        foreach (var required in new[]
        {
            "Retained artifact failed size or SHA-256 verification",
            "The retained manifest copy does not match the tracked manifest",
            "Artifact reference is not retained",
            "Repository reference is missing or escaped the repository",
            "Proof-run identity is missing",
            "The retained artifact root contains a reparse point",
            "The retained root contains an unmanifested or missing file",
        }) Assert.Contains(required, validation);

        foreach (var path in new[] { preservationPath, validationPath })
        {
            var quotedPath = path.Replace("'", "''", StringComparison.Ordinal);
            var result = RunPowerShell($"$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('{quotedPath}',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){{$errors|% Message;exit 1}}");
            Assert.True(result.ExitCode == 0, result.Output);
        }

        var wrongRoot = PathInRepo();
        var quotedWrongRoot = wrongRoot.Replace("'", "''", StringComparison.Ordinal);
        var quotedPreservation = preservationPath.Replace("'", "''", StringComparison.Ordinal);
        var preservationBoundary = RunPowerShell($"& '{quotedPreservation}' -ArtifactRoot '{quotedWrongRoot}' -P2Root missing -FixtureRoot missing -CorrectedProofRoot missing -P3Root missing");
        Assert.NotEqual(0, preservationBoundary.ExitCode);
        Assert.Contains("approved repository sibling", preservationBoundary.Output, StringComparison.OrdinalIgnoreCase);

        var quotedValidation = validationPath.Replace("'", "''", StringComparison.Ordinal);
        var validationBoundary = RunPowerShell($"& '{quotedValidation}' -ArtifactRoot '{quotedWrongRoot}'");
        Assert.NotEqual(0, validationBoundary.ExitCode);
        Assert.Contains("approved repository sibling", validationBoundary.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppendScriptUsesAnImmutableJournaledAppendWithCanonicalSiblingRootAndRecoveryGuards()
    {
        var appendPath = PathInRepo("eng", "gate0", "Add-Gate0RetainedProof.ps1");
        var validationPath = PathInRepo("eng", "gate0", "Test-Gate0ArtifactRetention.ps1");
        var append = File.ReadAllText(appendPath);
        var validation = File.ReadAllText(validationPath);

        foreach (var required in new[]
        {
            "Append journal state is unknown or inconsistent",
            "oldManifestBase64",
            "Assert-ImmutableCandidateExtension",
            "payloadCommitted",
            "manifestCommitted",
            "Duplicate immutable group ID",
            "Destination already exists or escapes the retained root",
            "Duplicate retained artifact filename",
            "Append staging is not on the artifact volume",
            "Assert-NoReparsePoints $source 'Source root'",
            "SourceTrustBoundary must be the repository parent and contain SourceRoot",
            "Invoke-Validation $resolvedRoot",
        }) Assert.Contains(required, append);
        Assert.DoesNotContain("REELFORGE_GATE0_TEST", append);
        Assert.DoesNotContain("REELFORGE_GATE0_TEST", validation);
        Assert.Contains("Assert-NoReparsePointAncestors $resolvedArtifactRoot", validation);

        foreach (var path in new[] { appendPath, validationPath })
        {
            var quotedPath = path.Replace("'", "''", StringComparison.Ordinal);
            var result = RunPowerShell($"$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('{quotedPath}',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){{$errors|% Message;exit 1}}");
            Assert.Equal(0, result.ExitCode);
        }
    }

    [Fact]
    public void AppendScriptCanRetainOneNewGroupInAnIsolatedExplicitlyTestOnlyCorpus()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0AppendTests", Guid.NewGuid().ToString("N"));
        try
        {
            var repo = Path.Combine(testRoot, "repo");
            var gate0 = Path.Combine(repo, "eng", "gate0");
            Directory.CreateDirectory(gate0);
            File.Copy(PathInRepo("eng", "gate0", "Add-Gate0RetainedProof.ps1"), Path.Combine(gate0, "Add-Gate0RetainedProof.ps1"));
            File.Copy(PathInRepo("eng", "gate0", "Test-Gate0ArtifactRetention.ps1"), Path.Combine(gate0, "Test-Gate0ArtifactRetention.ps1"));
            File.WriteAllText(Path.Combine(repo, ".gitignore"), "bin\n");
            File.WriteAllText(Path.Combine(repo, ".gate0-append-test-sentinel"), "isolated test corpus\n");

            var artifactRoot = Path.Combine(testRoot, "ReelForge.Gate0Artifacts");
            Directory.CreateDirectory(Path.Combine(artifactRoot, "baseline"));
            File.WriteAllText(Path.Combine(artifactRoot, "baseline", "evidence.txt"), "baseline");
            var baselineHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("baseline")));
            var manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                artifactSetId = "Gate0.InterimCorpus.20260825",
                generatedUtc = "2026-08-25T00:00:00.0000000+00:00",
                storage = new { rootName = "ReelForge.Gate0Artifacts", classification = "local-working-copy-r2-retention-pending", productionArtifactRepository = false, hostedCiEligible = false, separatelyBackedUpPrivateCopyVerified = false, twoCopyRetentionCondition = "incomplete" },
                anchors = new { },
                p3Authenticode = new { },
                groups = new List<object> { new { groupId = "Baseline", provenance = "test", producerRuntimeIdentity = Array.Empty<string>(), licenseRecords = Array.Empty<string>(), proofRunIdentity = new List<string> { "artifact:baseline/evidence.txt" }, fileCount = 1, totalBytes = 8, files = new List<object> { new { artifactId = "Baseline/evidence.txt", filename = "baseline/evidence.txt", size = 8, sha256 = baselineHash } } } },
                totals = new { groupCount = 1, fileCount = 1, totalBytes = 8 },
                limitations = Array.Empty<string>(),
            });
            File.WriteAllText(Path.Combine(gate0, "artifact-retention-manifest.json"), manifest);
            File.WriteAllText(Path.Combine(artifactRoot, "artifact-retention-manifest.json"), manifest);
            var source = Path.Combine(testRoot, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "proof.txt"), "proof");

            var append = Path.Combine(gate0, "Add-Gate0RetainedProof.ps1").Replace("'", "''", StringComparison.Ordinal);
            var root = artifactRoot.Replace("'", "''", StringComparison.Ordinal);
            var quotedSource = source.Replace("'", "''", StringComparison.Ordinal);
            var sourceTrustBoundary = testRoot.Replace("'", "''", StringComparison.Ordinal);
            var command = $"& '{append}' -ArtifactRoot '{root}' -SourceRoot '{quotedSource}' -SourceTrustBoundary '{sourceTrustBoundary}' -GroupId 'Proof' -DestinationName 'proofs/new' -Provenance 'test proof' -ProofRunIdentity 'artifact:proofs/new/proof.txt'";
            var result = RunPowerShell(command);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.True(File.Exists(Path.Combine(artifactRoot, "proofs", "new", "proof.txt")));
            Assert.False(File.Exists(artifactRoot + ".append-journal.json"));
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(gate0, "artifact-retention-manifest.json")));
            Assert.Equal(2, document.RootElement.GetProperty("totals").GetProperty("groupCount").GetInt32());
            Assert.Contains(document.RootElement.GetProperty("groups").EnumerateArray().ToArray(), group => group.GetProperty("groupId").GetString() == "Proof");

            var trackedManifestPath = Path.Combine(gate0, "artifact-retention-manifest.json");
            var localManifestPath = Path.Combine(artifactRoot, "artifact-retention-manifest.json");
            var committedManifest = File.ReadAllText(trackedManifestPath);
            var oldHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest)));
            var newHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(committedManifest)));
            File.WriteAllText(localManifestPath, manifest);
            var recoveryJournal = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                phase = "payloadCommitted",
                oldManifestSha256 = oldHash,
                oldManifestBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(manifest)),
                newManifestSha256 = newHash,
                newManifestBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(committedManifest)),
                destinationName = "proofs/new",
                stagingPath = artifactRoot + ".append-staging-recovery",
                groupId = "Proof",
                createdUtc = "2026-08-25T00:00:00.0000000+00:00",
            });
            File.WriteAllText(artifactRoot + ".append-journal.json", recoveryJournal);
            var duplicate = RunPowerShell(command);
            Assert.NotEqual(0, duplicate.ExitCode);
            Assert.True(duplicate.Output.Contains("Duplicate immutable group ID", StringComparison.Ordinal), duplicate.Output);
            Assert.False(File.Exists(artifactRoot + ".append-journal.json"));
            Assert.Equal(File.ReadAllText(trackedManifestPath), File.ReadAllText(localManifestPath));

            var invalidIndex = 0;
            foreach (var invalidDestination in new[] { ".", "proofs/./new", "proofs//new", "proofs/../new" })
            {
                var invalid = RunPowerShell(command.Replace("-GroupId 'Proof'", $"-GroupId 'Invalid{invalidIndex++}'", StringComparison.Ordinal).Replace("-DestinationName 'proofs/new'", $"-DestinationName '{invalidDestination}'", StringComparison.Ordinal));
                Assert.NotEqual(0, invalid.ExitCode);
            }
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("AfterPayloadMove")]
    [InlineData("AfterTrackedManifestWrite")]
    [InlineData("AfterLocalManifestWrite")]
    public void AppendRecoveryCompletesEachDurableCrashBoundary(string fault)
    {
        var corpus = CreateAppendTestCorpus();
        try
        {
            var failed = RunPowerShell(corpus.Command + $" -FaultInjection {fault}");
            Assert.NotEqual(0, failed.ExitCode);
            Assert.True(File.Exists(corpus.ArtifactRoot + ".append-journal.json"));
            var recovery = RunPowerShell(corpus.Command);
            Assert.NotEqual(0, recovery.ExitCode); // Recovery completes before duplicate immutable group rejection.
            Assert.Contains("Duplicate immutable group ID", recovery.Output);
            Assert.False(File.Exists(corpus.ArtifactRoot + ".append-journal.json"));
            Assert.Equal(File.ReadAllText(corpus.TrackedManifest), File.ReadAllText(corpus.LocalManifest));
        }
        finally { DeleteCorpus(corpus.TestRoot); }
    }

    [Fact]
    public void AppendRecoveryRejectsAJournalCandidateThatChangesAnExistingGroup()
    {
        var corpus = CreateAppendTestCorpus();
        try
        {
            Assert.NotEqual(0, RunPowerShell(corpus.Command + " -FaultInjection AfterPayloadMove").ExitCode);
            var journalPath = corpus.ArtifactRoot + ".append-journal.json";
            var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
            var candidate = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(journal["newManifestBase64"]!.GetValue<string>())))!.AsObject();
            candidate["groups"]![0]!["provenance"] = "tampered";
            var candidateText = candidate.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            journal["newManifestBase64"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(candidateText));
            journal["newManifestSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidateText)));
            File.WriteAllText(journalPath, journal.ToJsonString());
            var recovery = RunPowerShell(corpus.Command);
            Assert.NotEqual(0, recovery.ExitCode);
            Assert.Contains("changed existing group", recovery.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(journalPath));
        }
        finally { DeleteCorpus(corpus.TestRoot); }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("mixed-separator")]
    public void AppendRecoveryRejectsMalformedAppendedGroupBeforeAnyManifestWrite(string mutation)
    {
        var corpus = CreateAppendTestCorpus();
        try
        {
            Assert.NotEqual(0, RunPowerShell(corpus.Command + " -FaultInjection AfterPayloadMove").ExitCode);
            var journalPath = corpus.ArtifactRoot + ".append-journal.json";
            var journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
            var candidate = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(journal["newManifestBase64"]!.GetValue<string>())))!.AsObject();
            var appended = candidate["groups"]!.AsArray()[1]!.AsObject();
            if (mutation == "empty")
            {
                appended["files"] = new JsonArray(); appended["fileCount"] = 0; appended["totalBytes"] = 0;
                candidate["totals"]!["fileCount"] = 1; candidate["totals"]!["totalBytes"] = 8;
            }
            else
            {
                appended["files"]!.AsArray()[0]!["filename"] = "proofs\\new\\proof.txt";
            }
            var candidateText = candidate.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            journal["newManifestBase64"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(candidateText));
            journal["newManifestSha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(candidateText)));
            File.WriteAllText(journalPath, journal.ToJsonString());
            var recovery = RunPowerShell(corpus.Command);
            Assert.NotEqual(0, recovery.ExitCode);
            Assert.Contains(mutation == "empty" ? "contains no retained files" : "portable relative path", recovery.Output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(journalPath));
            Assert.DoesNotContain("Proof", File.ReadAllText(corpus.TrackedManifest));
        }
        finally { DeleteCorpus(corpus.TestRoot); }
    }

    [Fact]
    public void FaultInjectionIsRejectedOutsideAnIsolatedCopiedTestRepository()
    {
        var corpus = CreateAppendTestCorpus();
        try
        {
            File.Delete(Path.Combine(corpus.TestRoot, "repo", ".gate0-append-test-sentinel"));
            var result = RunPowerShell(corpus.Command + " -FaultInjection AfterPayloadMove");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("FaultInjection is permitted only", result.Output);
            Assert.False(Directory.Exists(Path.Combine(corpus.ArtifactRoot, "proofs", "new")));
        }
        finally { DeleteCorpus(corpus.TestRoot); }
    }

    [Fact]
    public void RetentionValidatorAcceptsPortableRepositoryReferencesAndRejectsBackslashes()
    {
        var corpus = CreateAppendTestCorpus();
        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(corpus.TrackedManifest))!.AsObject();
            manifest["groups"]!.AsArray()[0]!["producerRuntimeIdentity"] = new JsonArray("repository:eng/gate0/Add-Gate0RetainedProof.ps1");
            var text = manifest.ToJsonString();
            File.WriteAllText(corpus.TrackedManifest, text); File.WriteAllText(corpus.LocalManifest, text);
            var validator = Path.Combine(corpus.TestRoot, "repo", "eng", "gate0", "Test-Gate0ArtifactRetention.ps1").Replace("'", "''", StringComparison.Ordinal);
            var root = corpus.ArtifactRoot.Replace("'", "''", StringComparison.Ordinal);
            var valid = RunPowerShell($"& '{validator}' -ArtifactRoot '{root}'");
            Assert.Equal(0, valid.ExitCode);

            manifest["groups"]!.AsArray()[0]!["producerRuntimeIdentity"] = new JsonArray("repository:eng\\gate0\\Add-Gate0RetainedProof.ps1");
            text = manifest.ToJsonString();
            File.WriteAllText(corpus.TrackedManifest, text); File.WriteAllText(corpus.LocalManifest, text);
            var invalid = RunPowerShell($"& '{validator}' -ArtifactRoot '{root}'");
            Assert.NotEqual(0, invalid.ExitCode);
            Assert.Contains("Unsafe repository reference", invalid.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteCorpus(corpus.TestRoot); }
    }

    [Fact]
    public void LegacyValidatorExcludesOnlyFutureStage2AfterSeparateContainmentValidation()
    {
        var corpus = CreateAppendTestCorpus();
        try
        {
            var future = Path.Combine(corpus.ArtifactRoot, "future", "stage2", "run-a");
            Directory.CreateDirectory(future);
            File.WriteAllText(Path.Combine(future, "proof.txt"), "future");
            var gate0 = Path.Combine(corpus.TestRoot, "repo", "eng", "gate0");
            var separate = Path.Combine(gate0, "Test-Gate0EvidenceContainment.ps1");
            File.WriteAllText(separate, "param([string]$ArtifactRoot,[switch]$RequireEffectiveSeal) if(-not$RequireEffectiveSeal){throw 'seal required'}; [pscustomobject]@{disposition='passed'}");
            var validator = Path.Combine(gate0, "Test-Gate0ArtifactRetention.ps1").Replace("'", "''", StringComparison.Ordinal);
            var root = corpus.ArtifactRoot.Replace("'", "''", StringComparison.Ordinal);

            var strict = RunPowerShell($"& '{validator}' -ArtifactRoot '{root}'");
            Assert.NotEqual(0, strict.ExitCode);
            Assert.Contains("unmanifested", strict.Output, StringComparison.OrdinalIgnoreCase);

            var separated = RunPowerShell($"& '{validator}' -ArtifactRoot '{root}' -ValidateIndexedFutureEvidenceSeparately");
            Assert.Equal(0, separated.ExitCode);

            var unrelatedFuture = Path.Combine(corpus.ArtifactRoot, "future", "not-stage2", "unexpected.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(unrelatedFuture)!);
            File.WriteAllText(unrelatedFuture, "unexpected");
            var narrowPrefix = RunPowerShell($"& '{validator}' -ArtifactRoot '{root}' -ValidateIndexedFutureEvidenceSeparately");
            Assert.NotEqual(0, narrowPrefix.ExitCode);
            Assert.Contains("unmanifested", narrowPrefix.Output, StringComparison.OrdinalIgnoreCase);
            File.Delete(unrelatedFuture);
            Directory.Delete(Path.GetDirectoryName(unrelatedFuture)!);

            File.WriteAllText(separate, "param([string]$ArtifactRoot,[switch]$RequireEffectiveSeal) throw 'future validation failed'");
            var rejected = RunPowerShell($"& '{validator}' -ArtifactRoot '{root}' -ValidateIndexedFutureEvidenceSeparately");
            Assert.NotEqual(0, rejected.ExitCode);
            Assert.Contains("future validation failed", rejected.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteCorpus(corpus.TestRoot); }
    }

    private static (string TestRoot, string ArtifactRoot, string TrackedManifest, string LocalManifest, string Command) CreateAppendTestCorpus()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0AppendFaultTests", Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(testRoot, "repo");
        var gate0 = Path.Combine(repo, "eng", "gate0");
        Directory.CreateDirectory(gate0);
        File.Copy(PathInRepo("eng", "gate0", "Add-Gate0RetainedProof.ps1"), Path.Combine(gate0, "Add-Gate0RetainedProof.ps1"));
        File.Copy(PathInRepo("eng", "gate0", "Test-Gate0ArtifactRetention.ps1"), Path.Combine(gate0, "Test-Gate0ArtifactRetention.ps1"));
        File.WriteAllText(Path.Combine(repo, ".gitignore"), "bin\n");
        File.WriteAllText(Path.Combine(repo, ".gate0-append-test-sentinel"), "isolated\n");
        var artifactRoot = Path.Combine(testRoot, "ReelForge.Gate0Artifacts");
        Directory.CreateDirectory(Path.Combine(artifactRoot, "baseline"));
        File.WriteAllText(Path.Combine(artifactRoot, "baseline", "evidence.txt"), "baseline");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("baseline")));
        var baselineGroup = new
        {
            groupId = "Baseline",
            provenance = "test",
            producerRuntimeIdentity = Array.Empty<string>(),
            licenseRecords = Array.Empty<string>(),
            proofRunIdentity = new List<string> { "artifact:baseline/evidence.txt" },
            fileCount = 1,
            totalBytes = 8,
            files = new List<object> { new { artifactId = "Baseline/evidence.txt", filename = "baseline/evidence.txt", size = 8, sha256 = hash } },
        };
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            artifactSetId = "Gate0.InterimCorpus.20260825",
            generatedUtc = "2026-08-25T00:00:00.0000000+00:00",
            storage = new { rootName = "ReelForge.Gate0Artifacts", classification = "local-working-copy-r2-retention-pending", productionArtifactRepository = false, hostedCiEligible = false, separatelyBackedUpPrivateCopyVerified = false, twoCopyRetentionCondition = "incomplete" },
            anchors = new { },
            p3Authenticode = new { },
            limitations = Array.Empty<string>(),
            groups = new List<object> { baselineGroup },
            totals = new { groupCount = 1, fileCount = 1, totalBytes = 8 },
        });
        var tracked = Path.Combine(gate0, "artifact-retention-manifest.json"); var local = Path.Combine(artifactRoot, "artifact-retention-manifest.json");
        File.WriteAllText(tracked, manifest); File.WriteAllText(local, manifest);
        var source = Path.Combine(testRoot, "source"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source, "proof.txt"), "proof");
        var command = $"& '{Path.Combine(gate0, "Add-Gate0RetainedProof.ps1").Replace("'", "''", StringComparison.Ordinal)}' -ArtifactRoot '{artifactRoot.Replace("'", "''", StringComparison.Ordinal)}' -SourceRoot '{source.Replace("'", "''", StringComparison.Ordinal)}' -GroupId 'Proof' -DestinationName 'proofs/new' -Provenance 'test proof' -ProofRunIdentity 'artifact:proofs/new/proof.txt'";
        return (testRoot, artifactRoot, tracked, local, command);
    }

    private static void DeleteCorpus(string path) { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }

    private static (int ExitCode, string Output) RunPowerShell(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
