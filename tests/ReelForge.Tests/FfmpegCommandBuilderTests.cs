using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void ExtractFramePreservesPathsWithSpacesAsSingleArguments()
    {
        const string input = @"C:\A project\source clip (final).mp4";
        const string output = @"C:\A project\frames\anchor 01.png";

        var arguments = FfmpegCommandBuilder.BuildExtractFrameArguments(input, output, 12.3456);

        Assert.Contains(input, arguments);
        Assert.Contains(output, arguments);
        Assert.DoesNotContain($"\"{input}\"", arguments);
        Assert.Equal("12.346", arguments[3]);
    }

    [Fact]
    public void FrameAccurateTrimUsesDurationAndReEncoding()
    {
        var arguments = FfmpegCommandBuilder.BuildFrameAccurateTrimArguments("input.mp4", "output.mp4", 2.25, 8.75);

        Assert.Equal("2.25", arguments[3]);
        Assert.Equal("6.5", arguments[7]);
        Assert.Equal("libx264", arguments[9]);
        Assert.Equal("aac", arguments[11]);
    }

    [Fact]
    public void FrameAccurateTrimRejectsReversedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FfmpegCommandBuilder.BuildFrameAccurateTrimArguments("input.mp4", "output.mp4", 10, 5));
    }

    [Fact]
    public void ExactFrameExtractionSelectsPresentationTimestampOnPinnedStream()
    {
        var arguments = FfmpegCommandBuilder.BuildExtractExactFrameArguments(
            "input.mp4", "output.png", 2, 90123);

        Assert.Equal("0:2", arguments[5]);
        Assert.Equal("select=eq(pts\\,90123)", arguments[7]);
        Assert.Equal("vfr", arguments[11]);
        Assert.Equal("1", arguments[13]);
    }

    [Fact]
    public void CompatibleConcatBuildsFilterGraphWithoutShellQuoting()
    {
        var arguments = FfmpegCommandBuilder.BuildCompatibleConcatArguments(
            [@"C:\Project media\one.mp4", @"C:\Project media\two.mp4"],
            @"C:\Project media\joined.mp4",
            includeAudio: true);

        Assert.Equal(2, arguments.Count(argument => argument == "-i"));
        Assert.Contains(@"C:\Project media\one.mp4", arguments);
        Assert.Contains(@"C:\Project media\two.mp4", arguments);
        var graph = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("concat=n=2:v=1:a=1[v][a]", graph, StringComparison.Ordinal);
        Assert.Contains("[0:a:0]asetpts=PTS-STARTPTS[a0]", graph, StringComparison.Ordinal);
        Assert.Equal(@"C:\Project media\joined.mp4", arguments[^1]);
    }

    [Fact]
    public void CompatibleConcatRequiresMultipleInputs()
    {
        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.BuildCompatibleConcatArguments(
            ["one.mp4"], "joined.mp4", includeAudio: false));
    }

    [Fact]
    public void NormalizedConcatMatchesVideoAndCreatesSilenceForDisabledAudio()
    {
        var arguments = FfmpegCommandBuilder.BuildNormalizedConcatArguments(
            [
                new NormalizedConcatInput("one.mp4", 4.25, HasAudio: true, AudioEnabled: true),
                new NormalizedConcatInput("two.mp4", 2.5, HasAudio: true, AudioEnabled: false)
            ],
            "joined.mp4",
            new NormalizedConcatProfile(1920, 1080, 30000d / 1001d));

        var graph = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("scale=1920:1080", graph, StringComparison.Ordinal);
        Assert.Contains("fps=29.97", graph, StringComparison.Ordinal);
        Assert.Contains("[0:a:0]aresample=48000", graph, StringComparison.Ordinal);
        Assert.Contains("anullsrc=r=48000:cl=stereo,atrim=duration=2.5", graph, StringComparison.Ordinal);
        Assert.Contains("concat=n=2:v=1:a=1[v][a]", graph, StringComparison.Ordinal);
    }
}
