namespace ReelForge.Application;

public enum ProjectWorkspaceKind { Generate, Edit }

public static class GeneratedOutputPreviewPolicy
{
    public static bool ShouldAutoPreview(
        bool owningProjectIsOpen,
        ProjectWorkspaceKind workspace,
        bool isMediaPreparationActive) =>
        owningProjectIsOpen &&
        workspace == ProjectWorkspaceKind.Generate &&
        !isMediaPreparationActive;
}

public sealed class ProjectUserInterfaceState
{
    public ProjectWorkspaceKind Workspace { get; set; } = ProjectWorkspaceKind.Generate;
    public string? SelectedMediaKind { get; set; }
    public Guid? SelectedMediaId { get; set; }
    /// <summary>
    /// Machine-local intent to reopen a successfully rendered Working Composition preview.
    /// This deliberately records identity only: the derived media path remains disposable cache.
    /// </summary>
    public BakedCompositionPreviewPreference? BakedCompositionPreview { get; set; }
}

public sealed class BakedCompositionPreviewPreference
{
    public string ProjectFilePath { get; set; } = string.Empty;
    public Guid CompositionAssetId { get; set; }
    public Guid RecipeRevisionId { get; set; }

    public bool Matches(string projectFilePath, Guid compositionAssetId, Guid recipeRevisionId) =>
        string.Equals(ProjectFilePath, projectFilePath, StringComparison.OrdinalIgnoreCase) &&
        CompositionAssetId == compositionAssetId &&
        RecipeRevisionId == recipeRevisionId;
}
