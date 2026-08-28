using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class FfprobeMediaInspectionServiceTests
{
    [Fact]
    public void ParseMapsSelectedStreamMetadataAndKeepsContainerDurationSeparate()
    {
        const string json = """
            {
              "streams": [
                { "index": 4, "codec_type": "video", "codec_name": "h264", "profile": "High", "width": 1280, "height": 720, "pix_fmt": "yuv420p", "avg_frame_rate": "30000/1001", "time_base": "1/30000", "start_pts": "-6006", "duration_ts": "450450", "level": 40, "disposition": { "default": 1, "attached_pic": 0 } },
                { "index": 7, "codec_type": "audio", "codec_name": "aac", "sample_rate": "48000", "channels": 2, "channel_layout": "stereo", "time_base": "1/48000", "start_pts": "1024", "duration_ts": "721920", "disposition": { "default": 1 } }
              ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "15.250000", "size": "1048576", "bit_rate": "550000" }
            }
            """;

        var metadata = FfprobeMediaInspectionService.Parse(json);

        Assert.Equal(15.25, metadata.DurationSeconds);
        Assert.Equal(1048576, metadata.SizeBytes);
        Assert.Equal("h264", metadata.Video?.Codec);
        Assert.Equal(4, metadata.Video?.StreamIndex);
        Assert.Equal(1280, metadata.Video?.Width);
        Assert.Equal("30000/1001", metadata.Video?.FrameRate);
        Assert.Equal("1/30000", metadata.Video?.TimeBase);
        Assert.Equal(1, metadata.Video?.TimeBaseNumerator);
        Assert.Equal(30000, metadata.Video?.TimeBaseDenominator);
        Assert.Equal(-6006, metadata.Video?.StartPresentationTimestamp);
        Assert.Equal(450450, metadata.Video?.DurationPresentationTimestamp);
        Assert.Equal("aac", metadata.Audio?.Codec);
        Assert.Equal(7, metadata.Audio?.StreamIndex);
        Assert.Equal(48000, metadata.Audio?.SampleRate);
        Assert.Equal("stereo", metadata.Audio?.ChannelLayout);
        Assert.Equal(1, metadata.Audio?.TimeBaseNumerator);
        Assert.Equal(48000, metadata.Audio?.TimeBaseDenominator);
        Assert.Equal(1024, metadata.Audio?.StartPresentationTimestamp);
        Assert.Equal(721920, metadata.Audio?.DurationPresentationTimestamp);
    }

    [Fact]
    public void ParseSelectsDefaultsBeforeLowerIndicesIndependentlyByMediaType()
    {
        const string json = """
            { "streams": [
              { "index": 0, "codec_type": "video", "codec_name": "low-video", "disposition": { "default": 0 } },
              { "index": 1, "codec_type": "audio", "codec_name": "low-audio", "disposition": { "default": 0 } },
              { "index": 2, "codec_type": "video", "codec_name": "default-video", "disposition": { "default": 1 } },
              { "index": 3, "codec_type": "audio", "codec_name": "default-audio", "disposition": { "default": 1 } }
            ] }
            """;

        var metadata = FfprobeMediaInspectionService.Parse(json);

        Assert.Equal(2, metadata.Video?.StreamIndex);
        Assert.Equal("default-video", metadata.Video?.Codec);
        Assert.Equal(3, metadata.Audio?.StreamIndex);
        Assert.Equal("default-audio", metadata.Audio?.Codec);
    }

    [Fact]
    public void ParseFallsBackToLowestValidIndexAndExcludesAttachedPictures()
    {
        const string json = """
            { "streams": [
              { "index": 0, "codec_type": "video", "codec_name": "cover", "disposition": { "default": 1, "attached_pic": 1 } },
              { "index": 5, "codec_type": "video", "codec_name": "later-video", "disposition": { "default": 0 } },
              { "index": 2, "codec_type": "video", "codec_name": "first-video", "disposition": { "default": 0 } },
              { "index": 9, "codec_type": "audio", "codec_name": "later-audio", "disposition": { "default": 0 } },
              { "index": 4, "codec_type": "audio", "codec_name": "first-audio", "disposition": { "default": 0 } },
              { "index": -1, "codec_type": "audio", "codec_name": "invalid-index", "disposition": { "default": 1 } }
            ] }
            """;

        var metadata = FfprobeMediaInspectionService.Parse(json);

        Assert.Equal(2, metadata.Video?.StreamIndex);
        Assert.Equal("first-video", metadata.Video?.Codec);
        Assert.Equal(4, metadata.Audio?.StreamIndex);
        Assert.Equal("first-audio", metadata.Audio?.Codec);
    }

    [Fact]
    public void ParseLeavesInvalidOrUnavailableExactStreamFieldsNull()
    {
        const string json = """
            {
              "streams": [
                { "index": 0, "codec_type": "video", "codec_name": "h264", "time_base": "0/90000", "start_pts": "not-a-number", "duration_ts": "-1" },
                { "index": 1, "codec_type": "audio", "codec_name": "aac", "time_base": "1/not-a-number", "duration_ts": "not-a-number" }
              ],
              "format": { "duration": "2.500000" }
            }
            """;

        var metadata = FfprobeMediaInspectionService.Parse(json);

        Assert.Equal(2.5, metadata.DurationSeconds);
        Assert.Equal("0/90000", metadata.Video?.TimeBase);
        Assert.Null(metadata.Video?.TimeBaseNumerator);
        Assert.Null(metadata.Video?.TimeBaseDenominator);
        Assert.Null(metadata.Video?.StartPresentationTimestamp);
        Assert.Null(metadata.Video?.DurationPresentationTimestamp);
        Assert.Null(metadata.Audio?.TimeBaseNumerator);
        Assert.Null(metadata.Audio?.TimeBaseDenominator);
        Assert.Null(metadata.Audio?.StartPresentationTimestamp);
        Assert.Null(metadata.Audio?.DurationPresentationTimestamp);
    }

    [Fact]
    public void ParseDoesNotDeriveNativeDurationFromContainerOrFrameRate()
    {
        const string json = """
            {
              "streams": [
                { "index": 0, "codec_type": "video", "codec_name": "h264", "avg_frame_rate": "30000/1001", "time_base": "1/90000", "start_pts": "180000", "duration": "4.004000" }
              ],
              "format": { "duration": "4.250000" }
            }
            """;

        var metadata = FfprobeMediaInspectionService.Parse(json);

        Assert.Equal(4.25, metadata.DurationSeconds);
        Assert.Equal(180000, metadata.Video?.StartPresentationTimestamp);
        Assert.Null(metadata.Video?.DurationPresentationTimestamp);
        Assert.Equal(1, metadata.Video?.TimeBaseNumerator);
        Assert.Equal(90000, metadata.Video?.TimeBaseDenominator);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{ \"streams\": null, \"format\": [] }")]
    [InlineData("{ \"streams\": [null, 42, { \"index\": 0, \"codec_type\": \"video\", \"disposition\": null }] }")]
    [InlineData("{ \"streams\": [{ \"index\": 0, \"codec_type\": \"audio\", \"disposition\": [] }] }")]
    public void ParseIgnoresMalformedOptionalShapes(string json)
    {
        var metadata = FfprobeMediaInspectionService.Parse(json);

        if (json.Contains("video", StringComparison.Ordinal))
            Assert.Equal(0, metadata.Video?.StreamIndex);
        else if (json.Contains("audio", StringComparison.Ordinal))
            Assert.Equal(0, metadata.Audio?.StreamIndex);
        else
        {
            Assert.Null(metadata.Video);
            Assert.Null(metadata.Audio);
        }
    }
}
