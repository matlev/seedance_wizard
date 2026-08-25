using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0P3JpegInputProofTests
{
    [Fact]
    public void ContractBindsTheTwoAuthorizedRowsAndExactProducerBoundaries()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "gate0", "p3-jpeg-proof-contract.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("proofOnly").GetBoolean());

        var p2 = root.GetProperty("profiles").GetProperty("p2");
        Assert.Equal("mjpeg", p2.GetProperty("nativeDecoderUnderTest").GetString());
        Assert.Equal("image2", p2.GetProperty("inputDemuxer").GetString());
        var p3 = root.GetProperty("profiles").GetProperty("p3");
        Assert.Equal("fixture-production-only", p3.GetProperty("role").GetString());
        var closure = p3.GetProperty("closure").EnumerateArray().ToArray();
        Assert.Equal(["cjpeg.exe", "jpeg62.dll"], closure.Select(item => item.GetProperty("relativePath").GetString()));
        Assert.Equal("97C382C511F6D597E97141F4064C8E67ED64617D1D51793C1DF183004E21BF0F", closure[0].GetProperty("sha256").GetString());
        Assert.Equal(185856, closure[0].GetProperty("size").GetInt32());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(["I-JPEG-PROGRESSIVE-420", "I-JPEG-EXIF-ORIENTATION"], cases.Select(item => item.GetProperty("id").GetString()));
        var progressive = cases[0];
        Assert.Equal(["-quality", "90", "-dct", "int", "-progressive", "-sample", "2x2,1x1,1x1"], progressive.GetProperty("commandArguments").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("C2", progressive.GetProperty("requiredSofMarker").GetString());
        var sof = progressive.GetProperty("requiredSof");
        Assert.Equal("C2", sof.GetProperty("marker").GetString());
        Assert.Equal(8, sof.GetProperty("precision").GetInt32());
        Assert.Equal([(1, 2, 2), (2, 1, 1), (3, 1, 1)], sof.GetProperty("components").EnumerateArray().Select(component => (component.GetProperty("id").GetInt32(), component.GetProperty("horizontalSampling").GetInt32(), component.GetProperty("verticalSampling").GetInt32())));
        var orientation = cases[1];
        Assert.Equal(6, orientation.GetProperty("orientation").GetInt32());
        Assert.Equal("immediately after SOI", orientation.GetProperty("app1Placement").GetString());
        Assert.Equal("04F7B24A5C630F58A16DB75706E15F30658B9521D42395F007912E0A75EFE617", orientation.GetProperty("baselineSha256").GetString());
        Assert.Equal("P3.LibjpegTurboCjpeg.WindowsX64.3.2.0", p3.GetProperty("retentionGroupId").GetString());
        Assert.Equal(2361008, p3.GetProperty("installerSize").GetInt32());
        Assert.Equal("Valid", p3.GetProperty("authenticode").GetProperty("status").GetString());

        Assert.Contains(root.GetProperty("boundaries").EnumerateArray(), item => item.GetString()!.Contains("No system tool, PATH discovery", StringComparison.Ordinal));
    }

    [Fact]
    public void RunnerAndWriterParseAndRetainTheRequiredSemanticOracles()
    {
        var runnerPath = PathInRepo("eng", "gate0", "Invoke-P3JpegInputProof.ps1");
        var writerPath = PathInRepo("eng", "gate0", "Write-ExifOrientation.ps1");
        foreach (var path in new[] { runnerPath, writerPath })
        {
            var quoted = path.Replace("'", "''", StringComparison.Ordinal);
            var result = RunPowerShell($"$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseFile('{quoted}',[ref]$tokens,[ref]$errors)|Out-Null;if($errors.Count){{$errors|% Message;exit 1}}");
            Assert.True(result.ExitCode == 0, $"PowerShell parser rejected {path}:{Environment.NewLine}{result.Output}");
        }

        var script = File.ReadAllText(runnerPath);
        foreach (var required in new[]
        {
            "Test-Gate0ArtifactRetention.ps1", "Validate-P2Runtime.ps1", "p3-jpeg-proof-contract.json",
            "P3 cjpeg progressive fixture authoring", "p3-cjpeg-version", "Read-SofStructure", "Assert-ExactProgressive420Sof", "if($m-eq 0xDA){break}",
            "$sof=(@($Sofs))[0]", "[string]$SourcePath", "'-of','json',$SourcePath",
            "exactly one SOF segment", "explicit image2 inspection", "-f','image2", "-c:v','mjpeg",
            "noautorotate", "RotateCw", "exact 90-clockwise", "Pinned baseline 4:2:0 JPEG hash mismatch",
            "outside the retained corpus", "reparse-point ancestor",
            "noImageEncoder=$true", "-show_frames", "-show_packets", "-show_data_hash", "progressivePath.partial",
            "P3 retained installer identity", "signerThumbprint", "timestampThumbprint", "not-run-shared-preflight",
            "Assert-ExactNewMediaOutput", "RetainedArtifact $p3.installerRelativePath", "streamCount", "packets.Count-ne 1",
            "$inspect=Inspect 'inspect-progressive-420' $progressivePath;$semantic[$progressive.id]=$true",
            "$inspectO=Inspect 'inspect-orientation-6' $orientationPath;$semantic[$orientation.id]=$true",
            "executedSemanticProof=($semantic"
        }) Assert.Contains(required, script);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[string]$Input", script, StringComparison.Ordinal);
        Assert.Contains("all input bytes after SOI remain byte-identical", File.ReadAllText(writerPath), StringComparison.Ordinal);
        Assert.DoesNotContain("libx264", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Drawing", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriterInsertsOnlyTheMinimalOrientationSixApp1AfterSoi()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-P3-Exif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.jpg");
            var output = Path.Combine(root, "output.jpg");
            var source = new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0x00, 0x02, 0xff, 0xd9 };
            File.WriteAllBytes(input, source);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
            var result = RunProcess("pwsh", ["-NoProfile", "-File", PathInRepo("eng", "gate0", "Write-ExifOrientation.ps1"), "-InputJpeg", input, "-OutputJpeg", output, "-ExpectedInputSha256", hash, "-Orientation", "6"]);
            Assert.Equal(0, result.ExitCode);
            var actual = File.ReadAllBytes(output);
            Assert.Equal(source.Length + 36, actual.Length);
            Assert.Equal(new byte[] { 0xff, 0xd8, 0xff, 0xe1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 }, actual[..12]);
            Assert.Equal((byte)6, actual[30]);
            Assert.Equal(source[2..], actual[38..]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void WriterRejectsAnyExistingApp1InsteadOfAddingAnother()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-P3-Exif-App1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.jpg");
            var output = Path.Combine(root, "output.jpg");
            var source = new byte[] { 0xff, 0xd8, 0xff, 0xe1, 0x00, 0x02, 0xff, 0xd9 };
            File.WriteAllBytes(input, source);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source));
            var result = RunProcess("pwsh", ["-NoProfile", "-File", PathInRepo("eng", "gate0", "Write-ExifOrientation.ps1"), "-InputJpeg", input, "-OutputJpeg", output, "-ExpectedInputSha256", hash, "-Orientation", "6"]);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("already contains an APP1 segment", result.Output, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static (int ExitCode, string Output) RunPowerShell(string command) => RunProcess("pwsh", ["-NoProfile", "-Command", command]);
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
