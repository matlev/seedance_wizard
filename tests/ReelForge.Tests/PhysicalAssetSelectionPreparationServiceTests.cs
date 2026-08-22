using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PhysicalAssetSelectionPreparationServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge physical selection preparation tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AlreadyStaleSelectionDoesNoWork()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, Guid.NewGuid(), isFfprobeAvailable: true);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Stale, result.Kind);
        Assert.Equal(0, inspector.CallCount);
        Assert.Null(asset.Encoding);
    }

    [Fact]
    public async Task MissingFileMarksAssetMissingAndPersistsIt()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, path) = await CreateWorkspaceWithAssetAsync(inspector, createFile: false);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, workspace.Project!.Id, isFfprobeAvailable: true);

        Assert.False(File.Exists(path));
        Assert.Equal(PhysicalAssetSelectionPreparationKind.Missing, result.Kind);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical!.Availability);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetAvailability.Missing, Assert.Single(reopened.Project.Assets).Physical!.Availability);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task UnavailableFfprobeSkipsInspection()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, workspace.Project!.Id, isFfprobeAvailable: false);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Ready, result.Kind);
        Assert.Equal(0, inspector.CallCount);
        Assert.Null(asset.Encoding);
    }

    [Fact]
    public async Task ExistingMetadataSkipsInspection()
    {
        var inspector = new RecordingInspector();
        var existingEncoding = new MediaEncodingMetadata { DurationSeconds = 12 };
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector, existingEncoding);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, workspace.Project!.Id, isFfprobeAvailable: true);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Ready, result.Kind);
        Assert.Equal(0, inspector.CallCount);
        Assert.Same(existingEncoding, asset.Encoding);
    }

    [Fact]
    public async Task InspectionPersistsDurationAndDimensions()
    {
        var inspectedEncoding = new MediaEncodingMetadata
        {
            DurationSeconds = 9.5,
            Video = new VideoStreamMetadata { Width = 1920, Height = 1080 }
        };
        var inspector = new RecordingInspector(inspectedEncoding);
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, workspace.Project!.Id, isFfprobeAvailable: true);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Ready, result.Kind);
        Assert.Equal(1, inspector.CallCount);
        Assert.Same(inspectedEncoding, asset.Encoding);
        Assert.Equal(9.5, asset.DurationSeconds);
        Assert.Equal(1920, asset.Width);
        Assert.Equal(1080, asset.Height);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        var persisted = Assert.Single(reopened.Project.Assets);
        Assert.Equal(9.5, persisted.DurationSeconds);
        Assert.Equal(1920, persisted.Width);
        Assert.Equal(1080, persisted.Height);
    }

    [Fact]
    public async Task AudioInspectionPersistsDurationWithoutVideoDimensions()
    {
        var inspectedEncoding = new MediaEncodingMetadata
        {
            DurationSeconds = 7.25,
            Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 }
        };
        var inspector = new RecordingInspector(inspectedEncoding);
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(
            inspector,
            mediaType: MediaType.Audio);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, workspace.Project!.Id, isFfprobeAvailable: true);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Ready, result.Kind);
        Assert.Equal(1, inspector.CallCount);
        Assert.Equal(7.25, asset.DurationSeconds);
        Assert.Null(asset.Width);
        Assert.Null(asset.Height);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        var persisted = Assert.Single(reopened.Project.Assets);
        Assert.Equal(MediaType.Audio, persisted.MediaType);
        Assert.Equal(7.25, persisted.DurationSeconds);
        Assert.Null(persisted.Width);
        Assert.Null(persisted.Height);
    }

    [Fact]
    public async Task ProjectSwitchDuringInspectionReturnsStaleWithoutMutatingOldAsset()
    {
        var inspector = new BlockingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);
        var selectedProjectId = workspace.Project!.Id;

        var preparation = service.PrepareAsync(asset, selectedProjectId, isFfprobeAvailable: true);
        await inspector.Started.Task;
        await workspace.CreateAsync(Path.Combine(_temporaryRoot, "other-project"), "Other project");
        inspector.Release.SetResult(inspector.Encoding);

        var result = await preparation;

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Stale, result.Kind);
        Assert.Null(asset.Encoding);
        Assert.Null(asset.DurationSeconds);
        Assert.Null(asset.Width);
        Assert.Null(asset.Height);
    }

    [Fact]
    public async Task ProjectSwitchDuringSaveReturnsStale()
    {
        var store = new BlockingSaveProjectStore();
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector, store: store);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);
        var selectedProjectId = workspace.Project!.Id;
        store.BlockNextSave = true;

        var preparation = service.PrepareAsync(asset, selectedProjectId, isFfprobeAvailable: true);
        await store.SaveStarted.Task;
        await workspace.CreateAsync(Path.Combine(_temporaryRoot, "save-switch-project"), "Other project");
        store.ReleaseSave.SetResult();

        var result = await preparation;

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Stale, result.Kind);
        Assert.NotNull(asset.Encoding);
    }

    private async Task<(ProjectWorkspace Workspace, ProjectAsset Asset, string Path)> CreateWorkspaceWithAssetAsync(
        IMediaInspectionService inspector,
        MediaEncodingMetadata? encoding = null,
        bool createFile = true,
        IProjectStore? store = null,
        MediaType mediaType = MediaType.Video)
    {
        var root = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
        var workspace = new ProjectWorkspace(store ?? new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(root, "Selection test");
        var asset = new ProjectAsset
        {
            FileName = mediaType == MediaType.Audio ? "source.m4a" : "source.mp4",
            DisplayName = mediaType == MediaType.Audio ? "source.m4a" : "source.mp4",
            MediaType = mediaType,
            StorageKind = AssetStorageKind.Physical,
            Encoding = encoding,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = mediaType == MediaType.Audio
                    ? "assets/audio/source.m4a"
                    : "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = new ContentIdentity { Status = ContentHashStatus.Pending }
            }
        };
        workspace.Project!.AddAsset(asset);
        var path = workspace.GetAbsoluteAssetPath(asset);
        if (createFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "media bytes");
        }

        await workspace.SaveAsync();
        return (workspace, asset, path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class RecordingInspector(MediaEncodingMetadata? result = null) : IMediaInspectionService
    {
        public int CallCount { get; private set; }

        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result ?? new MediaEncodingMetadata());
        }
    }

    private sealed class BlockingInspector : IMediaInspectionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<MediaEncodingMetadata> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MediaEncodingMetadata Encoding { get; } = new()
        {
            DurationSeconds = 5,
            Video = new VideoStreamMetadata { Width = 640, Height = 480 }
        };

        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            return Release.Task;
        }
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not import assets.");
    }

    private sealed class BlockingSaveProjectStore : IProjectStore
    {
        private readonly PortableProjectStore _inner = new();

        public bool BlockNextSave { get; set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public async Task SaveAsync(
            VideoProject project,
            ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            if (BlockNextSave)
            {
                BlockNextSave = false;
                SaveStarted.SetResult();
                await ReleaseSave.Task.ConfigureAwait(false);
            }

            await _inner.SaveAsync(project, location, cancellationToken).ConfigureAwait(false);
        }
    }
}
