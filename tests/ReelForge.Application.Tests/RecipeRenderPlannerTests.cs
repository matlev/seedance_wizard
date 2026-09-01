using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class RecipeRenderPlannerTests
{
    [Fact]
    public void NestedVirtualSourcesProducePinnedDeterministicPlan()
    {
        var physical = PhysicalVideo();
        var inner = VirtualVideo("Inner clip");
        var outer = VirtualVideo("Outer clip");
        var project = new VideoProject { Assets = [physical, inner, outer] };
        var innerRevision = project.CommitRecipe(inner.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = physical.Id },
            Start = Timestamp(1),
            End = Timestamp(7)
        });
        var outerRevision = project.CommitRecipe(outer.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference
            {
                AssetId = inner.Id,
                RecipeRevisionId = innerRevision.Id
            },
            Start = Timestamp(2),
            End = Timestamp(4)
        });

        var first = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview,
            "preview");
        var second = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview,
            "preview");
        var upload = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.ProviderUpload,
            "preview");
        physical.Physical!.ContentIdentity.Sha256 = new string('b', 64);
        var changedSource = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview,
            "preview");

        var outerNode = Assert.IsType<TrimRenderPlanNode>(first.Root);
        var innerNode = Assert.IsType<TrimRenderPlanNode>(outerNode.Source);
        Assert.IsType<PhysicalSourceRenderPlanNode>(innerNode.Source);
        Assert.Equal(outerRevision.Id, first.TargetRecipeRevisionId);
        Assert.Equal(first.PlanHash, second.PlanHash);
        Assert.NotEqual(first.PlanHash, upload.PlanHash);
        Assert.NotEqual(first.PlanHash, changedSource.PlanHash);
    }

    [Fact]
    public void VirtualDependencyWithoutPinnedRevisionIsRejected()
    {
        var physical = PhysicalVideo();
        var inner = VirtualVideo("Inner clip");
        var outer = VirtualVideo("Outer clip");
        var project = new VideoProject { Assets = [physical, inner, outer] };
        project.CommitRecipe(inner.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = physical.Id }
        });
        var outerRevision = project.CommitRecipe(outer.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = inner.Id }
        });

        var exception = Assert.Throws<InvalidDataException>(() => RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview));

        Assert.Contains("pin an exact recipe revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecipeDependencyCycleIsRejectedDuringPlanning()
    {
        var first = VirtualVideo("First");
        var second = VirtualVideo("Second");
        var firstRevisionId = Guid.NewGuid();
        var secondRevisionId = Guid.NewGuid();
        first.Virtual!.CurrentRecipeRevisionId = firstRevisionId;
        second.Virtual!.CurrentRecipeRevisionId = secondRevisionId;
        var project = new VideoProject
        {
            Assets = [first, second],
            RecipeRevisions =
            [
                new RecipeRevision
                {
                    Id = firstRevisionId,
                    VirtualAssetId = first.Id,
                    RevisionNumber = 1,
                    Recipe = new TrimRecipe
                    {
                        Source = new AssetRevisionReference
                        {
                            AssetId = second.Id,
                            RecipeRevisionId = secondRevisionId
                        }
                    }
                },
                new RecipeRevision
                {
                    Id = secondRevisionId,
                    VirtualAssetId = second.Id,
                    RevisionNumber = 1,
                    Recipe = new TrimRecipe
                    {
                        Source = new AssetRevisionReference
                        {
                            AssetId = first.Id,
                            RecipeRevisionId = firstRevisionId
                        }
                    }
                }
            ]
        };

        var exception = Assert.Throws<InvalidDataException>(() => RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(first.Id, firstRevisionId),
            MaterializationPurpose.Preview));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CandidateMultitrackCompositionMaterializationIsRejectedUntilTrackAwareRenderingExists()
    {
        var first = PhysicalVideo();
        var composition = VirtualVideo("Composition");
        composition.Virtual!.Kind = VirtualAssetKind.Composition;
        var project = new VideoProject { Assets = [first, composition] };
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState([], [])
        });

        var exception = Assert.Throws<InvalidDataException>(() => RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(composition.Id, revision.Id),
            MaterializationPurpose.Preview));

        Assert.Contains("track-aware renderer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Milestone 6", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CandidateMultitrackCompositionIsNotFlattenedIntoLegacySegmentsOrAudioClips()
    {
        var video = PhysicalVideo();
        var composition = VirtualVideo("Composition");
        composition.Virtual!.Kind = VirtualAssetKind.Composition;
        var project = new VideoProject { Assets = [video, composition] };
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState(
                [new CompositionVideoTrack(Guid.NewGuid(), false, true,
                [
                    VideoItem(video, 0, 2)
                ])],
                [])
        });

        var exception = Assert.Throws<InvalidDataException>(() => RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(composition.Id, revision.Id),
            MaterializationPurpose.Preview));

        Assert.Contains("cannot be materialized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectAsset PhysicalVideo() => new()
    {
        DisplayName = "Source",
        FileName = "source.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = Path.Combine("assets", "videos", "source.mp4"),
            ContentIdentity = new ContentIdentity
            {
                Status = ContentHashStatus.Verified,
                Sha256 = new string('a', 64)
            }
        }
    };

    private static ProjectAsset VirtualVideo(string name) => new()
    {
        DisplayName = name,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Virtual,
        Physical = null,
        Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
    };

    private static CompositionVideoItem VideoItem(ProjectAsset source, long compositionStart, long duration)
    {
        var assessment = new StreamTimingAssessment(
            Guid.NewGuid(), new string('a', 64), MediaType.Video, 0, TimingReadiness.Exact,
            true, new ExactTime(duration, 1), [], new ExactTime(0, 1));
        return new CompositionVideoItem(
            Guid.NewGuid(),
            new AssetRevisionReference { AssetId = source.Id },
            0,
            new VideoSourceRange(
                new VideoPresentationTime(0, 1, 1),
                new VideoPresentationTime(duration, 1, 1)),
            assessment.CreatePlacementPin(),
            new ExactTime(compositionStart, 1));
    }

    private static RecipeBoundary Timestamp(double seconds) => new()
    {
        Kind = RecipeBoundaryKind.Timestamp,
        TimestampSeconds = seconds
    };

}
