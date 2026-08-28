using System.Windows.Media.Imaging;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

public sealed class ProjectMediaListItem
{
    public ProjectMediaListItem(ProjectAsset asset, bool canRestoreDeletedSource = false)
    {
        Asset = asset;
        CanRestoreDeletedSource = canRestoreDeletedSource;
    }

    public ProjectMediaListItem(FrameAnchor anchor, FrameAnchorRevision revision)
    {
        Anchor = anchor;
        AnchorRevision = revision;
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
    /// <summary>True only for an active verified physical asset that matches a deleted source identity.</summary>
    public bool CanRestoreDeletedSource { get; }
    public string? GlyphToolTip => IsMissingPhysicalAsset
        ? "Source media is missing. Right-click and choose Relink source…"
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
    public string Glyph => IsMissingPhysicalAsset ? "⚠" : Anchor is not null ? "▣" : Asset!.StorageKind == AssetStorageKind.Virtual
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
}
