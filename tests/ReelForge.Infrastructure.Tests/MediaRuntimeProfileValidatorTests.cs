using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Infrastructure.Tests;

public sealed class MediaRuntimeProfileValidatorTests
{
    private static readonly string RuntimeRootPath = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "ReelForge", "media-runtime-profile-validator"));

    [Fact]
    public void ValidateAcceptsExactPairedRuntimeAndObservedComponents()
    {
        var validator = new MediaRuntimeProfileValidator();

        var assessment = validator.Validate(Profile(), Observed());

        Assert.True(
            assessment.MatchesProfile,
            string.Join(Environment.NewLine, assessment.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Empty(assessment.Issues);
    }

    [Fact]
    public void ValidateRejectsClosureDriftForbiddenFlagsAndMissingComponents()
    {
        var validator = new MediaRuntimeProfileValidator();
        var observed = Observed(
            configuration: "--enable-version3 --enable-gpl",
            components: new Dictionary<MediaRuntimeComponentKind, IReadOnlyList<string>>
            {
                [MediaRuntimeComponentKind.Encoder] = [],
                [MediaRuntimeComponentKind.Muxer] = ["webm"]
            },
            runtimeFiles:
            [
                new MediaRuntimeFileObservation("bin/ffmpeg.exe", Hash('F')),
                new MediaRuntimeFileObservation("bin/ffprobe.exe", Hash('B')),
                new MediaRuntimeFileObservation("bin/unreviewed.dll", Hash('D'))
            ]);

        var assessment = validator.Validate(Profile(), observed);

        Assert.False(assessment.MatchesProfile);
        Assert.Contains(assessment.Issues, issue => issue.Code == "RuntimeFiles.Missing");
        Assert.Contains(assessment.Issues, issue => issue.Code == "RuntimeFiles.Unexpected");
        Assert.Contains(assessment.Issues, issue => issue.Code == "Configuration.Forbidden");
        Assert.Contains(assessment.Issues, issue => issue.Code == "Components.Missing");
    }

    [Fact]
    public void ValidateRejectsMalformedManifestBeforeComparingObservation()
    {
        var invalid = Profile() with
        {
            RuntimeFiles = [new MediaRuntimeFileManifest("../ffmpeg.exe", "not-a-hash")]
        };

        var assessment = new MediaRuntimeProfileValidator().Validate(invalid, Observed());

        Assert.False(assessment.MatchesProfile);
        Assert.Contains(assessment.Issues, issue => issue.Code == "Manifest.RuntimeFiles");
    }

    [Fact]
    public void ValidateRejectsFailedInspectionParserContract()
    {
        var observed = Observed() with
        {
            InspectionParserContract = new MediaRuntimeParserContractObservation("ProgramVersion.Json.1", false, string.Empty)
        };

        var assessment = new MediaRuntimeProfileValidator().Validate(Profile(), observed);

        Assert.False(assessment.MatchesProfile);
        Assert.Contains(assessment.Issues, issue => issue.Code == "ParserContract.Mismatch");
    }

    private static MediaRuntimeProfile Profile() => new(
        "P2.Test",
        Tool("bin/ffmpeg.exe", Hash('F'), "ffmpeg version n8.1.2"),
        Tool("bin/ffprobe.exe", Hash('B'), "ffprobe version n8.1.2"),
        [
            new MediaRuntimeFileManifest("bin/ffmpeg.exe", Hash('F')),
            new MediaRuntimeFileManifest("bin/ffprobe.exe", Hash('B')),
            new MediaRuntimeFileManifest("bin/avcodec.dll", Hash('A'))
        ],
        [
            new MediaRuntimeComponentRequirement(MediaRuntimeComponentKind.Encoder, ["libvpx-vp9"]),
            new MediaRuntimeComponentRequirement(MediaRuntimeComponentKind.Muxer, ["webm"])
        ],
        ["--enable-gpl", "--enable-nonfree"],
        new MediaRuntimeParserContractManifest("ProgramVersion.Json.1", "n8.1.2"));

    private static ObservedMediaRuntime Observed(
        string configuration = "--enable-version3 --enable-libvpx",
        IReadOnlyDictionary<MediaRuntimeComponentKind, IReadOnlyList<string>>? components = null,
        IReadOnlyList<MediaRuntimeFileObservation>? runtimeFiles = null) => new(
        RuntimeRootPath,
        ToolObservation(Path.Combine(RuntimeRootPath, "bin", "ffmpeg.exe"), Hash('F'), "ffmpeg version n8.1.2", configuration, components ?? new Dictionary<MediaRuntimeComponentKind, IReadOnlyList<string>>
        {
            [MediaRuntimeComponentKind.Encoder] = ["libvpx-vp9"],
            [MediaRuntimeComponentKind.Muxer] = ["webm"]
        }),
        ToolObservation(
            Path.Combine(RuntimeRootPath, "bin", "ffprobe.exe"),
            Hash('B'),
            "ffprobe version n8.1.2",
            configuration,
            new Dictionary<MediaRuntimeComponentKind, IReadOnlyList<string>>()),
        new MediaRuntimeParserContractObservation("ProgramVersion.Json.1", true, "n8.1.2"),
        runtimeFiles ??
        [
            new MediaRuntimeFileObservation("bin/ffmpeg.exe", Hash('F')),
            new MediaRuntimeFileObservation("bin/ffprobe.exe", Hash('B')),
            new MediaRuntimeFileObservation("bin/avcodec.dll", Hash('A'))
        ]);

    private static MediaRuntimeToolManifest Tool(string path, string hash, string version) => new(
        path,
        hash,
        version,
        "GCC 15.2.0",
        "--enable-version3 --enable-libvpx",
        new Dictionary<string, string> { ["libavutil"] = "60.26.102 / 60.26.102" });

    private static MediaRuntimeToolObservation ToolObservation(
        string path,
        string hash,
        string version,
        string configuration,
        IReadOnlyDictionary<MediaRuntimeComponentKind, IReadOnlyList<string>> components) => new(
        path,
        hash,
        version,
        "GCC 15.2.0",
        configuration,
        new Dictionary<string, string> { ["libavutil"] = "60.26.102 / 60.26.102" },
        components);

    private static string Hash(char value) => new(value, 64);
}
