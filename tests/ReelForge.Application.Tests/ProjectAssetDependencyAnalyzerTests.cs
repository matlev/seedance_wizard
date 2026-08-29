using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class ProjectAssetDependencyAnalyzerTests
{
    [Fact]
    public void AnalyzeReturnsEveryDependencyCategoryInStableUserFacingOrderWithoutDuplicates()
    {
        var asset = PhysicalAsset();
        var project = new VideoProject { Assets = [asset] };
        project.CurrentGenerationDraft = new GenerationDraft
        {
            References =
            [
                AssetReference(asset.Id),
                AssetReference(asset.Id)
            ]
        };
        project.Generations.Add(new GenerationRecord
        {
            RequestSnapshot = new GenerationRequestSnapshot
            {
                References =
                [
                    new GenerationReferenceSnapshot
                    {
                        ObjectKind = GenerationReferenceObjectKind.Asset,
                        LogicalObjectId = asset.Id
                    }
                ]
            },
            OutputAssetIds = [asset.Id]
        });
        project.AnchorRevisions.Add(new FrameAnchorRevision { SourceAssetId = asset.Id });
        project.Assets.Add(new ProjectAsset
        {
            StorageKind = AssetStorageKind.Virtual,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip },
            Provenance = new AssetProvenance { SourceAssetIds = [asset.Id, asset.Id] }
        });
        var recipeAsset = new ProjectAsset
        {
            StorageKind = AssetStorageKind.Virtual,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.Assets.Add(recipeAsset);
        project.RecipeRevisions.Add(new RecipeRevision
        {
            VirtualAssetId = recipeAsset.Id,
            Recipe = CompositionReferencing(asset.Id)
        });
        project.RecipeDrafts.Add(new RecipeDraft
        {
            VirtualAssetId = recipeAsset.Id,
            EditableRecipe = new TrimRecipe { Source = new AssetRevisionReference { AssetId = asset.Id } }
        });

        var report = new ProjectAssetDependencyAnalyzer().Analyze(project, asset.Id);

        Assert.Equal(
        [
            ProjectAssetDependency.CurrentGenerationDraft,
            ProjectAssetDependency.SubmittedGenerationReferences,
            ProjectAssetDependency.GeneratedOutputHistory,
            ProjectAssetDependency.SavedFrames,
            ProjectAssetDependency.DerivedAssetHistory,
            ProjectAssetDependency.MediaRecipes
        ], report.Dependencies);
        Assert.Equal(
        [
            "the current generation draft",
            "submitted generation references",
            "generated-output history",
            "saved frames",
            "derived-asset history",
            "media recipes"
        ], report.DisplayDescriptions);
    }

    [Fact]
    public void AnalyzeReturnsEmptyReportForAnUnreferencedAsset() 
    {
        var asset = PhysicalAsset();
        var report = new ProjectAssetDependencyAnalyzer().Analyze(new VideoProject { Assets = [asset] }, asset.Id);

        Assert.False(report.IsInUse);
        Assert.Empty(report.Dependencies);
        Assert.Empty(report.DisplayDescriptions);
    }

    [Fact]
    public void AnalyzeRejectsAssetThatDoesNotBelongToProject()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ProjectAssetDependencyAnalyzer().Analyze(new VideoProject(), Guid.NewGuid()));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportDoesNotRetainCallerMutableDependencyList()
    {
        var supplied = new List<ProjectAssetDependency>
        {
            ProjectAssetDependency.CurrentGenerationDraft
        };
        var report = new ProjectAssetDependencyReport(supplied);

        supplied.Clear();
        supplied.Add(ProjectAssetDependency.MediaRecipes);

        Assert.True(report.IsInUse);
        Assert.Equal([ProjectAssetDependency.CurrentGenerationDraft], report.Dependencies);
        Assert.Equal(["the current generation draft"], report.DisplayDescriptions);
        Assert.IsNotType<ProjectAssetDependency[]>(report.Dependencies);
        var exposed = Assert.IsAssignableFrom<IList<ProjectAssetDependency>>(report.Dependencies);
        Assert.Throws<NotSupportedException>(() => exposed[0] = ProjectAssetDependency.MediaRecipes);
        Assert.Equal([ProjectAssetDependency.CurrentGenerationDraft], report.Dependencies);
    }

    private static GenerationReferenceDraft AssetReference(Guid assetId) => new()
    {
        ObjectKind = GenerationReferenceObjectKind.Asset,
        LogicalObjectId = assetId
    };

    private static ProjectAsset PhysicalAsset() => new()
    {
        FileName = "source.mp4",
        DisplayName = "source.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage { RelativePath = "assets/videos/source.mp4" }
    };

    private static CompositionRecipe CompositionReferencing(Guid assetId) => new()
    {
        Composition = new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true,
            [
                new CompositionVideoItem(
                    Guid.NewGuid(),
                    new AssetRevisionReference { AssetId = assetId },
                    0,
                    null,
                    EstimatedPin(MediaType.Video),
                    new ExactTime(0, 1))
            ])],
            [new CompositionAudioTrack(Guid.NewGuid(), false, false,
            [
                new CompositionAudioItem(
                    Guid.NewGuid(),
                    new AssetRevisionReference { AssetId = assetId },
                    0,
                    null,
                    EstimatedPin(MediaType.Audio),
                    new ExactTime(0, 1))
            ])])
    };

    private static StreamTimingAssessmentPin EstimatedPin(MediaType mediaType) => new(
        new StreamTimingAssessment(
            Guid.NewGuid(),
            new string('a', 64),
            mediaType,
            0,
            TimingReadiness.Estimated,
            true,
            new ExactTime(1, 1),
            [TimingIssueClassification.NativeDurationUnavailable],
            null));
}
