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
}
