using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class SavedClipRenameServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "ReelForge saved clip rename tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenamePersistsTrimmedDisplayNameWithoutChangingRecipeIdentity()
    {
        var workspace = await CreateWorkspaceWithSavedClipAsync();
        var project = workspace.Project!;
        var clip = project.Assets.Single(asset => asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
        clip.ProviderReferences["test-provider"] = new ProviderAssetReference
        {
            Value = "asset://test-provider/clip",
            SourceRecipeRevisionId = clip.Virtual!.CurrentRecipeRevisionId,
            SourceContentHash = new string('a', 64)
        };
        var assetId = clip.Id;
        var fileName = clip.FileName;
        var virtualState = clip.Virtual!;
        var expectedMediaProperties = virtualState.ExpectedMediaProperties;
        var provenance = clip.Provenance;
        var providerReferences = clip.ProviderReferences;
        var currentRecipeRevisionId = virtualState.CurrentRecipeRevisionId;
        var recipeRevision = project.RecipeRevisions.Single(revision => revision.Id == currentRecipeRevisionId);
        var recipe = Assert.IsType<TrimRecipe>(recipeRevision.Recipe);
        var sourceId = recipe.Source.AssetId;
        var startAnchorRevisionId = recipe.Start.Anchor?.AnchorRevisionId;
        var recipeIds = project.RecipeRevisions.Select(revision => revision.Id).ToArray();
        var anchorIds = project.Anchors.Select(anchor => anchor.Id).ToArray();
        var anchorRevisionIds = project.AnchorRevisions.Select(revision => revision.Id).ToArray();

        await new SavedClipService(workspace).RenameAsync(assetId, "  Apple close-up  ");

        Assert.Equal("Apple close-up", clip.DisplayName);
        Assert.Equal(assetId, clip.Id);
        Assert.Equal(fileName, clip.FileName);
        Assert.Same(virtualState, clip.Virtual);
        Assert.Equal(currentRecipeRevisionId, virtualState.CurrentRecipeRevisionId);
        Assert.Same(expectedMediaProperties, virtualState.ExpectedMediaProperties);
        Assert.Same(provenance, clip.Provenance);
        Assert.Same(providerReferences, clip.ProviderReferences);
        Assert.Equal("asset://test-provider/clip", clip.ProviderReferences["test-provider"].Value);
        Assert.Equal(sourceId, recipe.Source.AssetId);
        Assert.Equal(startAnchorRevisionId, recipe.Start.Anchor?.AnchorRevisionId);
        Assert.Equal(recipeIds, project.RecipeRevisions.Select(revision => revision.Id));
        Assert.Equal(anchorIds, project.Anchors.Select(anchor => anchor.Id));
        Assert.Equal(anchorRevisionIds, project.AnchorRevisions.Select(revision => revision.Id));
        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal("Apple close-up", reopened.Assets.Single(asset => asset.Id == assetId).DisplayName);
    }

    [Fact]
    public async Task RenameRejectsAssetsThatAreNotSavedClips()
    {
        var workspace = await CreateWorkspaceWithSavedClipAsync();
        var physical = workspace.Project!.Assets.Single(asset => asset.StorageKind == AssetStorageKind.Physical);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SavedClipService(workspace).RenameAsync(physical.Id, "Not allowed"));

        Assert.Contains("Only a Saved Clip", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameRejectsBlankNames()
    {
        var workspace = await CreateWorkspaceWithSavedClipAsync();
        var clip = workspace.Project!.Assets.Single(asset => asset.Virtual?.Kind == VirtualAssetKind.SavedClip);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new SavedClipService(workspace).RenameAsync(clip.Id, "  "));
    }

    [Fact]
    public async Task RenameWithTheSameTrimmedNameDoesNotSaveOrTouchTheProject()
    {
        var project = new VideoProject { Name = "No-op rename" };
        var source = CreatePhysicalVideo();
        project.AddAsset(source);
        var clip = CreateSavedClip(source.Id);
        project.AddAsset(clip);
        var originalModifiedAt = project.ModifiedAt;
        var location = new ProjectLocation(_temporaryRoot, Path.Combine(_temporaryRoot, "No-op rename.rfp"));
        var store = new CountingSaveProjectStore(project, location);
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);

        await new SavedClipService(workspace).RenameAsync(clip.Id, "  Original clip  ");

        Assert.Equal(0, store.SaveCount);
        Assert.Equal("Original clip", clip.DisplayName);
        Assert.Equal(originalModifiedAt, project.ModifiedAt);
    }

    [Fact]
    public async Task RenameSaveFailureRestoresDisplayNameAndModifiedTime()
    {
        var project = new VideoProject { Name = "Rollback test" };
        var source = CreatePhysicalVideo();
        project.AddAsset(source);
        var clip = CreateSavedClip(source.Id);
        project.AddAsset(clip);
        var originalModifiedAt = project.ModifiedAt;
        var location = new ProjectLocation(_temporaryRoot, Path.Combine(_temporaryRoot, "Rollback test.rfp"));
        var workspace = new ProjectWorkspace(new FailingSaveProjectStore(project, location), new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);

        await Assert.ThrowsAsync<IOException>(() =>
            new SavedClipService(workspace).RenameAsync(clip.Id, "New name"));

        Assert.Equal("Original clip", clip.DisplayName);
        Assert.Equal(originalModifiedAt, project.ModifiedAt);
    }

    [Fact]
    public async Task RenameKeepsCommittedChangeWhenRecoveryRetirementFails()
    {
        var portable = new PortableProjectStore();
        var recovery = new FailingDiscardRecoveryStore(portable);
        var workspace = await CreateWorkspaceWithSavedClipAsync(portable, recovery);
        var clip = workspace.Project!.Assets.Single(asset => asset.Virtual?.Kind == VirtualAssetKind.SavedClip);

        await new SavedClipService(workspace).RenameAsync(clip.Id, "Committed despite cleanup failure");

        Assert.Equal("Committed despite cleanup failure", clip.DisplayName);
        Assert.Equal(ProjectWorkspaceState.Saved, workspace.State);
        Assert.Contains("recovery cleanup failed", workspace.FailureDetail!, StringComparison.OrdinalIgnoreCase);
        var (reopened, _) = await portable.OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal("Committed despite cleanup failure", reopened.Assets.Single(asset => asset.Id == clip.Id).DisplayName);
        Assert.True(File.Exists(PortableProjectStore.GetRecoveryFilePath(workspace.Location)));
    }

    private async Task<ProjectWorkspace> CreateWorkspaceWithSavedClipAsync(
        PortableProjectStore? store = null,
        IProjectRecoveryStore? recovery = null)
    {
        store ??= new PortableProjectStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), recovery);
        await workspace.CreateAsync(_temporaryRoot, "Saved Clip Rename");
        var source = CreatePhysicalVideo();
        source.DurationSeconds = 12;
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        var position = new ExactFramePosition(source.Id, new string('a', 64), 0, 450, 1, 100, 135);
        await new SavedClipService(workspace).CreateAsync(
            "Original clip",
            source.Id,
            ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.BeforeFrame),
            ClipBoundarySelection.SourceEnd);
        return workspace;
    }

    private static ProjectAsset CreatePhysicalVideo() => new()
    {
        FileName = "source.mp4",
        DisplayName = "source.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = "assets/videos/source.mp4",
            Availability = PhysicalAssetAvailability.Available,
            ContentIdentity = new ContentIdentity { Sha256 = new string('a', 64), Status = ContentHashStatus.Verified }
        }
    };

    private static ProjectAsset CreateSavedClip(Guid sourceAssetId) => new()
    {
        DisplayName = "Original clip",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Virtual,
        Origin = AssetOrigin.EditorDerived,
        Virtual = new VirtualAssetState
        {
            Kind = VirtualAssetKind.SavedClip,
            ExpectedMediaProperties = new MediaEncodingMetadata { ContainerFormat = "mp4", DurationSeconds = 3 }
        },
        Provenance = new AssetProvenance { Operation = "saved-clip", SourceAssetIds = [sourceAssetId] }
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not import assets.");
    }

    private sealed class FailingSaveProjectStore(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test opens an existing project.");

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated project save failure."));
    }

    private sealed class CountingSaveProjectStore(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public int SaveCount { get; private set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test opens an existing project.");

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDiscardRecoveryStore(PortableProjectStore inner) : IProjectRecoveryStore
    {
        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.ProbeAsync(location, cancellationToken);

        public Task WriteAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.WriteAsync(project, location, cancellationToken);

        public Task DiscardAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Simulated recovery retirement failure."));
    }
}
