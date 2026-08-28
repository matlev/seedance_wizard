using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReelForge.Infrastructure.Tests;

public sealed class MediaRuntimeEngineeringTests
{
    [Fact]
    public void BaselineProfileIsLgplFirstAndForbidsExcludedComponents()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(PathInRepo("eng", "media-runtime", "baseline-profile.json")));
        var root = json.RootElement;
        Assert.Equal("development-baseline-candidate-not-shipping", root.GetProperty("status").GetString());
        Assert.Equal("LGPLv3-path", root.GetProperty("sourceProfile").GetProperty("licensePath").GetString());
        var forbidden = root.GetProperty("configurationPolicy").GetProperty("forbidden").EnumerateArray().Select(x => x.GetString()).ToArray()
            .Concat(root.GetProperty("configurationPolicy").GetProperty("forbiddenComponents").EnumerateArray().Select(x => x.GetString())).ToArray();
        foreach (var name in new[] { "--enable-gpl", "--enable-nonfree", "libx264", "libx265", "libvidstab", "librubberband", "eq", "hqdn3d" }) Assert.Contains(name, forbidden);
        var source = root.GetProperty("sourceProfile");
        foreach (var property in new[] { "release", "upstreamUrl", "upstreamRetention", "ffmpegSourceCommit", "btbnBuildCommit", "version", "target", "configuration" }) Assert.True(source.TryGetProperty(property, out _));
        foreach (var kind in new[] { "encoder", "decoder", "muxer", "demuxer", "filter", "protocol" }) Assert.True(root.GetProperty("requiredComponents").TryGetProperty(kind, out _));
        Assert.Contains("mov,mp4,m4a,3gp,3g2,mj2", root.GetProperty("requiredComponents").GetProperty("demuxer").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(3, root.GetProperty("fonts").GetArrayLength());
    }

    [Fact]
    public void ValidatorStaticModeDoesNotNeedRuntimeOrCredentials()
    {
        var path = PathInRepo("eng", "media-runtime", "Validate-MediaRuntime.ps1");
        var result = RunPwsh($"& '{Escape(path)}' | ConvertTo-Json -Compress");
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("static-policy-valid", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("networkAccess").GetBoolean());
        Assert.False(json.RootElement.GetProperty("credentialsAccess").GetBoolean());
    }

    [Fact]
    public void ObservableComponentPatternMatchesActualFfmpegStyleLines()
    {
        const string listing = " V..... ffv1                 FFV1 (FFmpeg video codec #1)\n";
        Assert.Matches(new Regex("(?m)\\sffv1(\\s|$)"), listing);
        Assert.DoesNotMatch(new Regex("(?m)\\\\sffv1(\\\\s|$)"), listing);
    }

    [Fact]
    public void ValidatorRejectsForbiddenDeclarationsInConfigurationOrMappings()
    {
        var source = PathInRepo("eng", "media-runtime");
        var temporary = Path.Combine(Path.GetTempPath(), $"reelforge-media-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(Path.Combine(source, "Validate-MediaRuntime.ps1"), Path.Combine(temporary, "Validate-MediaRuntime.ps1"));
            var profile = File.ReadAllText(Path.Combine(source, "baseline-profile.json"));
            File.WriteAllText(Path.Combine(temporary, "baseline-profile.json"), profile.Replace("--enable-version3", "--enable-gpl --enable-version3", StringComparison.Ordinal));
            Directory.CreateDirectory(Path.Combine(temporary, "fonts"));
            foreach (var font in Directory.GetFiles(Path.Combine(source, "fonts"), "*.*")) File.Copy(font, Path.Combine(temporary, "fonts", Path.GetFileName(font)));
            var result = RunPwsh($"& '{Escape(Path.Combine(temporary, "Validate-MediaRuntime.ps1"))}'");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("declares forbidden configuration token --enable-gpl", result.Output, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(temporary, "baseline-profile.json"), profile.Replace("\"openDelivery\": [", "\"openDelivery\": [\"eq\", ", StringComparison.Ordinal));
            result = RunPwsh($"& '{Escape(Path.Combine(temporary, "Validate-MediaRuntime.ps1"))}'");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("declares forbidden component eq", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void SmokeToolDeclaresTheBoundedFamiliesWithoutRunningMedia()
    {
        var text = File.ReadAllText(PathInRepo("eng", "media-runtime", "Invoke-MediaSmokeTests.ps1"));
        foreach (var family in new[] { "frame-extraction", "trim-concat", "split-screen-mixed-audio", "transform-basic-color", "av-transition", "unicode-title-caption", "proxy", "conditional-mp4", "webm-vp9-opus", "flac", "png", "jpeg" }) Assert.Contains(family, text);
        Assert.DoesNotContain("Invoke-WebRequest", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[int] $Threads = 1", text, StringComparison.Ordinal);
        Assert.Contains("concat=n=2", text, StringComparison.Ordinal);
        Assert.Contains("asplit", text, StringComparison.Ordinal);
        Assert.Contains("Set-SemanticCheck", text, StringComparison.Ordinal);
        Assert.Contains("cues_to_front", text, StringComparison.Ordinal);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ReelForge.sln"))) directory = directory.Parent;
        Assert.NotNull(directory); return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }
    private static string Escape(string path) => path.Replace("'", "''", StringComparison.Ordinal);
    private static (int ExitCode, string Output) RunPwsh(string command)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pwsh", $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        Assert.NotNull(process); var output = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }
}
