using System.Security.Cryptography;
using System.Text;
using ReelForge.Core;

namespace ReelForge.Application;

public static class TransientFrameAnchorRevisionFactory
{
    public static FrameAnchorRevision Create(
        Guid sourceAssetId,
        string sourceContentHash,
        VideoPresentationFrame frame)
    {
        var identityBytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
            sourceContentHash,
            frame.VideoStreamIndex,
            frame.PresentationTimestamp,
            frame.TimeBaseNumerator,
            frame.TimeBaseDenominator)));

        return new FrameAnchorRevision
        {
            Id = new Guid(identityBytes.AsSpan(0, 16)),
            AnchorId = Guid.Empty,
            RevisionNumber = 0,
            SourceAssetId = sourceAssetId,
            SourceContentHash = sourceContentHash,
            VideoStreamIndex = frame.VideoStreamIndex,
            PresentationTimestamp = frame.PresentationTimestamp,
            TimeBaseNumerator = frame.TimeBaseNumerator,
            TimeBaseDenominator = frame.TimeBaseDenominator,
            FrameNumber = frame.FrameNumber
        };
    }
}
