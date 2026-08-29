using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class ProjectCleanupServiceTests
{
    [Fact]
    public void AnalyzeKeepsHealthySavedClipAndCompositionWithOrdinaryBoundaries()
    {
        var source = Physical("source.mp4", PhysicalAssetAvailability.Available);
        var clip = Virtual("Clip", VirtualAssetKind.SavedClip);
        var composition = Virtual("Composition", VirtualAssetKind.Composition);
        var project = new VideoProject { Assets = [source, clip, composition] };
        project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = RecipeBoundary.SourceStart,
            End = RecipeBoundary.SourceEnd
        });
        project.CommitRecipe(composition.Id, CompositionWithVideoSource(
            new AssetRevisionReference { AssetId = source.Id }));

        var report = new ProjectDegradationAnalyzer().Analyze(project);

        Assert.Empty(report.Items);
    }

    [Fact]
    public void AnalyzeFindsTransitiveDerivedFailuresButNotUnknownPhysicalMedia()
    {
        var unavailable = Physical("missing.mp4", PhysicalAssetAvailability.Missing);
        var unknown = Physical("unknown.mp4", PhysicalAssetAvailability.Unknown);
        var clip = Virtual("Broken clip", VirtualAssetKind.SavedClip);
        var healthyClip = Virtual("Unknown clip", VirtualAssetKind.SavedClip);
        var composition = Virtual("Broken composition", VirtualAssetKind.Composition);
        var project = new VideoProject { Assets = [unavailable, unknown, clip, healthyClip, composition] };
        var brokenRevision = project.CommitRecipe(clip.Id, Trim(unavailable.Id));
        project.CommitRecipe(healthyClip.Id, Trim(unknown.Id));
        project.CommitRecipe(composition.Id, CompositionWithVideoSource(
            new AssetRevisionReference { AssetId = clip.Id, RecipeRevisionId = brokenRevision.Id }));

        var report = new ProjectDegradationAnalyzer().Analyze(project);

        Assert.True(report.IsDegradedAsset(clip.Id));
        Assert.True(report.IsDegradedAsset(composition.Id));
        Assert.False(report.IsDegradedAsset(healthyClip.Id));
        Assert.Equal(2, report.CleanupCandidateCount);
    }

    [Fact]
    public async Task CleanupArchivesAndTombstonesDerivedMediaWhileRetainingHistory()
    {
        var source = Physical("missing.mp4", PhysicalAssetAvailability.Missing);
        var clip = Virtual("Broken clip", VirtualAssetKind.SavedClip);
        var composition = Virtual("Broken composition", VirtualAssetKind.Composition);
        var anchor = new FrameAnchor { DisplayLabel = "Broken frame" };
        var project = new VideoProject { Assets = [source, clip, composition], Anchors = [anchor], WorkingCompositionAssetId = composition.Id };
        var hash = new string('a', 64);
        var anchorRevision = new FrameAnchorRevision
        {
            AnchorId = anchor.Id, RevisionNumber = 1, SourceAssetId = source.Id, SourceContentHash = hash,
            VideoStreamIndex = 0, PresentationTimestamp = 0, TimeBaseNumerator = 1, TimeBaseDenominator = 1
        };
        anchor.CurrentRevisionId = anchorRevision.Id;
        project.AnchorRevisions.Add(anchorRevision);
        project.CommitRecipe(clip.Id, Trim(source.Id));
        var clipRevision = clip.Virtual!.CurrentRecipeRevisionId!.Value;
        project.CommitRecipe(composition.Id, CompositionWithVideoSource(
            new AssetRevisionReference { AssetId = clip.Id, RecipeRevisionId = clipRevision }));
        var workspace = await OpenAsync(project);

        var result = await new ProjectCleanupService().CleanupAsync(workspace);

        Assert.Equal(1, result.ArchivedSavedFrames);
        Assert.Equal(1, result.TombstonedSavedClips);
        Assert.Equal(1, result.TombstonedCompositions);
        Assert.True(anchor.IsArchived);
        Assert.True(clip.IsDeleted);
        Assert.True(composition.IsDeleted);
        Assert.Null(project.WorkingCompositionAssetId);
        Assert.NotEmpty(project.AnchorRevisions);
        Assert.NotEmpty(project.RecipeRevisions);
    }

    [Fact]
    public async Task CleanupRollsBackInMemoryStateWhenSaveFails()
    {
        var source = Physical("missing.mp4", PhysicalAssetAvailability.Missing);
        var clip = Virtual("Broken clip", VirtualAssetKind.SavedClip);
        var project = new VideoProject { Assets = [source, clip] };
        project.CommitRecipe(clip.Id, Trim(source.Id));
        var workspace = await OpenAsync(project, failSave: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectCleanupService().CleanupAsync(workspace));

        Assert.False(clip.IsDeleted);
    }

    [Fact]
    public async Task CleanupRejectsAnUnresolvedWorkspaceState()
    {
        var source = Physical("missing.mp4", PhysicalAssetAvailability.Missing);
        var clip = Virtual("Broken clip", VirtualAssetKind.SavedClip);
        var project = new VideoProject { Assets = [source, clip] };
        project.CommitRecipe(clip.Id, Trim(source.Id));
        var workspace = await OpenAsync(project);
        typeof(ProjectWorkspace).GetProperty(nameof(ProjectWorkspace.State))!
            .SetValue(workspace, ProjectWorkspaceState.RecoveryAvailable);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ProjectCleanupService().CleanupAsync(workspace));

        Assert.False(clip.IsDeleted);
    }

    private static TrimRecipe Trim(Guid sourceId) => new()
    {
        Source = new AssetRevisionReference { AssetId = sourceId },
        Start = RecipeBoundary.SourceStart,
        End = RecipeBoundary.SourceEnd
    };

    private static CompositionRecipe CompositionWithVideoSource(AssetRevisionReference source) => new()
    {
        Composition = new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true,
            [
                new CompositionVideoItem(
                    Guid.NewGuid(),
                    source,
                    0,
                    null,
                    EstimatedVideoPin(),
                    new ExactTime(0, 1))
            ])],
            [])
    };

    private static StreamTimingAssessmentPin EstimatedVideoPin() => new(
        new StreamTimingAssessment(
            Guid.NewGuid(),
            new string('a', 64),
            MediaType.Video,
            0,
            TimingReadiness.Estimated,
            true,
            new ExactTime(1, 1),
            [TimingIssueClassification.NativeDurationUnavailable],
            null));

    private static ProjectAsset Physical(string name, PhysicalAssetAvailability availability) => new()
    {
        FileName = name, DisplayName = name, MediaType = MediaType.Video,
        Physical = new PhysicalAssetStorage { RelativePath = $"assets/videos/{name}", Availability = availability }
    };

    private static ProjectAsset Virtual(string name, VirtualAssetKind kind) => new()
    {
        FileName = name, DisplayName = name, MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Virtual, Physical = null,
        Virtual = new VirtualAssetState { Kind = kind }
    };

    private static async Task<ProjectWorkspace> OpenAsync(VideoProject project, bool failSave = false)
    {
        var location = new ProjectLocation("C:\\cleanup", "C:\\cleanup\\Cleanup.rfp");
        var workspace = new ProjectWorkspace(new TestStore(project, location, failSave), new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);
        return workspace;
    }

    private sealed class TestStore(VideoProject project, ProjectLocation location, bool failSave) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.FromResult((project, location));
        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default) =>
            failSave ? Task.FromException(new IOException("simulated")) : Task.CompletedTask;
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
