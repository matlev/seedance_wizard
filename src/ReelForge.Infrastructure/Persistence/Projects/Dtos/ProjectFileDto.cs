namespace ReelForge.Infrastructure;

internal sealed class ProjectFileDto
{
    public const int CurrentFormatVersion = 5;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
    public List<ProjectAssetDto> Assets { get; set; } = [];
    public List<RecipeRevisionDto> RecipeRevisions { get; set; } = [];
    public List<RecipeDraftDto> RecipeDrafts { get; set; } = [];
    public List<FrameAnchorDto> Anchors { get; set; } = [];
    public List<FrameAnchorRevisionDto> AnchorRevisions { get; set; } = [];
    public Guid? WorkingCompositionAssetId { get; set; }
    public GenerationDraftDto? CurrentGenerationDraft { get; set; }
    public List<GenerationRecordDto> Generations { get; set; } = [];
    public List<TimingAssessmentAcknowledgementDto> TimingAssessmentAcknowledgements { get; set; } = [];
}
