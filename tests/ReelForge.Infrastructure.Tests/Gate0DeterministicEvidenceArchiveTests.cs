using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0DeterministicEvidenceArchiveTests
{
    [Fact]
    public void IdenticalInputsProduceIdenticalArchiveAndManifestBytesAtDifferentPaths()
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var first = RunArchive(corpus, "first", "en-US", "UTC");
            var second = RunArchive(corpus, "second", "tr-TR", "Pacific Standard Time");

            Assert.Equal(File.ReadAllBytes(first.Archive), File.ReadAllBytes(second.Archive));
            Assert.Equal(File.ReadAllBytes(first.Manifest), File.ReadAllBytes(second.Manifest));
        }
        finally { DeleteCorpus(corpus); }
    }

    [Fact]
    public void ManifestAndZipArePortableAndBindExactSortedContent()
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var result = RunArchive(corpus, "proof");
            var text = File.ReadAllText(result.Manifest);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("zip", root.GetProperty("archiveFormat").GetString());
            Assert.Equal("1980-01-01T00:00:00Z", root.GetProperty("entryTimestampUtc").GetString());
            Assert.DoesNotContain(corpus.Root, text, StringComparison.OrdinalIgnoreCase);
            var entries = root.GetProperty("entries").EnumerateArray().ToArray();
            Assert.Equal(["alpha.txt", "nested/beta.bin", "Ω-å.txt"], entries.Select(entry => entry.GetProperty("relativePath").GetString()));
            Assert.All(entries, entry =>
            {
                var path = entry.GetProperty("relativePath").GetString()!;
                Assert.False(Path.IsPathRooted(path));
                Assert.DoesNotContain("..", path.Split('/'));
                var original = Path.Combine(corpus.Source, path.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(new FileInfo(original).Length, entry.GetProperty("byteSize").GetInt64());
                Assert.Equal(Hash(original), entry.GetProperty("sha256").GetString());
            });
            Assert.Equal(new FileInfo(result.Archive).Length, root.GetProperty("archive").GetProperty("byteSize").GetInt64());
            Assert.Equal(Hash(result.Archive), root.GetProperty("archive").GetProperty("sha256").GetString());

            using var archive = ZipFile.OpenRead(result.Archive);
            Assert.Equal(entries.Select(entry => entry.GetProperty("relativePath").GetString()), archive.Entries.Select(entry => entry.FullName));
            foreach (var entry in archive.Entries)
            {
                var expected = entries.Single(item => item.GetProperty("relativePath").GetString() == entry.FullName);
                using var entryStream = entry.Open();
                Assert.Equal(expected.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(entryStream)));
                Assert.Equal(DateTimeOffset.Parse("1980-01-01T00:00:00Z", CultureInfo.InvariantCulture).UtcDateTime, entry.LastWriteTime.DateTime);
            }
        }
        finally { DeleteCorpus(corpus); }
    }

    [Fact]
    public void UtilityRejectsExistingOutputsAndEscapedOutputDirectory()
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var existing = Path.Combine(corpus.Output, "existing.zip");
            File.WriteAllText(existing, "existing");
            var failed = Invoke(corpus, corpus.Output, existing, Path.Combine(corpus.Output, "manifest.json"));
            Assert.NotEqual(0, failed.ExitCode);
            Assert.Contains("new paths", failed.Output, StringComparison.OrdinalIgnoreCase);

            var escapedOutput = Path.Combine(corpus.Root, "outside-output");
            Directory.CreateDirectory(escapedOutput);
            failed = Invoke(corpus, escapedOutput, Path.Combine(escapedOutput, "archive.zip"), Path.Combine(escapedOutput, "manifest.json"));
            Assert.NotEqual(0, failed.ExitCode);
            Assert.Contains("escaped ApprovedSourceRoot", failed.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteCorpus(corpus); }
    }

    [Theory]
    [InlineData("AfterArchiveTempWrite")]
    [InlineData("AfterArchiveTempValidation")]
    [InlineData("AfterManifestTempCreate")]
    [InlineData("AfterManifestTempWrite")]
    [InlineData("BeforeArchivePromotion")]
    public void UtilityCleansOnlyItsPartialTempsWhenAValidatedWriteFails(string failurePhase)
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var archive = Path.Combine(corpus.Output, "failure-" + failurePhase + ".zip");
            var manifest = Path.Combine(corpus.Output, "failure-" + failurePhase + ".json");
            var failed = Invoke(corpus, corpus.Output, archive, manifest, failurePhase: failurePhase);
            Assert.NotEqual(0, failed.ExitCode);
            Assert.False(File.Exists(archive));
            Assert.False(File.Exists(manifest));
            Assert.Empty(TemporaryFiles(corpus.Output));
        }
        finally { DeleteCorpus(corpus); }
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("Manifest")]
    public void UtilityNeverOverwritesARacedFinalAndCleansItsTemps(string racedFinal)
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var archive = Path.Combine(corpus.Output, "race-" + racedFinal + ".zip");
            var manifest = Path.Combine(corpus.Output, "race-" + racedFinal + ".json");
            var failed = Invoke(corpus, corpus.Output, archive, manifest, raceFinal: racedFinal);
            Assert.NotEqual(0, failed.ExitCode);
            var racedPath = racedFinal == "Archive" ? archive : manifest;
            Assert.Equal("raced-" + racedFinal, File.ReadAllText(racedPath));
            Assert.Empty(TemporaryFiles(corpus.Output));
            if (racedFinal == "Archive") Assert.False(File.Exists(manifest));
            else Assert.True(File.Exists(archive));
        }
        finally { DeleteCorpus(corpus); }
    }

    [Theory]
    [InlineData("Archive")]
    [InlineData("Manifest")]
    public void UtilityDoesNotDeleteAForeignTemporaryPathWhenExclusiveCreationCollides(string collidedTemporary)
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var archive = Path.Combine(corpus.Output, "collision-" + collidedTemporary + ".zip");
            var manifest = Path.Combine(corpus.Output, "collision-" + collidedTemporary + ".json");
            var failed = Invoke(corpus, corpus.Output, archive, manifest, precreateTemporary: collidedTemporary);
            Assert.NotEqual(0, failed.ExitCode);
            var foreignTemp = Assert.Single(TemporaryFiles(corpus.Output));
            Assert.Equal("raced-temporary-" + collidedTemporary, File.ReadAllText(foreignTemp));
            Assert.False(File.Exists(archive));
            Assert.False(File.Exists(manifest));
        }
        finally { DeleteCorpus(corpus); }
    }

    [ReelForge.Tests.WindowsReparsePointFact]
    public void UtilityRejectsReparsePointInSourceTree()
    {
        var corpus = CreateCorpus();
        try
        {
            CreateInput(corpus.Source);
            var target = Path.Combine(corpus.Root, "link-target");
            Directory.CreateDirectory(target);
            var link = Path.Combine(corpus.Source, "linked");
            Directory.CreateSymbolicLink(link, target);
            var failed = Invoke(corpus, corpus.Output, Path.Combine(corpus.Output, "archive.zip"), Path.Combine(corpus.Output, "manifest.json"));
            Assert.NotEqual(0, failed.ExitCode);
            Assert.Contains("reparse point", failed.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally { DeleteCorpus(corpus); }
    }

    private static (string Archive, string Manifest) RunArchive(Corpus corpus, string name, string? culture = null, string? timeZone = null)
    {
        var output = Path.Combine(corpus.Approved, name);
        Directory.CreateDirectory(output);
        var archive = Path.Combine(output, "evidence.zip");
        var manifest = Path.Combine(output, "evidence.manifest.json");
        var result = Invoke(corpus, output, archive, manifest, culture: culture, timeZone: timeZone);
        Assert.True(result.ExitCode == 0, result.Output);
        if (timeZone is not null) Assert.Contains("TEST_TZ=" + timeZone, result.Output, StringComparison.Ordinal);
        return (archive, manifest);
    }

    private static (int ExitCode, string Output) Invoke(Corpus corpus, string output, string archive, string manifest, string? failurePhase = null, string? raceFinal = null, string? precreateTemporary = null, string? culture = null, string? timeZone = null)
    {
        var parameters = $"-SourceRoot '{Quote(corpus.Source)}' -ApprovedSourceRoot '{Quote(corpus.Approved)}' -ArtifactRoot '{Quote(corpus.Artifact)}' -OutputDirectory '{Quote(output)}' -ArchivePath '{Quote(archive)}' -ManifestPath '{Quote(manifest)}'";
        if (failurePhase is not null) parameters += " -TestFailurePhase " + failurePhase;
        if (raceFinal is not null) parameters += " -TestRaceFinalPath " + raceFinal;
        if (precreateTemporary is not null) parameters += " -TestPrecreateTemporaryPath " + precreateTemporary;
        var culturePrefix = culture is null ? string.Empty : $"[cultureinfo]::CurrentCulture=[cultureinfo]'{culture}';[cultureinfo]::CurrentUICulture=[cultureinfo]'{culture}';";
        var environmentPrefix = timeZone is null ? string.Empty : "Write-Output ('TEST_TZ=' + [Environment]::GetEnvironmentVariable('TZ'));";
        var command = environmentPrefix + culturePrefix + $"& '{Quote(corpus.Script)}' {parameters}";
        var startInfo = new ProcessStartInfo("pwsh", $"-NoProfile -ExecutionPolicy Bypass -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (timeZone is not null) startInfo.Environment["TZ"] = timeZone;
        using var process = Process.Start(startInfo)!;
        var outputText = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, outputText);
    }

    private static Corpus CreateCorpus()
    {
        var root = Path.Combine(Path.GetTempPath(), "ReelForge-ArchiveTests-" + Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "repo");
        var approved = Path.Combine(root, "approved");
        var artifact = Path.Combine(root, "artifact");
        var source = Path.Combine(approved, "source");
        var output = Path.Combine(approved, "output");
        var gate0 = Path.Combine(repository, "eng", "gate0");
        Directory.CreateDirectory(gate0);
        File.WriteAllText(Path.Combine(repository, ".gate0-deterministic-archive-test-sentinel"), "isolated test corpus\n");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(artifact);
        var script = Path.Combine(gate0, "New-Gate0DeterministicEvidenceArchive.ps1");
        File.Copy(Path.Combine(RepoRoot(), "eng", "gate0", "New-Gate0DeterministicEvidenceArchive.ps1"), script);
        return new Corpus(root, approved, artifact, source, output, script);
    }

    private static void CreateInput(string source)
    {
        File.WriteAllText(Path.Combine(source, "alpha.txt"), "alpha\n");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllBytes(Path.Combine(source, "nested", "beta.bin"), [0, 1, 2, 3, 255]);
        File.WriteAllText(Path.Combine(source, "Ω-å.txt"), "café — مرحبا\n");
    }

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    private static string[] TemporaryFiles(string directory) => Directory.EnumerateFiles(directory, ".*.tmp-*", SearchOption.TopDirectoryOnly).ToArray();
    private static void DeleteCorpus(Corpus corpus)
    {
        foreach (var path in new[] { corpus.Root, corpus.Approved, corpus.Artifact })
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
    private static string Quote(string text) => text.Replace("'", "''");
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed record Corpus(string Root, string Approved, string Artifact, string Source, string Output, string Script);
}
