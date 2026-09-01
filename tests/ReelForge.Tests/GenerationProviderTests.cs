using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

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
    public void ModeRequirementsOverrideGeneralDurationAndAspectValidation()
    {
        var capabilities = new GenerationProviderCapabilities(
            ProviderId: "test",
            DisplayName: "Test provider",
            ModelVersion: "test-v1",
            Modes: [GenerationMode.VideoEdit],
            MinimumDurationSeconds: 4,
            MaximumDurationSeconds: 30,
            AspectRatios: ["16:9", "adaptive"],
            Resolutions: ["720p"],
            MaximumImageReferences: 2,
            MaximumVideoReferences: 2,
            MaximumAudioReferences: 2,
            SupportedReferenceTypes: new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
            ProviderParameters: new Dictionary<string, IReadOnlyList<string>>())
        {
            ModeRequirements = new Dictionary<GenerationMode, GenerationModeRequirements>
            {
                [GenerationMode.VideoEdit] = new(
                    FixedDurationSeconds: -1,
                    FixedAspectRatio: "adaptive",
                    RequiredImageReferences: 0,
                    RequiredVideoReferences: 1,
                    RequiredAudioReferences: 0,
                    MinimumVideoReferenceDurationSeconds: 4,
                    MaximumVideoReferenceDurationSeconds: 30)
            }
        };
        var video = new ProjectAsset { MediaType = MediaType.Video, FileName = "source.mp4", DurationSeconds = 12 };
        var request = new GenerationRequest
        {
            Prompt = "Change the lighting.",
            Mode = GenerationMode.VideoEdit,
            DurationSeconds = 4,
            AspectRatio = "16:9",
            Resolution = "720p",
            ReferenceAssetIds = [video.Id]
        };

        var errors = capabilities.Validate(request, [video]);

        Assert.NotNull(capabilities.GetModeRequirements(GenerationMode.VideoEdit));
        Assert.Contains(errors, error => error.Contains("-1 seconds", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'adaptive'", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("between 4 and 30", StringComparison.Ordinal));
    }

    [Fact]
    public void FixedModeSettingsDoNotSuppressTheGeneralReferenceRequirement()
    {
        var capabilities = new GenerationProviderCapabilities(
            "test",
            "Test provider",
            "test-v1",
            [GenerationMode.ReferenceToVideo],
            4,
            30,
            ["adaptive"],
            ["720p"],
            1,
            1,
            1,
            new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
            new Dictionary<string, IReadOnlyList<string>>())
        {
            ModeRequirements = new Dictionary<GenerationMode, GenerationModeRequirements>
            {
                [GenerationMode.ReferenceToVideo] = new(
                    FixedDurationSeconds: -1,
                    FixedAspectRatio: "adaptive")
            }
        };
        var request = new GenerationRequest
        {
            Prompt = "Use a reference",
            Mode = GenerationMode.ReferenceToVideo,
            DurationSeconds = -1,
            AspectRatio = "adaptive",
            Resolution = "720p"
        };

        var errors = capabilities.Validate(request, []);

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

    [Theory]
    [InlineData("portrait.PNG", true)]
    [InlineData("clip with spaces.MP4", true)]
    [InlineData("score.wav", true)]
    [InlineData("project.rfp", false)]
    [InlineData("notes.txt", false)]
    [InlineData("extensionless", false)]
    public void AssetImportReportsWhetherAFileCanBeDropped(string path, bool expected)
    {
        Assert.Equal(expected, AssetImportService.IsSupportedMediaFile(path));
    }
}
