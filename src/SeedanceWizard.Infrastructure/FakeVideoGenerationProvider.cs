using SeedanceWizard.Application;
using SeedanceWizard.Core;

namespace SeedanceWizard.Infrastructure;

public sealed class FakeVideoGenerationProvider : IVideoGenerationProvider
{
    private readonly TimeSpan _simulatedLatency;

    public FakeVideoGenerationProvider(TimeSpan? simulatedLatency = null)
    {
        _simulatedLatency = simulatedLatency ?? TimeSpan.FromMilliseconds(350);
    }

    public GenerationProviderCapabilities Capabilities { get; } = new(
        ProviderId: "fake.seedance",
        DisplayName: "Fake Seedance provider (no API calls)",
        ModelVersion: "development-v1",
        Modes: [GenerationMode.TextToVideo, GenerationMode.ImageToVideo, GenerationMode.ReferenceToVideo],
        MinimumDurationSeconds: 4,
        MaximumDurationSeconds: 30,
        AspectRatios: ["16:9", "9:16", "1:1", "4:3", "3:4", "21:9"],
        Resolutions: ["480p", "720p", "1080p", "4k"],
        MaximumImageReferences: 50,
        MaximumVideoReferences: 10,
        MaximumAudioReferences: 10,
        SupportedReferenceTypes: new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
        ProviderParameters: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["generateAudio"] = ["true", "false"]
        });

    public async Task<GenerationSubmission> SubmitAsync(
        GenerationRequest request,
        IReadOnlyCollection<ProjectAsset> projectAssets,
        CancellationToken cancellationToken = default)
    {
        var errors = Capabilities.Validate(request, projectAssets);
        if (errors.Count > 0)
        {
            throw new GenerationValidationException(errors);
        }

        await Task.Delay(_simulatedLatency, cancellationToken).ConfigureAwait(false);
        return new GenerationSubmission
        {
            ProviderJobId = $"fake-{Guid.NewGuid():N}",
            Status = GenerationStatus.Succeeded,
            ResponseMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["simulated"] = "true",
                ["billing"] = "none",
                ["note"] = "No media is produced by the milestone 1 fake provider."
            }
        };
    }
}
