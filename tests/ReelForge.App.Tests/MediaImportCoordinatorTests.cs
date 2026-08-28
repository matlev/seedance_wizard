using ReelForge.App.Views.Dialogs;
using ReelForge.App.Views.ProjectMedia;
using ReelForge.Application;
using ReelForge.Core;
using System.IO;

namespace ReelForge.App.Tests;

public sealed class MediaImportCoordinatorTests
{
    [Fact]
    public async Task CancelAfterEarlierRestoreChoiceLeavesAllMutationsUntouched()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var operations = new ScriptedOperations(first, second);
        var host = new ScriptedHost(
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, first),
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Cancel));

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\one.mp4", "C:\\two.mp4"]));

        Assert.Empty(operations.Restores);
        Assert.Empty(operations.Imports);
        Assert.Equal("Import cancelled; the project was unchanged.", host.Status);
    }

    [Fact]
    public async Task RestoreAndImportAsNewRouteToTheirExplicitMutations()
    {
        var restoredId = Guid.NewGuid();
        var importId = Guid.NewGuid();
        var operations = new ScriptedOperations(restoredId, importId);
        var host = new ScriptedHost(
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, restoredId),
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.ImportAsNew));

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\restore.mp4", "C:\\new.mp4"]));

        Assert.Equal((restoredId, "C:\\restore.mp4"), Assert.Single(operations.Restores));
        Assert.Equal(["C:\\new.mp4"], Assert.Single(operations.Imports));
        Assert.Equal("Restored 1 deleted source(s) and imported 1 asset(s).", host.Status);
    }

    [Fact]
    public async Task RepeatedIdenticalPathIsProbedAndMutatedOnce()
    {
        var restoredId = Guid.NewGuid();
        var operations = new ScriptedOperations(restoredId);
        var host = new ScriptedHost(new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, restoredId));

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\same.mp4", "c:\\SAME.mp4"]));

        Assert.Equal("C:\\same.mp4", Assert.Single(operations.ProbedPaths));
        Assert.Equal((restoredId, "C:\\same.mp4"), Assert.Single(operations.Restores));
        Assert.Empty(operations.Imports);
    }

    private sealed class ScriptedOperations(params Guid[] deletedIds) : IMediaImportOperations
    {
        private readonly Queue<Guid> _deletedIds = new(deletedIds);
        public List<string> ProbedPaths { get; } = [];
        public List<(Guid, string)> Restores { get; } = [];
        public List<string[]> Imports { get; } = [];

        public Task<DeletedPhysicalAssetRestoreProbe> ProbeDeletedRestoreAsync(string candidatePath, MediaType mediaType,
            CancellationToken cancellationToken = default)
        {
            ProbedPaths.Add(candidatePath);
            var id = _deletedIds.Dequeue();
            return Task.FromResult(new DeletedPhysicalAssetRestoreProbe(
                DeletedPhysicalAssetProbeStatus.Verified,
                new ContentIdentity { Status = ContentHashStatus.Verified, Sha256 = new string('a', 64) },
                [new DeletedPhysicalAssetRestoreMatch(id, Path.GetFileName(candidatePath), mediaType,
                    new ProjectAssetDependencyReport([]))]));
        }

        public Task<DeletedPhysicalAssetRestoreResult> RestoreDeletedExternalAsync(Guid deletedAssetId, string candidatePath,
            CancellationToken cancellationToken = default)
        {
            Restores.Add((deletedAssetId, candidatePath));
            return Task.FromResult(new DeletedPhysicalAssetRestoreResult(
                new PhysicalAssetRelinkResult(PhysicalAssetRelinkStatus.Verified, new ProjectAssetDependencyReport([])),
                deletedAssetId));
        }

        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(IReadOnlyCollection<string> sourcePaths,
            CancellationToken cancellationToken = default)
        {
            Imports.Add(sourcePaths.ToArray());
            return Task.FromResult<IReadOnlyList<ProjectAsset>>([new ProjectAsset()]);
        }
    }

    private sealed class ScriptedHost(params DeletedSourceRestoreChoice[] choices) : IMediaImportCoordinatorHost
    {
        private readonly Queue<DeletedSourceRestoreChoice> _choices = new(choices);
        public string? Status { get; private set; }
        public bool HasOpenProject => true;
        public Task RunUiActionAsync(string status, Func<Task> action) => action();
        public void SetProjectActionsEnabled(bool enabled) { }
        public void RefreshProjectMedia() { }
        public void SetStatus(string status) => Status = status;
        public DeletedSourceRestoreChoice PromptDeletedSourceRestore(string candidateName,
            IReadOnlyList<DeletedPhysicalAssetRestoreMatch> matches, bool allowImportAsNew) => _choices.Dequeue();
    }
}
