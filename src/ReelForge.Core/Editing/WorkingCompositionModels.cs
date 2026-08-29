namespace ReelForge.Core;

/// <summary>Immutable, ordered multitrack composition meaning.</summary>
public sealed class WorkingCompositionState
{
    public WorkingCompositionState(IEnumerable<CompositionVideoTrack> videoTracks, IEnumerable<CompositionAudioTrack> audioTracks)
    {
        VideoTracks = WorkingCompositionCollection.Copy(videoTracks);
        AudioTracks = WorkingCompositionCollection.Copy(audioTracks);
        ValidateGlobalIdentityAndLinks(VideoTracks, AudioTracks);
        ContributingVideoTracks = WorkingCompositionCollection.Copy(VideoTracks.Where(track => track.IsVisible));
        ContributingAudioTracks = WorkingCompositionCollection.Copy(AudioTracks.Where(track => !track.IsMuted));
    }

    public IReadOnlyList<CompositionVideoTrack> VideoTracks { get; }
    public IReadOnlyList<CompositionAudioTrack> AudioTracks { get; }
    public IReadOnlyList<CompositionVideoTrack> ContributingVideoTracks { get; }
    public IReadOnlyList<CompositionAudioTrack> ContributingAudioTracks { get; }

    private static void ValidateGlobalIdentityAndLinks(IReadOnlyList<CompositionVideoTrack> videoTracks, IReadOnlyList<CompositionAudioTrack> audioTracks)
    {
        var trackIds = videoTracks.Select(track => track.Id).Concat(audioTracks.Select(track => track.Id)).ToArray();
        if (trackIds.Distinct().Count() != trackIds.Length)
            throw new ArgumentException("Track identifiers must be unique across video and audio tracks.");

        var items = videoTracks.SelectMany(track => track.Items.Cast<ICompositionItem>())
            .Concat(audioTracks.SelectMany(track => track.Items.Cast<ICompositionItem>())).ToArray();
        if (items.Select(item => item.Id).Distinct().Count() != items.Length)
            throw new ArgumentException("Timeline-item identifiers must be unique across all tracks.");

        foreach (var group in items.Where(item => item.LinkGroupId.HasValue).GroupBy(item => item.LinkGroupId!.Value))
        {
            var members = group.ToArray();
            var videos = members.OfType<CompositionVideoItem>().ToArray();
            var audios = members.OfType<CompositionAudioItem>().ToArray();
            if (members.Length != 2 || videos.Length != 1 || audios.Length != 1)
                throw new ArgumentException("Each link group must contain exactly one video item and one audio item.");

            var video = videos[0];
            var audio = audios[0];
            if (video.Source != audio.Source)
                throw new ArgumentException("Linked video and audio items must reference the same exact source revision.");
            if (!string.Equals(video.TimingAssessment.SourceContentHash, audio.TimingAssessment.SourceContentHash, StringComparison.Ordinal))
                throw new ArgumentException("Linked video and audio items must pin the same source content hash.");
        }
    }
}

public sealed class CompositionVideoTrack
{
    public CompositionVideoTrack(Guid id, bool isLocked, bool isVisible, IEnumerable<CompositionVideoItem> items)
    {
        WorkingCompositionGuards.RequireId(id, nameof(id));
        Id = id;
        IsLocked = isLocked;
        IsVisible = isVisible;
        Items = WorkingCompositionCollection.Copy(items);
    }
    public Guid Id { get; }
    public bool IsLocked { get; }
    public bool IsVisible { get; }
    public IReadOnlyList<CompositionVideoItem> Items { get; }
}

public sealed class CompositionAudioTrack
{
    public CompositionAudioTrack(Guid id, bool isLocked, bool isMuted, IEnumerable<CompositionAudioItem> items)
    {
        WorkingCompositionGuards.RequireId(id, nameof(id));
        Id = id;
        IsLocked = isLocked;
        IsMuted = isMuted;
        Items = WorkingCompositionCollection.Copy(items);
    }
    public Guid Id { get; }
    public bool IsLocked { get; }
    public bool IsMuted { get; }
    public IReadOnlyList<CompositionAudioItem> Items { get; }
}

