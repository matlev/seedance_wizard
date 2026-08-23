using ReelForge.Core;
using System.Collections.ObjectModel;

namespace ReelForge.Application;

/// <summary>
/// Finds durable project state that would be invalidated if a physical asset were removed.
/// </summary>
public sealed class ProjectAssetDependencyAnalyzer
{
#pragma warning disable CA1822 // Kept as an injected application service at the coordination boundary.
    public ProjectAssetDependencyReport Analyze(VideoProject project, Guid assetId)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.Assets.Any(asset => asset.Id == assetId))
            throw new InvalidOperationException($"Asset '{assetId}' does not exist in this project.");

        var usages = new List<ProjectAssetDependency>();
        if (project.CurrentGenerationDraft?.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.Asset && reference.LogicalObjectId == assetId) == true)
            usages.Add(ProjectAssetDependency.CurrentGenerationDraft);
        if (project.Generations.Any(generation => generation.RequestSnapshot.References.Any(reference =>
                reference.ObjectKind == GenerationReferenceObjectKind.Asset && reference.LogicalObjectId == assetId)))
            usages.Add(ProjectAssetDependency.SubmittedGenerationReferences);
        if (project.Generations.Any(generation => generation.OutputAssetIds.Contains(assetId)))
            usages.Add(ProjectAssetDependency.GeneratedOutputHistory);
        if (project.AnchorRevisions.Any(revision => revision.SourceAssetId == assetId))
            usages.Add(ProjectAssetDependency.SavedFrames);
        if (project.Assets.Any(candidate => candidate.Id != assetId &&
                                           candidate.Provenance?.SourceAssetIds.Contains(assetId) == true))
            usages.Add(ProjectAssetDependency.DerivedAssetHistory);
        if (project.RecipeRevisions.Any(revision =>
                revision.VirtualAssetId != assetId && RecipeReferencesAsset(revision.Recipe, assetId)) ||
            project.RecipeDrafts.Any(draft =>
                draft.VirtualAssetId != assetId && RecipeReferencesAsset(draft.EditableRecipe, assetId)))
            usages.Add(ProjectAssetDependency.MediaRecipes);

        return new ProjectAssetDependencyReport(usages.Distinct().ToArray());
    }
#pragma warning restore CA1822

    private static bool RecipeReferencesAsset(AssetRecipe recipe, Guid assetId) => recipe switch
    {
        TrimRecipe trim => trim.Source.AssetId == assetId,
        ExtractFrameRecipe frame => frame.Source.AssetId == assetId,
        CompositionRecipe composition => composition.Segments.Any(segment => segment.Source.AssetId == assetId) ||
                                         composition.AudioClips.Any(clip => clip.Source.AssetId == assetId),
        _ => false
    };
}

public enum ProjectAssetDependency
{
    CurrentGenerationDraft,
    SubmittedGenerationReferences,
    GeneratedOutputHistory,
    SavedFrames,
    DerivedAssetHistory,
    MediaRecipes
}

public sealed class ProjectAssetDependencyReport
{
    private readonly ProjectAssetDependency[] _dependencies;
    private readonly ReadOnlyCollection<ProjectAssetDependency> _readOnlyDependencies;

    public ProjectAssetDependencyReport(IEnumerable<ProjectAssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _dependencies = dependencies.ToArray();
        _readOnlyDependencies = Array.AsReadOnly(_dependencies);
    }

    public IReadOnlyList<ProjectAssetDependency> Dependencies => _readOnlyDependencies;
    public bool IsInUse => Dependencies.Count > 0;

    public IReadOnlyList<string> DisplayDescriptions => Dependencies.Select(dependency => dependency switch
    {
        ProjectAssetDependency.CurrentGenerationDraft => "the current generation draft",
        ProjectAssetDependency.SubmittedGenerationReferences => "submitted generation references",
        ProjectAssetDependency.GeneratedOutputHistory => "generated-output history",
        ProjectAssetDependency.SavedFrames => "saved frames",
        ProjectAssetDependency.DerivedAssetHistory => "derived-asset history",
        ProjectAssetDependency.MediaRecipes => "media recipes",
        _ => throw new ArgumentOutOfRangeException(nameof(dependency))
    }).ToArray();
}
