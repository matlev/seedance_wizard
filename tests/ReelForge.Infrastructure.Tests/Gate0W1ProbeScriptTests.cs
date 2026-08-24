namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0W1ProbeScriptTests
{
    [Fact]
    public void ProbeScriptUsesExplicitRuntimeAndCapabilitySelections()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "Invoke-W1MediaFoundationProbe.ps1"));

        Assert.Contains("RuntimeRoot", script);
        Assert.Contains("OutputDirectory", script);
        Assert.Contains("Validate-P2Runtime.ps1", script);
        Assert.Contains("Generate-Fixtures.ps1", script);
        Assert.Contains("h264_mf", script);
        Assert.Contains("aac_mf", script);
        Assert.Contains("'ppm'", script);
        Assert.Contains("'pcm_s16le'", script);
        Assert.Contains(".partial", script);
        Assert.Contains("Move-Item", script);
        Assert.Contains("componentPresence", script);
        Assert.Contains("containerPresence", script);
        Assert.Contains("muxerPresence", script);
        Assert.Contains("observedStreamCodecs", script);
        Assert.Contains("basic-wrapper-supported", script);
        Assert.Contains("basic-wrapper-unsupported", script);
        Assert.Contains("independentPlayback", script);
        Assert.Contains("hardwareDriverProfile", script);
        Assert.Contains("rateControl", script);
        Assert.Contains("'-map'", script);
        Assert.Contains("'0:v:0'", script);
        Assert.Contains("'1:a:0'", script);
        Assert.Contains("'0:a:0'", script);
        Assert.Contains("probe.json", script);
        Assert.Contains("w1-evidence.json", script);
        Assert.Contains("portableBaseline = $false", script);
        Assert.DoesNotContain("Get-Command ffmpeg", script);
        Assert.DoesNotContain("Get-Command ffprobe", script);
    }

    [Fact]
    public void ProbeScriptRejectsRepositoryOutputAndMissingRuntime()
    {
        var script = RepositoryPath("eng", "gate0", "Invoke-W1MediaFoundationProbe.ps1");
        var result = RunPowerShell($"-File \"{script}\" -RuntimeRoot \"{RepositoryPath("eng") }\" -OutputDirectory \"{RepositoryPath()}\"");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the repository", result.StandardError + result.StandardOutput);
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShell(string arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = $"-NoProfile {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }
}
