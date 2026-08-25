using System.Security.Cryptography;
using System.Text.Json;

namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04WindowsMediaPlayerObservationTests
{
    [Fact]
    public void WmpObservationIsExplicitlyOptionalAndBindsV2ProvenanceBeforeComUse()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-G04WindowsMediaPlayerObservation.ps1"));
        Assert.Contains("WMPlayer.OCX", script); Assert.Contains("ApartmentState]::STA", script);
        Assert.Contains("Assert-PlaybackCorpusProvenance", script); Assert.Contains("schemaVersion -ne 2", script);
        Assert.Contains("bound-artifact provenance", script); Assert.Contains("CurrentVersion", script);
        Assert.Contains("Environment.OSVersion", script); Assert.Contains("FinalReleaseComObject", script);
        Assert.Contains("inherited-blocked-not-executed", script); Assert.Contains("WebM is capability-qualified", script);
        Assert.Contains("No audible/perceptual A/V-sync conclusion", script);
        Assert.DoesNotContain("Start-Process", script); Assert.DoesNotContain("launchURL", script);
    }

    [Fact]
    public void WmpObservationAcceptsRepresentativeFreshV2ProvenanceBeforeStaDisposition()
    {
        using var fixture = new CorpusFixture();
        var result = RunScript(fixture.CorpusRoot, fixture.NewOutput());
        var evidencePath = Path.Combine(fixture.LastOutput, "g0.4-wmp-observation-evidence.json");
        Assert.True(result.ExitCode == 0, result.Output + (File.Exists(evidencePath) ? File.ReadAllText(evidencePath) : string.Empty));
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.LastOutput, "g0.4-wmp-observation-evidence.json")));
        Assert.Equal(2, evidence.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.NotEqual("invalid-input-or-execution-failure", evidence.RootElement.GetProperty("status").GetString());
        Assert.Equal(4, evidence.RootElement.GetProperty("routes").GetArrayLength());
        Assert.Equal(2, evidence.RootElement.GetProperty("routes").EnumerateArray().Count(route => route.GetProperty("status").GetString() == "inherited-blocked-not-executed"));
    }

    [Fact]
    public void WmpObservationRejectsSubstitutedCorpusDespiteSelfConsistentManifest()
    {
        using var fixture = new CorpusFixture();
        File.AppendAllText(Path.Combine(fixture.CorpusRoot, "media", "vp9-opus.webm"), "substitution");
        var result = RunScript(fixture.CorpusRoot, fixture.NewOutput());
        Assert.NotEqual(0, result.ExitCode);
        using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.LastOutput, "g0.4-wmp-observation-evidence.json")));
        Assert.Equal("invalid-input-or-execution-failure", evidence.RootElement.GetProperty("status").GetString());
        Assert.Contains("hash or length mismatch", evidence.RootElement.GetProperty("failure").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string PathInRepo(params string[] parts) { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent; return Path.Combine([directory!.FullName, .. parts]); }
    private static (int ExitCode, string Output) RunScript(string corpus, string output) { var start = new System.Diagnostics.ProcessStartInfo { FileName = "pwsh", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var arg in new[] { "-NoProfile", "-File", PathInRepo("eng", "gate0", "Invoke-G04WindowsMediaPlayerObservation.ps1"), "-CorpusRoot", corpus, "-OutputDirectory", output, "-OpenTimeoutSeconds", "1", "-EndedTimeoutSeconds", "1" }) start.ArgumentList.Add(arg); using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start PowerShell."); var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, stdout + stderr); }

    private sealed class CorpusFixture : IDisposable
    {
        private const string Profile = "P2.BtbnLgplShared.WindowsX64.20260820";
        private readonly string root = Path.Combine(Path.GetTempPath(), "ReelForge-G04-wmp-v2-" + Guid.NewGuid().ToString("N"));
        private readonly string sourceRoot; private int outputNumber;
        public CorpusFixture()
        {
            CorpusRoot = Path.Combine(root, "corpus"); sourceRoot = Path.Combine(root, "source"); Directory.CreateDirectory(Path.Combine(CorpusRoot, "media")); Directory.CreateDirectory(Path.Combine(CorpusRoot, "transformations")); Directory.CreateDirectory(sourceRoot);
            var sourceRuntime = Path.Combine(sourceRoot, "runtime-identity.json"); WriteJson(sourceRuntime, new { observation = new { PrimaryTool = new { Sha256 = new string('A', 64) } } });
            var sourceEvidence = Path.Combine(sourceRoot, "g0.4-delivery-proof-evidence.json"); WriteJson(sourceEvidence, new { schemaVersion = 1, profileId = Profile, preflight = new { status = "passed" } });
            var runtime = Path.Combine(CorpusRoot, "runtime-identity.json"); WriteJson(runtime, new { observation = new { PrimaryTool = new { Sha256 = new string('A', 64) } } });
            File.WriteAllText(Path.Combine(CorpusRoot, "index.html"), "<!doctype html>"); File.WriteAllText(Path.Combine(CorpusRoot, "media", "vp9-opus.webm"), "open-webm-av"); File.WriteAllText(Path.Combine(CorpusRoot, "media", "vp9-video-only.webm"), "open-webm-video"); File.WriteAllText(Path.Combine(CorpusRoot, "transformations", "h264-aac.mp4.blocked.json"), "blocked-mp4-av"); File.WriteAllText(Path.Combine(CorpusRoot, "transformations", "h264-video-only.mp4.blocked.json"), "blocked-mp4-video");
            var manifest = Path.Combine(CorpusRoot, "manifest.json"); WriteJson(manifest, new { schemaVersion = 2, kind = "ReelForge Gate 0 independent-playback manual harness corpus", routes = new[] { Route("Video.Export.Open.WebmVp9Opus", "media/vp9-opus.webm", "video/webm", false), Route("Video.Export.Open.WebmVp9VideoOnly", "media/vp9-video-only.webm", "video/webm", true) }, blockedRoutes = new[] { new { id="Video.Export.Compatibility.Mp4H264Aac.P2OpenH264", reason="representative blocked MP4", transformation=Artifact(Path.Combine(CorpusRoot, "transformations", "h264-aac.mp4.blocked.json"), CorpusRoot) }, new { id="Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264", reason="representative blocked MP4", transformation=Artifact(Path.Combine(CorpusRoot, "transformations", "h264-video-only.mp4.blocked.json"), CorpusRoot) } } });
            var bound = Directory.GetFiles(CorpusRoot, "*", SearchOption.AllDirectories).Where(file => Path.GetFileName(file) != "manifest.json").OrderBy(file => file).Select(file => Artifact(file, CorpusRoot)).ToArray();
            WriteJson(Path.Combine(CorpusRoot, "g0.4-playback-corpus-evidence.json"), new { schemaVersion = 2, profileId = Profile, preflight = new { status = "passed" }, sourceEvidence = new { path = sourceEvidence, sha256 = Hash(sourceEvidence), runtimeIdentity = Artifact(sourceRuntime, sourceRoot) }, runtimeIdentityEvidence = Artifact(runtime, CorpusRoot), sourceRuntimePrimaryToolSha256 = new string('A', 64), routes = new[] { new { id="Video.Export.Compatibility.Mp4H264Aac.P2OpenH264", status="blocked", artifact=(object?)null }, new { id="Video.Export.Compatibility.Mp4H264VideoOnly.P2OpenH264", status="blocked", artifact=(object?)null }, new { id="Video.Export.Open.WebmVp9Opus", status="passed", artifact=(object?)Artifact(Path.Combine(CorpusRoot, "media", "vp9-opus.webm"), CorpusRoot) }, new { id="Video.Export.Open.WebmVp9VideoOnly", status="passed", artifact=(object?)Artifact(Path.Combine(CorpusRoot, "media", "vp9-video-only.webm"), CorpusRoot) } }, manifest = Artifact(manifest, CorpusRoot), indexHtml = Artifact(Path.Combine(CorpusRoot, "index.html"), CorpusRoot), boundArtifacts = bound });
        }
        public string CorpusRoot { get; } = null!; public string LastOutput { get; private set; } = null!;
        public string NewOutput() => LastOutput = Path.Combine(root, "output-" + ++outputNumber);
        public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        private object Route(string id, string url, string mime, bool videoOnly) { dynamic artifact = Artifact(Path.Combine(CorpusRoot, url.Replace('/', Path.DirectorySeparatorChar)), CorpusRoot); return new { id, url, mime, videoOnly, sha256 = artifact.sha256, length = artifact.length }; }
        private static dynamic Artifact(string path, string root) { var file = new FileInfo(path); return new { path = Path.GetRelativePath(root, path).Replace('\\', '/'), length = file.Length, sha256 = Hash(path) }; }
        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value));
    }
}
