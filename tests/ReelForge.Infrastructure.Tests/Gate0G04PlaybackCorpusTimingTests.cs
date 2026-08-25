namespace ReelForge.Infrastructure.Tests;

public sealed class Gate0G04PlaybackCorpusTimingTests
{
    [Fact]
    public void PreparationUsesApprovedNoRepairTimestampPolicies()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-P2G04PlaybackCorpusPreparation.ps1"));

        Assert.Contains("h264_mp4toannexb", script);
        Assert.Contains("Copy-ExactTsSegment", script);
        Assert.Contains("exactByteRepetition=$true", script);
        Assert.Contains("'-show_packets'", script);
        Assert.Contains("DTS is not strictly monotonic", script);
        Assert.Contains("PTS is not nondecreasing", script);
        Assert.Contains("expected exactly 120 video packets", script);
        Assert.Contains("expected exactly $expectedAudioPackets audio packets", script);
        Assert.Contains("audio terminal timestamp oracle failed", script);
        Assert.Contains("observedTerminalSeconds", script);
        Assert.Contains("durationToleranceSeconds=$tolerance", script);
        Assert.Contains("timestamp discontinuity", script);
        Assert.Contains("Do not repair timestamps, re-encode", script);
    }

    [Fact]
    public void PreparationBindsSourceAndDerivedEvidenceClosure()
    {
        var script = File.ReadAllText(PathInRepo("eng", "gate0", "Invoke-P2G04PlaybackCorpusPreparation.ps1"));

        Assert.Contains("Assert-SourceClosure", script);
        Assert.Contains("Source final artifact hash closure failed", script);
        Assert.Contains("sourceRuntimeIdentity", script);
        Assert.Contains("sourceRuntimePrimaryToolSha256", script);
        Assert.Contains("boundArtifacts", script);
        Assert.Contains("dispositionSummary", script);
        Assert.Contains("status='blocked'", script);
        Assert.Contains("G04PlaybackHarness.html", script);
        Assert.Contains("reparse-point", script);
        Assert.DoesNotContain("-c:v", script);
        Assert.DoesNotContain("-vf", script);
    }

    private static string PathInRepo(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".gitignore"))) directory = directory.Parent;
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
