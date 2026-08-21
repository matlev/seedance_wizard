namespace ReelForge.Infrastructure;

internal sealed class FrameAnchorDto
{
    public Guid Id { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public string? DisplayLabel { get; set; }
    public string? Notes { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class FrameAnchorRevisionDto
{
    public Guid Id { get; set; }
    public Guid AnchorId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public Guid SourceAssetId { get; set; }
    public Guid? SourceRecipeRevisionId { get; set; }
    public string SourceContentHash { get; set; } = string.Empty;
    public int VideoStreamIndex { get; set; }
    public long PresentationTimestamp { get; set; }
    public int TimeBaseNumerator { get; set; }
    public int TimeBaseDenominator { get; set; }
    public long? FrameNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
