using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class MediaCompatibilityAnalyzerTests
{
    [Fact]
    public void MatchingVideoAndAudioStreamsAreCompatible()
    {
        var report = MediaCompatibilityAnalyzer.Analyze([Encoding(), Encoding()]);

        Assert.Equal(CompositionCompatibilityDecision.Compatible, report.Decision);
        Assert.True(report.CanConcatWithoutNormalization);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void StreamDifferencesBecomeExplicitNormalizationRequirements()
    {
        var first = Encoding();
        var second = Encoding();
        second.Video!.Width = 1920;
        second.Video.FrameRate = "24/1";
        second.Audio!.SampleRate = 44100;

        var report = MediaCompatibilityAnalyzer.Analyze([first, second]);

        Assert.Equal(CompositionCompatibilityDecision.RequiresNormalization, report.Decision);
        Assert.Contains(report.Issues, issue => issue.Property.Contains("width", StringComparison.Ordinal));
        Assert.Contains(report.Issues, issue => issue.Property.Contains("frame rate", StringComparison.Ordinal));
        Assert.Contains(report.Issues, issue => issue.Property.Contains("sample rate", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingStreamMetadataProducesUnknownDecision()
    {
        var report = MediaCompatibilityAnalyzer.Analyze([Encoding(), new MediaEncodingMetadata()]);

        Assert.Equal(CompositionCompatibilityDecision.Unknown, report.Decision);
        Assert.False(report.CanConcatWithoutNormalization);
    }

    private static MediaEncodingMetadata Encoding() => new()
    {
        ContainerFormat = "mp4",
        Video = new VideoStreamMetadata
        {
            Codec = "h264",
            Width = 1280,
            Height = 720,
            PixelFormat = "yuv420p",
            FrameRate = "30/1"
        },
        Audio = new AudioStreamMetadata
        {
            Codec = "aac",
            SampleRate = 48000,
            Channels = 2,
            ChannelLayout = "stereo"
        }
    };
}
