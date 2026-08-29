using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using ReelForge.App.Views.Editing;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

public sealed class ProjectMediaListItem : INotifyPropertyChanged
{
    public ProjectMediaListItem(ProjectAsset asset, bool canRestoreDeletedSource = false, bool isDegraded = false)
    {
        Asset = asset;
        CanRestoreDeletedSource = canRestoreDeletedSource;
        IsDegradedDerivedAsset = isDegraded;
    }

    public ProjectMediaListItem(FrameAnchor anchor, FrameAnchorRevision revision, bool isDegraded = false)
    {
        Anchor = anchor;
        AnchorRevision = revision;
        IsDegradedDerivedAsset = isDegraded;
    }

    public ProjectAsset? Asset { get; }
    public FrameAnchor? Anchor { get; }
    public FrameAnchorRevision? AnchorRevision { get; }
    public BitmapSource? Thumbnail { get; set; }
    public bool IsMissingPhysicalAsset => Asset is
    {
        StorageKind: AssetStorageKind.Physical,
        Physical.Availability: PhysicalAssetAvailability.Missing
    };
    /// <summary>True for active derived media whose exact pinned dependencies can no longer be materialized.</summary>
    public bool IsDegradedDerivedAsset { get; private set; }
    /// <summary>True when current physical-stream evidence is not fully exact.</summary>
    public bool IsTimingDegraded => Asset is
    {
        StorageKind: AssetStorageKind.Physical,
        MediaType: MediaType.Video or MediaType.Audio
    } && Asset.TimingAssessments.Any(assessment => assessment.Readiness is
        TimingReadiness.Estimated or TimingReadiness.Unusable);

    /// <summary>Concise user-facing explanation of the current, non-occurrence timing evidence.</summary>
    public string? TimingWarningToolTip => IsTimingDegraded
        ? TimingWarningPresentation.FormatAssetTooltip(Asset!)
        : null;

    public void UpdateDegradedState(bool isDegradedDerivedAsset)
    {
        if (IsDegradedDerivedAsset == isDegradedDerivedAsset) return;
        IsDegradedDerivedAsset = isDegradedDerivedAsset;
        OnPropertyChanged(nameof(IsDegradedDerivedAsset));
        OnPropertyChanged(nameof(Glyph));
        OnPropertyChanged(nameof(GlyphToolTip));
    }
    /// <summary>True only for an active verified physical asset that matches a deleted source identity.</summary>
    public bool CanRestoreDeletedSource { get; }
    public string? GlyphToolTip => IsDegradedDerivedAsset
            ? "This media depends on unavailable project media. Cleanup Project will delete it."
        : IsMissingPhysicalAsset
            ? "Source media is missing. Right-click and choose Relink source…"
        : IsTimingDegraded
            ? TimingWarningToolTip
            : null;
    public string DisplayName => Anchor?.DisplayLabel ??
                                 (Asset!.StorageKind == AssetStorageKind.Physical
                                     ? Asset.FileName
                                     : Asset.EffectiveDisplayName);
    public string KindText => Anchor is not null ? "Saved Frame" : Asset!.StorageKind == AssetStorageKind.Virtual
        ? IsSavedClip ? "Saved Clip" : IsComposition ? "Working Composition" : $"Virtual {Asset.MediaType}"
        : Asset.MediaType.ToString();
    public string GroupName => Anchor is not null ? "SAVED FRAMES" : Asset!.StorageKind == AssetStorageKind.Virtual
        ? IsSavedClip ? "SAVED CLIPS" : IsComposition ? "COMPOSITIONS" : "VIRTUAL MEDIA"
        : Asset.MediaType switch
        {
            MediaType.Video => "VIDEOS",
            MediaType.Image => "IMAGES",
            MediaType.Audio => "AUDIO",
            _ => "MEDIA"
        };
    public int GroupOrder => GroupName switch
    {
        "VIDEOS" => 0,
        "IMAGES" => 1,
        "AUDIO" => 2,
        "SAVED FRAMES" => 3,
        "SAVED CLIPS" => 4,
        "COMPOSITIONS" => 5,
        _ => 6
    };
    public string Glyph => IsMissingPhysicalAsset || IsDegradedDerivedAsset || IsTimingDegraded ? "⚠" : Anchor is not null ? "▣" : Asset!.StorageKind == AssetStorageKind.Virtual
        ? IsComposition ? "▤" : "✂"
        : Asset.MediaType switch
        {
            MediaType.Video => "▶",
            MediaType.Image => "▧",
            MediaType.Audio => "♪",
            _ => "•"
        };

    private bool IsSavedClip => Asset?.Virtual?.Kind == VirtualAssetKind.SavedClip;
    private bool IsComposition => Asset?.Virtual?.Kind == VirtualAssetKind.Composition;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
