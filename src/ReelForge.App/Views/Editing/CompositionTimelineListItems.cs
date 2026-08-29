using System.Globalization;
using ReelForge.Core;

namespace ReelForge.App.Views.Editing;

/// <summary>
/// Presentation-only projection of a stable video timeline occurrence. The
/// legacy "segment" name is retained because it is part of the current WPF
/// control contract; it does not represent persisted composition meaning.
/// </summary>
public sealed class CompositionSegmentListItem
{
    public CompositionSegmentListItem(int index, Guid trackId, CompositionVideoItem item, ProjectAsset? source, bool audioEnabled)
    {
        Index = index;
        TrackId = trackId;
        SegmentId = item.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing source";
        DetailText = source is null
            ? $"Source {item.Source.AssetId:N} is unavailable"
            : source.StorageKind == AssetStorageKind.Virtual
                ? $"Saved Clip • {(item.SourceRange is not null ? "exact range • " : string.Empty)}pinned recipe " +
                  (item.Source.RecipeRevisionId?.ToString("N") ?? "missing")
                : $"Physical video • {(item.SourceRange is not null ? "exact range" : "estimated range")} • {item.TimingAssessment.Readiness}";
        AudioText = audioEnabled ? "Linked audio on" : "Linked audio unavailable or muted";
        AudioEnabled = audioEnabled;
        TimelineStart = item.CompositionStart.ToDoubleSeconds();
        DurationSeconds = item.TimingAssessment.TimelineDuration.ToDoubleSeconds();
        DurationText = DurationSeconds > 0 ? FormatDuration(DurationSeconds) : "Duration unavailable";
    }

    public int Index { get; }
    public Guid TrackId { get; }
    public Guid SegmentId { get; }
    public string PositionText => $"{Index + 1}.";
    public string DisplayName { get; }
    public string DetailText { get; }
    public string AudioText { get; }
    public bool AudioEnabled { get; }
    public double TimelineStart { get; }
    public double DurationSeconds { get; }
    public string DurationText { get; }

    internal static string FormatDuration(double seconds)
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
    public CompositionAudioClipListItem(Guid trackId, CompositionAudioItem item, ProjectAsset? source)
    {
        TrackId = trackId;
        AudioClipId = item.Id;
        DisplayName = source?.EffectiveDisplayName ?? "Missing audio source";
        TimelineStart = TimeSpan.FromSeconds(item.CompositionStart.ToDoubleSeconds());
        IsMuted = item.IsMuted;
        GainDecibels = item.GainDecibels;
        Pan = item.Pan;
        FadeIn = TimeSpan.FromSeconds(item.FadeIn.ToDoubleSeconds());
        FadeOut = TimeSpan.FromSeconds(item.FadeOut.ToDoubleSeconds());
        MixText = (IsMuted
            ? "Muted"
            : $"Gain {(GainDecibels > 0 ? "+" : string.Empty)}{GainDecibels:0} dB") +
            (Math.Abs(Pan) > 0.000_001
                ? $" • {Math.Round(Math.Abs(Pan) * 100):0}% {(Pan < 0 ? "left" : "right")}"
                : string.Empty) +
            (FadeIn > TimeSpan.Zero || FadeOut > TimeSpan.Zero
                ? $" • Fade {FadeIn.TotalSeconds:0.###}s in / {FadeOut.TotalSeconds:0.###}s out"
                : string.Empty);
        DurationSeconds = item.TimingAssessment.TimelineDuration.ToDoubleSeconds();
        DurationText = DurationSeconds > 0 ? CompositionSegmentListItem.FormatDuration(DurationSeconds) : "Duration unavailable";
    }

    public Guid TrackId { get; }
    public Guid AudioClipId { get; }
    public string DisplayName { get; }
    public TimeSpan TimelineStart { get; }
    public bool IsMuted { get; }
    public double GainDecibels { get; }
    public double Pan { get; }
    public TimeSpan FadeIn { get; }
    public TimeSpan FadeOut { get; }
    public string MixText { get; }
    public double DurationSeconds { get; }
    public string DurationText { get; }
}
