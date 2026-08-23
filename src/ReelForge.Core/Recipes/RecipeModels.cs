namespace ReelForge.Core;

public enum RecipeBoundaryKind { SourceStart, SourceEnd, Anchor, Timestamp }
public enum AnchorBoundaryEdge { BeforeFrame, AfterFrame }

public sealed class RecipeRevision
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid VirtualAssetId { get; init; }
    public int RevisionNumber { get; init; }
    public Guid? PreviousRevisionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public AssetRecipe Recipe { get; init; } = new ExtractFrameRecipe();
}

public sealed class RecipeDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? VirtualAssetId { get; set; }
    public Guid? BasedOnRevisionId { get; set; }
    public AssetRecipe EditableRecipe { get; set; } = new ExtractFrameRecipe();
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}

public abstract record AssetRecipe;

public sealed record TrimRecipe : AssetRecipe
{
    public AssetRevisionReference Source { get; init; } = new();
    public RecipeBoundary Start { get; init; } = RecipeBoundary.SourceStart;
    public RecipeBoundary End { get; init; } = RecipeBoundary.SourceEnd;
    public string? RenderProfile { get; init; }
}

public sealed record ExtractFrameRecipe : AssetRecipe
{
    public AssetRevisionReference Source { get; init; } = new();
    public AnchorRevisionReference Anchor { get; init; } = new();
    public string? ImageProfile { get; init; }
}

public sealed record CompositionRecipe : AssetRecipe
{
    public List<CompositionSegment> Segments { get; init; } = [];
    public List<CompositionAudioClip> AudioClips { get; init; } = [];
}

public sealed record CompositionSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AssetRevisionReference Source { get; init; } = new();
    public RecipeBoundary Start { get; init; } = RecipeBoundary.SourceStart;
    public RecipeBoundary End { get; init; } = RecipeBoundary.SourceEnd;
    public bool AudioEnabled { get; init; } = true;
}

public sealed record CompositionAudioClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AssetRevisionReference Source { get; init; } = new();
    public long TimelineStartTicks { get; init; }
    public bool IsMuted { get; init; }
    public double GainDecibels { get; init; }
    public double Pan { get; init; }
    public long FadeInMilliseconds { get; init; }
    public long FadeOutMilliseconds { get; init; }

    public TimeSpan TimelineStart => TimeSpan.FromTicks(TimelineStartTicks);
    public TimeSpan FadeIn => TimeSpan.FromMilliseconds(FadeInMilliseconds);
    public TimeSpan FadeOut => TimeSpan.FromMilliseconds(FadeOutMilliseconds);
}

public sealed record AssetRevisionReference
{
    public Guid AssetId { get; init; }
    public Guid? RecipeRevisionId { get; init; }
}

public sealed record AnchorRevisionReference
{
    public Guid AnchorId { get; init; }
    public Guid AnchorRevisionId { get; init; }
}

public sealed record RecipeBoundary
{
    public static RecipeBoundary SourceStart { get; } = new() { Kind = RecipeBoundaryKind.SourceStart };
    public static RecipeBoundary SourceEnd { get; } = new() { Kind = RecipeBoundaryKind.SourceEnd };

    public RecipeBoundaryKind Kind { get; init; }
    public AnchorRevisionReference? Anchor { get; init; }
    public AnchorBoundaryEdge? Edge { get; init; }
    public double? TimestampSeconds { get; init; }
}
