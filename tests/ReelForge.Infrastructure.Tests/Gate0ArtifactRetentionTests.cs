using System.Diagnostics;
using System.Text.Json;

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
        Assert.Equal("interim-local-only", storage.GetProperty("classification").GetString());
        Assert.False(storage.GetProperty("productionArtifactRepository").GetBoolean());
        Assert.False(storage.GetProperty("hostedCiEligible").GetBoolean());
        Assert.False(storage.GetProperty("separatelyBackedUpPrivateCopyVerified").GetBoolean());
        Assert.Equal("incomplete", storage.GetProperty("twoCopyRetentionCondition").GetString());
        Assert.False(storage.GetProperty("temporaryProviderR2Permitted").GetBoolean());

        var groups = root.GetProperty("groups").EnumerateArray().ToArray();
        Assert.Equal(5, groups.Length);
        Assert.Equal(
            [
                "P2.BtbnLgplShared.WindowsX64.20260820",
                "Gate0.Fixtures.F1-F8.20260824",
                "Gate0.G04.Input.Corrected.20260825",
                "P3.LibjpegTurboCjpeg.WindowsX64.3.2.0",
                "Gate0.RepositoryContracts.20260825",
            ],
            groups.Select(group => group.GetProperty("groupId").GetString()));

        var files = groups.SelectMany(group => group.GetProperty("files").EnumerateArray()).ToArray();
        Assert.Equal(2541, files.Length);
        Assert.Equal(453086511, files.Sum(file => file.GetProperty("size").GetInt64()));
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
            Assert.Equal(0, result.ExitCode);
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
