namespace ReelForge.App.Views.Editing;

/// <summary>
/// Immutable shell-to-control snapshot. The control deliberately knows only the
/// presentation projection; the shell remains the owner of project mutations.
/// </summary>
public sealed record CompositionTimelineState(
    IReadOnlyList<CompositionTimelineTrackRow> Tracks,
    IReadOnlyList<CompositionSegmentListItem> Segments,
    IReadOnlyList<CompositionAudioClipListItem> AudioClips,
    Guid? SelectedSegmentId,
    Guid? SelectedAudioClipId,
    double PlaybackSeconds,
    bool IsPlaybackVisible,
    bool IsPlaying,
    bool IsInteractive,
    bool IsCompositionSelected,
    string SplitActionLabel,
    bool SplitAfterSelectedFrame,
    IReadOnlyDictionary<Guid, CompositionTimelineItemCapabilities> Capabilities,
    IReadOnlyList<CompositionTimelineDropDescriptor> EligibleDropItems)
{
    public static CompositionTimelineState Empty { get; } = new(
        [],
        [],
        [],
        null,
        null,
        0,
        false,
        false,
        false,
        false,
        "Split",
        false,
        new Dictionary<Guid, CompositionTimelineItemCapabilities>(),
        []);
}

public enum CompositionTimelineTrackKind
{
    Video,
    Audio
}

/// <summary>Presentation state for one persisted, ordered track, including empty tracks.</summary>
public sealed record CompositionTimelineTrackRow(
    Guid TrackId,
    CompositionTimelineTrackKind Kind,
    int Index,
    bool IsLocked,
    bool IsVisibleOrMuted,
    int ItemCount)
{
    public string DisplayName => $"{(Kind == CompositionTimelineTrackKind.Video ? "Video" : "Audio")} {Index + 1}";
    public string StatusText => Kind == CompositionTimelineTrackKind.Video
        ? (IsVisibleOrMuted ? "Visible" : "Hidden")
        : (IsVisibleOrMuted ? "Muted" : "Audible");
}

public sealed record CompositionTimelineItemCapabilities(
    bool CanSplit = false,
    bool CanDetachAudio = false,
    bool CanShiftLeft = false,
    bool CanShiftRight = false,
    bool CanRemove = false);

/// <summary>
/// Read-only timing projection used by shell policy such as exact split and fade limits.
/// It intentionally exposes no WPF element or layout implementation detail.
/// </summary>
public sealed record CompositionTimelineSegmentSpan(
    Guid SegmentId,
    double StartSeconds,
    double DurationSeconds);

public sealed record CompositionTimelineDropDescriptor(
    Guid AssetId,
    string DisplayName,
    CompositionTimelineDropKind Kind);

public enum CompositionTimelineDropKind
{
    Video,
    Audio
}

public sealed class CompositionTimelineSelectionChangedEventArgs(
    Guid? segmentId,
    Guid? audioClipId) : EventArgs
{
    public Guid? SegmentId { get; } = segmentId;
    public Guid? AudioClipId { get; } = audioClipId;
}

public sealed class CompositionTimelineActivationEventArgs(
    double? pendingRulerSeekSeconds) : EventArgs
{
    public double? PendingRulerSeekSeconds { get; } = pendingRulerSeekSeconds;
}

public sealed class CompositionTimelineSeekEventArgs(
    double seconds,
    bool resumePlayback,
    CompositionTimelineSeekPhase phase) : EventArgs
{
    public double Seconds { get; } = seconds;
    public bool ResumePlayback { get; } = resumePlayback;
    public CompositionTimelineSeekPhase Phase { get; } = phase;
}

public enum CompositionTimelineSeekPhase
{
    Started,
    Changed,
    Completed,
    Cancelled
}

public sealed class CompositionTimelineReorderEventArgs(
    Guid segmentId,
    int targetIndex) : EventArgs
{
    public Guid SegmentId { get; } = segmentId;
    public int TargetIndex { get; } = targetIndex;
}

public sealed class CompositionTimelineAudioMoveEventArgs(
    Guid audioClipId,
    TimeSpan timelineStart) : EventArgs
{
    public Guid AudioClipId { get; } = audioClipId;
    public TimeSpan TimelineStart { get; } = timelineStart;
}

public sealed class CompositionTimelineDropEventArgs(
    Guid assetId,
    CompositionTimelineDropKind kind,
    double timelineSeconds,
    int insertionIndex) : EventArgs
{
    public Guid AssetId { get; } = assetId;
    public CompositionTimelineDropKind Kind { get; } = kind;
    public double TimelineSeconds { get; } = timelineSeconds;
    public int InsertionIndex { get; } = insertionIndex;
}

public sealed class CompositionTimelineItemEventArgs(Guid itemId) : EventArgs
{
    public Guid ItemId { get; } = itemId;
}

public sealed class CompositionTimelineTrackEventArgs(Guid trackId) : EventArgs
{
    public Guid TrackId { get; } = trackId;
}

public sealed class CompositionTimelineTrackReorderEventArgs(Guid trackId, int targetIndex) : EventArgs
{
    public Guid TrackId { get; } = trackId;
    public int TargetIndex { get; } = targetIndex;
}

public sealed class CompositionTimelineTrackBooleanEventArgs(Guid trackId, bool value) : EventArgs
{
    public Guid TrackId { get; } = trackId;
    public bool Value { get; } = value;
}

public sealed class CompositionTimelineTrackKindEventArgs(CompositionTimelineTrackKind kind) : EventArgs
{
    public CompositionTimelineTrackKind Kind { get; } = kind;
}
