using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application.Editing.Audio;

internal sealed class CompositionAudioCommands
{
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionAudioCommands(
        CompositionCurrentAccessor current,
        TransactionalCompositionRevisionEditor editor)
    {
        _current = current;
        _editor = editor;
    }

    public Task<RecipeRevision> AddAsync(
        Guid sourceAssetId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken)
    {
        var normalizedStart = NormalizeTimelineStart(timelineStart);

        return _editor.UpdateAsync(recipe =>
        {
            var source = _current.RequireAudioSource(sourceAssetId);
            recipe.AudioClips.Add(new CompositionAudioClip
            {
                Source = new AssetRevisionReference { AssetId = source.Id },
                TimelineStartTicks = normalizedStart.Ticks
            });
        }, cancellationToken);
    }

    public async Task<CompositionAudioDetachmentResult> AddDetachedAsync(
        Guid segmentId,
        Guid audioAssetId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken)
    {
        var normalizedStart = NormalizeTimelineStart(timelineStart);
        var audioClipId = Guid.NewGuid();
        var audioSource = _current.RequireAudioSource(audioAssetId);

        var revision = await _editor.UpdateAsync(recipe =>
        {
            var index = recipe.Segments.FindIndex(segment => segment.Id == segmentId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition segment no longer exists.");

            recipe.Segments[index] = recipe.Segments[index] with { AudioEnabled = false };
            recipe.AudioClips.Add(new CompositionAudioClip
            {
                Id = audioClipId,
                Source = new AssetRevisionReference { AssetId = audioSource.Id },
                TimelineStartTicks = normalizedStart.Ticks
            });
        }, cancellationToken).ConfigureAwait(false);

        return new CompositionAudioDetachmentResult(revision, audioClipId);
    }

    public Task<RecipeRevision> SetTimelineStartAsync(
        Guid audioClipId,
        TimeSpan timelineStart,
        CancellationToken cancellationToken)
    {
        var normalizedStart = NormalizeTimelineStart(timelineStart);
        var (_, revision, recipe) = _current.GetCurrent();
        var currentClip = recipe.AudioClips.SingleOrDefault(clip => clip.Id == audioClipId)
            ?? throw new InvalidOperationException("The selected composition audio clip no longer exists.");
        if (currentClip.TimelineStartTicks == normalizedStart.Ticks)
            return Task.FromResult(revision);

        return _editor.UpdateAsync(candidate =>
        {
            var index = candidate.AudioClips.FindIndex(clip => clip.Id == audioClipId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition audio clip no longer exists.");
            candidate.AudioClips[index] = candidate.AudioClips[index] with
            {
                TimelineStartTicks = normalizedStart.Ticks
            };
        }, cancellationToken);
    }

    public Task<RecipeRevision> SetMixAsync(
        Guid audioClipId,
        bool isMuted,
        double gainDecibels,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(gainDecibels) || gainDecibels is < -60 or > 12)
            throw new ArgumentOutOfRangeException(nameof(gainDecibels), "Audio gain must be between -60 dB and +12 dB.");

        var (_, revision, recipe) = _current.GetCurrent();
        var currentClip = recipe.AudioClips.SingleOrDefault(clip => clip.Id == audioClipId)
            ?? throw new InvalidOperationException("The selected composition audio clip no longer exists.");
        if (currentClip.IsMuted == isMuted && currentClip.GainDecibels.Equals(gainDecibels))
            return Task.FromResult(revision);

        return _editor.UpdateAsync(candidate =>
        {
            var index = candidate.AudioClips.FindIndex(clip => clip.Id == audioClipId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition audio clip no longer exists.");
            candidate.AudioClips[index] = candidate.AudioClips[index] with
            {
                IsMuted = isMuted,
                GainDecibels = gainDecibels
            };
        }, cancellationToken);
    }

    public Task<RecipeRevision> SetFadesAsync(
        Guid audioClipId,
        TimeSpan fadeIn,
        TimeSpan fadeOut,
        CancellationToken cancellationToken)
    {
        var normalizedFadeIn = NormalizeFade(fadeIn, nameof(fadeIn));
        var normalizedFadeOut = NormalizeFade(fadeOut, nameof(fadeOut));
        var (_, revision, recipe) = _current.GetCurrent();
        var currentClip = recipe.AudioClips.SingleOrDefault(clip => clip.Id == audioClipId)
            ?? throw new InvalidOperationException("The selected composition audio clip no longer exists.");
        if (currentClip.FadeInMilliseconds == (long)normalizedFadeIn.TotalMilliseconds &&
            currentClip.FadeOutMilliseconds == (long)normalizedFadeOut.TotalMilliseconds)
            return Task.FromResult(revision);

        var source = _current.Project.Assets.SingleOrDefault(asset => asset.Id == currentClip.Source.AssetId)
            ?? throw new InvalidOperationException("The selected composition audio source no longer exists.");
        var durationSeconds = source.DurationSeconds ?? source.Encoding?.DurationSeconds;
        if (durationSeconds is > 0 && normalizedFadeIn.TotalSeconds > durationSeconds.Value)
            throw new ArgumentOutOfRangeException(nameof(fadeIn), "Fade in cannot be longer than the source audio clip.");
        if (durationSeconds is > 0 && normalizedFadeOut.TotalSeconds > durationSeconds.Value)
            throw new ArgumentOutOfRangeException(nameof(fadeOut), "Fade out cannot be longer than the source audio clip.");

        return _editor.UpdateAsync(candidate =>
        {
            var index = candidate.AudioClips.FindIndex(clip => clip.Id == audioClipId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition audio clip no longer exists.");
            candidate.AudioClips[index] = candidate.AudioClips[index] with
            {
                FadeInMilliseconds = (long)normalizedFadeIn.TotalMilliseconds,
                FadeOutMilliseconds = (long)normalizedFadeOut.TotalMilliseconds
            };
        }, cancellationToken);
    }

    public Task<RecipeRevision> SetPanAsync(
        Guid audioClipId,
        double pan,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(pan) || pan is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(pan), "Audio pan must be between -1 and +1.");

        pan = Math.Round(pan, 2, MidpointRounding.AwayFromZero);
        var (_, revision, recipe) = _current.GetCurrent();
        var currentClip = recipe.AudioClips.SingleOrDefault(clip => clip.Id == audioClipId)
            ?? throw new InvalidOperationException("The selected composition audio clip no longer exists.");
        if (currentClip.Pan.Equals(pan))
            return Task.FromResult(revision);

        return _editor.UpdateAsync(candidate =>
        {
            var index = candidate.AudioClips.FindIndex(clip => clip.Id == audioClipId);
            if (index < 0)
                throw new InvalidOperationException("The selected composition audio clip no longer exists.");
            candidate.AudioClips[index] = candidate.AudioClips[index] with { Pan = pan };
        }, cancellationToken);
    }

    private static TimeSpan NormalizeTimelineStart(TimeSpan timelineStart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timelineStart, TimeSpan.Zero);
        var milliseconds = Math.Round(timelineStart.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static TimeSpan NormalizeFade(TimeSpan fade, string parameterName)
    {
        if (fade < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "Audio fades cannot be negative.");

        var milliseconds = Math.Round(fade.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
