using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Application.Editing.Composition;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Editing;

/// <summary>
/// Projects the current Working Composition into the editing controls and owns
/// timeline item selection. Rendering and media playback remain shell concerns.
/// </summary>
internal sealed class CompositionWorkspaceCoordinator : IDisposable, ICompositionPlacementDecisionProvider
{
    private readonly ProjectWorkspace _workspace;
    private readonly CompositionTimelineControl _timeline;
    private readonly EditToolsPanel _editTools;
    private readonly Func<double> _playbackSeconds;
    private readonly Func<bool> _isAuditionActive;
    private readonly Func<bool> _isPlaying;
    private readonly Func<bool> _isPlaybackEnabled;
    private readonly Func<bool> _isCompositionSelected;
    private readonly ICompositionWorkspaceHost _host;
    private readonly RecipeMediaMaterializer _materializer;
    private readonly ExactVideoFrameService _exactFrames;
    private readonly FfmpegAudioExtractionEngine _audioExtraction;
    private readonly FfprobeMediaInspectionService _mediaInspector;
    private readonly IStreamTimingAssessmentService _timingAssessment;
    private readonly IContentHashService _contentHash;
    private bool _disposed;

    public CompositionWorkspaceCoordinator(
        ProjectWorkspace workspace,
        CompositionTimelineControl timeline,
        EditToolsPanel editTools,
        Func<double> playbackSeconds,
        Func<bool> isAuditionActive,
        Func<bool> isPlaying,
        Func<bool> isPlaybackEnabled,
        Func<bool> isCompositionSelected,
        ICompositionWorkspaceHost host,
        RecipeMediaMaterializer materializer,
        ExactVideoFrameService exactFrames,
        FfmpegAudioExtractionEngine audioExtraction,
        FfprobeMediaInspectionService mediaInspector,
        IStreamTimingAssessmentService timingAssessment,
        IContentHashService contentHash)
    {
        _workspace = workspace;
        _timeline = timeline;
        _editTools = editTools;
        _playbackSeconds = playbackSeconds;
        _isAuditionActive = isAuditionActive;
        _isPlaying = isPlaying;
        _isPlaybackEnabled = isPlaybackEnabled;
        _isCompositionSelected = isCompositionSelected;
        _host = host;
        _materializer = materializer;
        _exactFrames = exactFrames;
        _audioExtraction = audioExtraction;
        _mediaInspector = mediaInspector;
        _timingAssessment = timingAssessment;
        _contentHash = contentHash;
        _timeline.SelectionChanged += Timeline_SelectionChanged;
        _timeline.SegmentReorderRequested += Timeline_SegmentReorderRequested;
        _timeline.AudioMoveRequested += Timeline_AudioMoveRequested;
        _timeline.MediaDropRequested += Timeline_MediaDropRequested;
        _timeline.SplitRequested += Timeline_SplitRequested;
        _timeline.ShiftLeftRequested += Timeline_ShiftLeftRequested;
        _timeline.ShiftRightRequested += Timeline_ShiftRightRequested;
        _timeline.DetachAudioRequested += Timeline_DetachAudioRequested;
        _timeline.RemoveRequested += Timeline_RemoveRequested;
        _timeline.TrackRenameRequested += Timeline_TrackRenameRequested;
        _timeline.TrackAppendRequested += Timeline_TrackAppendRequested;
        _timeline.TrackDeleteRequested += Timeline_TrackDeleteRequested;
        _timeline.TrackMoveUpRequested += Timeline_TrackMoveRequested;
        _timeline.TrackMoveDownRequested += Timeline_TrackMoveRequested;
        _timeline.TrackLockChanged += Timeline_TrackLockChanged;
        _timeline.VideoTrackVisibilityChanged += Timeline_VideoTrackVisibilityChanged;
        _timeline.AudioTrackMuteChanged += Timeline_AudioTrackMuteChanged;
        _editTools.SegmentAudioChanged += EditTools_SegmentAudioChanged;
        _editTools.AudioClipMutedChanged += EditTools_AudioClipMutedChanged;
        _editTools.AudioClipGainCommitted += EditTools_AudioClipGainCommitted;
        _editTools.AudioClipPanCommitted += EditTools_AudioClipPanCommitted;
        _editTools.AudioClipFadesCommitted += EditTools_AudioClipFadesCommitted;
    }

