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
    public void VideoWithoutAudioCopiesOnlyTheVideoStream()
    {
        var arguments = FfmpegCommandBuilder.BuildVideoWithoutAudioArguments("source.mp4", "muted.mp4");

        Assert.Contains("0:v:0", arguments);
        Assert.Contains("copy", arguments);
        Assert.Contains("-an", arguments);
        Assert.DoesNotContain("aac", arguments);
    }

    [Fact]
    public void ExtractAudioSelectsFirstAudioStreamAndCreatesM4a()
    {
        var arguments = FfmpegCommandBuilder.BuildExtractAudioArguments(
            @"C:\Project media\source clip.mp4",
            @"C:\Project media\source clip audio.m4a");

        Assert.Contains("0:a:0", arguments);
        Assert.Contains("-vn", arguments);
        Assert.Equal("aac", arguments[arguments.ToList().IndexOf("-c:a") + 1]);
        Assert.Equal("192k", arguments[arguments.ToList().IndexOf("-b:a") + 1]);
        Assert.Equal(@"C:\Project media\source clip audio.m4a", arguments[^1]);
    }

    [Fact]
    public void ExtractAudioRejectsAnOutputWithTheWrongFileType()
    {
        Assert.Throws<ArgumentException>(() =>
            FfmpegCommandBuilder.BuildExtractAudioArguments("source.mp4", "audio.mp3"));
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
    public void AudioOverlayPreservesBaseAudioAndDelaysDroppedClips()
    {
        var arguments = FfmpegCommandBuilder.BuildAudioOverlayArguments(
            "composition.mp4",
            videoHasAudio: true,
            [new AudioOverlayInput(@"C:\Project media\music.mp3", TimeSpan.FromSeconds(2.345))],
            "mixed.mp4");

        Assert.Equal(2, arguments.Count(argument => argument == "-i"));
        var graph = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("[0:a:0]asetpts=PTS-STARTPTS[baseaudio]", graph, StringComparison.Ordinal);
        Assert.Contains("[1:a:0]adelay=2345:all=1", graph, StringComparison.Ordinal);
        Assert.Contains("amix=inputs=2:duration=longest", graph, StringComparison.Ordinal);
        Assert.Contains("amix=inputs=2:duration=longest:dropout_transition=0,apad[aout]", graph, StringComparison.Ordinal);
        Assert.Contains("-shortest", arguments);
        Assert.Equal("mixed.mp4", arguments[^1]);
    }

    [Fact]
    public void AudioOverlayWorksWhenVideoHasNoAudio()
    {
        var arguments = FfmpegCommandBuilder.BuildAudioOverlayArguments(
            "composition.mp4",
            videoHasAudio: false,
            [new AudioOverlayInput("voice.wav", TimeSpan.Zero)],
            "mixed.mp4");

        var graph = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.DoesNotContain("[0:a:0]", graph, StringComparison.Ordinal);
        Assert.Contains("[overlay0]anull,apad[aout]", graph, StringComparison.Ordinal);
    }

    [Fact]
    public void AudioOverlayAppliesPerClipGainAndMuteBeforeDelay()
    {
        var arguments = FfmpegCommandBuilder.BuildAudioOverlayArguments(
            "composition.mp4",
            videoHasAudio: false,
            [
                new AudioOverlayInput("music.wav", TimeSpan.FromSeconds(1.25), GainDecibels: -6),
                new AudioOverlayInput("voice.wav", TimeSpan.FromSeconds(2), IsMuted: true, GainDecibels: 4)
            ],
            "mixed.mp4");

        var graph = arguments[arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("[1:a:0]volume=-6dB,adelay=1250:all=1", graph, StringComparison.Ordinal);
        Assert.Contains("[2:a:0]volume=0,adelay=2000:all=1", graph, StringComparison.Ordinal);
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
