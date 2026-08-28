using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PhysicalAssetRemovalServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge removal tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RemoveDeletesStoredFileAndPersistsMetadata()
    {
        var workspace = await CreateWorkspaceAsync();
        var asset = Assert.Single(workspace.Project!.Assets);
        var path = workspace.GetAbsoluteAssetPath(asset);

        await new PhysicalAssetRemovalService().RemoveAsync(workspace, asset.Id);

        Assert.Empty(workspace.Project.Assets);
        Assert.False(File.Exists(path));
        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Empty(reopened.Assets);
    }

    [Fact]
    public async Task RemoveSucceedsWhenStoredFileIsAlreadyMissing()
    {
        var workspace = await CreateWorkspaceAsync();
        var asset = Assert.Single(workspace.Project!.Assets);
        File.Delete(workspace.GetAbsoluteAssetPath(asset));

        await new PhysicalAssetRemovalService().RemoveAsync(workspace, asset.Id);

        Assert.Empty(workspace.Project.Assets);
    }

    [Fact]
    public async Task RemoveWithPreservationRetainsDeletedRecordAndHistoricalReferencesAfterReopen()
    {
        var workspace = await CreateWorkspaceAsync();
        var source = Assert.Single(workspace.Project!.Assets);
        source.Provenance = new AssetProvenance
        {
            Operation = "Imported from camera",
            SourceAssetIds = [Guid.NewGuid()],
            Parameters = new Dictionary<string, string> { ["device"] = "camera" }
        };
        source.ProviderReferences["provider"] = new ProviderAssetReference
        {
            Value = "provider-reference",
            SourceContentHash = "a".PadLeft(64, 'a')
        };
        var derived = new ProjectAsset
        {
            FileName = "saved clip",
            DisplayName = "Saved clip",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        workspace.Project.AddAsset(derived);
        var recipe = workspace.Project.CommitRecipe(derived.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 1 }
        });
        await workspace.SaveAsync();

        await new PhysicalAssetRemovalService().RemoveAsync(workspace, source.Id, preserveLogicalRecord: true);

        Assert.True(source.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Missing, source.Physical!.Availability);
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(source)));

        await File.WriteAllTextAsync(workspace.GetAbsoluteAssetPath(source), "leftover media");
        var projectPath = workspace.Location!.ProjectFilePath;
        var persisted = await File.ReadAllTextAsync(projectPath);
        Assert.Contains("\"availability\": \"missing\"", persisted, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            projectPath,
            persisted.Replace("\"availability\": \"missing\"", "\"availability\": \"available\"", StringComparison.Ordinal));

        var (reopened, _) = await new PortableProjectStore().OpenAsync(projectPath);
        var reopenedSource = Assert.Single(reopened.Assets.Where(asset => asset.Id == source.Id));
        Assert.True(reopenedSource.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Missing, reopenedSource.Physical!.Availability);
        Assert.Equal(source.Provenance.Operation, reopenedSource.Provenance?.Operation);
        Assert.Equal(source.Provenance.SourceAssetIds, reopenedSource.Provenance?.SourceAssetIds);
        Assert.Equal("provider-reference", reopenedSource.ProviderReferences["provider"].Value);
        var reopenedRecipe = Assert.IsType<TrimRecipe>(Assert.Single(reopened.RecipeRevisions).Recipe);
        Assert.Equal(recipe.Id, reopened.RecipeRevisions.Single().Id);
        Assert.Equal(source.Id, reopenedRecipe.Source.AssetId);
    }

    [Fact]
    public async Task SaveFailureRestoresOriginalAssetOrderModifiedTimeAndFile()
    {
        var first = PhysicalAsset("first.mp4");
        var target = PhysicalAsset("target.mp4");
        var last = PhysicalAsset("last.mp4");
        var project = new VideoProject { Name = "Rollback" , Assets = [first, target, last] };
        var originalModifiedAt = project.ModifiedAt;
        var location = new ProjectLocation(_root, Path.Combine(_root, "Rollback.rfp"));
        var targetPath = Path.Combine(_root, target.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllTextAsync(targetPath, "media");
        var workspace = new ProjectWorkspace(new FailingStore(project, location), new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);

        await Assert.ThrowsAsync<IOException>(() => new PhysicalAssetRemovalService().RemoveAsync(workspace, target.Id));

        Assert.Equal([first.Id, target.Id, last.Id], project.Assets.Select(asset => asset.Id));
        Assert.Equal(originalModifiedAt, project.ModifiedAt);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task PreservingRemovalRollsBackDeletedFlagWhenSaveFails()
    {
        var target = PhysicalAsset("target.mp4");
        var project = new VideoProject { Name = "Rollback", Assets = [target] };
        var originalModifiedAt = project.ModifiedAt;
        var location = new ProjectLocation(_root, Path.Combine(_root, "Rollback.rfp"));
        target.Physical!.Availability = PhysicalAssetAvailability.Inaccessible;
        var targetPath = Path.Combine(_root, target.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await File.WriteAllTextAsync(targetPath, "media");
        var workspace = new ProjectWorkspace(new FailingStore(project, location), new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);

        await Assert.ThrowsAsync<IOException>(() => new PhysicalAssetRemovalService()
            .RemoveAsync(workspace, target.Id, preserveLogicalRecord: true));

        Assert.False(target.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Inaccessible, target.Physical!.Availability);
        Assert.Same(target, Assert.Single(project.Assets));
        Assert.Equal(originalModifiedAt, project.ModifiedAt);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task RemoveRejectsMissingAndVirtualAssets()
    {
        var workspace = await CreateWorkspaceAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PhysicalAssetRemovalService().RemoveAsync(workspace, Guid.NewGuid()));

        var virtualAsset = new ProjectAsset
        {
            StorageKind = AssetStorageKind.Virtual,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        workspace.Project!.Assets.Add(virtualAsset);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PhysicalAssetRemovalService().RemoveAsync(workspace, virtualAsset.Id));
    }

    private async Task<ProjectWorkspace> CreateWorkspaceAsync()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Removal");
        var asset = PhysicalAsset("source.mp4");
        workspace.Project!.Assets.Add(asset);
        var path = workspace.GetAbsoluteAssetPath(asset);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "media");
        await workspace.SaveAsync();
        return workspace;
    }

    private static ProjectAsset PhysicalAsset(string name) => new()
    {
        FileName = name,
        DisplayName = name,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage { RelativePath = $"assets/videos/{name}" }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingStore(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated project save failure."));
    }
}
