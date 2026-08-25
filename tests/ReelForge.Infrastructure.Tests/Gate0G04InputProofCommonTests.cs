using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04InputProofCommonTests
{
    [Fact]
    public void CommonModuleExposesTheReviewedSafetyAndEvidenceSeams()
    {
        var script = File.ReadAllText(RepositoryPath("eng", "gate0", "input-proof", "Common.ps1"));

        foreach (var name in new[]
        {
            "Assert-G04NewOutsideRepositoryDirectory", "Assert-G04RootedNonReparseDirectory",
            "Resolve-G04RuntimeTool", "Read-G04InputContract", "Test-G04FixtureClosure",
            "Invoke-G04RecordedCommand", "Get-G04ArtifactRecord", "Test-G04UndeclaredDiagnostics"
        }) Assert.Contains($"function {name}", script);

        Assert.Contains("PATH fallback is prohibited", script);
        Assert.Contains("reparse-point", script);
        Assert.Contains("guaranteedCases = 256", script);
        Assert.Contains("fixtureRecipes = 163", script);
        Assert.Contains("oracleProfiles = 24", script);
        Assert.Contains("Fixture report hash or length mismatch", script);
        Assert.Contains("Undeclared diagnostic", script);
    }

    [Fact]
    public void CommonModuleRejectsUnsafeOutputAndInvalidRuntimeToolPaths()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
        var output = RunPowerShell($". '{Escape(module)}'; Assert-G04NewOutsideRepositoryDirectory '{Escape(RepositoryPath())}'");
        Assert.NotEqual(0, output.ExitCode);
        Assert.Contains("outside the repository", output.AllOutput, StringComparison.OrdinalIgnoreCase);

        var tool = RunPowerShell($". '{Escape(module)}'; Resolve-G04RuntimeTool 'ffmpeg.exe' 'ffmpeg.exe' '{Escape(Path.GetTempPath())}'");
        Assert.NotEqual(0, tool.ExitCode);
        Assert.Contains("explicit rooted", tool.AllOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PATH fallback", tool.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommonModuleReadsTheClosedApprovedContract()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
        var contract = RepositoryPath("eng", "gate0", "g0.4-input-proof-contract.json");
        var result = RunPowerShell($". '{Escape(module)}'; $c=Read-G04InputContract '{Escape(contract)}'; Write-Output $c.profileId");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("P2.BtbnLgplShared.WindowsX64.20260820", result.StandardOutput);
    }

    [Fact]
    public void CommonModuleRejectsForgedFixtureReportAndInventoryClosure()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G04Common", Guid.NewGuid().ToString("N"));
        var fixtures = Path.Combine(root, "fixtures");
        var inventory = Path.Combine(root, "fixture-source-inventory.json");
        Directory.CreateDirectory(fixtures);
        try
        {
            var payload = Encoding.UTF8.GetBytes("fixture closure proof");
            File.WriteAllBytes(Path.Combine(fixtures, "a.bin"), payload);
            var hash = Convert.ToHexString(SHA256.HashData(payload));
            File.WriteAllText(inventory, $$"""{"files":[{"path":"a.bin","length":{{payload.Length}},"sha256":"{{hash}}"}]}""");
            var inventoryHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(inventory)));
            File.WriteAllText(Path.Combine(fixtures, "generated-fixture-report.json"), $$"""{"profileId":"P2.BtbnLgplShared.WindowsX64.20260820","externalMediaCommandsExecuted":false,"approvedInventory":{"path":"eng/gate0/fixture-source-inventory.json","sha256":"{{inventoryHash}}"},"sourceFiles":[{"path":"a.bin","length":{{payload.Length + 1}},"sha256":"{{hash}}"}]}""");

            var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
            var result = RunPowerShell($". '{Escape(module)}'; Test-G04FixtureClosure '{Escape(fixtures)}' '{Escape(inventory)}' | Out-Null");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("hash or length mismatch", result.AllOutput, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("[h264 @ 000] error while decoding MB 1 2")]
    [InlineData("timestamp discontinuity repaired")]
    [InlineData("Invalid data found when processing input")]
    public void CommonModuleRejectsUndeclaredMediaRepairDiagnostics(string diagnostic)
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
        var result = RunPowerShell($". '{Escape(module)}'; Test-G04UndeclaredDiagnostics -Stderr '{Escape(diagnostic)}' -AllowedPatterns @() | Out-Null");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Undeclared diagnostic", result.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommonModuleAllowsOnlyAnExplicitDiagnosticException()
    {
        var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
        var result = RunPowerShell($". '{Escape(module)}'; Test-G04UndeclaredDiagnostics -Stderr 'timestamp discontinuity repaired' -AllowedPatterns @('^timestamp discontinuity repaired$') | Out-Null");

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void CommonModuleRecordsTheFirstCommandIntoAnEmptyGenericList()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G04CommonCommand", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        try
        {
            var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
            var command = $". '{Escape(module)}'; $context=[pscustomobject]@{{Output='{Escape(root)}';Work='{Escape(root)}';Logs='{Escape(logs)}';Commands=[Collections.Generic.List[object]]::new()}}; Invoke-G04RecordedCommand -Context $context -Name 'first-command' -Executable (Join-Path $PSHOME 'pwsh.exe') -Arguments @('-NoProfile','-Command','exit 0') -Components @{{ purpose='test' }} | Out-Null; if($context.Commands.Count -ne 1) {{ throw 'empty command list did not retain first record' }}";
            var result = RunPowerShell(command);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(logs, "first-command.stdout.txt")));
            Assert.True(File.Exists(Path.Combine(logs, "first-command.stderr.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CommonModuleRecordsTheFirstCommandIntoAnEmptyGenericDictionaryList()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-G04CommonDictionaryCommand", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        try
        {
            var module = RepositoryPath("eng", "gate0", "input-proof", "Common.ps1");
            var command = $". '{Escape(module)}'; $context=@{{Output='{Escape(root)}';Work='{Escape(root)}';Logs='{Escape(logs)}';Commands=[Collections.Generic.List[object]]::new()}}; Invoke-G04RecordedCommand -Context $context -Name 'first-dictionary-command' -Executable (Join-Path $PSHOME 'pwsh.exe') -Arguments @('-NoProfile','-Command','exit 0') -Components @{{ purpose='test' }} | Out-Null; if($context['Commands'].Count -ne 1) {{ throw 'empty dictionary command list did not retain first record' }}";
            var result = RunPowerShell(command);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(logs, "first-dictionary-command.stdout.txt")));
            Assert.True(File.Exists(Path.Combine(logs, "first-dictionary-command.stderr.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static ProcessResult RunPowerShell(string command)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        if (directory is null) throw new DirectoryNotFoundException("Could not locate repository root.");
        return Path.Combine([directory.FullName, .. segments]);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string AllOutput => StandardOutput + StandardError;
    }
}