    public ObservableCollection<CompositionSegmentListItem> Segments { get; } = [];
    public ObservableCollection<CompositionAudioClipListItem> AudioClips { get; } = [];
    public ObservableCollection<CompositionTimelineTrackRow> Tracks { get; } = [];
    public Guid? SelectedSegmentId { get; private set; }
    public Guid? SelectedAudioClipId { get; private set; }
    public bool HasSegments => Segments.Count > 0;

    public event EventHandler? StateChanged;

    /// <summary>
    /// Raised only after a Working Composition mutation has completed successfully.
    /// Hosts use this semantic notification to invalidate revision-pinned previews;
    /// <see cref="StateChanged"/> also represents non-mutating selection and projection updates.
    /// </summary>
    public event EventHandler? RecipeMutationCommitted;

    /// <summary>
    /// Creates the project's initial Working Composition, selects it in Project Media,
    /// and lets the normal projection refresh route update the editor.
    /// </summary>
    public async Task<ProjectAsset> CreateInitialCompositionAsync(
        Guid sourceAssetId,
        CancellationToken cancellationToken = default)
    {
        var composition = await new WorkingCompositionService(_workspace)
            .CreateInitialAsync(sourceAssetId, cancellationToken);
        _host.RefreshProjectMedia(composition.Id);
        return composition;
    }

    public void SetSelection(Guid? segmentId, Guid? audioClipId)
    {
        SelectedSegmentId = segmentId;
        SelectedAudioClipId = audioClipId;
    }

