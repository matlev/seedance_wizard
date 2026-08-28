using ReelForge.Application;
using ReelForge.Infrastructure;

namespace ReelForge.Infrastructure.Tests;

public sealed class PairedMediaRuntimeObserverTests : IDisposable
{
    private readonly string _runtimeRoot = Path.Combine(Path.GetTempPath(), "ReelForge", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ObserveAsyncCollectsPairedIdentityComponentsAndRuntimeClosure()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_runtimeRoot, "bin"));
        var ffmpeg = CreateFile(Path.Combine(bin.FullName, "ffmpeg.exe"), "ffmpeg");
        var ffprobe = CreateFile(Path.Combine(bin.FullName, "ffprobe.exe"), "ffprobe");
        CreateFile(Path.Combine(bin.FullName, "avcodec.dll"), "library");
        var runner = new RecordingRunner();
        var observer = new PairedMediaRuntimeObserver(runner);

        var observed = await observer.ObserveAsync(new MediaRuntimePair(_runtimeRoot, ffmpeg, ffprobe));

        Assert.Equal(Path.GetFullPath(_runtimeRoot), observed.RuntimeRootPath);
        Assert.Equal("ffmpeg version n8.1.2", observed.PrimaryTool.Version);
        Assert.Equal("ffprobe version n8.1.2", observed.InspectionTool.Version);
        Assert.Equal("GCC 15.2.0", observed.PrimaryTool.Compiler);
        Assert.Equal("--enable-version3 --enable-libvpx", observed.PrimaryTool.Configuration);
        Assert.Equal("60.26.102 / 60.26.102", observed.PrimaryTool.LibraryVersions["libavutil"]);
        Assert.Contains("libvpx-vp9", observed.PrimaryTool.Components[MediaRuntimeComponentKind.Encoder]);
        Assert.Contains("webm", observed.PrimaryTool.Components[MediaRuntimeComponentKind.Muxer]);
        Assert.Contains("scale", observed.PrimaryTool.Components[MediaRuntimeComponentKind.Filter]);
        Assert.Contains("file", observed.PrimaryTool.Components[MediaRuntimeComponentKind.Protocol]);
        Assert.True(observed.InspectionParserContract.Succeeded);
        Assert.Equal("ProgramVersion.Json.1", observed.InspectionParserContract.ContractId);
        Assert.Equal("n8.1.2", observed.InspectionParserContract.ProgramVersion);
        Assert.Equal(3, observed.RuntimeFiles.Count);
        Assert.All(observed.RuntimeFiles, file => Assert.True(file.Sha256.Length == 64));
        Assert.Equal(
            ["-version", "-version", "-v error -print_format json -show_program_version", "-hide_banner -encoders", "-hide_banner -decoders", "-hide_banner -muxers", "-hide_banner -demuxers", "-hide_banner -filters", "-hide_banner -protocols"],
            runner.Requests.Select(request => string.Join(' ', request.Arguments)).ToArray());
    }

    [Fact]
    public async Task ObserveAsyncRejectsExecutableOutsideRuntimeRoot()
    {
        Directory.CreateDirectory(_runtimeRoot);
        var outside = CreateFile(Path.Combine(Path.GetTempPath(), $"ReelForge-{Guid.NewGuid():N}.exe"), "outside");
        var ffprobe = CreateFile(Path.Combine(_runtimeRoot, "ffprobe.exe"), "ffprobe");
        var observer = new PairedMediaRuntimeObserver(new RecordingRunner());

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => observer.ObserveAsync(new MediaRuntimePair(_runtimeRoot, outside, ffprobe)));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_runtimeRoot))
        {
            Directory.Delete(_runtimeRoot, recursive: true);
        }
    }

    private static string CreateFile(string path, string contents)
    {
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed class RecordingRunner : IExternalProcessRunner
    {
        public List<ExternalProcessRequest> Requests { get; } = [];

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var arguments = string.Join(' ', request.Arguments);
            var output = arguments switch
            {
                "-version" when Path.GetFileName(request.ExecutablePath).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) => """
                    ffmpeg version n8.1.2
                    built with GCC 15.2.0
                    configuration: --enable-version3  --enable-libvpx
                    libavutil      60.26.102 / 60.26.102
                    """,
                "-version" => """
                    ffprobe version n8.1.2
                    built with GCC 15.2.0
                    configuration: --enable-version3  --enable-libvpx
                    libavutil      60.26.102 / 60.26.102
                    """,
                "-v error -print_format json -show_program_version" => """
                    { "program_version": { "version": "n8.1.2" } }
                    """,
                "-hide_banner -encoders" => " V....D libvpx-vp9 VP9 encoder",
                "-hide_banner -decoders" => " V....D vp9 VP9 decoder",
                "-hide_banner -muxers" => "  E webm WebM",
                "-hide_banner -demuxers" => " D  matroska,webm Matroska",
                "-hide_banner -filters" => " ... scale V->V Scale",
                "-hide_banner -protocols" => """
                    Supported file protocols:
                    Input:
                    file
                    Output:
                    file
                    """,
                _ => throw new InvalidOperationException($"Unexpected command: {arguments}")
            };

            return Task.FromResult(new ExternalProcessResult(0, output, string.Empty));
        }
    }
}
