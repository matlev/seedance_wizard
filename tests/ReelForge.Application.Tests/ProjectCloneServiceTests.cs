using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class ProjectCloneServiceTests
{
    [Fact]
    public async Task CloneCreatesNewRootIdentityWhilePreservingProjectGraph()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "clone-source.rfp"));
        var sourceId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        var source = new VideoProject
        {
            Id = sourceId,
            Name = "Original",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            ModifiedAt = DateTimeOffset.UtcNow.AddHours(-2),
            Assets = [new ProjectAsset { Id = assetId, FileName = "clip.mp4", DisplayName = "Clip" }],
            RecipeRevisions = [new RecipeRevision { Id = recipeId, VirtualAssetId = assetId, RevisionNumber = 1 }],
            WorkingCompositionAssetId = assetId
        };
        var store = new CloneStore(sourcePath, source);
        var fileSystem = new RecordingCloneFileSystem();
        var service = new ProjectCloneService(store, fileSystem, new ProjectSaveCoordinator());
        var before = DateTimeOffset.UtcNow;

        var result = await service.CloneAsync(new ProjectCloneRequest(sourcePath, "destination", "Copy"));

        Assert.NotEqual(sourceId, result.Project.Id);
        Assert.Equal("Copy", result.Project.Name);
        Assert.True(result.Project.CreatedAt >= before);
        Assert.Equal(result.Project.CreatedAt, result.Project.ModifiedAt);
        Assert.Equal(assetId, result.Project.Assets.Single().Id);
        Assert.Equal(recipeId, result.Project.RecipeRevisions.Single().Id);
        Assert.Equal(assetId, result.Project.WorkingCompositionAssetId);
        Assert.Equal(1, store.SaveCount);
        Assert.True(fileSystem.Published);
    }

    [Fact]
    public async Task CloneWritesAndReopensStagingBeforePublishing()
    {
        var store = new CloneStore("source.rfp", new VideoProject());
        var fileSystem = new RecordingCloneFileSystem();
        var service = new ProjectCloneService(store, fileSystem, new ProjectSaveCoordinator());

        await service.CloneAsync(new ProjectCloneRequest("source.rfp", "destination", "Copy"));

        Assert.Equal(["open-source", "save", "open-staging"], store.Operations);
        Assert.Equal(["stage", "publish"], fileSystem.Operations);
    }

    [Fact]
    public async Task CancellationAfterStagingRollsBackAndDoesNotPublish()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new CloneStore("source.rfp", new VideoProject()) { CancelAfterSave = cancellation };
        var fileSystem = new RecordingCloneFileSystem();
        var service = new ProjectCloneService(store, fileSystem, new ProjectSaveCoordinator());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.CloneAsync(new ProjectCloneRequest("source.rfp", "destination", "Copy"), cancellationToken: cancellation.Token));

        Assert.Contains("rollback", fileSystem.Operations);
        Assert.DoesNotContain("publish", fileSystem.Operations);
    }

    [Fact]
    public async Task PublishFailureRollsBackStaging()
    {
        var store = new CloneStore("source.rfp", new VideoProject());
        var fileSystem = new RecordingCloneFileSystem { FailPublish = true };
        var service = new ProjectCloneService(store, fileSystem, new ProjectSaveCoordinator());

        await Assert.ThrowsAsync<IOException>(() =>
            service.CloneAsync(new ProjectCloneRequest("source.rfp", "destination", "Copy")));

        Assert.Contains("rollback", fileSystem.Operations);
    }

    private sealed class CloneStore(string sourcePath, VideoProject source) : IProjectStore
    {
        private readonly string _sourcePath = Path.GetFullPath(sourcePath);
        private VideoProject? _saved;
        public List<string> Operations { get; } = [];
        public int SaveCount { get; private set; }
        public CancellationTokenSource? CancelAfterSave { get; init; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default)
        {
            var fullPath = Path.GetFullPath(projectFilePath);
            if (_saved is not null && !fullPath.Equals(_sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                Operations.Add("open-staging");
                return Task.FromResult((_saved, new ProjectLocation(Path.GetDirectoryName(fullPath)!, fullPath)));
            }

            Operations.Add("open-source");
            // A real persistence store materializes a distinct object graph on open.
            // Keep the fake faithful so the test focuses on clone semantics, not aliasing.
            var opened = new VideoProject
            {
                Id = source.Id,
                Name = source.Name,
                CreatedAt = source.CreatedAt,
                ModifiedAt = source.ModifiedAt,
                Assets = source.Assets,
                RecipeRevisions = source.RecipeRevisions,
                RecipeDrafts = source.RecipeDrafts,
                Anchors = source.Anchors,
                AnchorRevisions = source.AnchorRevisions,
                WorkingCompositionAssetId = source.WorkingCompositionAssetId,
                CurrentGenerationDraft = source.CurrentGenerationDraft,
                Generations = source.Generations
            };
            return Task.FromResult((opened, new ProjectLocation(Path.GetDirectoryName(_sourcePath)!, _sourcePath)));
        }

        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            Operations.Add("save");
            SaveCount++;
            CancelAfterSave?.Cancel();
            _saved = project;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCloneFileSystem : IProjectCloneFileSystem
    {
        public List<string> Operations { get; } = [];
        public bool Published { get; private set; }
        public bool CancelBeforePublish { get; init; }
        public bool FailPublish { get; init; }

        public Task<ProjectCloneStaging> StageDurableContentAsync(ProjectLocation sourceLocation, string destinationParentDirectory, string cloneName, IProgress<ProjectCloneProgress>? progress, CancellationToken cancellationToken)
        {
            Operations.Add("stage");
            return Task.FromResult(new ProjectCloneStaging(
                new ProjectLocation("staging", "staging/Copy.rfp"),
                new ProjectLocation("destination/Copy", "destination/Copy/Copy.rfp"), 2, 100));
        }

        public Task PublishAsync(ProjectCloneStaging staging, CancellationToken cancellationToken)
        {
            Operations.Add("publish");
            if (CancelBeforePublish) throw new OperationCanceledException(cancellationToken);
            if (FailPublish) throw new IOException("publish failed");
            Published = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(ProjectCloneStaging staging)
        {
            Operations.Add("rollback");
            return Task.CompletedTask;
        }
    }
}
