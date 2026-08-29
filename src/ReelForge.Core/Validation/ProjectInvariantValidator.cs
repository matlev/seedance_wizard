namespace ReelForge.Core;

public sealed class ProjectValidationException : Exception
{
    public ProjectValidationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors)) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

public static class ProjectInvariantValidator
{
    public static IReadOnlyList<string> Validate(VideoProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var errors = new List<string>();
        ProjectIdentityInvariantValidator.Validate(project, errors);
        var context = ProjectValidationContext.Create(project);
        AssetInvariantValidator.Validate(project, context, errors);
        AnchorInvariantValidator.Validate(project, context, errors);
        RecipeInvariantValidator.Validate(project, context, errors);
        GenerationInvariantValidator.ValidateDraft(project, context, errors);
        GenerationInvariantValidator.ValidateHistory(project, context, errors);
        return errors;
    }

    public static void ThrowIfInvalid(VideoProject project)
    {
        var errors = Validate(project);
        if (errors.Count > 0) throw new ProjectValidationException(errors);
    }
}

internal sealed record ProjectValidationContext(
    Dictionary<Guid, ProjectAsset> Assets,
    Dictionary<Guid, FrameAnchor> Anchors,
    Dictionary<Guid, FrameAnchorRevision> AnchorRevisions,
    Dictionary<Guid, RecipeRevision> RecipeRevisions,
    Dictionary<Guid, GenerationRecord> Generations)
{
    public static ProjectValidationContext Create(VideoProject project) => new(
        FirstById(project.Assets, asset => asset.Id),
        FirstById(project.Anchors, anchor => anchor.Id),
        FirstById(project.AnchorRevisions, revision => revision.Id),
        FirstById(project.RecipeRevisions, revision => revision.Id),
        FirstById(project.Generations, generation => generation.Id));

    private static Dictionary<Guid, T> FirstById<T>(IEnumerable<T> values, Func<T, Guid> getId) =>
        values.GroupBy(getId).ToDictionary(group => group.Key, group => group.First());
}

internal static class ProjectIdentityInvariantValidator
{
    public static void Validate(VideoProject project, List<string> errors)
    {
        ValidationHelpers.AddDuplicateErrors(project.Assets.Select(asset => asset.Id), "asset", errors);
        ValidationHelpers.AddDuplicateErrors(project.Anchors.Select(anchor => anchor.Id), "anchor", errors);
        ValidationHelpers.AddDuplicateErrors(
            project.AnchorRevisions.Select(revision => revision.Id), "anchor revision", errors);
        ValidationHelpers.AddDuplicateErrors(
            project.RecipeRevisions.Select(revision => revision.Id), "recipe revision", errors);
        ValidationHelpers.AddDuplicateErrors(project.RecipeDrafts.Select(draft => draft.Id), "recipe draft", errors);
        ValidationHelpers.AddDuplicateErrors(
            project.Generations.Select(generation => generation.Id), "generation", errors);
        ValidationHelpers.AddDuplicateErrors(
            project.Assets.SelectMany(asset => asset.TimingAssessments).Select(assessment => assessment.AssessmentId),
            "timing assessment", errors);
        ValidationHelpers.AddDuplicateErrors(
            project.TimingAssessmentAcknowledgements.Select(acknowledgement => acknowledgement.AssessmentId),
            "timing assessment acknowledgement", errors);
    }
}

internal static class ValidationHelpers
{
    public static void AddDuplicateErrors(IEnumerable<Guid> ids, string label, List<string> errors)
    {
        foreach (var duplicate in ids.GroupBy(id => id).Where(group => group.Count() > 1))
            errors.Add($"Duplicate {label} ID '{duplicate.Key}'.");
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));
}
