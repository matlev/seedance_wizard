using System.Globalization;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed record SavedFrameMutation(FrameAnchor Anchor, FrameAnchorRevision Revision);

public sealed class SavedFrameService
{
    private readonly ProjectWorkspace _workspace;

    public SavedFrameService(ProjectWorkspace workspace) => _workspace = workspace;

    public async Task<SavedFrameMutation> CreateAsync(
        ExactFramePosition position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var anchor = new FrameAnchor { DisplayLabel = DefaultLabel(position) };
        project.Anchors.Add(anchor);
        var revision = project.CommitAnchorRevision(anchor.Id, position);
        await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new SavedFrameMutation(anchor, revision);
    }

    public async Task<SavedFrameMutation> UpdateAsync(
        Guid anchorId,
        string? label,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var anchor = project.Anchors.SingleOrDefault(candidate => candidate.Id == anchorId)
            ?? throw new InvalidOperationException("The Saved Frame no longer exists.");
        var revision = anchor.CurrentRevisionId is { } revisionId
            ? project.AnchorRevisions.SingleOrDefault(candidate => candidate.Id == revisionId)
            : null;
        if (revision is null)
            throw new InvalidOperationException("The Saved Frame no longer has an exact revision.");

        anchor.DisplayLabel = NullIfWhiteSpace(label) ?? DefaultLabel(revision.TimestampSeconds);
        anchor.Notes = NullIfWhiteSpace(notes);
        project.Touch();
        await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new SavedFrameMutation(anchor, revision);
    }

    public async Task<AnchorRemovalDisposition> RemoveAsync(
        Guid anchorId,
        CancellationToken cancellationToken = default)
    {
        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var disposition = project.RemoveOrArchiveAnchor(anchorId);
        await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
        return disposition;
    }

    public static string DefaultLabel(ExactFramePosition position) =>
        DefaultLabel(position.PresentationTimestamp * (double)position.TimeBaseNumerator / position.TimeBaseDenominator);

    public static string DefaultLabel(double seconds) =>
        $"Saved frame {TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture)}";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