    public void Clear()
    {
        Segments.Clear();
        Tracks.Clear();
        AudioClips.Clear();
        SelectedSegmentId = null;
        SelectedAudioClipId = null;
        _timeline.Clear();
        _editTools.ShowSelection(null, null);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Refresh()
    {
        var composition = _workspace.Project?.WorkingCompositionAssetId is { } compositionId
            ? _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == compositionId)
            : null;
        if (composition?.Virtual?.CurrentRecipeRevisionId is not { } revisionId)
        {
            Clear();
            return;
        }

        var project = _workspace.Project!;
        var recipe = AssertRecipe(project.RecipeRevisions.Single(candidate => candidate.Id == revisionId));
        var state = recipe.Composition;
        var priorSegment = SelectedSegmentId;
        var priorAudio = SelectedAudioClipId;
        Tracks.Clear();
        for (var index = 0; index < state.VideoTracks.Count; index++)
        {
            var track = state.VideoTracks[index];
            Tracks.Add(new CompositionTimelineTrackRow(track.Id, CompositionTimelineTrackKind.Video, index,
                track.IsLocked, track.IsVisible, track.Items.Count, track.Name));
        }
        for (var index = 0; index < state.AudioTracks.Count; index++)
        {
            var track = state.AudioTracks[index];
            Tracks.Add(new CompositionTimelineTrackRow(track.Id, CompositionTimelineTrackKind.Audio, index,
                track.IsLocked, track.IsMuted, track.Items.Count, track.Name));
        }
        Segments.Clear();
        var videos = state.VideoTracks
            .SelectMany(track => track.Items.Select(item => (Track: track, Item: item)))
            .OrderBy(entry => entry.Item.CompositionStart)
            .ThenBy(entry => entry.Track.Id)
            .ToArray();
        for (var index = 0; index < videos.Length; index++)
        {
            var (track, item) = videos[index];
            var source = project.Assets.SingleOrDefault(asset => asset.Id == item.Source.AssetId);
            var linkedAudio = item.LinkGroupId is { } linkGroupId
                ? state.AudioTracks.SelectMany(audioTrack => audioTrack.Items.Select(audio => (Track: audioTrack, Item: audio)))
                    .SingleOrDefault(entry => entry.Item.LinkGroupId == linkGroupId)
                : default;
            Segments.Add(new CompositionSegmentListItem(index, track.Id, item, source,
                linkedAudio.Item is { IsMuted: false } && !linkedAudio.Track.IsMuted));
        }

        AudioClips.Clear();
        foreach (var (track, item) in state.AudioTracks
                     .SelectMany(track => track.Items.Select(item => (Track: track, Item: item)))
                     .OrderBy(entry => entry.Item.CompositionStart)
                     .ThenBy(entry => entry.Track.Id))
        {
            var source = project.Assets.SingleOrDefault(asset => asset.Id == item.Source.AssetId);
            AudioClips.Add(new CompositionAudioClipListItem(track.Id, item, source));
        }

        SelectedSegmentId = priorSegment is { } segmentId && Segments.Any(item => item.SegmentId == segmentId) ? segmentId : null;
        SelectedAudioClipId = priorAudio is { } audioId && AudioClips.Any(item => item.AudioClipId == audioId) ? audioId : null;
        UpdateControls();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateControls()
    {
        var detachment = new CompositionSegmentAudioDetachmentService(
            _workspace,
            _materializer,
            _audioExtraction,
            new Sha256ContentHashService(),
            _mediaInspector,
            _timingAssessment);
        var capabilities = Segments.Select((segment, index) => new
        {
            ItemId = segment.SegmentId,
            Capability = new CompositionTimelineItemCapabilities(
                CanDetachAudio: detachment.CanDetach(segment.SegmentId),
                CanRemove: !IsTrackLocked(segment.TrackId))
        }).Concat(AudioClips.Select(clip => new
        {
            ItemId = clip.AudioClipId,
            Capability = new CompositionTimelineItemCapabilities(CanRemove: !IsTrackLocked(clip.TrackId))
        })).ToDictionary(item => item.ItemId, item => item.Capability);
        var eligibleAssets = _workspace.Project?.Assets
            .Where(asset => asset.StorageKind == AssetStorageKind.Physical)
            .Where(ProjectMediaDragData.CanAddToComposition)
            .Select(asset => new CompositionTimelineDropDescriptor(
                asset.Id,
                asset.EffectiveDisplayName,
                asset.MediaType == MediaType.Video
                    ? CompositionTimelineDropKind.Video
                    : CompositionTimelineDropKind.Audio))
            .ToArray() ?? [];
        _timeline.UpdateState(new CompositionTimelineState(Tracks.ToArray(), Segments.ToArray(), AudioClips.ToArray(), SelectedSegmentId,
            SelectedAudioClipId, _playbackSeconds(), _isAuditionActive(), _isPlaying(), _isPlaybackEnabled(),
            _isCompositionSelected(), SplitActionLabel(), _host.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame,
            capabilities, eligibleAssets));
        UpdateEditTools();
    }

    private void UpdateEditTools()
    {
        var segment = GetSelectedSegment();
        var audioClip = GetSelectedAudioClip();
        var videoState = segment is null ? null : new VideoSegmentEditState(segment.DisplayName, segment.DetailText,
            $"{segment.DurationText} • starts at {FormatTime(segment.TimelineStart)} • video track {segment.TrackId.ToString("N")[..8]}",
            segment.AudioEnabled,
            CanChangeSourceAudio: false,
            segment.IsTimingDegraded,
            segment.TimingWarningDetail);
        var maximumFadeSeconds = audioClip is null ? 0 : Math.Max(0, Math.Min(audioClip.DurationSeconds,
            Math.Max(0, _timeline.ProjectedDurationSeconds - audioClip.TimelineStart.TotalSeconds)));
        var audioState = audioClip is null ? null : new AudioClipEditState(audioClip.DisplayName,
            $"Starts at {FormatTime(audioClip.TimelineStart.TotalSeconds)} • {audioClip.DurationText}", audioClip.IsMuted,
            audioClip.GainDecibels, audioClip.Pan, audioClip.FadeIn, audioClip.FadeOut, maximumFadeSeconds,
            CanEdit: !IsTrackLocked(audioClip.TrackId),
            audioClip.IsTimingDegraded,
            audioClip.TimingWarningDetail);
        _editTools.ShowSelection(videoState, audioState);
    }

    private void Timeline_SelectionChanged(object? sender, CompositionTimelineSelectionChangedEventArgs e)
    {
        SetSelection(e.SegmentId, e.AudioClipId);
        UpdateControls();
    }

    private async void Timeline_SegmentReorderRequested(object? sender, CompositionTimelineReorderEventArgs e) =>
        await MutateAsync("Reordering composition segment…", async () =>
        {
            await new WorkingCompositionService(_workspace).MoveSegmentToIndexAsync(e.SegmentId, e.TargetIndex);
            SetSelection(e.SegmentId, null);
            Refresh();
            _host.SetStatus("Reordered the Working Composition. Preview it to rebuild the video.");
        }, completePending: true);

    private async void Timeline_AudioMoveRequested(object? sender, CompositionTimelineAudioMoveEventArgs e) =>
        await MutateAsync("Moving composition audio clip…", async () =>
        {
            await new WorkingCompositionService(_workspace).SetAudioClipTimelineStartAsync(e.AudioClipId, e.TimelineStart);
            SetSelection(null, e.AudioClipId);
            Refresh();
            _host.SetStatus($"Moved the audio clip to {FormatTime(e.TimelineStart.TotalSeconds)}. Preview the composition to rebuild it.");
        }, completePending: true);

    private async void Timeline_MediaDropRequested(object? sender, CompositionTimelineDropEventArgs e)
    {
        var source = _workspace.Project?.Assets.SingleOrDefault(asset => asset.Id == e.AssetId);
        if (source is not { IsDeleted: false, StorageKind: AssetStorageKind.Physical } ||
            !ProjectMediaDragData.CanAddToComposition(source))
            return;

        Guid? audioTargetTrackId = null;
        if (source.MediaType == MediaType.Video && source.Encoding?.Audio is not null)
        {
            var availableAudioTracks = Tracks
                .Where(track => track.Kind == CompositionTimelineTrackKind.Audio && !track.IsLocked)
                .ToArray();
            var selectedAudioTrack = _timeline.SelectedTrackId is { } selectedId
                ? availableAudioTracks.SingleOrDefault(track => track.TrackId == selectedId)
                : null;
            audioTargetTrackId = selectedAudioTrack?.TrackId ??
                                 (availableAudioTracks.Length == 1
                                     ? availableAudioTracks[0].TrackId
                                     : _host.SelectAudioPlacementTrack(availableAudioTracks));
            if (audioTargetTrackId is null && availableAudioTracks.Length > 0)
            {
                _host.SetStatus("Placement cancelled because no audio track was selected.");
                return;
            }
        }

        CompositionPhysicalPlacementResult? result = null;
        await _host.RunUiActionAsync("Assessing media timing for placement…", async () =>
        {
            var service = new CompositionPhysicalPlacementService(
                _workspace,
                _timingAssessment,
                _contentHash,
                this);
            result = await service.PlaceAsync(new CompositionPhysicalPlacementRequest(
                source.Id,
                ExactTimelineTime(e.TimelineSeconds),
                e.TargetTrackId,
                audioTargetTrackId,
                source.MediaType == MediaType.Video
                    ? CompositionPhysicalPlacementMode.AppendToVideoTrack
                    : CompositionPhysicalPlacementMode.AtRequestedTime));
        });
        if (result is null) return;

        _host.RefreshProjectMedia(_workspace.Project?.WorkingCompositionAssetId);
        switch (result.Status)
        {
            case CompositionPhysicalPlacementStatus.Placed:
                SetSelection(result.VideoItemId, result.VideoItemId is null ? result.AudioItemId : null);
                Refresh();
                RecipeMutationCommitted?.Invoke(this, EventArgs.Empty);
                _host.SetStatus(result.VideoReadiness == TimingReadiness.Estimated ||
                                result.AudioReadiness == TimingReadiness.Estimated
                    ? $"Placed {source.EffectiveDisplayName} with estimated-timing warnings."
                    : $"Placed {source.EffectiveDisplayName} in the Working Composition.");
                break;
            case CompositionPhysicalPlacementStatus.RepairRequested:
                _host.ShowPlacementInformation(
                    "Attempt Repair",
                    "Repair is not implemented in this milestone. The original media and its timing assessment were kept unchanged.");
                _host.SetStatus("No media was placed. The timing assessment remains available.");
                break;
            case CompositionPhysicalPlacementStatus.Cancelled:
                _host.SetStatus("Placement cancelled. No timeline occurrence was created.");
                break;
            case CompositionPhysicalPlacementStatus.Stale:
            case CompositionPhysicalPlacementStatus.Blocked:
                _host.ShowPlacementInformation("Unable to place media", result.Detail);
                _host.SetStatus(result.Detail);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown physical placement status '{result.Status}'.");
        }
    }

    public async Task<CompositionPlacementDecision> DecideAsync(
        CompositionPlacementDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_timeline.Dispatcher.CheckAccess())
            return _host.DecidePlacement(request);

        return await _timeline.Dispatcher.InvokeAsync(
            () => _host.DecidePlacement(request),
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken);
    }

