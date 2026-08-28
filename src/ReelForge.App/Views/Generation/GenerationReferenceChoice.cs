using System.Windows.Media.Imaging;
using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

public sealed class GenerationReferenceChoice
{
    private static readonly IReadOnlyList<GenerationReferenceRole?> ReferenceRoles =
        Enum.GetValues<GenerationReferenceRole>().Cast<GenerationReferenceRole?>().Prepend(null).ToArray();
    private readonly IReadOnlyList<GenerationReferenceRole?> _availableRoles = ReferenceRoles;

    public GenerationReferenceChoice(ProjectAsset asset, int order, BitmapSource? thumbnail = null)
    {
        UpdateAsset(asset, thumbnail);
        Order = order;
    }

    public GenerationReferenceChoice(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        string? sourceDisplayName,
        int order)
    {
        UpdateAnchor(anchor, revision, sourceDisplayName);
        Order = order;
    }

    public Guid ReferenceId { get; set; } = Guid.NewGuid();
    public GenerationReferenceObjectKind ObjectKind { get; private set; }
    public Guid LogicalObjectId { get; private set; }
    public Guid? AnchorRevisionId { get; set; }
    public string DisplayName { get; private set; } = string.Empty;
    public MediaType MediaType { get; private set; }
    public string MediaTypeText { get; private set; } = string.Empty;
    public string Glyph { get; private set; } = "•";
    public BitmapSource? Thumbnail { get; private set; }
    public bool HasThumbnail => Thumbnail is not null;
    public IReadOnlyList<GenerationReferenceRole?> AvailableRoles => _availableRoles;
    public bool IsSelected { get; set; }
    public GenerationReferenceRole? Role { get; set; }
    public int Order { get; set; }
    public string? Label { get; set; }
    public string? Notes { get; set; }
    /// <summary>
    /// True when the referenced project object, or the durable source of a Saved Frame,
    /// was deliberately removed from the project. Existing draft occurrences remain
    /// visible, but cannot be duplicated or submitted.
    /// </summary>
    public bool IsDeleted { get; private set; }
    public bool CanCreateAdditionalOccurrence => !IsDeleted;

    public void UpdateAsset(ProjectAsset asset, BitmapSource? thumbnail = null)
    {
        ObjectKind = GenerationReferenceObjectKind.Asset;
        LogicalObjectId = asset.Id;
        AnchorRevisionId = null;
        DisplayName = asset.EffectiveDisplayName;
        MediaType = asset.MediaType;
        MediaTypeText = asset.Virtual?.Kind == VirtualAssetKind.SavedClip
            ? "Saved Clip • Video"
            : asset.MediaType.ToString();
        Glyph = asset.Virtual?.Kind == VirtualAssetKind.SavedClip
            ? "✂"
            : asset.MediaType switch
            {
                MediaType.Video => "▶",
                MediaType.Image => "▧",
                MediaType.Audio => "♪",
                _ => "•"
            };
        IsDeleted = asset.IsDeleted;
        if (thumbnail is not null) Thumbnail = thumbnail;
    }

    public void UpdateAnchor(
        FrameAnchor anchor,
        FrameAnchorRevision revision,
        string? sourceDisplayName,
        bool sourceIsDeleted = false)
    {
        ObjectKind = GenerationReferenceObjectKind.FrameAnchor;
        LogicalObjectId = anchor.Id;
        AnchorRevisionId = revision.Id;
        DisplayName = $"Saved Frame • {anchor.DisplayLabel ?? "Untitled"}" +
                      (string.IsNullOrWhiteSpace(sourceDisplayName) ? string.Empty : $" ({sourceDisplayName})");
        MediaType = MediaType.Image;
        MediaTypeText = "Saved Frame • Image";
        Glyph = "▣";
        IsDeleted = sourceIsDeleted;
    }

    public void UpdateThumbnail(BitmapSource? thumbnail) => Thumbnail = thumbnail;

    public GenerationReferenceChoice Duplicate(int order)
    {
        var duplicate = (GenerationReferenceChoice)MemberwiseClone();
        duplicate.ReferenceId = Guid.NewGuid();
        duplicate.Order = order;
        duplicate.IsSelected = true;
        return duplicate;
    }
}
