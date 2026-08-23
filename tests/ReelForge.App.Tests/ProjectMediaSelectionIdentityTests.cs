using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;
using System.IO;

namespace ReelForge.App.Tests;

public sealed class ProjectMediaSelectionIdentityTests
{
    [Fact]
    public async Task CapturedIdentityMatchesTheExactCurrentProjectLocationAndItem()
    {
        var workspace = await CreateWorkspaceAsync();
        var item = new ProjectMediaListItem(new ProjectAsset());
        var identity = ProjectMediaSelectionIdentity.Capture(workspace, item, CancellationToken.None);

        Assert.NotNull(identity);
        Assert.True(identity.IsCurrent(workspace, item));
    }

    [Fact]
    public async Task SameGuidReplacementProjectIsNotCurrent()
    {
        var workspace = await CreateWorkspaceAsync();
        var item = new ProjectMediaListItem(new ProjectAsset());
        var identity = ProjectMediaSelectionIdentity.Capture(workspace, item, CancellationToken.None)!;
        var projectFilePath = workspace.Location!.ProjectFilePath;

        await workspace.OpenAsync(projectFilePath);

        Assert.Equal(identity.Project.Id, workspace.Project!.Id);
        Assert.NotSame(identity.Project, workspace.Project);
        Assert.False(identity.IsCurrent(workspace, item));
    }

    [Fact]
    public async Task EqualPathReplacementLocationIsNotCurrent()
    {
        var workspace = await CreateWorkspaceAsync();
        var item = new ProjectMediaListItem(new ProjectAsset());
        var identity = ProjectMediaSelectionIdentity.Capture(workspace, item, CancellationToken.None)!;
        var projectFilePath = workspace.Location!.ProjectFilePath;

        await workspace.OpenAsync(projectFilePath);

        Assert.Equal(identity.Location.ProjectFilePath, workspace.Location!.ProjectFilePath);
        Assert.NotSame(identity.Location, workspace.Location);
        Assert.False(identity.IsCurrent(workspace, item));
    }

    [Fact]
    public async Task ANewListItemForTheSameAssetIsNotCurrent()
    {
        var workspace = await CreateWorkspaceAsync();
        var asset = new ProjectAsset();
        var identity = ProjectMediaSelectionIdentity.Capture(
            workspace,
            new ProjectMediaListItem(asset),
            CancellationToken.None)!;

        Assert.False(identity.IsCurrent(workspace, new ProjectMediaListItem(asset)));
    }

    [Fact]
    public async Task CancelledSelectionTokenIsNotCurrent()
    {
        var workspace = await CreateWorkspaceAsync();
        var item = new ProjectMediaListItem(new ProjectAsset());
        using var cancellation = new CancellationTokenSource();
        var identity = ProjectMediaSelectionIdentity.Capture(workspace, item, cancellation.Token)!;

        cancellation.Cancel();

        Assert.False(identity.IsCurrent(workspace, item));
    }

    private static async Task<ProjectWorkspace> CreateWorkspaceAsync()
    {
        var workspace = new ProjectWorkspace(new ReopenableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync("C:\\test-project", "Selection identity");
        return workspace;
    }

    private sealed class ReopenableProjectStore : IProjectStore
    {
        private VideoProject? _project;
        private ProjectLocation? _location;

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default)
        {
            _project = new VideoProject { Name = name };
            _location = new ProjectLocation(rootDirectory, Path.Combine(rootDirectory, "project.rfp"));
            return Task.FromResult((_project, _location));
        }

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((
                new VideoProject { Id = _project!.Id, Name = _project.Name },
                new ProjectLocation(_location!.RootDirectory, projectFilePath)));
        }

        public Task SaveAsync(
            VideoProject project,
            ProjectLocation location,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
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
