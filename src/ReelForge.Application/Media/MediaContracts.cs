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
