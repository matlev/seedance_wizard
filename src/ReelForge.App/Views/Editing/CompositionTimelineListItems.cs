using System.Globalization;
using ReelForge.Core;

namespace ReelForge.App.Views.Editing;

public sealed class CompositionSegmentListItem
{
    public CompositionSegmentListItem(
        int index,
        CompositionSegment segment,
        ProjectAsset? source,
        double? durationSeconds)
    {
        Index = index;
        SegmentId = segment.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing source";
        var isExactRange = segment.Start.Kind != RecipeBoundaryKind.SourceStart ||
                           segment.End.Kind != RecipeBoundaryKind.SourceEnd;
        DetailText = source is null
            ? $"Source {segment.Source.AssetId:N} is unavailable"
            : source.StorageKind == AssetStorageKind.Virtual
                ? $"Saved Clip • {(isExactRange ? "exact range • " : string.Empty)}pinned recipe " +
                  (segment.Source.RecipeRevisionId?.ToString("N") ?? "missing")
                : $"Physical video • {(isExactRange ? "exact range" : "full source")}";
        AudioText = segment.AudioEnabled ? "Audio on" : "Audio muted";
        AudioEnabled = segment.AudioEnabled;
        DurationSeconds = durationSeconds;
        DurationText = DurationSeconds is > 0 ? FormatDuration(DurationSeconds.Value) : "Duration unknown";
    }

    public int Index { get; }
    public Guid SegmentId { get; }
    public string PositionText => $"{Index + 1}.";
    public string DisplayName { get; }
    public string DetailText { get; }
    public string AudioText { get; }
    public bool AudioEnabled { get; }
    public double? DurationSeconds { get; }
    public string DurationText { get; }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        if (seconds < 10 || Math.Abs(seconds - Math.Round(seconds)) > 0.000_5)
            return time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}

public sealed class CompositionAudioClipListItem
{
    public CompositionAudioClipListItem(CompositionAudioClip clip, ProjectAsset? source)
    {
        AudioClipId = clip.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing audio source";
        TimelineStart = clip.TimelineStart;
        IsMuted = clip.IsMuted;
        GainDecibels = clip.GainDecibels;
        Pan = clip.Pan;
        FadeIn = clip.FadeIn;
        FadeOut = clip.FadeOut;
        MixText = (IsMuted
            ? "Muted"
            : $"Gain {(GainDecibels > 0 ? "+" : string.Empty)}{GainDecibels:0} dB") +
            (Math.Abs(Pan) > 0.000_001
                ? $" • {Math.Round(Math.Abs(Pan) * 100):0}% {(Pan < 0 ? "left" : "right")}"
                : string.Empty) +
            (FadeIn > TimeSpan.Zero || FadeOut > TimeSpan.Zero
                ? $" • Fade {FadeIn.TotalSeconds:0.###}s in / {FadeOut.TotalSeconds:0.###}s out"
                : string.Empty);
        DurationSeconds = source?.DurationSeconds ?? source?.Encoding?.DurationSeconds ??
                          source?.Virtual?.ExpectedMediaProperties?.DurationSeconds;
        DurationText = DurationSeconds is > 0
            ? TimeSpan.FromSeconds(DurationSeconds.Value).ToString(@"m\:ss", CultureInfo.InvariantCulture)
            : "Duration unknown";
    }

    public Guid AudioClipId { get; }
    public string DisplayName { get; }
    public TimeSpan TimelineStart { get; }
    public bool IsMuted { get; }
    public double GainDecibels { get; }
    public double Pan { get; }
    public TimeSpan FadeIn { get; }
    public TimeSpan FadeOut { get; }
    public string MixText { get; }
    public double? DurationSeconds { get; }
    public string DurationText { get; }
}
