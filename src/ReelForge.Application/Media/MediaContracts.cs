using ReelForge.Core;

namespace ReelForge.Application;

public interface IMediaInspectionService
{
    Task<MediaEncodingMetadata> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);
}

public interface IAudioExtractionEngine
{
    Task ExtractToM4aAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an audio-only durable representation of one selected source stream over the
    /// supplied half-open sample range. The range remains in the source stream's native sample
    /// domain; implementations must not select a different audio stream.
    /// </summary>
    Task ExtractExactRangeToM4aAsync(
        string inputPath,
        string outputPath,
        int audioStreamIndex,
        AudioSourceRange sourceRange,
        CancellationToken cancellationToken = default);
}

public interface IContentHashService
{
    Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default);
    Task<ContentVerificationResult> VerifyAsync(
        string path,
        ContentIdentity expected,
        CancellationToken cancellationToken = default);
}

public sealed record ContentVerificationResult(bool MatchesExpected, ContentIdentity Observed);
