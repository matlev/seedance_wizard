using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class SavedFrameServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForgeTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateCommitsExactRevisionAndPersistsIt()
    {
        var (workspace, source) = await CreateWorkspaceAsync();
        var position = Position(source, timestamp: 2_791, timeBaseDenominator: 1_000);

        var saved = await new SavedFrameService(workspace).CreateAsync(position);

        Assert.Equal("Saved frame 00:00:02.791", saved.Anchor.DisplayLabel);
        Assert.Equal(saved.Anchor.Id, saved.Revision.AnchorId);
        Assert.Equal(source.Id, saved.Revision.SourceAssetId);
        Assert.Equal(position.PresentationTimestamp, saved.Revision.PresentationTimestamp);
        Assert.Equal(position.TimeBaseDenominator, saved.Revision.TimeBaseDenominator);
        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(saved.Anchor.Id, Assert.Single(reopened.Anchors).Id);
        Assert.Equal(saved.Revision.Id, Assert.Single(reopened.AnchorRevisions).Id);
    }

    [Fact]
    public async Task UpdateTrimsMetadataAndRestoresDefaultTimestampLabel()
    {
        var (workspace, source) = await CreateWorkspaceAsync();
        var service = new SavedFrameService(workspace);
        var saved = await service.CreateAsync(Position(source, timestamp: 125, timeBaseDenominator: 10));

        var updated = await service.UpdateAsync(saved.Anchor.Id, "  Hero expression  ", "  use for reference  ");

        Assert.Equal("Hero expression", updated.Anchor.DisplayLabel);
        Assert.Equal("use for reference", updated.Anchor.Notes);
        updated = await service.UpdateAsync(saved.Anchor.Id, "   ", " \t ");
        Assert.Equal("Saved frame 00:00:12.500", updated.Anchor.DisplayLabel);
        Assert.Null(updated.Anchor.Notes);
    }

    [Fact]
    public async Task RemoveDeletesUnreferencedAnchorAndPersistsIt()
    {
        var (workspace, source) = await CreateWorkspaceAsync();
        var service = new SavedFrameService(workspace);
        var saved = await service.CreateAsync(Position(source));

        var disposition = await service.RemoveAsync(saved.Anchor.Id);

        Assert.Equal(AnchorRemovalDisposition.Removed, disposition);
        Assert.Empty(workspace.Project!.Anchors);
        Assert.Empty(workspace.Project.AnchorRevisions);
        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Empty(reopened.Anchors);
        Assert.Empty(reopened.AnchorRevisions);
    }

    [Fact]
    public async Task RemoveArchivesAnchorReferencedByGenerationDraft()
    {
        var (workspace, source) = await CreateWorkspaceAsync();
        var service = new SavedFrameService(workspace);
        var saved = await service.CreateAsync(Position(source));
        workspace.Project!.CurrentGenerationDraft = new GenerationDraft
        {
            References =
            [
                new GenerationReferenceDraft
                {
                    ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                    LogicalObjectId = saved.Anchor.Id
                }
            ]
        };

        var disposition = await service.RemoveAsync(saved.Anchor.Id);

        Assert.Equal(AnchorRemovalDisposition.Archived, disposition);
        Assert.True(Assert.Single(workspace.Project.Anchors).IsArchived);
        Assert.Single(workspace.Project.AnchorRevisions);
    }

    private async Task<(ProjectWorkspace Workspace, ProjectAsset Source)> CreateWorkspaceAsync()
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Saved Frames");
        var source = new ProjectAsset
        {
            FileName = "source.mp4",
            DisplayName = "source.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = new ContentIdentity
                {
                    Sha256 = new string('a', 64),
                    Status = ContentHashStatus.Verified
                }
            }
        };
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        return (workspace, source);
    }

    private static ExactFramePosition Position(
        ProjectAsset source,
        long timestamp = 2_791,
        int timeBaseDenominator = 1_000) =>
        new(source.Id, new string('a', 64), 0, timestamp, 1, timeBaseDenominator, 67);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not import assets.");
    }
}
