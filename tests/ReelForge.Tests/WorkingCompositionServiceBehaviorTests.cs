using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class WorkingCompositionServiceBehaviorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ReelForge-composition-command-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateInitialAsyncCreatesOneStableEmptyTrackOfEachKindInOneRevision()
    {
        var workspace = await CreateWorkspaceAsync();
        var source = AddVideo(workspace.Project!, "source.mp4");
        var service = new WorkingCompositionService(workspace);

        var composition = await service.CreateInitialAsync(source.Id);
        var (_, revision, recipe) = service.GetCurrent();

        var videoTrack = Assert.Single(recipe.Composition.VideoTracks);
        var audioTrack = Assert.Single(recipe.Composition.AudioTracks);
        Assert.NotEqual(Guid.Empty, videoTrack.Id);
        Assert.NotEqual(Guid.Empty, audioTrack.Id);
        Assert.Empty(videoTrack.Items);
        Assert.Empty(audioTrack.Items);
        Assert.Equal(revision.Id, composition.Virtual!.CurrentRecipeRevisionId);
        Assert.Equal(revision.Id, Assert.Single(workspace.Project!.RecipeDrafts).BasedOnRevisionId);
        Assert.Empty(composition.Provenance!.SourceAssetIds);

        var again = await service.CreateInitialAsync(source.Id);
        Assert.Same(composition, again);
        Assert.Equal(revision.Id, service.GetCurrent().Revision.Id);
    }

    [Fact]
    public async Task CreateInitialAsyncSaveFailureRestoresProjectState()
    {
        var project = new VideoProject { Name = "Initial composition rollback" };
        var location = new ProjectLocation(_root, Path.Combine(_root, "initial-rollback.rfp"));
        var store = new ToggleFailingProjectStore(project, location) { FailSaves = true };
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);
        var source = AddVideo(project, "source.mp4");
        var modifiedAt = project.ModifiedAt;
        var assets = project.Assets.ToArray();

        await Assert.ThrowsAsync<IOException>(() => new WorkingCompositionService(workspace).CreateInitialAsync(source.Id));

        Assert.Equal(modifiedAt, project.ModifiedAt);
        Assert.Equal(assets, project.Assets);
        Assert.Empty(project.RecipeRevisions);
        Assert.Empty(project.RecipeDrafts);
        Assert.Null(project.WorkingCompositionAssetId);
    }

    [Fact]
    public async Task LegacyPlacementCommandsRefuseBeforeMutatingComposition()
    {
        var workspace = await CreateWorkspaceAsync();
        var first = AddVideo(workspace.Project!, "first.mp4");
        var second = AddVideo(workspace.Project!, "second.mp4");
        var audio = AddAudio(workspace.Project!, "audio.wav");
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(first.Id);
        var before = service.GetCurrent();

        var videoError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddSegmentAsync(second.Id));
        var audioError = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAudioClipAsync(audio.Id, TimeSpan.Zero));

        Assert.Contains("timing-assessment", videoError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timing-assessment", audioError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(before.Revision, service.GetCurrent().Revision);
    }

    [Fact]
    public async Task AudioMixOperationsPreserveOccurrenceIdentityAndRejectLockedTracks()
    {
        var workspace = await CreateWorkspaceAsync();
        var source = AddAudio(workspace.Project!, "audio.wav");
        var service = new WorkingCompositionService(workspace);
        var composition = await service.CreateInitialAsync(AddVideo(workspace.Project!, "video.mp4").Id);
        var item = AudioItem(source.Id);
        ReplaceComposition(workspace.Project!, composition, new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [])],
            [new CompositionAudioTrack(Guid.NewGuid(), false, false, [item])]));

        await service.SetAudioClipMixAsync(item.Id, isMuted: true, gainDecibels: -6);
        await service.SetAudioClipPanAsync(item.Id, 0.25);
        await service.SetAudioClipFadesAsync(item.Id, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200));
        var changed = Assert.Single(service.GetCurrent().Recipe.Composition.AudioTracks.Single().Items);
        Assert.Equal(item.Id, changed.Id);
        Assert.True(changed.IsMuted);
        Assert.Equal(-6, changed.GainDecibels);
        Assert.Equal(0.25, changed.Pan);
        Assert.Equal(new ExactTime(1, 10), changed.FadeIn);
        Assert.Equal(new ExactTime(1, 5), changed.FadeOut);

        ReplaceComposition(workspace.Project!, composition, new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [])],
            [new CompositionAudioTrack(Guid.NewGuid(), true, false, [changed])]));
        var locked = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAudioClipPanAsync(item.Id, 0));
        Assert.Contains("Unlock the audio track", locked.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AudioOccurrenceEditSaveFailureRestoresRevisionCursorDraftAndIdentity()
    {
        var project = new VideoProject { Name = "Occurrence edit rollback" };
        var location = new ProjectLocation(_root, Path.Combine(_root, "occurrence-edit-rollback.rfp"));
        var store = new ToggleFailingProjectStore(project, location);
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);
        var source = AddAudio(project, "audio.wav");
        var service = new WorkingCompositionService(workspace);
        var composition = await service.CreateInitialAsync(AddVideo(project, "video.mp4").Id);
        var item = AudioItem(source.Id);
        ReplaceComposition(project, composition, new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [])],
            [new CompositionAudioTrack(Guid.NewGuid(), false, false, [item])]));
        var before = service.GetCurrent();
        var draft = Assert.Single(project.RecipeDrafts);
        var draftRecipe = draft.EditableRecipe;
        var draftBasedOn = draft.BasedOnRevisionId;
        var draftModifiedAt = draft.ModifiedAt;
        var revisionCount = project.RecipeRevisions.Count;
        var cursor = composition.Virtual!.CurrentRecipeRevisionId;
        var modifiedAt = project.ModifiedAt;

        store.FailSaves = true;
        await Assert.ThrowsAsync<IOException>(() => service.SetAudioClipPanAsync(item.Id, 0.25));

        var after = service.GetCurrent();
        Assert.Equal(revisionCount, project.RecipeRevisions.Count);
        Assert.Equal(cursor, composition.Virtual.CurrentRecipeRevisionId);
        Assert.Equal(modifiedAt, project.ModifiedAt);
        Assert.Same(before.Revision, after.Revision);
        Assert.Same(before.Recipe, after.Recipe);
        Assert.Same(item, Assert.Single(after.Recipe.Composition.AudioTracks.Single().Items));
        Assert.Equal(draftBasedOn, draft.BasedOnRevisionId);
        Assert.Same(draftRecipe, draft.EditableRecipe);
        Assert.Equal(draftModifiedAt, draft.ModifiedAt);
    }

    [Fact]
    public async Task RemovingLinkedItemRemovesItsPartnerAndHonorsEveryAffectedLock()
    {
        var workspace = await CreateWorkspaceAsync();
        var project = workspace.Project!;
        var source = AddVideo(project, "source.mp4");
        var composition = await new WorkingCompositionService(workspace).CreateInitialAsync(source.Id);
        var (video, audio) = LinkedItems(source.Id);
        ReplaceComposition(project, composition, new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [video])],
            [new CompositionAudioTrack(Guid.NewGuid(), false, false, [audio])]));
        var service = new WorkingCompositionService(workspace);

        await service.RemoveItemAsync(video.Id);
        var state = service.GetCurrent().Recipe.Composition;
        Assert.Empty(state.VideoTracks.Single().Items);
        Assert.Empty(state.AudioTracks.Single().Items);

        ReplaceComposition(project, composition, new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [video])],
            [new CompositionAudioTrack(Guid.NewGuid(), true, false, [audio])]));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveItemAsync(video.Id));
        Assert.Contains("Unlock every affected track", error.Message, StringComparison.Ordinal);
    }

    private async Task<ProjectWorkspace> CreateWorkspaceAsync()
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Composition command tests");
        return workspace;
    }

    private static ProjectAsset AddVideo(VideoProject project, string fileName) => AddPhysical(project, fileName, MediaType.Video);
    private static ProjectAsset AddAudio(VideoProject project, string fileName) => AddPhysical(project, fileName, MediaType.Audio);
    private static ProjectAsset AddPhysical(VideoProject project, string fileName, MediaType mediaType)
    {
        var asset = new ProjectAsset
        {
            DisplayName = fileName, FileName = fileName, MediaType = mediaType, StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage { RelativePath = fileName, ContentIdentity = new ContentIdentity { Sha256 = new string('b', 64), Status = ContentHashStatus.Verified } },
            Encoding = mediaType == MediaType.Video
                ? new MediaEncodingMetadata
                {
                    Video = new VideoStreamMetadata { StreamIndex = 0 },
                    Audio = new AudioStreamMetadata { StreamIndex = 1 }
                }
                : new MediaEncodingMetadata { Audio = new AudioStreamMetadata { StreamIndex = 1 } }
        };
        project.AddAsset(asset);
        return asset;
    }

    private static void ReplaceComposition(VideoProject project, ProjectAsset composition, WorkingCompositionState state)
        => project.CommitRecipe(composition.Id, new CompositionRecipe { Composition = state });

    private static CompositionAudioItem AudioItem(Guid sourceId) => new(
        Guid.NewGuid(), new AssetRevisionReference { AssetId = sourceId }, 1,
        new AudioSourceRange(new AudioSampleTime(0, 48000), new AudioSampleTime(192000, 48000)),
        Pin(MediaType.Audio, new ExactTime(4, 1)), new ExactTime(0, 1));

    private static (CompositionVideoItem Video, CompositionAudioItem Audio) LinkedItems(Guid sourceId)
    {
        var link = Guid.NewGuid();
        return (
            new CompositionVideoItem(Guid.NewGuid(), new AssetRevisionReference { AssetId = sourceId }, 0,
                new VideoSourceRange(new VideoPresentationTime(0, 1, 25), new VideoPresentationTime(100, 1, 25)),
                Pin(MediaType.Video, new ExactTime(4, 1)), new ExactTime(0, 1), link),
            new CompositionAudioItem(Guid.NewGuid(), new AssetRevisionReference { AssetId = sourceId }, 1,
                new AudioSourceRange(new AudioSampleTime(0, 48000), new AudioSampleTime(192000, 48000)),
                Pin(MediaType.Audio, new ExactTime(4, 1)), new ExactTime(0, 1), link));
    }

    private static StreamTimingAssessmentPin Pin(MediaType type, ExactTime duration) => new StreamTimingAssessment(
        Guid.NewGuid(), new string('b', 64), type, type == MediaType.Video ? 0 : 1,
        TimingReadiness.Exact, true, duration, [], new ExactTime(0, 1)).CreatePlacementPin();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ToggleFailingProjectStore(VideoProject project, ProjectLocation location) : IProjectStore
    {
        public bool FailSaves { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            Task.FromResult((project, location));

        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default) =>
            FailSaves ? Task.FromException(new IOException("Simulated project save failure.")) : Task.CompletedTask;
    }
}
