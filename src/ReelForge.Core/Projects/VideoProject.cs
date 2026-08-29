namespace ReelForge.Core;

public sealed class VideoProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled project";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectAsset> Assets { get; set; } = [];
    public List<RecipeRevision> RecipeRevisions { get; set; } = [];
    public List<RecipeDraft> RecipeDrafts { get; set; } = [];
    public List<FrameAnchor> Anchors { get; set; } = [];
    public List<FrameAnchorRevision> AnchorRevisions { get; set; } = [];
    public Guid? WorkingCompositionAssetId { get; set; }
    public GenerationDraft? CurrentGenerationDraft { get; set; }
    public List<GenerationRecord> Generations { get; set; } = [];
    /// <summary>Project-local acknowledgements; intentionally outside Working Composition history.</summary>
    public List<TimingAssessmentAcknowledgement> TimingAssessmentAcknowledgements { get; set; } = [];

    public void Touch() => ModifiedAt = DateTimeOffset.UtcNow;

    public void AddAsset(ProjectAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (Assets.Any(existing => existing.Id == asset.Id))
            throw new InvalidOperationException($"Asset '{asset.Id}' already belongs to the project.");

        Assets.Add(asset);
        Touch();
    }

    public void AcknowledgeEstimatedTimingAssessment(Guid assessmentId, DateTimeOffset acknowledgedAt)
    {
        if (assessmentId == Guid.Empty)
            throw new ArgumentException("An assessment identifier is required.", nameof(assessmentId));
        var assessment = Assets.SelectMany(asset => asset.TimingAssessments)
            .SingleOrDefault(candidate => candidate.AssessmentId == assessmentId);
        if (assessment is null || assessment.Readiness != TimingReadiness.Estimated)
            throw new InvalidOperationException("Only a current Estimated timing assessment can be acknowledged.");
        if (TimingAssessmentAcknowledgements.Any(existing => existing.AssessmentId == assessmentId))
            return;

        TimingAssessmentAcknowledgements.Add(new TimingAssessmentAcknowledgement(assessmentId, acknowledgedAt));
        Touch();
    }

    public RecipeRevision CommitRecipe(Guid virtualAssetId, AssetRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var asset = Assets.SingleOrDefault(candidate => candidate.Id == virtualAssetId)
            ?? throw new InvalidOperationException($"Virtual asset '{virtualAssetId}' does not exist.");
        if (asset.StorageKind != AssetStorageKind.Virtual || asset.Virtual is null)
            throw new InvalidOperationException($"Asset '{virtualAssetId}' is not virtual.");

        var previousId = asset.Virtual.CurrentRecipeRevisionId;
        var previous = previousId is null
            ? null
            : RecipeRevisions.SingleOrDefault(candidate => candidate.Id == previousId.Value)
                ?? throw new InvalidOperationException($"Current recipe revision '{previousId}' does not exist.");
        var revision = new RecipeRevision
        {
            VirtualAssetId = virtualAssetId,
            RevisionNumber = checked(RecipeRevisions
                .Where(candidate => candidate.VirtualAssetId == virtualAssetId)
                .Select(candidate => candidate.RevisionNumber)
                .DefaultIfEmpty(0)
                .Max() + 1),
            PreviousRevisionId = previous?.Id,
            Recipe = recipe,
            CreatedAt = DateTimeOffset.UtcNow
        };

        RecipeRevisions.Add(revision);
        asset.Virtual.CurrentRecipeRevisionId = revision.Id;
        Touch();
        return revision;
    }

    public FrameAnchorRevision CommitAnchorRevision(Guid anchorId, ExactFramePosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        var anchor = Anchors.SingleOrDefault(candidate => candidate.Id == anchorId)
            ?? throw new InvalidOperationException($"Frame anchor '{anchorId}' does not exist.");
        var source = Assets.SingleOrDefault(candidate => candidate.Id == position.SourceAssetId)
            ?? throw new InvalidOperationException($"Anchor source asset '{position.SourceAssetId}' does not exist.");
        if (source.MediaType != MediaType.Video)
            throw new InvalidOperationException("Frame anchors require a video source.");
        if (position.VideoStreamIndex < 0 ||
            position.TimeBaseNumerator <= 0 || position.TimeBaseDenominator <= 0)
            throw new InvalidOperationException("An exact frame position requires a valid stream, PTS, and rational time base.");
        if (!IsSha256(position.SourceContentHash))
            throw new InvalidOperationException("An exact frame position requires a verified source SHA-256 hash.");
        if (source.StorageKind == AssetStorageKind.Physical)
        {
            if (position.SourceRecipeRevisionId is not null)
                throw new InvalidOperationException("A physical frame position cannot pin a recipe revision.");
            if (source.Physical?.ContentIdentity is not { Status: ContentHashStatus.Verified, Sha256: { } sourceHash } ||
                !sourceHash.Equals(position.SourceContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The exact frame position must match the source video's verified content identity.");
        }
        else
        {
            if (position.SourceRecipeRevisionId is not { } sourceRevisionId ||
                !RecipeRevisions.Any(revision =>
                    revision.Id == sourceRevisionId && revision.VirtualAssetId == source.Id))
                throw new InvalidOperationException(
                    "An exact position in virtual media must pin a recipe revision belonging to that source.");
        }

        var previous = anchor.CurrentRevisionId is { } currentId
            ? AnchorRevisions.SingleOrDefault(candidate => candidate.Id == currentId)
                ?? throw new InvalidOperationException($"Current anchor revision '{currentId}' does not exist.")
            : null;
        var revision = new FrameAnchorRevision
        {
            AnchorId = anchorId,
            RevisionNumber = (previous?.RevisionNumber ?? 0) + 1,
            PreviousRevisionId = previous?.Id,
            SourceAssetId = position.SourceAssetId,
            SourceRecipeRevisionId = position.SourceRecipeRevisionId,
            SourceContentHash = position.SourceContentHash,
            VideoStreamIndex = position.VideoStreamIndex,
            PresentationTimestamp = position.PresentationTimestamp,
            TimeBaseNumerator = position.TimeBaseNumerator,
            TimeBaseDenominator = position.TimeBaseDenominator,
            FrameNumber = position.FrameNumber
        };
        AnchorRevisions.Add(revision);
        anchor.CurrentRevisionId = revision.Id;
        Touch();
        return revision;
    }

    public AnchorRemovalDisposition RemoveOrArchiveAnchor(Guid anchorId)
    {
        var anchor = Anchors.SingleOrDefault(candidate => candidate.Id == anchorId)
            ?? throw new InvalidOperationException($"Frame anchor '{anchorId}' does not exist.");
        var isReferenced = RecipeRevisions.Any(revision => RecipeReferencesAnchor(revision.Recipe, anchorId)) ||
            RecipeDrafts.Any(draft => RecipeReferencesAnchor(draft.EditableRecipe, anchorId)) ||
            CurrentGenerationDraft?.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                reference.LogicalObjectId == anchorId) == true ||
            Generations.Any(generation => generation.RequestSnapshot.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor &&
                reference.LogicalObjectId == anchorId));
        if (isReferenced)
        {
            anchor.IsArchived = true;
            Touch();
            return AnchorRemovalDisposition.Archived;
        }

        AnchorRevisions.RemoveAll(revision => revision.AnchorId == anchorId);
        Anchors.Remove(anchor);
        Touch();
        return AnchorRemovalDisposition.Removed;
    }

    private static bool RecipeReferencesAnchor(AssetRecipe recipe, Guid anchorId) => recipe switch
    {
        TrimRecipe trim => trim.Start.Anchor?.AnchorId == anchorId || trim.End.Anchor?.AnchorId == anchorId,
        ExtractFrameRecipe frame => frame.Anchor.AnchorId == anchorId,
        CompositionRecipe => false,
        _ => false
    };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));
}
