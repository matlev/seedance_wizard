using SeedanceWizard.Core;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class GenerationProviderTests
{
    [Fact]
    public void CapabilitiesDriveReferenceAndDurationValidation()
    {
        var provider = new FakeVideoGenerationProvider(TimeSpan.Zero);
        var request = new GenerationRequest
        {
            Prompt = "Continue the camera move",
            Mode = GenerationMode.ReferenceToVideo,
            DurationSeconds = 31,
            AspectRatio = "16:9",
            Resolution = "720p"
        };

        var errors = provider.Capabilities.Validate(request, []);

        Assert.Contains(errors, error => error.Contains("between 4 and 30", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("requires at least one reference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FakeProviderCompletesWithoutProducingOrBillingMedia()
    {
        var provider = new FakeVideoGenerationProvider(TimeSpan.Zero);
        var request = new GenerationRequest
        {
            Prompt = "A paper boat crossing a puddle",
            Mode = GenerationMode.TextToVideo,
            DurationSeconds = 15,
            AspectRatio = "16:9",
            Resolution = "720p"
        };

        var submission = await provider.SubmitAsync(request, []);

        Assert.Equal(GenerationStatus.Succeeded, submission.Status);
        Assert.StartsWith("fake-", submission.ProviderJobId, StringComparison.Ordinal);
        Assert.Equal("none", submission.ResponseMetadata["billing"]);
    }

    [Theory]
    [InlineData("portrait.PNG", MediaType.Image)]
    [InlineData("clip with spaces.MP4", MediaType.Video)]
    [InlineData("score.wav", MediaType.Audio)]
    public void AssetImportRecognizesSupportedTypes(string path, MediaType expected)
    {
        Assert.Equal(expected, AssetImportService.DetermineMediaType(path));
    }
}
