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
        var host = new ScriptedHost(choices:
        [
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, first),
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Cancel)
        ]);

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
        var host = new ScriptedHost(choices:
        [
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, restoredId),
            new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.ImportAsNew)
        ]);

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
        var host = new ScriptedHost(choices: [new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, restoredId)]);

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\same.mp4", "c:\\SAME.mp4"]));

        Assert.Equal("C:\\same.mp4", Assert.Single(operations.ProbedPaths));
        Assert.Equal((restoredId, "C:\\same.mp4"), Assert.Single(operations.Restores));
        Assert.Empty(operations.Imports);
    }

    [Fact]
    public async Task UniqueMissingMatchRelinksWithoutImportOrPrompt()
    {
        var missingId = Guid.NewGuid();
        var operations = new ScriptedOperations().WithMissingMatches(missingId);
        var host = new ScriptedHost();

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\returning.mp4"]));

        Assert.Equal((missingId, "C:\\returning.mp4"), Assert.Single(operations.MissingRelinks));
        Assert.Empty(operations.Imports);
        Assert.Empty(host.MissingChoicesRequested);
    }

    [Fact]
    public async Task AmbiguousMissingMatchCanBeImportedAsNew()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var operations = new ScriptedOperations().WithMissingMatches(first, second);
        var host = new ScriptedHost(missingChoices: [new MissingSourceRelinkChoice(MissingSourceRelinkChoiceKind.ImportAsNew)]);

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\ambiguous.mp4"]));

        Assert.Empty(operations.MissingRelinks);
        Assert.Equal(["C:\\ambiguous.mp4"], Assert.Single(operations.Imports));
    }

    [Fact]
    public async Task AmbiguousMissingMatchRelinksTheExplicitSelection()
    {
        var first = Guid.NewGuid();
        var selected = Guid.NewGuid();
        var operations = new ScriptedOperations().WithMissingMatches(first, selected);
        var host = new ScriptedHost(missingChoices:
            [new MissingSourceRelinkChoice(MissingSourceRelinkChoiceKind.Relink, selected)]);

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\ambiguous.mp4"]));

        Assert.Equal((selected, "C:\\ambiguous.mp4"), Assert.Single(operations.MissingRelinks));
        Assert.Empty(operations.Imports);
    }

    [Fact]
    public async Task AmbiguousMissingCancellationLeavesBatchUntouched()
    {
        var operations = new ScriptedOperations().WithMissingMatches(Guid.NewGuid(), Guid.NewGuid());
        var host = new ScriptedHost(missingChoices: [new MissingSourceRelinkChoice(MissingSourceRelinkChoiceKind.Cancel)]);

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\ambiguous.mp4", "C:\\later.mp4"]));

        Assert.Empty(operations.MissingRelinks);
        Assert.Empty(operations.Restores);
        Assert.Empty(operations.Imports);
        Assert.Equal("Import cancelled; the project was unchanged.", host.Status);
    }

    [Fact]
    public async Task VerifiedMissingProbeIdentityIsReusedForDeletedLookup()
    {
        var operations = new ScriptedOperations().WithVerifiedMissingProbeWithoutMatches();
        var host = new ScriptedHost();

        await new MediaImportCoordinator(operations, host).ImportAsync(
            MediaImportInput.FromDialogSelection(["C:\\ordinary.mp4"]));

        Assert.Equal(1, operations.MissingProbeCount);
        Assert.Equal(0, operations.DeletedProbeCount);
        Assert.Equal(1, operations.DeletedIdentityLookupCount);
    }

    private sealed class ScriptedOperations(params Guid[] deletedIds) : IMediaImportOperations
    {
        private readonly Queue<Guid> _deletedIds = new(deletedIds);
        private MissingPhysicalAssetRelinkMatch[] _missingMatches = [];
        private bool _verifiedMissingProbeWithoutMatches;
        public List<string> ProbedPaths { get; } = [];
        public List<(Guid, string)> Restores { get; } = [];
        public List<(Guid, string)> MissingRelinks { get; } = [];
        public List<string[]> Imports { get; } = [];
        public int MissingProbeCount { get; private set; }
        public int DeletedProbeCount { get; private set; }
        public int DeletedIdentityLookupCount { get; private set; }

        public ScriptedOperations WithMissingMatches(params Guid[] ids)
        {
            _missingMatches = ids.Select(id => new MissingPhysicalAssetRelinkMatch(
                id, "missing.mp4", MediaType.Video, new ProjectAssetDependencyReport([]))).ToArray();
            return this;
        }

        public ScriptedOperations WithVerifiedMissingProbeWithoutMatches()
        {
            _verifiedMissingProbeWithoutMatches = true;
            return this;
        }

        public Task<MissingPhysicalAssetRelinkProbe> ProbeMissingRelinkAsync(string candidatePath, MediaType mediaType,
            CancellationToken cancellationToken = default)
        {
            MissingProbeCount++;
            if (_missingMatches.Length > 0 || _verifiedMissingProbeWithoutMatches)
                return Task.FromResult(new MissingPhysicalAssetRelinkProbe(
                    MissingPhysicalAssetProbeStatus.Verified,
                    new ContentIdentity { Status = ContentHashStatus.Verified, Sha256 = new string('b', 64) },
                    _missingMatches));
            return Task.FromResult(new MissingPhysicalAssetRelinkProbe(MissingPhysicalAssetProbeStatus.NotApplicable, null, []));
        }

        public IReadOnlyList<DeletedPhysicalAssetRestoreMatch> FindDeletedRestoreMatches(ContentIdentity identity, MediaType mediaType)
        {
            DeletedIdentityLookupCount++;
            return [];
        }

        public Task<PhysicalAssetRelinkResult> RelinkMissingExternalAsync(Guid missingAssetId, string candidatePath,
            CancellationToken cancellationToken = default)
        {
            MissingRelinks.Add((missingAssetId, candidatePath));
            return Task.FromResult(new PhysicalAssetRelinkResult(
                PhysicalAssetRelinkStatus.Verified, new ProjectAssetDependencyReport([])));
        }

        public Task<DeletedPhysicalAssetRestoreProbe> ProbeDeletedRestoreAsync(string candidatePath, MediaType mediaType,
            CancellationToken cancellationToken = default)
        {
            DeletedProbeCount++;
            ProbedPaths.Add(candidatePath);
            if (_deletedIds.Count == 0)
                return Task.FromResult(new DeletedPhysicalAssetRestoreProbe(
                    DeletedPhysicalAssetProbeStatus.NotApplicable, null, []));
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

    private sealed class ScriptedHost : IMediaImportCoordinatorHost
    {
        private readonly Queue<DeletedSourceRestoreChoice> _choices;
        private readonly Queue<MissingSourceRelinkChoice> _missingChoices;
        public ScriptedHost(DeletedSourceRestoreChoice[]? choices = null, MissingSourceRelinkChoice[]? missingChoices = null)
        {
            _choices = new Queue<DeletedSourceRestoreChoice>(choices ?? []);
            _missingChoices = new Queue<MissingSourceRelinkChoice>(missingChoices ?? []);
        }
        public string? Status { get; private set; }
        public List<string> MissingChoicesRequested { get; } = [];
        public bool HasOpenProject => true;
        public Task RunUiActionAsync(string status, Func<Task> action) => action();
        public void SetProjectActionsEnabled(bool enabled) { }
        public void RefreshProjectMedia() { }
        public void SetStatus(string status) => Status = status;
        public DeletedSourceRestoreChoice PromptDeletedSourceRestore(string candidateName,
            IReadOnlyList<DeletedPhysicalAssetRestoreMatch> matches, bool allowImportAsNew) => _choices.Dequeue();
        public MissingSourceRelinkChoice PromptMissingSourceRelink(string candidateName,
            IReadOnlyList<MissingPhysicalAssetRelinkMatch> matches)
        {
            MissingChoicesRequested.Add(candidateName);
            return _missingChoices.Dequeue();
        }
    }
}
