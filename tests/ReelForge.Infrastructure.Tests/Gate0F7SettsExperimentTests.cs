using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0F7SettsExperimentTests
{
    [Fact]
    public void ContractLimitsSettsToTheSixDirectF7CasesAndTargetsPtsNotOrdinal()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "f7-setts-experiment-contract.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Gate0.G04.F7.Setts.20260825", root.GetProperty("experimentId").GetString());
        Assert.True(root.GetProperty("proofOnly").GetBoolean());

        var mapping = root.GetProperty("componentMapping");
        Assert.Equal(["setts"], mapping.GetProperty("bitstreamFilters").EnumerateArray().Select(item => item.GetString()));
        var setts = mapping.GetProperty("setts");
        Assert.Equal("libavcodec/bsf/setts.c", setts.GetProperty("sourceFile").GetString());
        Assert.Equal("LGPL-2.1-or-later", setts.GetProperty("sourceLicense").GetString());
        Assert.Contains("LGPLv3-path", setts.GetProperty("p2BinaryLicensePath").GetString());
        var expression = setts.GetProperty("expression").GetString()!;
        Assert.Contains("eq(PTS\\,1200)", expression);
        Assert.Contains("800\\,DURATION", expression);
        Assert.DoesNotContain("eq(N", expression, StringComparison.Ordinal);
        Assert.Contains("packet ordinal is prohibited", setts.GetProperty("targetSelection").GetString(), StringComparison.OrdinalIgnoreCase);

        using var p2Document = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "manifests", "p2-btbn-lgplv3-shared-windows-x64-20260820.json")));
        var p2 = p2Document.RootElement;
        Assert.Equal(p2.GetProperty("ffmpegSourceCommit").GetString(), setts.GetProperty("ffmpegSourceCommit").GetString());
        Assert.Equal("LGPLv3-path", p2.GetProperty("licensePath").GetString());
        Assert.Contains("--enable-version3", p2.GetProperty("configuration").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-gpl", p2.GetProperty("configuration").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("--enable-nonfree", p2.GetProperty("configuration").GetString(), StringComparison.Ordinal);

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(6, cases.Length);
        Assert.Equal(4, cases.Count(item => item.GetProperty("muxer").GetString() == "mp4"));
        Assert.Equal(2, cases.Count(item => item.GetProperty("muxer").GetString() == "webm"));
        Assert.All(cases, item => Assert.EndsWith("VFR_OFFSET", item.GetProperty("id").GetString(), StringComparison.Ordinal));
        Assert.DoesNotContain(cases, item => item.GetProperty("id").GetString()!.Contains("MKV", StringComparison.Ordinal));

        var semantics = root.GetProperty("requiredSemantics");
        Assert.Equal([1000, 1040, 1120, 1130, 1200], semantics.GetProperty("presentationTimestamps").EnumerateArray().Select(item => item.GetInt32()));
        Assert.Equal(800, semantics.GetProperty("terminalPacketDuration").GetInt32());
        Assert.Equal(2000, semantics.GetProperty("terminalPresentationEnd").GetInt32());
        Assert.Contains(root.GetProperty("stopConditions").EnumerateArray(), item => item.GetString()!.Contains("any direct MP4 or WebM case fails", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunnerRecordsPostMuxIdentityOraclesAndRejectsUnsafeOutputRoots()
    {
        var scriptPath = PathInRepo("eng", "gate0", "Invoke-P2F7SettsExperiment.ps1");
        var script = File.ReadAllText(scriptPath);
        foreach (var required in new[]
        {
            "Test-Gate0ArtifactRetention.ps1",
            "Validate-P2Runtime.ps1",
            "preflight-setts-list",
            "preflight-setts-help",
            "packets_and_frames",
            "ffprobe JSON lacks both split and combined packet/frame arrays",
            "The setts source commit mapping does not match exact P2",
            "requires the exact P2 LGPLv3 license path",
            "Compare-PacketStreams",
            "Packet PTS or DTS changed",
            "Packet duration did not meet the bounded setts contract",
            "Packet payload hashes are not unique enough",
            "Decoded video frame identities changed",
            "Decoded audio identity changed",
            "Automatically inserted bitstream filter",
            "targetSelection = 'unique input video packet with PTS=1200, never packet ordinal'",
            "The Matroska pilot remains blocked",
        }) Assert.Contains(required, script);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-c:v','h264_nvenc", script, StringComparison.OrdinalIgnoreCase);

        var quotedScript = scriptPath.Replace("'", "''", StringComparison.Ordinal);
        var parser = RunPowerShell($"$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('{quotedScript}',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){{$errors|% Message;exit 1}}");
        Assert.Equal(0, parser.ExitCode);

        var repository = PathInRepo();
        var artifactRoot = Path.Combine(Directory.GetParent(repository)!.FullName, "ReelForge.Gate0Artifacts");
        var repositoryBoundary = RunRunner(scriptPath, artifactRoot, repository);
        Assert.NotEqual(0, repositoryBoundary.ExitCode);
        Assert.Contains("outside the repository", repositoryBoundary.Output, StringComparison.OrdinalIgnoreCase);

        var artifactBoundary = RunRunner(scriptPath, artifactRoot, Path.Combine(artifactRoot, "unmanifested-proof"));
        Assert.NotEqual(0, artifactBoundary.ExitCode);
        Assert.Contains("outside the retained corpus", artifactBoundary.Output, StringComparison.OrdinalIgnoreCase);
    }

    [ReelForge.Tests.WindowsReparsePointFact]
    public void RunnerRejectsOutputThroughAReparsePointParent()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-F7-Setts-Reparse", Guid.NewGuid().ToString("N"));
        var linkParent = Path.Combine(temporaryRoot, "link-parent");
        var physicalTarget = Path.Combine(temporaryRoot, "physical-target");
        Directory.CreateDirectory(temporaryRoot);
        Directory.CreateDirectory(physicalTarget);
        Directory.CreateSymbolicLink(linkParent, physicalTarget);
        try
        {
            var repository = PathInRepo();
            var artifactRoot = Path.Combine(Directory.GetParent(repository)!.FullName, "ReelForge.Gate0Artifacts");
            var result = RunRunner(PathInRepo("eng", "gate0", "Invoke-P2F7SettsExperiment.ps1"), artifactRoot, Path.Combine(linkParent, "proof"));
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse-point ancestor", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(physicalTarget, "proof")));
        }
        finally
        {
            if (Directory.Exists(linkParent)) new DirectoryInfo(linkParent).Delete();
            if (Directory.Exists(physicalTarget)) Directory.Delete(physicalTarget, true);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    private static (int ExitCode, string Output) RunRunner(string script, string artifactRoot, string output) =>
        RunProcess("pwsh", ["-NoProfile", "-File", script, "-ArtifactRoot", artifactRoot, "-OutputDirectory", output]);

    private static (int ExitCode, string Output) RunPowerShell(string command) =>
        RunProcess("pwsh", ["-NoProfile", "-Command", command]);

    private static (int ExitCode, string Output) RunProcess(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
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
