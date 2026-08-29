using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

/// <summary>
/// Immutable Working Composition track-management commands. These commands deliberately
/// stop at track structure and controls; timeline occurrence placement is owned by the
/// timing-aware placement surface.
/// </summary>
internal sealed class CompositionTrackCommands
{
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionTrackCommands(
        CompositionCurrentAccessor current,
        TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
        _editor = editor;
    }

    public async Task<CompositionTrackCommandResult> CreateAsync(
        CompositionTrackKind kind,
        int? insertionIndex,
        CancellationToken cancellationToken)
    {
        var (_, _, recipe) = _current.GetCurrent();
        var count = GetTrackCount(recipe.Composition, kind);
        var index = insertionIndex ?? count;
        RequireInsertionIndex(index, count);

        var trackId = Guid.NewGuid();
        var revision = await _editor.UpdateAsync(
            state => InsertTrack(state, kind, trackId, index),
            cancellationToken).ConfigureAwait(false);
        return new CompositionTrackCommandResult(revision, trackId, Changed: true);
    }

    public async Task<CompositionTrackCommandResult> DeleteEmptyAsync(
        CompositionTrackKind kind,
        Guid trackId,
        CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var track = RequireTrack(recipe.Composition, kind, trackId);
        if (track.IsLocked)
            throw new InvalidOperationException("Unlock the track before deleting it.");
        if (track.ItemCount != 0)
            throw new InvalidOperationException("Remove or move the timeline items before deleting this track.");

        var committed = await _editor.UpdateAsync(
            state => DeleteTrack(state, kind, trackId),
            cancellationToken).ConfigureAwait(false);
        return new CompositionTrackCommandResult(committed, trackId, Changed: committed.Id != revision.Id);
    }

    public async Task<CompositionTrackCommandResult> ReorderAsync(
        CompositionTrackKind kind,
        Guid trackId,
        int targetIndex,
        CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var track = RequireTrack(recipe.Composition, kind, trackId);
        if (track.IsLocked)
            throw new InvalidOperationException("Unlock the track before reordering it.");

        var tracks = GetTracks(recipe.Composition, kind);
        RequireExistingIndex(targetIndex, tracks.Count);
        var currentIndex = tracks.FindIndex(candidate => candidate.Id == trackId);
        if (currentIndex == targetIndex)
            return new CompositionTrackCommandResult(revision, trackId, Changed: false);

        var committed = await _editor.UpdateAsync(
            state => ReorderTrack(state, kind, trackId, targetIndex),
            cancellationToken).ConfigureAwait(false);
        return new CompositionTrackCommandResult(committed, trackId, Changed: true);
    }

    public async Task<CompositionTrackCommandResult> SetLockAsync(
        Guid trackId,
        bool isLocked,
        CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var track = RequireAnyTrack(recipe.Composition, trackId);
        if (track.IsLocked == isLocked)
            return new CompositionTrackCommandResult(revision, trackId, Changed: false);

        var committed = await _editor.UpdateAsync(
            state => SetLock(state, trackId, isLocked),
            cancellationToken).ConfigureAwait(false);
        return new CompositionTrackCommandResult(committed, trackId, Changed: true);
    }

    public async Task<CompositionTrackCommandResult> SetVideoVisibilityAsync(
        Guid trackId,
        bool isVisible,
        CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var track = RequireTrack(recipe.Composition, CompositionTrackKind.Video, trackId);
        if (track.IsLocked)
            throw new InvalidOperationException("Unlock the video track before changing its visibility.");
        if (track.Video!.IsVisible == isVisible)
            return new CompositionTrackCommandResult(revision, trackId, Changed: false);

        var committed = await _editor.UpdateAsync(
            state => SetVideoVisibility(state, trackId, isVisible),
            cancellationToken).ConfigureAwait(false);
        return new CompositionTrackCommandResult(committed, trackId, Changed: true);
    }

    public async Task<CompositionTrackCommandResult> SetAudioMuteAsync(
        Guid trackId,
        bool isMuted,
        CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var track = RequireTrack(recipe.Composition, CompositionTrackKind.Audio, trackId);
        if (track.IsLocked)
            throw new InvalidOperationException("Unlock the audio track before changing its mute state.");
        if (track.Audio!.IsMuted == isMuted)
            return new CompositionTrackCommandResult(revision, trackId, Changed: false);

        var committed = await _editor.UpdateAsync(
            state => SetAudioMute(state, trackId, isMuted),
            cancellationToken).ConfigureAwait(false);
        return new CompositionTrackCommandResult(committed, trackId, Changed: true);
    }

