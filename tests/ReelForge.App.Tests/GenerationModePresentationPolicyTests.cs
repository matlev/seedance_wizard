using ReelForge.App.Views.Generation;
using ReelForge.Core;

namespace ReelForge.App.Tests;

public sealed class GenerationModePresentationPolicyTests
{
    [Fact]
    public void VideoEditLocksProviderRequiredSourceGeometry()
    {
        var capabilities = CreateCapabilities();

        var policy = GenerationModePresentationPolicy.Create(
            capabilities,
            GenerationMode.VideoEdit,
            flexibleDurationSeconds: 12,
            flexibleAspectRatio: "16:9");

        Assert.True(policy.ReferencesEnabled);
        Assert.True(policy.DurationIsLocked);
        Assert.Equal(-1, policy.DurationSeconds);
        Assert.True(policy.AspectRatioIsLocked);
        Assert.Equal("adaptive", policy.AspectRatio);
        Assert.Contains("exactly one 4–30 second source video", policy.ReferenceHelpText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrdinaryModeRetainsFlexibleSettings()
    {
        var policy = GenerationModePresentationPolicy.Create(
            CreateCapabilities(),
            GenerationMode.ReferenceToVideo,
            flexibleDurationSeconds: 12,
            flexibleAspectRatio: "16:9");

        Assert.False(policy.DurationIsLocked);
        Assert.Equal(12, policy.DurationSeconds);
        Assert.False(policy.AspectRatioIsLocked);
        Assert.Equal("16:9", policy.AspectRatio);
    }

    [Fact]
    public void TextModeDoesNotCarryAdaptiveRatioFromAnotherMode()
    {
        var policy = GenerationModePresentationPolicy.Create(
            CreateCapabilities(),
            GenerationMode.TextToVideo,
            flexibleDurationSeconds: 12,
            flexibleAspectRatio: "adaptive");

        Assert.Equal("16:9", policy.AspectRatio);
    }

    private static GenerationProviderCapabilities CreateCapabilities() => new(
        "test.edit",
        "Edit test provider",
        "test-model",
        [GenerationMode.ReferenceToVideo, GenerationMode.VideoEdit],
        4,
        30,
        ["16:9", "adaptive"],
        ["720p"],
        3,
        3,
        3,
        new HashSet<MediaType> { MediaType.Image, MediaType.Video, MediaType.Audio },
        new Dictionary<string, IReadOnlyList<string>>())
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
}
