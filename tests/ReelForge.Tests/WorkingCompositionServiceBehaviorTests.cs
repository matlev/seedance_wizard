using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class WorkingCompositionServiceBehaviorTests
{
    [Fact]
    public async Task AddSegmentAsyncSaveFailureRestoresCompositionAndDraftState()
    {
        var project = new VideoProject { Name = "Rollback" };
        var location = new ProjectLocation(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "rollback.rfp"));
        var store = new ToggleFailingProjectStore(project, location);
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);

        var firstAsset = CreateVideo("first.mp4");
        var secondAsset = CreateVideo("second.mp4");
        project.AddAsset(firstAsset);
        project.AddAsset(secondAsset);
        var service = new WorkingCompositionService(workspace);
        var composition = await service.CreateInitialAsync(firstAsset.Id);
        var (_, revision, recipe) = service.GetCurrent();
        var draft = Assert.Single(project.RecipeDrafts);
        var revisionCount = project.RecipeRevisions.Count;
        var currentRevisionId = composition.Virtual!.CurrentRecipeRevisionId;
        var recipeSnapshot = recipe;
        var draftRecipe = draft.EditableRecipe;
        var draftBasedOn = draft.BasedOnRevisionId;
        var draftModifiedAt = draft.ModifiedAt;
        var provenanceSources = composition.Provenance!.SourceAssetIds.ToArray();
        var projectModifiedAt = project.ModifiedAt;

        store.FailSaves = true;

        await Assert.ThrowsAsync<IOException>(() => service.AddSegmentAsync(secondAsset.Id));

        var (_, restoredRevision, restoredRecipe) = service.GetCurrent();
        Assert.Equal(revisionCount, project.RecipeRevisions.Count);
        Assert.Equal(currentRevisionId, composition.Virtual.CurrentRecipeRevisionId);
        Assert.Equal(revision.Id, restoredRevision.Id);
        Assert.Same(recipeSnapshot, restoredRecipe);
        Assert.Same(draftRecipe, draft.EditableRecipe);
        Assert.Equal(draftBasedOn, draft.BasedOnRevisionId);
        Assert.Equal(draftModifiedAt, draft.ModifiedAt);
        Assert.Equal(provenanceSources, composition.Provenance.SourceAssetIds);
        Assert.Equal(projectModifiedAt, project.ModifiedAt);
        Assert.DoesNotContain(secondAsset.Id, composition.Provenance.SourceAssetIds);
    }

    [Fact]
    public async Task CreateInitialAsyncSaveFailureRestoresProjectModifiedAt()
    {
        var project = new VideoProject { Name = "Initial rollback" };
        var location = new ProjectLocation(Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "initial-rollback.rfp"));
        var store = new ToggleFailingProjectStore(project, location) { FailSaves = true };
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);
        var source = CreateVideo("source.mp4");
        project.AddAsset(source);
        var modifiedAt = project.ModifiedAt;

        await Assert.ThrowsAsync<IOException>(() =>
            new WorkingCompositionService(workspace).CreateInitialAsync(source.Id));

        Assert.Equal(modifiedAt, project.ModifiedAt);
        Assert.Empty(project.RecipeRevisions);
        Assert.Empty(project.RecipeDrafts);
        Assert.Null(project.WorkingCompositionAssetId);
    }

    [Fact]
    public async Task AddAudioClipAsyncReturnsFaultedTaskForImmediateWorkspaceValidationFailure()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        var service = new WorkingCompositionService(workspace);

        Task<RecipeRevision>? task = null;
        var synchronousException = Record.Exception(() =>
        {
            task = service.AddAudioClipAsync(Guid.NewGuid(), TimeSpan.Zero);
        });

        Assert.Null(synchronousException);
        await Assert.ThrowsAsync<InvalidOperationException>(() => task!);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static ProjectAsset CreateVideo(string fileName) => new()
    {
        DisplayName = fileName,
        FileName = fileName,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = fileName,
            ContentIdentity = new ContentIdentity
            {
                Sha256 = new string('a', 64),
                Status = ContentHashStatus.Verified
            }
        }
    };

    private sealed class ToggleFailingProjectStore(
        VideoProject project,
        ProjectLocation location) : IProjectStore
    {
        public bool FailSaves { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task SaveAsync(
            VideoProject savedProject,
            ProjectLocation savedLocation,
            CancellationToken cancellationToken = default) =>
            FailSaves
                ? Task.FromException(new IOException("Simulated project save failure."))
                : Task.CompletedTask;
    }
}