public sealed class CompositionVideoItem : ICompositionItem
{
    public CompositionVideoItem(Guid id, AssetRevisionReference source, int selectedStreamIndex, VideoSourceRange? sourceRange, StreamTimingAssessmentPin timingAssessment, ExactTime compositionStart, Guid? linkGroupId = null)
    {
        WorkingCompositionGuards.RequireId(id, nameof(id));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        WorkingCompositionGuards.RequireSource(Source, nameof(source));
        WorkingCompositionGuards.RequireNonnegative(selectedStreamIndex, nameof(selectedStreamIndex));
        TimingAssessment = timingAssessment ?? throw new ArgumentNullException(nameof(timingAssessment));
        if (TimingAssessment.MediaType != MediaType.Video)
            throw new ArgumentException("A video item requires a video timing assessment pin.", nameof(timingAssessment));
        if (TimingAssessment.SelectedStreamIndex != selectedStreamIndex)
            throw new ArgumentException("The timing assessment pin must match the selected video stream.", nameof(timingAssessment));
        if (TimingAssessment.Readiness == TimingReadiness.Exact && sourceRange is null)
            throw new ArgumentException("Exact video items require an exact source range.", nameof(sourceRange));
        if (sourceRange is not null && sourceRange.Duration != TimingAssessment.TimelineDuration)
            throw new ArgumentException("An exact video source range must match the pinned timeline duration.", nameof(sourceRange));
        SourceRange = sourceRange;
        CompositionStart = WorkingCompositionGuards.RequireNonnegative(compositionStart, nameof(compositionStart));
        WorkingCompositionGuards.RequireOptionalId(linkGroupId, nameof(linkGroupId));
        Id = id;
        SelectedStreamIndex = selectedStreamIndex;
        LinkGroupId = linkGroupId;
    }
    public Guid Id { get; }
    public AssetRevisionReference Source { get; }
    public int SelectedStreamIndex { get; }
    public VideoSourceRange? SourceRange { get; }
    public StreamTimingAssessmentPin TimingAssessment { get; }
    public ExactTime CompositionStart { get; }
    public Guid? LinkGroupId { get; }
}

public sealed class CompositionAudioItem : ICompositionItem
{
    public CompositionAudioItem(Guid id, AssetRevisionReference source, int selectedStreamIndex, AudioSourceRange? sourceRange, StreamTimingAssessmentPin timingAssessment, ExactTime compositionStart, Guid? linkGroupId = null)
    {
        WorkingCompositionGuards.RequireId(id, nameof(id));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        WorkingCompositionGuards.RequireSource(Source, nameof(source));
        WorkingCompositionGuards.RequireNonnegative(selectedStreamIndex, nameof(selectedStreamIndex));
        TimingAssessment = timingAssessment ?? throw new ArgumentNullException(nameof(timingAssessment));
        if (TimingAssessment.MediaType != MediaType.Audio)
            throw new ArgumentException("An audio item requires an audio timing assessment pin.", nameof(timingAssessment));
        if (TimingAssessment.SelectedStreamIndex != selectedStreamIndex)
            throw new ArgumentException("The timing assessment pin must match the selected audio stream.", nameof(timingAssessment));
        if (TimingAssessment.Readiness == TimingReadiness.Exact && sourceRange is null)
            throw new ArgumentException("Exact audio items require an exact source range.", nameof(sourceRange));
        if (sourceRange is not null && sourceRange.Duration != TimingAssessment.TimelineDuration)
            throw new ArgumentException("An exact audio source range must match the pinned timeline duration.", nameof(sourceRange));
        SourceRange = sourceRange;
        CompositionStart = WorkingCompositionGuards.RequireNonnegative(compositionStart, nameof(compositionStart));
        WorkingCompositionGuards.RequireOptionalId(linkGroupId, nameof(linkGroupId));
        Id = id;
        SelectedStreamIndex = selectedStreamIndex;
        LinkGroupId = linkGroupId;
    }
    public Guid Id { get; }
    public AssetRevisionReference Source { get; }
    public int SelectedStreamIndex { get; }
    public AudioSourceRange? SourceRange { get; }
    public StreamTimingAssessmentPin TimingAssessment { get; }
    public ExactTime CompositionStart { get; }
    public Guid? LinkGroupId { get; }
}

