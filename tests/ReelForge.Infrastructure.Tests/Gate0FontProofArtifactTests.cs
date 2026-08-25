using System.Diagnostics;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0FontProofArtifactTests
{
    private static readonly string[] ExpectedLocales = ["ar", "und-Latn", "zh-Hans"];
    private static readonly string[] ExpectedFontPaths = ["NotoSans-Regular.ttf", "NotoSansArabic-Regular.ttf", "NotoSansCJKsc-Regular.otf"];
    private static readonly string[] ExpectedLicenseArchiveMembers = ["OFL.txt", "OFL.txt", "LICENSE"];

    [Fact]
    public void ManifestPinsApprovedOflOnlyFontStackAndClosedArtifactSet()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(RepositoryPath("eng", "gate0", "font-proof-artifacts.json")));
        var root = manifest.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("P2.BtbnLgplShared.WindowsX64.20260820", root.GetProperty("profileId").GetString());
        Assert.True(root.GetProperty("scope").GetProperty("proofOnly").GetBoolean());
        Assert.True(root.GetProperty("scope").GetProperty("systemFontFallbackProhibited").GetBoolean());
        Assert.False(root.GetProperty("scope").GetProperty("networkAccessPermitted").GetBoolean());
        Assert.False(root.GetProperty("scope").GetProperty("pathDiscoveryPermitted").GetBoolean());
        Assert.Equal("optional-blocked", root.GetProperty("scope").GetProperty("colorEmojiStatus").GetString());

        var archives = root.GetProperty("sourceArchives").EnumerateArray().ToArray();
        Assert.Equal(3, archives.Length);
        Assert.All(archives, archive =>
        {
            Assert.StartsWith("https://github.com/", archive.GetProperty("officialReleaseUrl").GetString());
            Assert.StartsWith("https://github.com/", archive.GetProperty("officialArchiveUrl").GetString());
            Assert.Matches("^[0-9A-F]{64}$", archive.GetProperty("sha256").GetString());
            Assert.True(archive.GetProperty("byteLength").GetInt64() > 0);
        });

        var licenses = root.GetProperty("licenses").EnumerateArray().ToArray();
        Assert.Equal(3, licenses.Length);
        Assert.All(licenses, license =>
        {
            Assert.Equal("OFL-1.1", license.GetProperty("spdx").GetString());
            Assert.False(Path.IsPathRooted(license.GetProperty("sourceArchiveMemberPath").GetString()));
            Assert.DoesNotContain("..", license.GetProperty("sourceArchiveMemberPath").GetString()!.Split('/'));
        });

        var fonts = root.GetProperty("fonts").EnumerateArray().ToArray();
        Assert.Equal(3, fonts.Length);
        Assert.Equal(ExpectedLocales, fonts.Select(font => font.GetProperty("locale").GetString()).Order());
        Assert.Equal(
            ExpectedFontPaths,
            fonts.Select(font => font.GetProperty("relativePath").GetString()).ToArray());
        Assert.All(fonts, font =>
        {
            Assert.False(Path.IsPathRooted(font.GetProperty("sourceArchiveMemberPath").GetString()));
            Assert.DoesNotContain("..", font.GetProperty("sourceArchiveMemberPath").GetString()!.Split('/'));
        });

        Assert.Equal(
            "NotoSans/hinted/ttf/NotoSans-Regular.ttf",
            fonts.Single(font => font.GetProperty("id").GetString() == "NotoSans-Regular").GetProperty("sourceArchiveMemberPath").GetString());
        Assert.Equal(
            "NotoSansArabic/hinted/ttf/NotoSansArabic-Regular.ttf",
            fonts.Single(font => font.GetProperty("id").GetString() == "NotoSansArabic-Regular").GetProperty("sourceArchiveMemberPath").GetString());
        Assert.Equal(
            "NotoSansCJKsc-Regular.otf",
            fonts.Single(font => font.GetProperty("id").GetString() == "NotoSansCJKsc-Regular").GetProperty("sourceArchiveMemberPath").GetString());
        Assert.Equal(
            ExpectedLicenseArchiveMembers,
            licenses.Select(license => license.GetProperty("sourceArchiveMemberPath").GetString()).ToArray());
    }

    [Fact]
    public void ValidatorAcceptsTheCheckedInClosedArtifactSet()
    {
        var result = RunValidator(RepositoryPath("eng", "gate0", "artifacts", "fonts"));

        Assert.Equal(0, result.ExitCode);
        using var evidence = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("validated", evidence.RootElement.GetProperty("status").GetString());
        Assert.True(evidence.RootElement.GetProperty("systemFontFallbackProhibited").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("networkAccessPermitted").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("pathDiscoveryPermitted").GetBoolean());
        Assert.Equal(7, evidence.RootElement.GetProperty("filesValidated").GetArrayLength());
    }

    [Fact]
    public void ValidatorRejectsTamperedMissingAndAdditionalArtifacts()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-FontArtifactTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var tamperedRoot = CopyArtifactRoot(temporaryRoot, "tampered");
            var tamperedFont = Path.Combine(tamperedRoot, "NotoSans-Regular.ttf");
            using (var stream = File.Open(tamperedFont, FileMode.Open, FileAccess.ReadWrite))
            {
                var firstByte = stream.ReadByte();
                Assert.NotEqual(-1, firstByte);
                stream.Position = 0;
                stream.WriteByte((byte)(firstByte ^ 0xFF));
            }
            var tampered = RunValidator(tamperedRoot);
            Assert.NotEqual(0, tampered.ExitCode);
            Assert.Contains("SHA-256 does not match", tampered.StandardError, StringComparison.OrdinalIgnoreCase);

            var missingRoot = CopyArtifactRoot(temporaryRoot, "missing");
            File.Delete(Path.Combine(missingRoot, "NotoSansArabic-Regular.ttf"));
            var missing = RunValidator(missingRoot);
            Assert.NotEqual(0, missing.ExitCode);
            Assert.Contains("missing required file", missing.StandardError, StringComparison.OrdinalIgnoreCase);

            var additionalRoot = CopyArtifactRoot(temporaryRoot, "additional");
            File.WriteAllText(Path.Combine(additionalRoot, "unreviewed-font.ttf"), "not a reviewed proof artifact");
            var additional = RunValidator(additionalRoot);
            Assert.NotEqual(0, additional.ExitCode);
            Assert.Contains("additional file", additional.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void ValidatorRejectsTraversalAndRootedManifestPaths()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-FontManifestTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var approvedManifest = File.ReadAllText(RepositoryPath("eng", "gate0", "font-proof-artifacts.json"));

        try
        {
            var traversalManifest = Path.Combine(temporaryRoot, "traversal-manifest.json");
            File.WriteAllText(
                traversalManifest,
                approvedManifest.Replace(
                    "\"relativePath\": \"NotoSans-Regular.ttf\"",
                    "\"relativePath\": \"../escape.ttf\"",
                    StringComparison.Ordinal));
            var traversal = RunValidator(RepositoryPath("eng", "gate0", "artifacts", "fonts"), traversalManifest);
            Assert.NotEqual(0, traversal.ExitCode);
            Assert.Contains("unsafe path", traversal.StandardError, StringComparison.OrdinalIgnoreCase);

            var rootedManifest = Path.Combine(temporaryRoot, "rooted-manifest.json");
            File.WriteAllText(
                rootedManifest,
                approvedManifest.Replace(
                    "\"sourceArchiveMemberPath\": \"NotoSans/hinted/ttf/NotoSans-Regular.ttf\"",
                    "\"sourceArchiveMemberPath\": \"C:/escape.ttf\"",
                    StringComparison.Ordinal));
            var rooted = RunValidator(RepositoryPath("eng", "gate0", "artifacts", "fonts"), rootedManifest);
            Assert.NotEqual(0, rooted.ExitCode);
            Assert.Contains("relative path", rooted.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [global::ReelForge.Tests.WindowsReparsePointFact]
    public void ValidatorRejectsReparsePointArtifactRoot()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge-Gate0-FontReparseTests", Guid.NewGuid().ToString("N"));
        var linkPath = Path.Combine(temporaryRoot, "artifact-root-link");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            Directory.CreateSymbolicLink(linkPath, RepositoryPath("eng", "gate0", "artifacts", "fonts"));
            var result = RunValidator(linkPath);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse point", result.StandardError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(linkPath)) Directory.Delete(linkPath);
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string CopyArtifactRoot(string temporaryRoot, string name)
    {
        var destination = Path.Combine(temporaryRoot, name);
        foreach (var source in Directory.GetFiles(RepositoryPath("eng", "gate0", "artifacts", "fonts"), "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(RepositoryPath("eng", "gate0", "artifacts", "fonts"), source);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        return destination;
    }

    private static ProcessResult RunValidator(string artifactRoot, string? manifestPath = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-File", RepositoryPath("eng", "gate0", "Validate-FontProofArtifacts.ps1"),
            "-ArtifactRoot", artifactRoot,
            "-ManifestPath", manifestPath ?? RepositoryPath("eng", "gate0", "font-proof-artifacts.json")
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the font proof artifact validator.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
