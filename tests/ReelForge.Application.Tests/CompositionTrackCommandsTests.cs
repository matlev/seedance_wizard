using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Application.Tests;

public sealed class CompositionTrackCommandsTests
{
    [Fact]
    public async Task CreateTracksInsertsAtRequestedKindSpecificIndexAndCommitsOncePerChange()
    {
        var videoA = Guid.NewGuid();
        var videoB = Guid.NewGuid();
        var audio = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([Video(videoA), Video(videoB)], [Audio(audio)]);
        var service = new WorkingCompositionService(workspace);
        var originalRevisionCount = workspace.Project!.RecipeRevisions.Count;

        var video = await service.CreateTrackAsync(CompositionTrackKind.Video, 1);
        var audioResult = await service.CreateTrackAsync(CompositionTrackKind.Audio);

        var state = service.GetCurrent().Recipe.Composition;
        Assert.Equal([videoA, video.TrackId, videoB], state.VideoTracks.Select(track => track.Id));
        Assert.Equal([audio, audioResult.TrackId], state.AudioTracks.Select(track => track.Id));
        Assert.All(state.VideoTracks, track => Assert.True(track.IsVisible));
        Assert.All(state.AudioTracks, track => Assert.False(track.IsMuted));
        Assert.Equal(originalRevisionCount + 2, workspace.Project.RecipeRevisions.Count);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task DeleteAllowsLastEmptyTrackButRefusesNonEmptyTrackWithoutSaving()
    {
        var empty = Guid.NewGuid();
        var nonEmpty = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([Video(empty)], []);
        var service = new WorkingCompositionService(workspace);

        var deleted = await service.DeleteEmptyTrackAsync(CompositionTrackKind.Video, empty);

        Assert.True(deleted.Changed);
        Assert.Empty(service.GetCurrent().Recipe.Composition.VideoTracks);
        Assert.Equal(1, store.SaveCount);

        var (nonEmptyWorkspace, nonEmptyStore, _) = await OpenAsync([VideoWithItem(nonEmpty)], []);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkingCompositionService(nonEmptyWorkspace).DeleteEmptyTrackAsync(CompositionTrackKind.Video, nonEmpty));
        Assert.Contains("Remove or move", error.Message);
        Assert.Equal(0, nonEmptyStore.SaveCount);
    }

    [Fact]
    public async Task ReorderPreservesTrackIdentityItemsAndOppositeKindOrderWhileNoOpRetainsRevision()
    {
        var videoA = Guid.NewGuid();
        var videoB = Guid.NewGuid();
        var audioA = Guid.NewGuid();
        var audioB = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([VideoWithItem(videoA), Video(videoB)], [Audio(audioA), Audio(audioB)]);
        var service = new WorkingCompositionService(workspace);
        var current = service.GetCurrent();

        var noOp = await service.ReorderTrackAsync(CompositionTrackKind.Video, videoA, 0);
        var moved = await service.ReorderTrackAsync(CompositionTrackKind.Video, videoA, 1);

        var state = service.GetCurrent().Recipe.Composition;
        Assert.False(noOp.Changed);
        Assert.Equal(current.Revision.Id, noOp.Revision.Id);
        Assert.True(moved.Changed);
        Assert.Equal([videoB, videoA], state.VideoTracks.Select(track => track.Id));
        Assert.Single(state.VideoTracks[1].Items);
        Assert.Equal([audioA, audioB], state.AudioTracks.Select(track => track.Id));
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task TrackControlsChangeOnlyTheirApplicableTrackAndNoOpDoesNotSave()
    {
        var video = Guid.NewGuid();
        var audio = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([Video(video)], [Audio(audio)]);
        var service = new WorkingCompositionService(workspace);
        var original = service.GetCurrent().Revision;

        var visibilityNoOp = await service.SetVideoTrackVisibilityAsync(video, true);
        await service.SetVideoTrackVisibilityAsync(video, false);
        await service.SetAudioTrackMutedAsync(audio, true);

        var state = service.GetCurrent().Recipe.Composition;
        Assert.False(visibilityNoOp.Changed);
        Assert.Equal(original.Id, visibilityNoOp.Revision.Id);
        Assert.False(state.VideoTracks.Single().IsVisible);
        Assert.True(state.AudioTracks.Single().IsMuted);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task LockedTracksRefuseTrackMutationUntilExplicitlyUnlocked()
    {
        var video = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([Video(video)], []);
        var service = new WorkingCompositionService(workspace);

        await service.SetTrackLockAsync(video, true);
        var delete = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteEmptyTrackAsync(CompositionTrackKind.Video, video));
        var reorder = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReorderTrackAsync(CompositionTrackKind.Video, video, 0));
        var visibility = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetVideoTrackVisibilityAsync(video, false));
        Assert.Contains("Unlock", delete.Message);
        Assert.Contains("Unlock", reorder.Message);
        Assert.Contains("Unlock", visibility.Message);

