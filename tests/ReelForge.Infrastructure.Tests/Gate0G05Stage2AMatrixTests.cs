using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G05Stage2AMatrixTests
{
    private static readonly string[] ExpectedGroups =
    ["baseline-720p", "typical-720p", "stress-720p", "baseline-1080p", "typical-1080p", "stress-1080p"];
    private static readonly string[] MeasuredPhase = ["measured", "measured", "measured", "measured", "measured"];
    private static readonly int[] CellOrdinals = [1, 2, 3, 4, 5, 6];
    private static readonly int[] PhaseOrdinals = [1, 1, 2, 3, 4, 5];

    [Fact]
    public void ScheduleHasApprovedOrderShapeAndRouteCounts()
    {
        using var document = ReadJson("eng", "gate0", "g0.5-stage2a-schedule.json");
        var root = document.RootElement;
        Assert.Equal("Gate0.G05.Stage2A.Schedule.V1", root.GetProperty("scheduleId").GetString());
        Assert.Equal(ExpectedGroups, root.GetProperty("groupOrder").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(108, root.GetProperty("attempts").GetArrayLength());
        Assert.Equal(6, root.GetProperty("groupOrder").GetArrayLength());

        var attempts = root.GetProperty("attempts").EnumerateArray().ToArray();
        Assert.Equal(36, attempts.Count(x => x.GetProperty("candidateId").GetString() == "mp4-one"));
        Assert.Equal(36, attempts.Count(x => x.GetProperty("candidateId").GetString() == "webm-one"));
        Assert.Equal(36, attempts.Count(x => x.GetProperty("candidateId").GetString() == "webm-eight"));
        Assert.Equal(36, attempts.Count(x => x.GetProperty("routeId").GetString() == "mp4-openh264-aac"));
        Assert.Equal(72, attempts.Count(x => x.GetProperty("routeId").GetString() == "webm-vp9-opus"));
        Assert.Equal(18, attempts.Select(x => x.GetProperty("cellId").GetString()).Distinct().Count());
        Assert.Equal(18, attempts.Count(x => x.GetProperty("phase").GetString() == "warmup"));
        Assert.Equal(90, attempts.Count(x => x.GetProperty("phase").GetString() == "measured"));
    }

    [Fact]
    public void SchedulePreservesApprovedRotationsAndPerCellOrdinals()
    {
        using var document = ReadJson("eng", "gate0", "g0.5-stage2a-schedule.json");
        var attempts = document.RootElement.GetProperty("attempts").EnumerateArray().ToArray();
        var expectedRotations = new[]
        {
            new[] { "mp4-one", "webm-one", "webm-eight" },
            new[] { "webm-one", "webm-eight", "mp4-one" },
            new[] { "webm-eight", "mp4-one", "webm-one" },
        };

        for (var group = 0; group < 6; group++)
        {
            var groupAttempts = attempts.Skip(group * 18).Take(18).ToArray();
            var cells = groupAttempts.Chunk(6).ToArray();
            Assert.Equal(expectedRotations[group % 3], cells.Select(cell => cell[0].GetProperty("candidateId").GetString()));
            foreach (var cell in cells)
            {
                Assert.Equal("warmup", cell[0].GetProperty("phase").GetString());
                Assert.Equal(MeasuredPhase, cell.Skip(1).Select(x => x.GetProperty("phase").GetString()));
                Assert.Equal(CellOrdinals, cell.Select(x => x.GetProperty("cellAttemptOrdinal").GetInt32()));
                Assert.Equal(PhaseOrdinals, cell.Select(x => x.GetProperty("phaseOrdinal").GetInt32()));
            }
        }
    }

    [Fact]
    public void ScheduleReaderRejectsReorderedOrTamperedAttempts()
    {
        var source = PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json");
        using var fixture = new TempFile(JsonDocument.Parse(File.ReadAllText(source)).RootElement.GetRawText());
        var json = JsonDocument.Parse(File.ReadAllText(fixture.Path));
        var attempts = json.RootElement.GetProperty("attempts").EnumerateArray().Select(x => x.GetRawText()).ToArray();
        (attempts[0], attempts[1]) = (attempts[1], attempts[0]);
        File.WriteAllText(fixture.Path, ReplaceAttempts(json.RootElement, attempts));
        var reordered = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2ASchedule '{Escape(fixture.Path)}'");
        Assert.NotEqual(0, reordered.ExitCode);

        using var tampered = new TempFile(File.ReadAllText(source));
        var text = File.ReadAllText(tampered.Path).Replace("\"webm-one\"", "\"webm-tampered\"", StringComparison.Ordinal);
        File.WriteAllText(tampered.Path, text);
        var changed = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2ASchedule '{Escape(tampered.Path)}'");
        Assert.NotEqual(0, changed.ExitCode);

        foreach (var property in new[] { "workloadId", "resolutionId" })
        {
            using var semanticTamper = new TempFile(File.ReadAllText(source).Replace($"\"{property}\": \"", $"\"{property}\": \"tampered-", StringComparison.Ordinal));
            var semanticResult = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2ASchedule '{Escape(semanticTamper.Path)}'");
            Assert.NotEqual(0, semanticResult.ExitCode);
        }
    }

    [Fact]
    public void StatisticsUseFiveMeasuredValuesAndExcludeWarmup()
    {
        var result = RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AStatistics @(1,2,3,4,5) | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("median").GetDouble());
        Assert.Equal(1, root.GetProperty("minimum").GetDouble());
        Assert.Equal(5, root.GetProperty("maximum").GetDouble());
        Assert.Equal(4, root.GetProperty("range").GetDouble());
        Assert.Equal(1, root.GetProperty("medianAbsoluteDeviation").GetDouble());
        Assert.True(root.GetProperty("warmupExcluded").GetBoolean());
        Assert.Equal(5, root.GetProperty("observationCount").GetInt32());
    }

    [Fact]
    public void StatisticsRejectWrongCountOrNonFiniteValues()
    {
        Assert.NotEqual(0, RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AStatistics @(1,2,3,4)").ExitCode);
        Assert.NotEqual(0, RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AStatistics @(1,2,[double]::NaN,4,5)").ExitCode);
    }

    [Fact]
    public void ReservationIncludesOrdinaryCompactAndExceptionalBytes()
    {
        var result = RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AReservation 100 1000 200 300 | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(2300, root.GetProperty("requiredForNextCellBytes").GetInt64());
        Assert.Equal(805303968, root.GetProperty("remainingAfterReservationBytes").GetInt64());
        Assert.True(root.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void ReservationRejectsNegativeInputsAndCeilingOverflow()
    {
        Assert.NotEqual(0, RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AReservation -1 1 1 1").ExitCode);
        var result = RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AReservation 805306000 100 100 100 | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("passed").GetBoolean());
        var zero = RunPwsh($"Import-Module '{ModulePath()}'; Get-G05Stage2AReservation 0 0 0 0 | ConvertTo-Json -Compress");
        Assert.True(zero.ExitCode == 0, zero.Output);
        using var zeroJson = JsonDocument.Parse(zero.Output);
        Assert.Equal(0, zeroJson.RootElement.GetProperty("requiredForNextCellBytes").GetInt64());
        Assert.True(zeroJson.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void SupersededV1RunnerFailsClosedOnItsHistoricalAuthorizationWithoutMedia()
    {
        var path = PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1");
        var result = RunPwsh($"& '{Escape(path)}' | ConvertTo-Json -Compress");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("authorization binding changed", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffprobe", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunnerAuthorizationBindsPendingHashContract()
    {
        using var auth = ReadJson("eng", "gate0", "g0.5-stage2a-execution-authorization.json");
        var status = auth.RootElement.GetProperty("status").GetString();
        Assert.True(status is "owner-authorized-execution-implementation-pending" or "owner-authorized-and-prerequisites-verified");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner-decision"] = "docs/gate-0-g0.5-stage2a-owner-decisions.md",
            ["execution-owner-approval"] = "docs/gate-0-g0.5-stage2a-execution-approval.md",
            ["replacement-warmup-approval"] = "docs/gate-0-g0.5-stage2a-replacement-warmup-approval.md",
            ["replacement-activation-summary"] = "eng/gate0/g0.5-stage2a-replacement-activation-summary.json",
            ["replacement-execution-block"] = "docs/gate-0-g0.5-stage2a-replacement-execution-block.md",
            ["retained-path-repair-approval"] = "docs/gate-0-g0.5-stage2a-retained-path-repair-approval.md",
            ["retained-path-restart-activation"] = "eng/gate0/g0.5-stage2a-retained-path-restart-activation-summary.json",
            ["stage2a-result-summary"] = "eng/gate0/g0.5-stage2a-result-summary.json",
            ["stage2a-owner-packet"] = "docs/gate-0-g0.5-stage2a-results.md",
            ["schedule"] = "eng/gate0/g0.5-stage2a-schedule.json",
            ["runner"] = "eng/gate0/Invoke-G05Stage2AMatrix.ps1",
            ["preflight"] = "eng/gate0/Test-G05Stage2AMatrixPreflight.ps1",
            ["legacy-retention-validator"] = "eng/gate0/Test-Gate0ArtifactRetention.ps1",
            ["helper"] = "eng/gate0/G05Stage2AMatrixHelpers.psm1",
            ["semantic-executor"] = "eng/gate0/G05Stage2ASemanticExecutor.psm1",
            ["semantic-helper"] = "eng/gate0/G05Stage2ASemanticHelpers.psm1",
            ["smoke-helper"] = "eng/gate0/G05Stage2SmokeHelpers.psm1",
            ["marker-helper"] = "eng/gate0/G05MarkerSurvivabilityHelpers.psm1",
            ["runtime-validator"] = "eng/gate0/Validate-P2Runtime.ps1",
            ["runtime-manifest"] = "eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json",
            ["workload-contract"] = "eng/gate0/g0.5-stage2-workload-contract.json",
            ["containment-contract"] = "eng/gate0/g0.5-stage2-containment-dry-run-contract.json",
            ["audio-oracle-contract"] = "eng/gate0/g0.5-lossy-audio-oracle-contract.json",
            ["audio-oracle-amendment"] = "eng/gate0/g0.5-lossy-audio-oracle-amendment-v4.json",
            ["audio-oracle-amendment-freeze"] = "eng/gate0/g0.5-lossy-audio-oracle-amendment-v4-freeze.json",
            ["structured-audio-control-summary"] = "eng/gate0/g0.5-structured-audio-control-result-summary.json",
            ["structured-audio-control-retention-summary"] = "eng/gate0/g0.5-structured-audio-control-retention-result-summary.json",
            ["replacement-smoke-authorization"] = "eng/gate0/g0.5-stage2-replacement-smoke-authorization-summary.json",
            ["replacement-smoke-result"] = "eng/gate0/g0.5-stage2-replacement-smoke-result-summary.json",
            ["retention-contract"] = "eng/gate0/g0.5-stage2a-retention-contract.json",
            ["evidence-writer"] = "eng/gate0/Add-Gate0EvidenceShard.ps1",
            ["evidence-containment"] = "eng/gate0/evidence/Gate0EvidenceContainment.psm1",
        };
        foreach (var binding in auth.RootElement.GetProperty("bindings").EnumerateArray())
        {
            Assert.True(expected.ContainsKey(binding.GetProperty("role").GetString()!));
            var role = binding.GetProperty("role").GetString()!;
            var path = PathInRepo(binding.GetProperty("path").GetString()!.Split('/'));
            var authorizedHash = binding.GetProperty("sha256").GetString();
            if (role is "smoke-helper" or "legacy-retention-validator")
            {
                var historicalHash = role == "smoke-helper"
                    ? "13763E718E35F5794C39835B46F69EF3EAF0ECE4C7C1B562B956DBCBED48E8E4"
                    : "97DCFA4BB91C0EA8A009F5E4F1DE0ABEDD28E4B41AFB10C48C18CC2C086C8D98";
                Assert.Equal(historicalHash, authorizedHash);
                Assert.NotEqual(authorizedHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                continue;
            }

            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), authorizedHash);
        }
        Assert.Equal(expected.Count, auth.RootElement.GetProperty("bindings").GetArrayLength());
    }

    [Fact]
    public void ReplacementSmokeClosureBindsAllAdmittedCandidatesAndDurableRetention()
    {
        var result = RunPwsh($"Import-Module '{ModulePath()}' -Force; Assert-G05Stage2AReplacementSmokeClosure '{Escape(PathInRepo())}' | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("localAndR2Verified").GetBoolean());
        Assert.False(json.RootElement.GetProperty("historicalFullMatrixFlag").GetBoolean());
        Assert.Equal(3, json.RootElement.GetProperty("candidateIds").GetArrayLength());
        Assert.Equal("docs/gate-0-g0.5-stage2a-execution-approval.md", json.RootElement.GetProperty("supersedingExecutionApproval").GetString());
    }

    [Fact]
    public void RetentionContractAndSemanticSeamPreserveApprovedAttemptRules()
    {
        using var contract = ReadJson("eng", "gate0", "g0.5-stage2a-retention-contract.json");
        var root = contract.RootElement;
        Assert.Equal(805306368, root.GetProperty("stage2ARetentionCeilingBytes").GetInt64());
        Assert.Equal(38878888, root.GetProperty("requiredReservationPerCellBytes").GetInt64());
        Assert.Contains("that attempt independently", root.GetProperty("compactRule").GetString(), StringComparison.Ordinal);
        Assert.Contains("first successfully completed measured attempt", root.GetProperty("ordinaryRule").GetString(), StringComparison.Ordinal);

        var command = SemanticImportCommand() + "; " +
            "$schedule=Read-G05Stage2ASchedule '" + Escape(PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json")) + "'; " +
            "$workload=Get-Content -Raw '" + Escape(PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json")) + "'|ConvertFrom-Json -Depth 100; " +
            "$cell=Get-G05Stage2ACellRows $schedule.Schedule $workload 'baseline-720p-mp4-one'; " +
            "$retention=Read-G05Stage2ARetentionContract '" + Escape(PathInRepo("eng", "gate0", "g0.5-stage2a-retention-contract.json")) + "'; " +
            "[pscustomobject]@{attempts=$cell.Attempts.Count;threads=$cell.Threads;reservation=$retention.Contract.requiredReservationPerCellBytes}|ConvertTo-Json -Compress";
        var result = RunPwsh(command);
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(6, json.RootElement.GetProperty("attempts").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("threads").GetInt32());
        Assert.Equal(38878888, json.RootElement.GetProperty("reservation").GetInt64());
    }

    [Fact]
    public void SemanticSeamRejectsUnknownWorkloadWithoutIndexingNull()
    {
        var workloadPath = PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json");
        using var workload = new TempFile(File.ReadAllText(workloadPath).Replace("\"baseline-1v1a\"", "\"removed-baseline\"", StringComparison.Ordinal));
        var command = SemanticImportCommand() + "; " +
            "$schedule=Read-G05Stage2ASchedule '" + Escape(PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json")) + "'; " +
            "$workload=Get-Content -Raw '" + Escape(workload.Path) + "'|ConvertFrom-Json -Depth 100; " +
            "Get-G05Stage2ACellRows $schedule.Schedule $workload 'baseline-720p-mp4-one'";
        var result = RunPwsh(command);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("cannot resolve its frozen workload", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SemanticSeamRejectsMixedRowsAndWritesPortableImmutableBindings()
    {
        var scheduleText = File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2a-schedule.json"));
        using var schedule = new TempFile(scheduleText.Replace("\"globalOrdinal\": 2,", "\"globalOrdinal\": 2,", StringComparison.Ordinal)
            .Replace("\"cellAttemptOrdinal\": 2,", "\"cellAttemptOrdinal\": 2,\n      \"routeIdTamperMarker\": true,", StringComparison.Ordinal));
        using var parsed = JsonDocument.Parse(File.ReadAllText(schedule.Path));
        var attempts = parsed.RootElement.GetProperty("attempts").EnumerateArray().Select(x => x.GetRawText()).ToArray();
        attempts[1] = attempts[1].Replace("\"routeId\": \"mp4-openh264-aac\"", "\"routeId\": \"webm-vp9-opus\"", StringComparison.Ordinal);
        File.WriteAllText(schedule.Path, ReplaceAttempts(parsed.RootElement, attempts));

        var workload = Escape(PathInRepo("eng", "gate0", "g0.5-stage2-workload-contract.json"));
        var mixedCommand = SemanticImportCommand() + "; " +
            "$schedule=Get-Content -Raw '" + Escape(schedule.Path) + "'|ConvertFrom-Json -Depth 100; " +
            "$workload=Get-Content -Raw '" + workload + "'|ConvertFrom-Json -Depth 100; " +
            "Get-G05Stage2ACellRows $schedule $workload 'baseline-720p-mp4-one'";
        var mixed = RunPwsh(mixedCommand);
        Assert.NotEqual(0, mixed.ExitCode);
        Assert.Contains("mixed or noncontiguous", mixed.Output, StringComparison.OrdinalIgnoreCase);

        using var directory = new TempDirectory();
        var nested = System.IO.Path.Combine(directory.Path, "nested");
        Directory.CreateDirectory(nested);
        var summary = System.IO.Path.Combine(nested, "summary.json");
        var bindingCommand = SemanticImportCommand() + "; " +
            "$attempt=[pscustomobject]@{globalOrdinal=1;phase='warmup'}; $summary=[ordered]@{disposition='passed'}; " +
            "$binding=New-G05Stage2AAttemptBinding $attempt $summary '" + Escape(summary) + "' '" + Escape(directory.Path) + "' 'complete'; " +
            "$secondFailed=$false; try { New-G05Stage2AAttemptBinding $attempt $summary '" + Escape(summary) + "' '" + Escape(directory.Path) + "' 'complete' | Out-Null } catch { $secondFailed=$true }; " +
            "[pscustomobject]@{path=$binding.recordPath;secondFailed=$secondFailed}|ConvertTo-Json -Compress";
        var binding = RunPwsh(bindingCommand);
        Assert.True(binding.ExitCode == 0, binding.Output);
        using var bindingJson = JsonDocument.Parse(binding.Output);
        Assert.Equal("nested/summary.json", bindingJson.RootElement.GetProperty("path").GetString());
        Assert.True(bindingJson.RootElement.GetProperty("secondFailed").GetBoolean());

        var outside = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N") + ".json");
        var outsideCommand = SemanticImportCommand() + "; " +
            "$attempt=[pscustomobject]@{globalOrdinal=1;phase='warmup'}; try { New-G05Stage2AAttemptBinding $attempt ([ordered]@{disposition='passed'}) '" + Escape(outside) + "' '" + Escape(directory.Path) + "' 'complete' | Out-Null; 'not-blocked' } catch { $_.Exception.Message }";
        var escaped = RunPwsh(outsideCommand);
        Assert.True(escaped.ExitCode == 0, escaped.Output);
        Assert.Contains("escaped its cell source root", escaped.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompactBindingRejectsTamperedSemanticSummary()
    {
        using var directory = new TempDirectory();
        var summaryPath = System.IO.Path.Combine(directory.Path, "compact.json");
        var command = SemanticImportCommand() + "; " +
            "$attempt=[pscustomobject]@{globalOrdinal=7;phase='measured'}; " +
            "$hashes=[ordered]@{outputSha256=('A'*64);frameProbeSha256=('B'*64);packetProbeSha256=('C'*64);decodedVideoIdentitySha256=('D'*64);decodedAudioRawSha256=('E'*64);decodedAudioContentNormalizedSha256=('F'*64)}; " +
            "$validations=[ordered]@{encode=$true;probe=$true;timing=$true;visual=$true;audio=$true;cleanup=$true}; " +
            "$summary=[ordered]@{disposition='passed';encodedByteEqualityClaim=$false;hashes=$hashes;validations=$validations}; " +
            "$binding=New-G05Stage2AAttemptBinding $attempt $summary '" + Escape(summaryPath) + "' '" + Escape(directory.Path) + "' 'compact' 'stage2a-8'; " +
            "Assert-G05Stage2ACompactBinding $binding '" + Escape(directory.Path) + "' 262144; " +
            "Add-Content -LiteralPath '" + Escape(summaryPath) + "' -Value ' '; " +
            "$blocked=$false;try{Assert-G05Stage2ACompactBinding $binding '" + Escape(directory.Path) + "' 262144}catch{$blocked=$true};if(-not$blocked){exit 32}";
        var result = RunPwsh(command);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void RouteSuspensionUsesOnlyApprovedDeterministicTaxonomy()
    {
        var command = SemanticImportCommand() + "; " +
            "$approved=@('structurally-divergent','semantically-divergent','byte-divergent'); " +
            "$rejected=@('failed','blocked','cleanup-failed','failed-command','failed-integrity','failed-oracle'); " +
            "foreach($d in $approved){if(-not(Test-G05Stage2ADeterministicIntegrityFailure ([pscustomobject]@{disposition=$d}))){exit 41}}; " +
            "foreach($d in $rejected){if(Test-G05Stage2ADeterministicIntegrityFailure ([pscustomobject]@{disposition=$d})){exit 42}}";
        var result = RunPwsh(command);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void AudioOracleFailureIsFriendlyRetainedAndRouteSuspendingWithoutMedia()
    {
        var path = PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1");
        var runner = File.ReadAllText(path);
        const string audioAssignment = "$summary.audio=[ordered]@";
        const string friendlyThrow = "throw 'Audio timing or quality oracle failed.'";
        const string malformedThrow = "throw'Audio timing or quality oracle failed.'";
        const string retainedFailure = "$summary.failures+=,(ConvertTo-G05Stage2ASanitizedText $_.Exception.Message $portableRoots)";
        const string suspension = "if(Test-G05Stage2ADeterministicIntegrityFailure $summary){[void]$suspended.Add([string]$row.routeId)}";

        Assert.DoesNotContain(malformedThrow, runner, StringComparison.Ordinal);
        var assignmentIndex = runner.IndexOf(audioAssignment, StringComparison.Ordinal);
        var throwIndex = runner.IndexOf(friendlyThrow, StringComparison.Ordinal);
        var failureIndex = runner.IndexOf(retainedFailure, StringComparison.Ordinal);
        var suspensionIndex = runner.IndexOf(suspension, StringComparison.Ordinal);
        Assert.True(assignmentIndex >= 0, "The structured audio result must be assigned before the audio gate.");
        Assert.True(throwIndex > assignmentIndex, "The friendly audio gate must run after structured audio assignment.");
        Assert.True(failureIndex > throwIndex, "The caught friendly exception must be retained in the summary flow.");
        Assert.True(suspensionIndex > failureIndex, "A semantically-divergent audio gate must reach deterministic route suspension after retention.");
        Assert.Contains("$failureDisposition='semantically-divergent';$summary.audio", runner, StringComparison.Ordinal);
        Assert.Contains("$summary.disposition=$failureDisposition", runner, StringComparison.Ordinal);

        var syntax = RunPwsh($"$tokens=$null;$errors=$null; $ast=[System.Management.Automation.Language.Parser]::ParseFile('{Escape(path)}',[ref]$tokens,[ref]$errors); if($errors.Count){{ $errors | ForEach-Object Message; exit 73 }}; @($ast.FindAll({{param($node) $node -is [System.Management.Automation.Language.ThrowStatementAst]}},$true)|ForEach-Object {{$_.Extent.Text}})|ConvertTo-Json -Compress");
        Assert.True(syntax.ExitCode == 0, syntax.Output);
        using var json = JsonDocument.Parse(syntax.Output);
        Assert.Contains(friendlyThrow, json.RootElement.EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public void BlockedAttemptUsesCurrentSemanticHashShape()
    {
        var command = SemanticImportCommand() + "; " +
            "$blocked=New-G05Stage2ABlockedAttempt ([pscustomobject]@{globalOrdinal=1;cellAttemptOrdinal=1;phase='warmup'}) 'route suspended'; " +
            "$actual=@($blocked.hashes.Keys|Sort-Object)-join '|'; " +
            "$expected=@('decodedAudioContentNormalizedSha256','decodedAudioRawSha256','decodedVideoIdentitySha256','frameProbeSha256','outputSha256','packetProbeSha256')-join '|'; " +
            "if($actual-ne$expected){throw \"Unexpected blocked hash shape: $actual\"}";
        var result = RunPwsh(command);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public void SemanticArtifactDescriptorRejectsPathOutsideItsRoot()
    {
        using var directory = new TempDirectory();
        using var outside = new TempFile("outside");
        var command = SemanticImportCommand() + "; " +
            "try { Get-G05Stage2ASemanticFile '" + Escape(outside.Path) + "' '" + Escape(directory.Path) + "' | Out-Null; 'not-blocked' } catch { $_.Exception.Message }";
        var result = RunPwsh(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("escaped", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthorizationRejectsStaleHistoricalBindingsAndPreflightRejectsZeroCellReservations()
    {
        var authorization = File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2a-execution-authorization.json"));
        var helperHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PathInRepo("eng", "gate0", "G05Stage2AMatrixHelpers.psm1"))));
        using var tampered = new TempFile(authorization.Replace(helperHash, new string('F', 64), StringComparison.Ordinal));
        var result = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AExecutionAuthorization '{Escape(tampered.Path)}' '{Escape(PathInRepo())}'");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("binding changed", result.Output, StringComparison.OrdinalIgnoreCase);

        var preflight = File.ReadAllText(PathInRepo("eng", "gate0", "Test-G05Stage2AMatrixPreflight.ps1"));
        Assert.Contains("Per-cell preflight requires positive contract-bound ordinary, compact-repeat, and exceptional-closure reservations", preflight, StringComparison.Ordinal);
        Assert.Contains("3758096384", preflight, StringComparison.Ordinal);
        Assert.Contains("artifactDrive.AvailableFreeSpace", preflight, StringComparison.Ordinal);
        Assert.Contains("stagingDrive.AvailableFreeSpace", preflight, StringComparison.Ordinal);
        Assert.Contains("Validate-P2Runtime.ps1", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveRequestFailsClosedBeforeMediaBecauseImplementationIsPending()
    {
        var path = PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1");
        var result = RunPwsh($"& '{Escape(path)}' -ExecuteMedia -RuntimeRoot 'C:\\unused' -ArtifactRoot 'C:\\unused' -StagingRoot 'C:\\unused'");
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(result.Output.Contains("authorization binding changed", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("implementation pending", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("exact role bytes", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("effective authorization", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("exact existing non-reparse", StringComparison.OrdinalIgnoreCase), result.Output);
        Assert.DoesNotContain("ffmpeg process", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage2AArtifactsRemainProofOnlyAndDoNotReferenceLegacyAppenderOrProduct()
    {
        var paths = new[]
        {
            "eng/gate0/G05Stage2AMatrixHelpers.psm1",
            "eng/gate0/G05Stage2ASemanticHelpers.psm1",
            "eng/gate0/Test-G05Stage2AMatrixPreflight.ps1",
            "eng/gate0/Invoke-G05Stage2AMatrix.ps1",
            "eng/gate0/g0.5-stage2a-execution-authorization.json",
            "eng/gate0/g0.5-stage2a-schedule.json",
        };
        foreach (var relative in paths)
        {
            var text = File.ReadAllText(PathInRepo(relative.Split('/')));
            Assert.DoesNotContain("Add-Gate0RetainedProof", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ReelForge product", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src/ReelForge", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string ReplaceAttempts(JsonElement root, string[] attempts)
    {
        using var document = JsonDocument.Parse(root.GetRawText());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("attempts"))
                {
                    writer.WritePropertyName("attempts"); writer.WriteStartArray();
                    foreach (var attempt in attempts) using (var item = JsonDocument.Parse(attempt)) item.RootElement.WriteTo(writer);
                    writer.WriteEndArray();
                }
                else { property.WriteTo(writer); }
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static (int ExitCode, string Output) RunPwsh(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static JsonDocument ReadJson(params string[] parts) => JsonDocument.Parse(File.ReadAllText(PathInRepo(parts)));
    private static string ModulePath() => PathInRepo("eng", "gate0", "G05Stage2AMatrixHelpers.psm1").Replace("'", "''", StringComparison.Ordinal);
    private static string SemanticModulePath() => PathInRepo("eng", "gate0", "G05Stage2ASemanticExecutor.psm1").Replace("'", "''", StringComparison.Ordinal);
    private static string SemanticHelperModulePath() => PathInRepo("eng", "gate0", "G05Stage2ASemanticHelpers.psm1").Replace("'", "''", StringComparison.Ordinal);
    private static string SmokeModulePath() => PathInRepo("eng", "gate0", "G05Stage2SmokeHelpers.psm1").Replace("'", "''", StringComparison.Ordinal);
    private static string SemanticImportCommand() => $"Import-Module '{ModulePath()}'; Import-Module '{SmokeModulePath()}'; Import-Module '{SemanticHelperModulePath()}'; Import-Module '{SemanticModulePath()}'";
    private static string Escape(string path) => path.Replace("'", "''", StringComparison.Ordinal);
    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string content) { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReelForge-Stage2A-" + Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(Path, content); }
        public string Path { get; }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }


    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ReelForge-Stage2A-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