public sealed class VideoSourceRange
{
    public VideoSourceRange(VideoPresentationTime start, VideoPresentationTime end)
    {
        Start = start ?? throw new ArgumentNullException(nameof(start));
        End = end ?? throw new ArgumentNullException(nameof(end));
        if (Start.TimeBaseNumerator != End.TimeBaseNumerator || Start.TimeBaseDenominator != End.TimeBaseDenominator)
            throw new ArgumentException("Video source-range endpoints must use the same native time base.");
        if (End.PresentationTimestamp <= Start.PresentationTimestamp)
            throw new ArgumentException("Video source ranges are half-open and require end to be after start.");
    }
    public VideoPresentationTime Start { get; }
    public VideoPresentationTime End { get; }
    public ExactTime Duration => ExactTime.FromBigInteger(
        ((System.Numerics.BigInteger)End.PresentationTimestamp - Start.PresentationTimestamp) * Start.TimeBaseNumerator,
        Start.TimeBaseDenominator);
}

public sealed class AudioSourceRange
{
    public AudioSourceRange(AudioSampleTime start, AudioSampleTime end)
    {
        Start = start ?? throw new ArgumentNullException(nameof(start));
        End = end ?? throw new ArgumentNullException(nameof(end));
        if (Start.SampleRate != End.SampleRate)
            throw new ArgumentException("Audio source-range endpoints must use the same sample rate.");
        if (Start.SampleFrameOffset < 0 || End.SampleFrameOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(start), "Audio source-range sample offsets must be nonnegative.");
        if (End.SampleFrameOffset <= Start.SampleFrameOffset)
            throw new ArgumentException("Audio source ranges are half-open and require end to be after start.");
    }
    public AudioSampleTime Start { get; }
    public AudioSampleTime End { get; }
    public ExactTime Duration => ExactTime.FromBigInteger(
        (System.Numerics.BigInteger)End.SampleFrameOffset - Start.SampleFrameOffset,
        Start.SampleRate);
}

internal interface ICompositionItem
{
    Guid Id { get; }
    ExactTime CompositionStart { get; }
    Guid? LinkGroupId { get; }
}

internal static class WorkingCompositionCollection
{
    public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
            throw new ArgumentException("Composition collections cannot contain null entries.", nameof(values));
        return Array.AsReadOnly(copy);
    }
}

internal static class WorkingCompositionGuards
{
    private static readonly ExactTime Zero = new(0, 1);
    public static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("An explicit stable identifier is required.", parameterName);
    }
    public static void RequireOptionalId(Guid? id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("A link group identifier cannot be empty.", parameterName);
    }
    public static void RequireSource(AssetRevisionReference source, string parameterName)
    {
        if (source.AssetId == Guid.Empty)
            throw new ArgumentException("A source asset identifier is required.", parameterName);
        if (source.RecipeRevisionId == Guid.Empty)
            throw new ArgumentException("A pinned source revision identifier cannot be empty.", parameterName);
    }
    public static void RequireNonnegative(int value, string parameterName)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(parameterName);
    }
    public static ExactTime RequireNonnegative(ExactTime value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value < Zero) throw new ArgumentOutOfRangeException(parameterName, "Composition time must be nonnegative.");
        return value;
    }
}