        var unlocked = await service.SetTrackLockAsync(video, false);
        Assert.True(unlocked.Changed);
        Assert.False(service.GetCurrent().Recipe.Composition.VideoTracks.Single().IsLocked);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact]
    public async Task InvalidAndWrongKindCommandsRefuseBeforeMutation()
    {
        var video = Guid.NewGuid();
        var audio = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([Video(video)], [Audio(audio)]);
        var service = new WorkingCompositionService(workspace);
        var revisions = workspace.Project!.RecipeRevisions.Count;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.CreateTrackAsync(CompositionTrackKind.Video, 2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.ReorderTrackAsync(CompositionTrackKind.Video, video, 1));
        var wrongKind = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetVideoTrackVisibilityAsync(audio, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetTrackLockAsync(Guid.NewGuid(), true));

        Assert.Contains("audio, not video", wrongKind.Message);
        Assert.Equal(revisions, workspace.Project.RecipeRevisions.Count);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SaveFailureRollsBackTrackChangeAndRevision()
    {
        var video = Guid.NewGuid();
        var (workspace, store, _) = await OpenAsync([Video(video)], [], failSave: true);
        var service = new WorkingCompositionService(workspace);
        var revision = service.GetCurrent().Revision.Id;

        await Assert.ThrowsAsync<IOException>(() => service.CreateTrackAsync(CompositionTrackKind.Video));

        Assert.Equal(revision, service.GetCurrent().Revision.Id);
        Assert.Equal([video], service.GetCurrent().Recipe.Composition.VideoTracks.Select(track => track.Id));
        Assert.Equal(1, store.SaveCount);
    }

    private static CompositionVideoTrack Video(Guid id) => new(id, false, true, []);
    private static CompositionAudioTrack Audio(Guid id) => new(id, false, false, []);

    private static CompositionVideoTrack VideoWithItem(Guid id) => new(id, false, true,
    [
        new CompositionVideoItem(
            Guid.NewGuid(),
            new AssetRevisionReference { AssetId = Guid.NewGuid(), RecipeRevisionId = Guid.NewGuid() },
            0,
            null,
            EstimatedVideoPin(),
            new ExactTime(0, 1))
    ]);

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

    private static async Task<(ProjectWorkspace Workspace, RecordingStore Store, ProjectAsset Composition)> OpenAsync(
        IEnumerable<CompositionVideoTrack> videoTracks,
        IEnumerable<CompositionAudioTrack> audioTracks,
        bool failSave = false)
    {
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            FileName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        var project = new VideoProject
        {
            Assets = [composition],
            WorkingCompositionAssetId = composition.Id
        };
        project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState(videoTracks, audioTracks)
        });

        var location = new ProjectLocation("C:\\tracks", "C:\\tracks\\Tracks.rfp");
        var store = new RecordingStore(project, location, failSave);
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.OpenAsync(location.ProjectFilePath);
        return (workspace, store, composition);
    }

    private sealed class RecordingStore(VideoProject project, ProjectLocation location, bool failSave) : IProjectStore
    {
        public int SaveCount { get; private set; }
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) => Task.FromResult((project, location));
        public Task SaveAsync(VideoProject savedProject, ProjectLocation savedLocation, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return failSave ? Task.FromException(new IOException("simulated save failure")) : Task.CompletedTask;
        }
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