    private async void Timeline_SplitRequested(object? sender, CompositionTimelineItemEventArgs e)
    {
        await SplitAsync(e.ItemId);
    }

    private async void Timeline_ShiftLeftRequested(object? sender, CompositionTimelineItemEventArgs e)
    {
        await MoveAsync(e.ItemId, -1);
    }

    private async void Timeline_ShiftRightRequested(object? sender, CompositionTimelineItemEventArgs e)
    {
        await MoveAsync(e.ItemId, 1);
    }

    private async void Timeline_DetachAudioRequested(object? sender, CompositionTimelineItemEventArgs e)
    {
        await DetachAsync(e.ItemId);
    }

    private async void Timeline_RemoveRequested(object? sender, CompositionTimelineItemEventArgs e)
    {
        await RemoveAsync(e.ItemId);
    }

    private async void Timeline_TrackRenameRequested(object? sender, CompositionTimelineTrackRenameEventArgs e)
    {
        var track = Tracks.SingleOrDefault(item => item.TrackId == e.TrackId);
        if (track is null || track.IsLocked) return;
        var name = _host.PromptTrackName(e.CurrentName, track.Kind);
        if (name is null) return;
        await MutateAsync("Renaming track…", async () =>
        {
            var result = await new WorkingCompositionService(_workspace)
                .RenameTrackAsync(ToCommandKind(track.Kind), track.TrackId, name);
            Refresh();
            _host.SetStatus(result.Changed ? $"Renamed track to {name.Trim()}." : "Track name is unchanged.");
        });
    }

