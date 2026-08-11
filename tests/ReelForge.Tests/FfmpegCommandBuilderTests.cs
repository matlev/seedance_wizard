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
}
