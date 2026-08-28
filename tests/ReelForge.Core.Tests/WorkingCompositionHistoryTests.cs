using ReelForge.Core;

namespace ReelForge.Core.Tests;

public sealed class WorkingCompositionHistoryTests
{
    [Fact]
    public void CommittingWorkingCompositionRevisionsUsesMonotonicOrdinalsAcrossDivergence()
    {
        var source = CreatePhysicalVideo();
        var composition = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        var project = new VideoProject
        {
            Assets = [source, composition],
            WorkingCompositionAssetId = composition.Id
        };

        var first = project.CommitRecipe(composition.Id, CreateComposition(source.Id));
        var second = project.CommitRecipe(composition.Id, CreateComposition(source.Id));

        composition.Virtual!.CurrentRecipeRevisionId = first.Id;
        var divergent = project.CommitRecipe(composition.Id, CreateComposition(source.Id));

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

    private static CompositionRecipe CreateComposition(Guid sourceAssetId) => new()
    {
        Segments =
        [
            new CompositionSegment
            {
                Source = new AssetRevisionReference { AssetId = sourceAssetId },
                Start = RecipeBoundary.SourceStart,
                End = RecipeBoundary.SourceEnd
            }
        ]
    };

    private static ProjectAsset CreatePhysicalVideo() => new()
    {
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = "assets/videos/source.mp4",
            ContentIdentity = new ContentIdentity
            {
                Sha256 = new string('a', 64),
                Status = ContentHashStatus.Verified
            }
        },
        Virtual = null
    };
}