    private async void Timeline_TrackAppendRequested(object? sender, CompositionTimelineTrackKindEventArgs e) =>
        await MutateAsync($"Creating {e.Kind.ToString().ToLowerInvariant()} track…", async () =>
        {
            await new WorkingCompositionService(_workspace).CreateTrackAsync(ToCommandKind(e.Kind));
            Refresh();
            _host.SetStatus($"Created a {e.Kind.ToString().ToLowerInvariant()} track.");
        });

    private async void Timeline_TrackDeleteRequested(object? sender, CompositionTimelineTrackEventArgs e)
    {
        var track = Tracks.SingleOrDefault(item => item.TrackId == e.TrackId);
        if (track is null || track.ItemCount != 0) return;
        await MutateAsync("Deleting empty track…", async () =>
        {
            await new WorkingCompositionService(_workspace).DeleteEmptyTrackAsync(ToCommandKind(track.Kind), track.TrackId);
            Refresh();
            _host.SetStatus($"Deleted empty {track.Kind.ToString().ToLowerInvariant()} track.");
        });
    }

    private async void Timeline_TrackMoveRequested(object? sender, CompositionTimelineTrackReorderEventArgs e)
    {
        var track = Tracks.SingleOrDefault(item => item.TrackId == e.TrackId);
        if (track is null || e.TargetIndex < 0) return;
        var sameKindCount = Tracks.Count(item => item.Kind == track.Kind);
        if (e.TargetIndex >= sameKindCount) return;
        await MutateAsync("Reordering track…", async () =>
        {
            await new WorkingCompositionService(_workspace).ReorderTrackAsync(ToCommandKind(track.Kind), track.TrackId, e.TargetIndex);
            Refresh();
            _host.SetStatus($"Reordered {track.Kind.ToString().ToLowerInvariant()} track.");
        });
    }

    private async void Timeline_TrackLockChanged(object? sender, CompositionTimelineTrackBooleanEventArgs e) =>
        await MutateTrackFlagAsync("Updating track lock…", e.TrackId, async service =>
            await service.SetTrackLockAsync(e.TrackId, e.Value), e.Value ? "Locked track." : "Unlocked track.");

