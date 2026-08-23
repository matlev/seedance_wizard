using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Editing;

/// <summary>
/// Projects the current Working Composition into the editing controls and owns
/// timeline item selection. Rendering and media playback remain shell concerns.
/// </summary>
internal sealed class CompositionWorkspaceCoordinator : IDisposable
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
        FfprobeMediaInspectionService mediaInspector)
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
        _timeline.SelectionChanged += Timeline_SelectionChanged;
        _timeline.SegmentReorderRequested += Timeline_SegmentReorderRequested;
        _timeline.AudioMoveRequested += Timeline_AudioMoveRequested;
        _timeline.MediaDropRequested += Timeline_MediaDropRequested;
        _timeline.SplitRequested += Timeline_SplitRequested;
        _timeline.ShiftLeftRequested += Timeline_ShiftLeftRequested;
        _timeline.ShiftRightRequested += Timeline_ShiftRightRequested;
        _timeline.DetachAudioRequested += Timeline_DetachAudioRequested;
        _timeline.RemoveRequested += Timeline_RemoveRequested;
        _editTools.SegmentAudioChanged += EditTools_SegmentAudioChanged;
        _editTools.AudioClipMutedChanged += EditTools_AudioClipMutedChanged;
        _editTools.AudioClipGainCommitted += EditTools_AudioClipGainCommitted;
        _editTools.AudioClipPanCommitted += EditTools_AudioClipPanCommitted;
        _editTools.AudioClipFadesCommitted += EditTools_AudioClipFadesCommitted;
    }

    public ObservableCollection<CompositionSegmentListItem> Segments { get; } = [];
    public ObservableCollection<CompositionAudioClipListItem> AudioClips { get; } = [];
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
        var priorSegment = SelectedSegmentId;
        var priorAudio = SelectedAudioClipId;
        Segments.Clear();
        for (var index = 0; index < recipe.Segments.Count; index++)
        {
            var segment = recipe.Segments[index];
            var source = project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId);
            Segments.Add(new CompositionSegmentListItem(index, segment, source,
                CompositionSegmentTiming.ResolveDuration(project, segment, source)));
        }

        AudioClips.Clear();
        foreach (var clip in recipe.AudioClips)
        {
            var source = project.Assets.SingleOrDefault(asset => asset.Id == clip.Source.AssetId);
            AudioClips.Add(new CompositionAudioClipListItem(clip, source));
        }

        SelectedSegmentId = priorSegment is { } segmentId && Segments.Any(item => item.SegmentId == segmentId) ? segmentId : null;
        SelectedAudioClipId = priorAudio is { } audioId && AudioClips.Any(item => item.AudioClipId == audioId) ? audioId : null;
        UpdateControls();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateControls()
    {
        var capabilities = Segments.Select((segment, index) => new
        {
            ItemId = segment.SegmentId,
            Capability = new CompositionTimelineItemCapabilities(segment.DurationSeconds is > 0, CanDetachAudio(segment.SegmentId),
                index > 0, index < Segments.Count - 1, Segments.Count > 1)
        }).Concat(AudioClips.Select(clip => new
        {
            ItemId = clip.AudioClipId,
            Capability = new CompositionTimelineItemCapabilities(CanRemove: true)
        })).ToDictionary(item => item.ItemId, item => item.Capability);
        var eligibleAssets = _workspace.Project?.Assets
            .Where(asset => asset.Id != _workspace.Project.WorkingCompositionAssetId)
            .Where(ProjectMediaDragData.CanAddToComposition)
            .Select(asset => new CompositionTimelineDropDescriptor(asset.Id, asset.EffectiveDisplayName,
                asset.MediaType == MediaType.Video ? CompositionTimelineDropKind.Video : CompositionTimelineDropKind.Audio))
            .ToArray() ?? [];
        _timeline.UpdateState(new CompositionTimelineState(Segments.ToArray(), AudioClips.ToArray(), SelectedSegmentId,
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
            $"{segment.DurationText} • position {segment.Index + 1} of {Segments.Count} on the sequential video track", segment.AudioEnabled);
        var maximumFadeSeconds = audioClip is null ? 0 : Math.Max(0, Math.Min(audioClip.DurationSeconds ?? 30,
            Math.Max(0, _timeline.ProjectedDurationSeconds - audioClip.TimelineStart.TotalSeconds)));
        var audioState = audioClip is null ? null : new AudioClipEditState(audioClip.DisplayName,
            $"Starts at {FormatTime(audioClip.TimelineStart.TotalSeconds)} • {audioClip.DurationText}", audioClip.IsMuted,
            audioClip.GainDecibels, audioClip.Pan, audioClip.FadeIn, audioClip.FadeOut, maximumFadeSeconds);
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
        var asset = _workspace.Project?.Assets.SingleOrDefault(item => item.Id == e.AssetId);
        if (asset is null) return;
        var action = e.Kind == CompositionTimelineDropKind.Video
            ? $"Inserting {asset.EffectiveDisplayName} into the composition…"
            : $"Adding {asset.EffectiveDisplayName} to the audio track…";
        await MutateAsync(action, async () =>
        {
            var service = new WorkingCompositionService(_workspace);
            if (e.Kind == CompositionTimelineDropKind.Video)
            {
                var recipe = AssertRecipe(await service.AddSegmentAsync(asset.Id, e.InsertionIndex));
                SetSelection(recipe.Segments[Math.Clamp(e.InsertionIndex, 0, recipe.Segments.Count - 1)].Id, null);
                _host.SetStatus($"Inserted {asset.EffectiveDisplayName} into the Working Composition.");
            }
            else
            {
                var recipe = AssertRecipe(await service.AddAudioClipAsync(asset.Id, TimeSpan.FromSeconds(e.TimelineSeconds)));
                SetSelection(null, recipe.AudioClips[^1].Id);
                _host.SetStatus($"Added {asset.EffectiveDisplayName} at {FormatTime(e.TimelineSeconds)}.");
            }
            Refresh();
        });
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
                new Sha256ContentHashService(), _mediaInspector).DetachAsync(id, fileName);
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

    private bool CanDetachAudio(Guid segmentId)
    {
        if (_workspace.Project?.WorkingCompositionAssetId is null) return false;
        var recipe = AssertRecipe(new WorkingCompositionService(_workspace).GetCurrent().Revision);
        var segment = recipe.Segments.SingleOrDefault(candidate => candidate.Id == segmentId);
        if (segment is null) return false;
        var source = _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == segment.Source.AssetId);
        if (!MediaAudioCapabilityPolicy.CanAttemptAudioOperation(source)) return false;
        return !recipe.AudioClips.Any(clip => _workspace.Project.Assets.SingleOrDefault(asset => asset.Id == clip.Source.AssetId)?.Provenance is
            { Operation: "detach-segment-audio" } provenance && provenance.Parameters.GetValueOrDefault("compositionSegmentId") == segmentId.ToString("D"));
    }

    private string SplitActionLabel() => _host.SplitBehavior == MediaSplitBehavior.AfterSelectedFrame
        ? "Split after playhead frame" : "Split before playhead frame";
    private static CompositionRecipe AssertRecipe(RecipeRevision revision) => revision.Recipe as CompositionRecipe
        ?? throw new InvalidDataException("The Working Composition update did not produce a composition recipe.");
    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
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
    MediaSplitBehavior SplitBehavior { get; }
}
