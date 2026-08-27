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
    public void RunnerDefaultsToContractOnlyAndExecutesNoMedia()
    {
        var path = PathInRepo("eng", "gate0", "Invoke-G05Stage2AMatrix.ps1");
        var result = RunPwsh($"& '{Escape(path)}' | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("contract-only", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("noMediaExecuted").GetBoolean());
        Assert.Equal(108, json.RootElement.GetProperty("schedule").GetProperty("attemptCount").GetInt32());
        Assert.Equal(18, json.RootElement.GetProperty("schedule").GetProperty("cellCount").GetInt32());
    }

    [Fact]
    public void RunnerAuthorizationBindsPendingHashContract()
    {
        using var auth = ReadJson("eng", "gate0", "g0.5-stage2a-execution-authorization.json");
        Assert.Equal("owner-authorized-execution-implementation-pending", auth.RootElement.GetProperty("status").GetString());
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["owner-decision"] = "docs/gate-0-g0.5-stage2a-owner-decisions.md",
            ["schedule"] = "eng/gate0/g0.5-stage2a-schedule.json",
            ["runner"] = "eng/gate0/Invoke-G05Stage2AMatrix.ps1",
            ["preflight"] = "eng/gate0/Test-G05Stage2AMatrixPreflight.ps1",
            ["helper"] = "eng/gate0/G05Stage2AMatrixHelpers.psm1",
            ["runtime-validator"] = "eng/gate0/Validate-P2Runtime.ps1",
            ["runtime-manifest"] = "eng/gate0/manifests/p2-btbn-lgplv3-shared-windows-x64-20260820.json",
            ["workload-contract"] = "eng/gate0/g0.5-stage2-workload-contract.json",
            ["containment-contract"] = "eng/gate0/g0.5-stage2-containment-dry-run-contract.json",
        };
        foreach (var binding in auth.RootElement.GetProperty("bindings").EnumerateArray())
        {
            Assert.True(expected.ContainsKey(binding.GetProperty("role").GetString()!));
            var path = PathInRepo(binding.GetProperty("path").GetString()!.Split('/'));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), binding.GetProperty("sha256").GetString());
        }
        Assert.Equal(expected.Count, auth.RootElement.GetProperty("bindings").GetArrayLength());
    }

    [Fact]
    public void AuthorizationRejectsHelperHashTamperingAndPreflightRejectsZeroCellReservations()
    {
        var authorization = File.ReadAllText(PathInRepo("eng", "gate0", "g0.5-stage2a-execution-authorization.json"));
        var helperHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PathInRepo("eng", "gate0", "G05Stage2AMatrixHelpers.psm1"))));
        using var tampered = new TempFile(authorization.Replace(helperHash, new string('F', 64), StringComparison.Ordinal));
        var result = RunPwsh($"Import-Module '{ModulePath()}'; Read-G05Stage2AExecutionAuthorization '{Escape(tampered.Path)}' '{Escape(PathInRepo())}'");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("helper", result.Output, StringComparison.OrdinalIgnoreCase);

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
        Assert.True(result.Output.Contains("implementation pending", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("exact role bytes", StringComparison.OrdinalIgnoreCase)
            || result.Output.Contains("effective authorization binding", StringComparison.OrdinalIgnoreCase), result.Output);
        Assert.DoesNotContain("ffmpeg process", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage2AArtifactsRemainProofOnlyAndDoNotReferenceLegacyAppenderOrProduct()
    {
        var paths = new[]
        {
            "eng/gate0/G05Stage2AMatrixHelpers.psm1",
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
}