    private async void Timeline_VideoTrackVisibilityChanged(object? sender, CompositionTimelineTrackBooleanEventArgs e) =>
        await MutateTrackFlagAsync("Updating video track visibility…", e.TrackId, async service =>
            await service.SetVideoTrackVisibilityAsync(e.TrackId, e.Value), e.Value ? "Video track visible." : "Video track hidden.");

    private async void Timeline_AudioTrackMuteChanged(object? sender, CompositionTimelineTrackBooleanEventArgs e) =>
        await MutateTrackFlagAsync("Updating audio track mute…", e.TrackId, async service =>
            await service.SetAudioTrackMutedAsync(e.TrackId, e.Value), e.Value ? "Audio track muted." : "Audio track audible.");

    private async Task MutateTrackFlagAsync(string action, Guid trackId,
        Func<WorkingCompositionService, Task<CompositionTrackCommandResult>> mutate, string status) =>
        await MutateAsync(action, async () =>
        {
            await mutate(new WorkingCompositionService(_workspace));
            Refresh();
            _host.SetStatus(status);
        });

    private async void EditTools_SegmentAudioChanged(object? sender, BooleanValueEventArgs e)
    {
        if (GetSelectedSegment() is not { } item || item.AudioEnabled == e.Value)
        {
            return;
        }
        await MutateAsync("Updating composition source audio…", async () =>
        {
            await new WorkingCompositionService(_workspace).SetSegmentAudioEnabledAsync(item.SegmentId, e.Value);
            SetSelection(item.SegmentId, null);
            Refresh();
            _host.SetStatus(e.Value ? $"Enabled source audio for {item.DisplayName}. Preview the composition to rebuild it."
                : $"Muted source audio for {item.DisplayName}. Preview the composition to rebuild it.");
        });
    }

    private async void EditTools_AudioClipMutedChanged(object? sender, BooleanValueEventArgs e)
    {
        if (GetSelectedAudioClip() is not { } item || item.IsMuted == e.Value)
        {
            return;
        }
        await UpdateAudioMixAsync(item, e.Value, item.GainDecibels, "Updating composition audio clip…", e.Value
            ? $"Muted {item.DisplayName}. Preview the composition to rebuild it."
            : $"Enabled {item.DisplayName}. Preview the composition to rebuild it.");
    }

    private async void EditTools_AudioClipGainCommitted(object? sender, DoubleValueEventArgs e)
    {
        if (GetSelectedAudioClip() is not { } item || Math.Abs(item.GainDecibels - e.Value) < 0.000_001)
        {
            return;
        }
        await UpdateAudioMixAsync(item, item.IsMuted, e.Value, "Updating composition audio gain…",
            $"Set {item.DisplayName} gain to {EditToolsPanel.FormatGainDecibels(e.Value)}. Preview the composition to rebuild it.");
    }

    private async void EditTools_AudioClipPanCommitted(object? sender, DoubleValueEventArgs e)
    {
        if (GetSelectedAudioClip() is not { } item || Math.Abs(item.Pan - e.Value) < 0.000_001)
        {
            return;
        }
        await MutateAsync("Updating composition audio pan…", async () =>
        {
            await new WorkingCompositionService(_workspace).SetAudioClipPanAsync(item.AudioClipId, e.Value);
            SetSelection(null, item.AudioClipId);
            Refresh();
            _host.SetStatus($"Set {item.DisplayName} pan to {EditToolsPanel.FormatAudioPan(e.Value)}. Preview the composition to rebuild it.");
        });
    }

    private async void EditTools_AudioClipFadesCommitted(object? sender, AudioFadesEventArgs e)
    {
        if (GetSelectedAudioClip() is not { } item ||
            (item.FadeIn == e.FadeIn && item.FadeOut == e.FadeOut))
        {
            return;
        }
        await MutateAsync("Updating composition audio fades…", async () =>
        {
            await new WorkingCompositionService(_workspace).SetAudioClipFadesAsync(item.AudioClipId, e.FadeIn, e.FadeOut);
            SetSelection(null, item.AudioClipId);
            Refresh();
            _host.SetStatus($"Set {item.DisplayName} fades to {EditToolsPanel.FormatFadeDuration(e.FadeIn.TotalSeconds)} in / " +
                $"{EditToolsPanel.FormatFadeDuration(e.FadeOut.TotalSeconds)} out. Preview the composition to rebuild it.");
        });
    }