    private static WorkingCompositionState InsertTrack(WorkingCompositionState state, CompositionTrackKind kind, Guid trackId, int index) =>
        kind switch
        {
            CompositionTrackKind.Video => new WorkingCompositionState(
                Insert(state.VideoTracks, new CompositionVideoTrack(trackId, false, true, []), index),
                state.AudioTracks),
            CompositionTrackKind.Audio => new WorkingCompositionState(
                state.VideoTracks,
                Insert(state.AudioTracks, new CompositionAudioTrack(trackId, false, false, []), index)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static WorkingCompositionState DeleteTrack(WorkingCompositionState state, CompositionTrackKind kind, Guid trackId) =>
        kind switch
        {
            CompositionTrackKind.Video => new WorkingCompositionState(state.VideoTracks.Where(track => track.Id != trackId), state.AudioTracks),
            CompositionTrackKind.Audio => new WorkingCompositionState(state.VideoTracks, state.AudioTracks.Where(track => track.Id != trackId)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static WorkingCompositionState ReorderTrack(WorkingCompositionState state, CompositionTrackKind kind, Guid trackId, int targetIndex) =>
        kind switch
        {
            CompositionTrackKind.Video => new WorkingCompositionState(Reorder(state.VideoTracks, trackId, targetIndex), state.AudioTracks),
            CompositionTrackKind.Audio => new WorkingCompositionState(state.VideoTracks, Reorder(state.AudioTracks, trackId, targetIndex)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static WorkingCompositionState SetLock(WorkingCompositionState state, Guid trackId, bool isLocked) => new(
        state.VideoTracks.Select(track => track.Id == trackId
            ? new CompositionVideoTrack(track.Id, isLocked, track.IsVisible, track.Items)
            : track),
        state.AudioTracks.Select(track => track.Id == trackId
            ? new CompositionAudioTrack(track.Id, isLocked, track.IsMuted, track.Items)
            : track));

    private static WorkingCompositionState SetVideoVisibility(WorkingCompositionState state, Guid trackId, bool isVisible) => new(
        state.VideoTracks.Select(track => track.Id == trackId
            ? new CompositionVideoTrack(track.Id, track.IsLocked, isVisible, track.Items)
            : track),
        state.AudioTracks);

    private static WorkingCompositionState SetAudioMute(WorkingCompositionState state, Guid trackId, bool isMuted) => new(
        state.VideoTracks,
        state.AudioTracks.Select(track => track.Id == trackId
            ? new CompositionAudioTrack(track.Id, track.IsLocked, isMuted, track.Items)
            : track));

    private static List<T> Insert<T>(IReadOnlyList<T> tracks, T track, int index)
    {
        var result = tracks.ToList();
        result.Insert(index, track);
        return result;
    }

    private static List<T> Reorder<T>(IReadOnlyList<T> tracks, Guid trackId, int targetIndex) where T : class
    {
        var result = tracks.ToList();
        var currentIndex = result.FindIndex(track => GetTrackId(track) == trackId);
        var track = result[currentIndex];
        result.RemoveAt(currentIndex);
        result.Insert(targetIndex, track);
        return result;
    }

    private static int GetTrackCount(WorkingCompositionState state, CompositionTrackKind kind) =>
        GetTracks(state, kind).Count;

    private static List<TrackReference> GetTracks(WorkingCompositionState state, CompositionTrackKind kind) =>
        kind switch
        {
            CompositionTrackKind.Video => state.VideoTracks.Select(track => new TrackReference(track)).ToList(),
            CompositionTrackKind.Audio => state.AudioTracks.Select(track => new TrackReference(track)).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static TrackReference RequireTrack(WorkingCompositionState state, CompositionTrackKind kind, Guid trackId)
    {
        RequireTrackId(trackId);
        var matches = GetTracks(state, kind).SingleOrDefault(track => track.Id == trackId);
        if (matches is not null)
            return matches;

        var oppositeKind = kind == CompositionTrackKind.Video ? CompositionTrackKind.Audio : CompositionTrackKind.Video;
        if (GetTracks(state, oppositeKind).Any(track => track.Id == trackId))
            throw new InvalidOperationException($"The selected track is {oppositeKind.ToString().ToLowerInvariant()}, not {kind.ToString().ToLowerInvariant()}.");
        throw new InvalidOperationException("The selected composition track no longer exists.");
    }

    private static TrackReference RequireAnyTrack(WorkingCompositionState state, Guid trackId)
    {
        RequireTrackId(trackId);
        return state.VideoTracks.Select(track => new TrackReference(track))
            .Concat(state.AudioTracks.Select(track => new TrackReference(track)))
            .SingleOrDefault(track => track.Id == trackId)
            ?? throw new InvalidOperationException("The selected composition track no longer exists.");
    }

    private static Guid GetTrackId<T>(T track) where T : class => track switch
    {
        CompositionVideoTrack video => video.Id,
        CompositionAudioTrack audio => audio.Id,
        _ => throw new ArgumentException("A Working Composition track is required.", nameof(track))
    };

    private static void RequireTrackId(Guid trackId)
    {
        if (trackId == Guid.Empty)
            throw new ArgumentException("A composition track identifier is required.", nameof(trackId));
    }

    private static void RequireInsertionIndex(int index, int count)
    {
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(nameof(index), "A new track index must be within the track list.");
    }

    private static void RequireExistingIndex(int index, int count)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index), "A track reorder index must identify an existing track.");
    }

    private sealed class TrackReference
    {
        public TrackReference(CompositionVideoTrack track) => Video = track;
        public TrackReference(CompositionAudioTrack track) => Audio = track;

        public CompositionVideoTrack? Video { get; }
        public CompositionAudioTrack? Audio { get; }
        public Guid Id => Video?.Id ?? Audio!.Id;
        public bool IsLocked => Video?.IsLocked ?? Audio!.IsLocked;
        public int ItemCount => Video?.Items.Count ?? Audio!.Items.Count;
    }
}
