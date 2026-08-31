using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application.Editing.Audio;

internal sealed class CompositionAudioCommands
{
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionAudioCommands(CompositionCurrentAccessor current, TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
        _editor = editor;
    }

    public Task<RecipeRevision> AddAsync(Guid sourceAssetId, TimeSpan timelineStart, CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<RecipeRevision>(CompositionCurrentAccessor.TimingAwarePlacementRequired());
    }

    public Task<RecipeRevision> SetTimelineStartAsync(Guid audioClipId, TimeSpan timelineStart, CancellationToken cancellationToken)
    {
        _ = _current;
        return Task.FromException<RecipeRevision>(CompositionCurrentAccessor.OccurrenceAdapterRequired("Timeline-item movement"));
    }

    public Task<RecipeRevision> SetMixAsync(
        Guid audioClipId,
        bool isMuted,
        double gainDecibels,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(gainDecibels) || gainDecibels is < -60 or > 12)
            throw new ArgumentOutOfRangeException(nameof(gainDecibels), "Audio gain must be between -60 dB and +12 dB.");

        return UpdateAudioItemAsync(audioClipId, item => new CompositionAudioItem(
            item.Id, item.Source, item.SelectedStreamIndex, item.SourceRange, item.TimingAssessment,
            item.CompositionStart, item.LinkGroupId, isMuted, gainDecibels, item.Pan, item.FadeIn, item.FadeOut), cancellationToken);
    }

    public Task<RecipeRevision> SetFadesAsync(
        Guid audioClipId,
        TimeSpan fadeIn,
        TimeSpan fadeOut,
        CancellationToken cancellationToken)
    {
        var normalizedFadeIn = NormalizeFade(fadeIn, nameof(fadeIn));
        var normalizedFadeOut = NormalizeFade(fadeOut, nameof(fadeOut));
        return UpdateAudioItemAsync(audioClipId, item => new CompositionAudioItem(
            item.Id, item.Source, item.SelectedStreamIndex, item.SourceRange, item.TimingAssessment,
            item.CompositionStart, item.LinkGroupId, item.IsMuted, item.GainDecibels, item.Pan,
            normalizedFadeIn, normalizedFadeOut), cancellationToken);
    }

    public Task<RecipeRevision> SetPanAsync(Guid audioClipId, double pan, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(pan) || pan is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(pan), "Audio pan must be between -1 and +1.");

        pan = Math.Round(pan, 2, MidpointRounding.AwayFromZero);
        return UpdateAudioItemAsync(audioClipId, item => new CompositionAudioItem(
            item.Id, item.Source, item.SelectedStreamIndex, item.SourceRange, item.TimingAssessment,
            item.CompositionStart, item.LinkGroupId, item.IsMuted, item.GainDecibels, pan, item.FadeIn, item.FadeOut), cancellationToken);
    }

    private Task<RecipeRevision> UpdateAudioItemAsync(
        Guid audioClipId,
        Func<CompositionAudioItem, CompositionAudioItem> transform,
        CancellationToken cancellationToken)
    {
        var (_, revision, recipe) = _current.GetCurrent();
        var currentTrack = recipe.Composition.AudioTracks.SingleOrDefault(track => track.Items.Any(item => item.Id == audioClipId))
            ?? throw new InvalidOperationException("The selected composition audio item no longer exists.");
        if (currentTrack.IsLocked)
            throw new InvalidOperationException("Unlock the audio track before changing this timeline item.");
        var currentItem = currentTrack.Items.Single(item => item.Id == audioClipId);
        var changed = transform(currentItem);
        if (ReferenceEquals(currentItem, changed) || AudioItemEquals(currentItem, changed))
            return Task.FromResult(revision);

        return _editor.UpdateAsync(state =>
        {
            var track = state.AudioTracks.SingleOrDefault(candidate => candidate.Items.Any(item => item.Id == audioClipId))
                ?? throw new InvalidOperationException("The selected composition audio item no longer exists.");
            if (track.IsLocked)
                throw new InvalidOperationException("Unlock the audio track before changing this timeline item.");

            return new WorkingCompositionState(
                state.VideoTracks,
                state.AudioTracks.Select(candidate => candidate.Id != track.Id
                    ? candidate
                    : new CompositionAudioTrack(candidate.Id, candidate.IsLocked, candidate.IsMuted,
                        candidate.Items.Select(item => item.Id == audioClipId ? transform(item) : item), candidate.Name)));
        }, cancellationToken);
    }

    private static bool AudioItemEquals(CompositionAudioItem left, CompositionAudioItem right) =>
        left.IsMuted == right.IsMuted && left.GainDecibels.Equals(right.GainDecibels) && left.Pan.Equals(right.Pan) &&
        left.FadeIn == right.FadeIn && left.FadeOut == right.FadeOut;

    private static ExactTime NormalizeFade(TimeSpan fade, string parameterName)
    {
        if (fade < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "Audio fades cannot be negative.");
        var milliseconds = checked((long)Math.Round(fade.TotalMilliseconds, MidpointRounding.AwayFromZero));
        return new ExactTime(milliseconds, 1000);
    }
}
