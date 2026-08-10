using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class FfprobeMediaInspectionServiceTests
{
    [Fact]
    public void ParseMapsReadableVideoAndAudioMetadata()
    {
        const string json = """
            {
              "streams": [
                {
                  "codec_type": "video",
                  "codec_name": "h264",
                  "profile": "High",
                  "width": 1280,
                  "height": 720,
                  "pix_fmt": "yuv420p",
                  "avg_frame_rate": "30000/1001",
                  "time_base": "1/30000",
                  "level": 40
                },
                {
                  "codec_type": "audio",
                  "codec_name": "aac",
                  "sample_rate": "48000",
                  "channels": 2,
                  "channel_layout": "stereo"
                }
              ],
              "format": {
                "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
                "duration": "15.250000",
                "size": "1048576",
                "bit_rate": "550000"
              }
            }
            """;

        var metadata = FfprobeMediaInspectionService.Parse(json);

        Assert.Equal(15.25, metadata.DurationSeconds);
        Assert.Equal(1048576, metadata.SizeBytes);
        Assert.Equal("h264", metadata.Video?.Codec);
        Assert.Equal(1280, metadata.Video?.Width);
        Assert.Equal("30000/1001", metadata.Video?.FrameRate);
        Assert.Equal("aac", metadata.Audio?.Codec);
        Assert.Equal(48000, metadata.Audio?.SampleRate);
        Assert.Equal("stereo", metadata.Audio?.ChannelLayout);
    }
}
