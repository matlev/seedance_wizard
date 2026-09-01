using ReelForge.App.Views.MediaPreview;
using ReelForge.Core;

namespace ReelForge.App.Tests;

#pragma warning disable CA1707 // Test names describe behavior with readable clauses.
public sealed class MediaPreviewCompositionStateTests
{
    [Fact]
    public void EmptyComposition_IsARecognizedNormalPreviewState()
    {
        var revision = new RecipeRevision
        {
            Recipe = new CompositionRecipe
            {
                Composition = new WorkingCompositionState(
                    [new CompositionVideoTrack(Guid.NewGuid(), false, true, [])],
                    [])
            }
        };

        Assert.True(MediaPreviewCoordinator.IsEmptyComposition(revision));
    }

    [Fact]
    public void NonCompositionRecipe_IsNotTreatedAsAnEmptyComposition()
    {
        Assert.False(MediaPreviewCoordinator.IsEmptyComposition(new RecipeRevision()));
    }

    [Fact]
    public void AudioOnlyCompositionIsNotTreatedAsEmpty()
    {
        var source = new AssetRevisionReference { AssetId = Guid.NewGuid() };
        var timing = new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Audio, 0,
            TimingReadiness.Exact, true, new ExactTime(1, 1), [], new ExactTime(0, 1));
        var revision = new RecipeRevision
        {
            Recipe = new CompositionRecipe
            {
                Composition = new WorkingCompositionState(
                    [new CompositionVideoTrack(Guid.NewGuid(), false, true, [])],
                    [new CompositionAudioTrack(Guid.NewGuid(), false, false,
                        [new CompositionAudioItem(Guid.NewGuid(), source, 0,
                            new AudioSourceRange(new AudioSampleTime(0, 48000), new AudioSampleTime(48000, 48000)),
                            timing.CreatePlacementPin(), new ExactTime(0, 1))])])
            }
        };

        Assert.False(MediaPreviewCoordinator.IsEmptyComposition(revision));
    }
}
