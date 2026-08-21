namespace ReelForge.Core;

public enum AnchorRemovalDisposition { Removed, Archived }

public sealed class FrameAnchor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? CurrentRevisionId { get; set; }
    public string? DisplayLabel { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FrameAnchorRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AnchorId { get; init; }
    public int RevisionNumber { get; init; }
    public Guid? PreviousRevisionId { get; init; }
    public Guid SourceAssetId { get; init; }
    public Guid? SourceRecipeRevisionId { get; init; }
    public string SourceContentHash { get; init; } = string.Empty;
    public int VideoStreamIndex { get; init; }
    public long PresentationTimestamp { get; init; }
    public int TimeBaseNumerator { get; init; }
    public int TimeBaseDenominator { get; init; }
    public long? FrameNumber { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public double TimestampSeconds =>
        PresentationTimestamp * (double)TimeBaseNumerator / TimeBaseDenominator;
}

public sealed record ExactFramePosition(
    Guid SourceAssetId,
    string SourceContentHash,
    int VideoStreamIndex,
    long PresentationTimestamp,
    int TimeBaseNumerator,
    int TimeBaseDenominator,
    long? FrameNumber = null,
    Guid? SourceRecipeRevisionId = null);
