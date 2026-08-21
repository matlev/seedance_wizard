using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal sealed class RecipeRevisionDto
{
    public Guid Id { get; set; }
    public Guid VirtualAssetId { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? PreviousRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public AssetRecipeDto Recipe { get; set; } = new();
}

internal sealed class RecipeDraftDto
{
    public Guid Id { get; set; }
    public Guid? VirtualAssetId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public AssetRecipeDto EditableRecipe { get; set; } = new();
    public DateTimeOffset ModifiedAt { get; set; }
}

internal sealed class AssetRecipeDto
{
    public string Type { get; set; } = "extractFrame";
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public RecipeBoundaryDto? Start { get; set; }
    public RecipeBoundaryDto? End { get; set; }
    public AnchorRevisionReferenceDto? Anchor { get; set; }
    public List<CompositionSegmentDto> Segments { get; set; } = [];
    public List<CompositionAudioClipDto> AudioClips { get; set; } = [];
    public string? Profile { get; set; }
}

internal sealed class CompositionSegmentDto
{
    public Guid Id { get; set; }
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public RecipeBoundaryDto Start { get; set; } = new();
    public RecipeBoundaryDto End { get; set; } = new();
    public bool AudioEnabled { get; set; }
}

internal sealed class CompositionAudioClipDto
{
    public Guid Id { get; set; }
    public AssetRevisionReferenceDto Source { get; set; } = new();
    public long TimelineStartTicks { get; set; }
    public bool IsMuted { get; set; }
    public double GainDecibels { get; set; }
    public double Pan { get; set; }
    public long FadeInMilliseconds { get; set; }
    public long FadeOutMilliseconds { get; set; }
}

internal sealed class RecipeBoundaryDto
{
    public RecipeBoundaryKind Kind { get; set; }
    public AnchorRevisionReferenceDto? Anchor { get; set; }
    public AnchorBoundaryEdge? Edge { get; set; }
    public double? TimestampSeconds { get; set; }
}

internal sealed class AnchorRevisionReferenceDto
{
    public Guid AnchorId { get; set; }
    public Guid AnchorRevisionId { get; set; }
}
