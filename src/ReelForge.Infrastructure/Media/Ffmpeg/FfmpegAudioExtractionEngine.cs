using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class FfmpegAudioExtractionEngine : IAudioExtractionEngine
{
    private readonly IExternalProcessRunner _runner;
    private string? _ffmpegPath;

    public FfmpegAudioExtractionEngine(string? ffmpegPath, IExternalProcessRunner runner)
    {
        _ffmpegPath = ffmpegPath;
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public void UpdateExecutablePath(string? ffmpegPath) => _ffmpegPath = ffmpegPath;

    public async Task ExtractToM4aAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to extract audio.");
        var result = await _runner.RunAsync(
                new ExternalProcessRequest(
                    ffmpegPath,
                    FfmpegCommandBuilder.BuildExtractAudioArguments(inputPath, outputPath)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length <= 0)
            throw new InvalidDataException("FFmpeg completed without producing extracted audio.");
    }

    public async Task ExtractExactRangeToM4aAsync(
        string inputPath,
        string outputPath,
        int audioStreamIndex,
        AudioSourceRange sourceRange,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceRange);
        var ffmpegPath = _ffmpegPath ?? throw new MediaToolUnavailableException(
            "FFmpeg is not configured. Configure it in Settings > Media Tools to detach audio.");
        var result = await _runner.RunAsync(
                new ExternalProcessRequest(
                    ffmpegPath,
                    FfmpegCommandBuilder.BuildExtractExactAudioRangeArguments(
                        inputPath, outputPath, audioStreamIndex, sourceRange)),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded) throw new ExternalProcessException(ffmpegPath, result);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length <= 0)
            throw new InvalidDataException("FFmpeg completed without producing detached audio.");
    }
}
