using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Captures the exact in-memory Project Media selection that began an asynchronous
/// operation. Project identifiers and locations are intentionally insufficient here:
/// reopening or copying a development project can produce equivalent values while
/// referring to a different in-memory project session.
/// </summary>
internal sealed class ProjectMediaSelectionIdentity
{
    private ProjectMediaSelectionIdentity(
        VideoProject project,
        ProjectLocation location,
        ProjectMediaListItem item,
        CancellationToken cancellationToken)
    {
        Project = project;
        Location = location;
        Item = item;
        CancellationToken = cancellationToken;
    }

    public VideoProject Project { get; }
    public ProjectLocation Location { get; }
    public ProjectMediaListItem Item { get; }
    public CancellationToken CancellationToken { get; }

    public static ProjectMediaSelectionIdentity? Capture(
        ProjectWorkspace workspace,
        ProjectMediaListItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(item);

        return workspace.Project is { } project && workspace.Location is { } location
            ? new ProjectMediaSelectionIdentity(project, location, item, cancellationToken)
            : null;
    }

    public bool IsCurrent(ProjectWorkspace workspace, ProjectMediaListItem? selectedItem) =>
        ReferenceEquals(workspace.Project, Project) &&
        ReferenceEquals(workspace.Location, Location) &&
        ReferenceEquals(selectedItem, Item) &&
        !CancellationToken.IsCancellationRequested;
}