    private async Task UpdateAudioMixAsync(CompositionAudioClipListItem item, bool muted, double gain, string action, string status) =>
        await MutateAsync(action, async () =>
        {
            await new WorkingCompositionService(_workspace).SetAudioClipMixAsync(item.AudioClipId, muted, gain);
            SetSelection(null, item.AudioClipId);
            Refresh();
            _host.SetStatus(status);
        });

    private async Task MoveAsync(Guid id, int offset)
    {
        if (GetSegment(id) is not { } item)
        {
            return;
        }
        await MutateAsync("Reordering the Working Composition…", async () =>
        {
            await new WorkingCompositionService(_workspace).MoveSegmentAsync(id, offset);
            SetSelection(item.SegmentId, null);
            Refresh();
            _host.SetStatus("Working Composition order updated.");
        });
    }

    private async Task RemoveAsync(Guid id)
    {
        var displayName = GetSegment(id)?.DisplayName ?? AudioClips.FirstOrDefault(item => item.AudioClipId == id)?.DisplayName;
        if (displayName is null)
        {
            return;
        }
        await MutateAsync($"Removing {displayName} from the composition…", async () =>
        {
            await new WorkingCompositionService(_workspace).RemoveItemAsync(id);
            SetSelection(null, null);
            Refresh();
            _host.SetStatus($"Removed {displayName} from the Working Composition.");
        });
    }

    private async Task SplitAsync(Guid id)
    {
        if (GetSegment(id) is not { } item || !_timeline.TryGetSegmentSpan(id, out var span))
        {
            return;
        }
        var offset = _playbackSeconds() - span.StartSeconds;
        _host.PausePreview();
        var edge = _host.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame ? AnchorBoundaryEdge.AfterFrame : AnchorBoundaryEdge.BeforeFrame;
        await MutateAsync("Splitting composition segment at the exact playhead frame…", async () =>
        {
            var result = await new CompositionSegmentSplitService(_workspace, _materializer, _exactFrames)
                .SplitAsync(id, TimeSpan.FromSeconds(offset), edge);
            SetSelection(result.TrailingSegmentId, null);
            var leading = _workspace.Project!.Assets.Single(asset => asset.Id == result.LeadingClipAssetId).EffectiveDisplayName;
            var trailing = _workspace.Project.Assets.Single(asset => asset.Id == result.TrailingClipAssetId).EffectiveDisplayName;
            _host.RefreshProjectMedia();
            _host.SetStatus($"Split {item.DisplayName} into Saved Clips '{leading}' and '{trailing}' at source " +
                $"{FormatTime(result.SourceTimestampSeconds)} ({(edge == AnchorBoundaryEdge.BeforeFrame ? "before" : "after")} selected frame).");
        });
    }

    private async Task DetachAsync(Guid id)
    {
        if (GetSegment(id) is not { } item)
        {
            return;
        }
        var fileName = _host.PromptDetachAudioFileName(item.DisplayName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }
        _host.PausePreview();
        await MutateAsync("Detaching exact segment audio…", async () =>
        {
            var result = await new CompositionSegmentAudioDetachmentService(_workspace, _materializer, _audioExtraction,
                new Sha256ContentHashService(), _mediaInspector, _timingAssessment).DetachAsync(id, fileName);
            SetSelection(null, result.AudioClipId);
            _host.RefreshProjectMedia();
            _host.SetStatus($"Detached {item.DisplayName} audio as '{result.AudioAsset.FileName}' at " +
                $"{FormatTime(result.TimelineStart.TotalSeconds)}.");
        });
    }

