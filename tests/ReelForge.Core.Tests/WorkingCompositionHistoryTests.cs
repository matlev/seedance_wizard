using ReelForge.Core;

namespace ReelForge.Core.Tests;

public sealed class WorkingCompositionHistoryTests
{
    [Fact]
    public void CommittingWorkingCompositionRevisionsUsesMonotonicOrdinalsAcrossDivergence()
    {
        var composition = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        var project = new VideoProject
        {
            Assets = [composition],
            WorkingCompositionAssetId = composition.Id
        };

        var first = project.CommitRecipe(composition.Id, CreateComposition());
        var second = project.CommitRecipe(composition.Id, CreateComposition());

        composition.Virtual!.CurrentRecipeRevisionId = first.Id;
        var divergent = project.CommitRecipe(composition.Id, CreateComposition());

        Assert.Equal(1, first.RevisionNumber);
        Assert.Equal(2, second.RevisionNumber);
        Assert.Equal(3, divergent.RevisionNumber);
        Assert.Equal(first.Id, divergent.PreviousRevisionId);
        Assert.Equal(divergent.Id, composition.Virtual.CurrentRecipeRevisionId);
        Assert.Contains(project.RecipeRevisions, revision => revision.Id == second.Id);
        Assert.Equal(3, project.RecipeRevisions
            .Where(revision => revision.VirtualAssetId == composition.Id)
            .Select(revision => revision.RevisionNumber)
            .Distinct()
            .Count());
        Assert.Empty(ProjectInvariantValidator.Validate(project));
    }

    [Fact]
    public void CompositionRecipeValidationTraversesEveryMultitrackOccurrenceSource()
    {
        var missingVideoSource = Guid.NewGuid();
        var missingAudioSource = Guid.NewGuid();
        var composition = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        var state = new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true,
            [
                new CompositionVideoItem(
                    Guid.NewGuid(),
                    new AssetRevisionReference { AssetId = missingVideoSource },
                    0,
                    new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30)),
                    ExactPin(MediaType.Video, 0, new ExactTime(1, 1)),
                    new ExactTime(0, 1))
            ])],
            [new CompositionAudioTrack(Guid.NewGuid(), false, false,
            [
                new CompositionAudioItem(
                    Guid.NewGuid(),
                    new AssetRevisionReference { AssetId = missingAudioSource },
                    0,
                    new AudioSourceRange(new AudioSampleTime(0, 48_000), new AudioSampleTime(48_000, 48_000)),
                    ExactPin(MediaType.Audio, 0, new ExactTime(1, 1)),
                    new ExactTime(0, 1))
            ])]);
        var project = new VideoProject { Assets = [composition] };

        project.CommitRecipe(composition.Id, new CompositionRecipe { Composition = state });

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains(missingVideoSource.ToString(), StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains(missingAudioSource.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void CompositionRecipeValidationBindsPhysicalOccurrencesToVerifiedContentAndSelectedStreams()
    {
        var source = new ProjectAsset
        {
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                ContentIdentity = new ContentIdentity { Status = ContentHashStatus.Verified, Sha256 = new string('a', 64) }
            },
            Encoding = new MediaEncodingMetadata
            {
                Video = new VideoStreamMetadata { StreamIndex = 2 },
                Audio = new AudioStreamMetadata { StreamIndex = 3 }
            }
        };
        var composition = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        var project = new VideoProject { Assets = [source, composition], WorkingCompositionAssetId = composition.Id };
        project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState(
                [new CompositionVideoTrack(Guid.NewGuid(), false, true,
                [
                    new CompositionVideoItem(Guid.NewGuid(), new AssetRevisionReference { AssetId = source.Id }, 2,
                        new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30)),
                        ExactPin(MediaType.Video, 2, new ExactTime(1, 1), new string('b', 64)), new ExactTime(0, 1))
                ])],
                [new CompositionAudioTrack(Guid.NewGuid(), false, false,
                [
                    new CompositionAudioItem(Guid.NewGuid(), new AssetRevisionReference { AssetId = source.Id }, 4,
                        new AudioSourceRange(new AudioSampleTime(0, 48_000), new AudioSampleTime(48_000, 48_000)),
                        ExactPin(MediaType.Audio, 4, new ExactTime(1, 1)), new ExactTime(0, 1))
                ])])
        });

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains("verified content identity", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("selected audio stream identity", StringComparison.Ordinal));
    }

    private static CompositionRecipe CreateComposition() => new()
    {
        Composition = new WorkingCompositionState([], [])
    };

    private static StreamTimingAssessmentPin ExactPin(
        MediaType type,
        int stream,
        ExactTime duration,
        string? sourceContentHash = null) => new(
        new StreamTimingAssessment(Guid.NewGuid(), sourceContentHash ?? new string('a', 64), type, stream,
            TimingReadiness.Exact, true, duration, [], new ExactTime(0, 1)));
}
