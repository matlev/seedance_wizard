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

        var result = await service.PrepareAsync(
            asset,
            new VideoProject { Id = workspace.Project!.Id },
            workspace.Location!,
            isFfprobeAvailable: true);

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

        var result = await service.PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: true);

        Assert.False(File.Exists(path));
        Assert.Equal(PhysicalAssetSelectionPreparationKind.Missing, result.Kind);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical!.Availability);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetAvailability.Missing, Assert.Single(reopened.Project.Assets).Physical!.Availability);
        Assert.Equal(0, inspector.CallCount);
    }

    [Fact]
    public async Task ChangedVerifiedFileMarksAssetMismatchedWithoutReplacingExpectedIdentity()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, path) = await CreateWorkspaceWithAssetAsync(inspector);
        var expected = await new Sha256ContentHashService().ComputeAsync(path);
        asset.Physical!.ContentIdentity = expected;
        await workspace.SaveAsync();
        await File.WriteAllTextAsync(path, "different media bytes");

        var result = await new PhysicalAssetSelectionPreparationService(workspace, inspector)
            .PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: false);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Mismatched, result.Kind);
        Assert.Equal(PhysicalAssetAvailability.Mismatched, asset.Physical.Availability);
        Assert.Equal(ContentHashStatus.Verified, asset.Physical.ContentIdentity.Status);
        Assert.Equal(expected.Sha256, asset.Physical.ContentIdentity.Sha256);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetAvailability.Mismatched, Assert.Single(reopened.Project.Assets).Physical!.Availability);
    }

    [Fact]
    public async Task HashAccessFailureMarksAssetInaccessibleAndPersistsIt()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        asset.Physical!.ContentIdentity = new ContentIdentity
        {
            Status = ContentHashStatus.Verified,
            Sha256 = new string('a', 64)
        };
        await workspace.SaveAsync();
        var service = new PhysicalAssetSelectionPreparationService(
            workspace,
            inspector,
            new AccessDeniedHashService());

        var result = await service.PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: false);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Inaccessible, result.Kind);
        Assert.Equal(PhysicalAssetAvailability.Inaccessible, asset.Physical.Availability);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetAvailability.Inaccessible, Assert.Single(reopened.Project.Assets).Physical!.Availability);
    }

    [Fact]
    public async Task ReadableUnverifiedFileRestoresAvailableAvailability()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        asset.Physical!.Availability = PhysicalAssetAvailability.Missing;
        await workspace.SaveAsync();

        var result = await new PhysicalAssetSelectionPreparationService(workspace, inspector)
            .PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: false);

        Assert.Equal(PhysicalAssetSelectionPreparationKind.Ready, result.Kind);
        Assert.Equal(PhysicalAssetAvailability.Available, asset.Physical.Availability);
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetAvailability.Available, Assert.Single(reopened.Project.Assets).Physical!.Availability);
    }

    [Fact]
    public async Task UnavailableFfprobeSkipsInspection()
    {
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var result = await service.PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: false);

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

        var result = await service.PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: true);

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

        var result = await service.PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: true);

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

        var result = await service.PrepareAsync(asset, workspace.Project!, workspace.Location!, isFfprobeAvailable: true);

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
        var selectedProject = workspace.Project!;
        var selectedLocation = workspace.Location!;

        var preparation = service.PrepareAsync(asset, selectedProject, selectedLocation, isFfprobeAvailable: true);
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
    public async Task ReopeningSameGuidProjectDuringInspectionReturnsStaleWithoutMutatingOldAsset()
    {
        var inspector = new BlockingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var selectedProject = workspace.Project!;
        var selectedLocation = workspace.Location!;
        var projectFilePath = selectedLocation.ProjectFilePath;
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);

        var preparation = service.PrepareAsync(asset, selectedProject, selectedLocation, isFfprobeAvailable: true);
        await inspector.Started.Task;
        await workspace.OpenAsync(projectFilePath);
        inspector.Release.SetResult(inspector.Encoding);

        var result = await preparation;

        Assert.NotSame(selectedProject, workspace.Project);
        Assert.NotSame(selectedLocation, workspace.Location);
        Assert.Equal(selectedProject.Id, workspace.Project!.Id);
        Assert.Equal(selectedLocation.ProjectFilePath, workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetSelectionPreparationKind.Stale, result.Kind);
        Assert.Null(asset.Encoding);
        Assert.Null(asset.DurationSeconds);
        Assert.Null(asset.Width);
        Assert.Null(asset.Height);
    }

    [Fact]
    public async Task CancellingSelectionDuringInspectionDoesNotMutateTheCurrentProjectAsset()
    {
        var inspector = new BlockingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);
        using var cancellation = new CancellationTokenSource();

        var preparation = service.PrepareAsync(
            asset,
            workspace.Project!,
            workspace.Location!,
            isFfprobeAvailable: true,
            cancellation.Token);
        await inspector.Started.Task;
        cancellation.Cancel();
        inspector.Release.SetResult(inspector.Encoding);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
        Assert.Null(asset.Encoding);
        Assert.Null(asset.DurationSeconds);
        Assert.Null(asset.Width);
        Assert.Null(asset.Height);
    }

    [Fact]
    public async Task ProjectSwitchDuringSaveWaitsForCoordinatedCommit()
    {
        var store = new BlockingSaveProjectStore();
        var inspector = new RecordingInspector();
        var (workspace, asset, _) = await CreateWorkspaceWithAssetAsync(inspector, store: store);
        var service = new PhysicalAssetSelectionPreparationService(workspace, inspector);
        var selectedProject = workspace.Project!;
        var selectedLocation = workspace.Location!;
        store.SavedProjects.Clear();
        store.BlockNextSave = true;

        var preparation = service.PrepareAsync(asset, selectedProject, selectedLocation, isFfprobeAvailable: true);
        await store.SaveStarted.Task;
        var projectSwitch = workspace.CreateAsync(Path.Combine(_temporaryRoot, "save-switch-project"), "Other project");
        await Task.Delay(50);
        Assert.False(projectSwitch.IsCompleted);
        store.ReleaseSave.SetResult();

        var result = await preparation;
        await projectSwitch;

        Assert.True(result.Kind is PhysicalAssetSelectionPreparationKind.Ready or PhysicalAssetSelectionPreparationKind.Stale);
        Assert.NotNull(asset.Encoding);
        Assert.Contains(store.SavedProjects, project => ReferenceEquals(project, selectedProject));
        Assert.DoesNotContain(store.SavedProjects, project => ReferenceEquals(project, workspace.Project));
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

    private sealed class AccessDeniedHashService : IContentHashService
    {
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Injected media access failure.");

        public Task<ContentVerificationResult> VerifyAsync(
            string path,
            ContentIdentity expected,
            CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Injected media access failure.");
    }

    private sealed class BlockingSaveProjectStore : IProjectStore, IProjectCommitGuardedStore
    {
        private readonly PortableProjectStore _inner = new();

        public bool BlockNextSave { get; set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<VideoProject> SavedProjects { get; } = [];

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
            _ = await SaveIfAsync(
                project,
                location,
                static commit =>
                {
                    commit();
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> SaveIfAsync(
            VideoProject project,
            ProjectLocation location,
            Func<Action, bool> tryCommit,
            CancellationToken cancellationToken = default)
        {
            if (BlockNextSave)
            {
                BlockNextSave = false;
                SaveStarted.SetResult();
                await ReleaseSave.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var committed = await _inner
                .SaveIfAsync(project, location, tryCommit, cancellationToken)
                .ConfigureAwait(false);
            if (committed)
                SavedProjects.Add(project);
            return committed;
        }
    }
}