    private async Task MutateAsync(string action, Func<Task> mutation, bool completePending = false)
    {
        var mutationCompleted = false;
        try
        {
            await _host.RunUiActionAsync(action, async () =>
            {
                await mutation();
                mutationCompleted = true;
            });
            if (mutationCompleted)
            {
                RecipeMutationCommitted?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            if (completePending)
            {
                UpdateControls();
                _timeline.CompletePendingMutation();
            }
        }
    }

    private CompositionSegmentListItem? GetSelectedSegment() => SelectedSegmentId is { } id ? GetSegment(id) : null;
    private CompositionAudioClipListItem? GetSelectedAudioClip() => SelectedAudioClipId is { } id ? AudioClips.FirstOrDefault(item => item.AudioClipId == id) : null;
    private CompositionSegmentListItem? GetSegment(Guid id) => Segments.FirstOrDefault(item => item.SegmentId == id);

    private bool IsTrackLocked(Guid trackId)
    {
        var state = AssertRecipe(new WorkingCompositionService(_workspace).GetCurrent().Revision).Composition;
        return state.VideoTracks.Any(track => track.Id == trackId && track.IsLocked) ||
               state.AudioTracks.Any(track => track.Id == trackId && track.IsLocked);
    }

    private string SplitActionLabel() => _host.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame
        ? "Split after playhead frame" : "Split before playhead frame";
    private static CompositionRecipe AssertRecipe(RecipeRevision revision) => revision.Recipe as CompositionRecipe
        ?? throw new InvalidDataException("The Working Composition update did not produce a composition recipe.");
    private static CompositionTrackKind ToCommandKind(CompositionTimelineTrackKind kind) => kind == CompositionTimelineTrackKind.Video
        ? CompositionTrackKind.Video
        : CompositionTrackKind.Audio;
    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
    }

    internal static ExactTime ExactTimelineTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        return new ExactTime(
            checked((long)Math.Round(seconds * 1000, MidpointRounding.AwayFromZero)),
            1000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timeline.SelectionChanged -= Timeline_SelectionChanged;
        _timeline.SegmentReorderRequested -= Timeline_SegmentReorderRequested;
        _timeline.AudioMoveRequested -= Timeline_AudioMoveRequested;
        _timeline.MediaDropRequested -= Timeline_MediaDropRequested;
        _timeline.SplitRequested -= Timeline_SplitRequested;
        _timeline.ShiftLeftRequested -= Timeline_ShiftLeftRequested;
        _timeline.ShiftRightRequested -= Timeline_ShiftRightRequested;
        _timeline.DetachAudioRequested -= Timeline_DetachAudioRequested;
        _timeline.RemoveRequested -= Timeline_RemoveRequested;
        _timeline.TrackRenameRequested -= Timeline_TrackRenameRequested;
        _timeline.TrackAppendRequested -= Timeline_TrackAppendRequested;
        _timeline.TrackDeleteRequested -= Timeline_TrackDeleteRequested;
        _timeline.TrackMoveUpRequested -= Timeline_TrackMoveRequested;
        _timeline.TrackMoveDownRequested -= Timeline_TrackMoveRequested;
        _timeline.TrackLockChanged -= Timeline_TrackLockChanged;
        _timeline.VideoTrackVisibilityChanged -= Timeline_VideoTrackVisibilityChanged;
        _timeline.AudioTrackMuteChanged -= Timeline_AudioTrackMuteChanged;
        _editTools.SegmentAudioChanged -= EditTools_SegmentAudioChanged;
        _editTools.AudioClipMutedChanged -= EditTools_AudioClipMutedChanged;
        _editTools.AudioClipGainCommitted -= EditTools_AudioClipGainCommitted;
        _editTools.AudioClipPanCommitted -= EditTools_AudioClipPanCommitted;
        _editTools.AudioClipFadesCommitted -= EditTools_AudioClipFadesCommitted;
    }
}

internal interface ICompositionWorkspaceHost
{
    Task RunUiActionAsync(string status, Func<Task> action);
    void SetStatus(string status);
    void RefreshProjectMedia(Guid? selectedAssetId = null);
    void PausePreview();
    string? PromptDetachAudioFileName(string displayName);
    Guid? SelectAudioPlacementTrack(IReadOnlyList<CompositionTimelineTrackRow> tracks);
    string? PromptTrackName(string currentName, CompositionTimelineTrackKind kind);
    CompositionPlacementDecision DecidePlacement(CompositionPlacementDecisionRequest request);
    void ShowPlacementInformation(string title, string message);
    MediaSplitBehavior SplitBehavior { get; }
}
