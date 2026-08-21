using System.Globalization;
using System.Windows.Media.Imaging;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.MediaPreparation;

public sealed class FrameContactListItem(VideoPresentationFrame frame, BitmapSource thumbnail)
{
    public VideoPresentationFrame Frame { get; } = frame;
    public BitmapSource Thumbnail { get; } = thumbnail;
    public string TimestampText => TimeSpan.FromSeconds(Math.Max(0, Frame.TimestampSeconds))
        .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}

public sealed class SavedFrameListItem(
    FrameAnchor anchor,
    FrameAnchorRevision revision,
    BitmapSource? thumbnail,
    string? error)
{
    public FrameAnchor Anchor { get; } = anchor;
    public FrameAnchorRevision Revision { get; } = revision;
    public BitmapSource? Thumbnail { get; } = thumbnail;
    public string? Error { get; } = error;
    public string DisplayLabel => Anchor.DisplayLabel ?? "Saved Frame";
    public string TimestampText => TimeSpan.FromSeconds(Math.Max(0, Revision.TimestampSeconds))
        .ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
