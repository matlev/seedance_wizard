using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0P2TextProofScriptTests
{
    [Fact]
    public void TextProofUsesOnlyPinnedManifestFontsAndExplicitAssMapping()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-P2TextProof.ps1"));

        Assert.Contains("Validate-FontProofArtifacts.ps1", script);
        Assert.Contains("fontselect", script);
        Assert.Contains("NotoSans-Regular", script);
        Assert.Contains("NotoSansArabic-Regular", script);
        Assert.Contains("NotoSansCJKsc-Regular", script);
        Assert.Contains("shaping=complex", script);
        Assert.Contains("simple-versus-complex", script);
        Assert.Contains("Negative missing-CJK control", script);
        Assert.Contains("Empty-fonts/no-fontsdir control", script);
        Assert.Contains("Assert-LoadedOnlyCleanFonts", script);
        Assert.Contains("same-provider binary-origin attestation", script);
        Assert.Contains("f3-arabic-shaping-oracle.ass", script);
        Assert.Contains("Get-ArtifactBindings", script);
        Assert.Contains("ambient/system font", script);
        Assert.Contains("f3-unicode-proof.ass", script);
        Assert.Contains("f3-text-layout.json", script);
        Assert.Contains("rawvideo", script);
        Assert.DoesNotContain("Get-Command", script, StringComparison.OrdinalIgnoreCase);
    }

    [Gate0RuntimeFact]
    public void TextProofAgainstApprovedP2PassesPredeclaredGoldenAndRejectsAmbientFallback()
    {
        var runtime = Environment.GetEnvironmentVariable("REELFORGE_GATE0_P2_RUNTIME_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(runtime));
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-TextProofTest", Guid.NewGuid().ToString("N"));
        var fixtures = Path.Combine(root, "fixtures");
        var output = Path.Combine(root, "text");
        try
        {
            Assert.Equal(0, Run(RepositoryPath("eng", "gate0", "Generate-Fixtures.ps1"), "-FfmpegPath", Path.Combine(runtime!, "bin", "ffmpeg.exe"), "-FfprobePath", Path.Combine(runtime!, "bin", "ffprobe.exe"), "-ApprovedRuntimeRoot", runtime!, "-OutputDirectory", fixtures).ExitCode);
            var result = Run(RepositoryPath("eng", "gate0", "Invoke-P2TextProof.ps1"), "-RuntimeRoot", runtime!, "-FixtureRoot", fixtures, "-OutputDirectory", output);
            Assert.True(result.ExitCode == 0, result.Output);
            using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "text-proof-evidence.json")));
            var rootElement = evidence.RootElement;
            Assert.Equal("passed", rootElement.GetProperty("status").GetString());
            Assert.Equal("553388ADF0479FA593051370685AEB34C36F916B5729E6D7D857C3BA572677BD", rootElement.GetProperty("positive").GetProperty("complexRenderSha256").GetString());
            Assert.True(rootElement.GetProperty("negativeMissingCjkControl").GetProperty("rejected").GetBoolean());
            Assert.True(rootElement.GetProperty("emptyFontsNoFontsdirControl").GetProperty("approvedTargetsAbsent").GetBoolean());
            Assert.Equal(3, rootElement.GetProperty("positive").GetProperty("lineBands").GetArrayLength());
            Assert.NotEmpty(rootElement.GetProperty("artifacts").EnumerateArray());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static (int ExitCode, string Output) Run(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-File"); start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        Assert.NotNull(directory); return Path.Combine([directory!.FullName, .. segments]);
    }
}
