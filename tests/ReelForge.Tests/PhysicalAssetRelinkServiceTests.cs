using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PhysicalAssetRelinkServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge relink tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VerifiedRelinkCopiesProjectRelativeBytesAndPreservesAssetHistory()
    {
        var candidate = await WriteCandidateAsync("matching bytes");
        var (workspace, asset, expected) = await CreateMissingAssetAsync(candidate);
        asset.Provenance = new AssetProvenance { Operation = "source", SourceAssetIds = [Guid.NewGuid()] };
        asset.ProviderReferences["provider"] = new ProviderAssetReference { Value = "historic-reference" };
        var derived = new ProjectAsset
        {
            DisplayName = "Derived",
            FileName = "Derived",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Origin = AssetOrigin.EditorDerived,
            Physical = null,
            Virtual = new VirtualAssetState()
        };
        workspace.Project!.AddAsset(derived);
        var recipe = workspace.Project.CommitRecipe(derived.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = asset.Id },
            Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 3 }
        });
        var anchor = new FrameAnchor { DisplayLabel = "Relink anchor" };
        workspace.Project.Anchors.Add(anchor);
        var anchorRevision = workspace.Project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            asset.Id, expected.Sha256!, 0, 0, 1, 90_000, 0));
        workspace.Project.Generations.Add(new GenerationRecord
        {
            Status = GenerationStatus.Succeeded,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "test-provider",
                ModelVersion = "test-model",
                Prompt = "Preserve snapshot",
                Mode = GenerationMode.ReferenceToVideo,
                DurationSeconds = 3,
                AspectRatio = "16:9",
                Resolution = "720p",
                References = Array.AsReadOnly(new[]
                {
                    new GenerationReferenceSnapshot
                    {
                        ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
                        LogicalObjectId = anchor.Id,
                        ContentHash = expected.Sha256!,
                        Anchor = new FrameAnchorReferenceSnapshot
                        {
                            AnchorRevisionId = anchorRevision.Id,
                            SourceAssetId = asset.Id,
                            SourceContentHash = expected.Sha256!,
                            VideoStreamIndex = 0,
                            PresentationTimestamp = 0,
                            TimeBaseNumerator = 1,
                            TimeBaseDenominator = 90_000,
                            FrameNumber = 0
                        }
                    }
                })
            }
        });
        await workspace.SaveAsync();

        var service = new PhysicalAssetRelinkService(
            workspace,
            new Sha256ContentHashService(),
            new PhysicalAssetRelinkStager(),
            new ProjectAssetDependencyAnalyzer());
        var result = await service.RelinkAsync(asset.Id, candidate);

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Status);
        Assert.True(result.DependencyReport.IsInUse);
        Assert.Equal(asset.Id, workspace.Project!.Assets.Single(candidateAsset => candidateAsset.Id == asset.Id).Id);
        Assert.Equal(PhysicalAssetAvailability.Available, asset.Physical!.Availability);
        Assert.Equal(expected.Sha256, asset.Physical.ContentIdentity.Sha256);
        Assert.False(Path.IsPathRooted(asset.Physical.RelativePath));
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
        Assert.Equal("matching bytes", await File.ReadAllTextAsync(workspace.GetAbsoluteAssetPath(asset)));

        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        var persisted = reopened.Project.Assets.Single(candidateAsset => candidateAsset.Id == asset.Id);
        Assert.Equal(asset.Id, persisted.Id);
        Assert.Equal("source", persisted.Provenance!.Operation);
        Assert.Equal("historic-reference", persisted.ProviderReferences["provider"].Value);
        Assert.Equal(expected.Sha256, persisted.Physical!.ContentIdentity.Sha256);
        Assert.Equal(PhysicalAssetAvailability.Available, persisted.Physical.Availability);
        Assert.Equal(recipe.Id, Assert.Single(reopened.Project.RecipeRevisions).Id);
        Assert.Equal(anchor.Id, Assert.Single(reopened.Project.Anchors).Id);
        Assert.Equal(anchorRevision.Id, Assert.Single(reopened.Project.AnchorRevisions).Id);
        var reference = Assert.Single(Assert.Single(reopened.Project.Generations).RequestSnapshot.References);
        Assert.Equal(asset.Id, reference.Anchor!.SourceAssetId);
    }

    [Fact]
    public async Task MismatchedCandidateIsRefusedWithoutMutatingTheExpectedIdentity()
    {
        var matchingCandidate = await WriteCandidateAsync("expected bytes");
        var mismatchedCandidate = await WriteCandidateAsync("different bytes", "mismatch.mp4");
        var (workspace, asset, expected) = await CreateMissingAssetAsync(matchingCandidate);
        var originalPath = asset.Physical!.RelativePath;

        var result = await new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new PhysicalAssetRelinkStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, mismatchedCandidate);

        Assert.Equal(PhysicalAssetRelinkStatus.Mismatched, result.Status);
        Assert.Equal(originalPath, asset.Physical.RelativePath);
        Assert.Equal(expected.Sha256, asset.Physical.ContentIdentity.Sha256);
        Assert.Equal(ContentHashStatus.Verified, asset.Physical.ContentIdentity.Status);
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
    }

    [Fact]
    public async Task MissingProbeFindsOnlyMatchingLiveMissingIdentity()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, expected) = await CreateMissingAssetAsync(candidate);
        var service = new PhysicalAssetRelinkService(
            workspace, new Sha256ContentHashService(), new ThrowingStager(), new ProjectAssetDependencyAnalyzer());

        var probe = await service.ProbeMissingAsync(candidate, MediaType.Video);

        Assert.Equal(MissingPhysicalAssetProbeStatus.Verified, probe.Status);
        Assert.Equal(expected.Sha256, probe.CandidateIdentity!.Sha256);
        Assert.Equal(asset.Id, Assert.Single(probe.Matches).AssetId);
    }

    [Fact]
    public async Task MissingProbeDoesNotHashWithoutEligibleMissingSource()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, _) = await CreateMissingAssetAsync(candidate);
        asset.Physical!.Availability = PhysicalAssetAvailability.Available;
        var service = new PhysicalAssetRelinkService(
            workspace, new ThrowingHashService(new InvalidOperationException("Hashing was not expected.")),
            new ThrowingStager(), new ProjectAssetDependencyAnalyzer());

        var probe = await service.ProbeMissingAsync(candidate, MediaType.Video);

        Assert.Equal(MissingPhysicalAssetProbeStatus.NotApplicable, probe.Status);
        Assert.Empty(probe.Matches);
    }

    [Fact]
    public async Task CandidateNotFoundReturnsMissingAndCandidateAccessFailureReturnsInaccessible()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, _) = await CreateMissingAssetAsync(candidate);
        var service = new PhysicalAssetRelinkService(
            workspace,
            new ThrowingHashService(new FileNotFoundException("gone")),
            new ThrowingStager(),
            new ProjectAssetDependencyAnalyzer());

        var missing = await service.RelinkAsync(asset.Id, Path.Combine(_root, "gone.mp4"));
        Assert.Equal(PhysicalAssetRelinkStatus.Missing, missing.Status);

        service = new PhysicalAssetRelinkService(
            workspace,
            new ThrowingHashService(new UnauthorizedAccessException("denied")),
            new ThrowingStager(),
            new ProjectAssetDependencyAnalyzer());
        var inaccessible = await service.RelinkAsync(asset.Id, candidate);
        Assert.Equal(PhysicalAssetRelinkStatus.Inaccessible, inaccessible.Status);
    }

    [Fact]
    public async Task SaveFailureRestoresMetadataRemovesStagedFileAndRetiresRecovery()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var store = new ToggleFailingStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), store);
        await workspace.CreateAsync(Path.Combine(_root, "failing-project"), "Relink project");
        var expected = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset
        {
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Missing,
                ContentIdentity = expected
            }
        };
        workspace.Project!.AddAsset(asset);
        await workspace.SaveAsync();
        store.FailSaves = true;

        var result = await new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new PhysicalAssetRelinkStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate);

        Assert.Equal(PhysicalAssetRelinkStatus.Failed, result.Status);
        Assert.Equal("assets/videos/source.mp4", asset.Physical!.RelativePath);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical.Availability);
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(workspace.Location!)));
    }

    [Fact]
    public async Task CopyFailureAndCancellationDoNotClaimRelinkSuccess()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, _) = await CreateMissingAssetAsync(candidate);
        var failedCopy = new PhysicalAssetRelinkService(
            workspace,
            new Sha256ContentHashService(),
            new FailingStager(),
            new ProjectAssetDependencyAnalyzer());

        var copyFailure = await failedCopy.RelinkAsync(asset.Id, candidate);
        Assert.Equal(PhysicalAssetRelinkStatus.Failed, copyFailure.Status);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical!.Availability);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new ThrowingStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate, cancellation.Token);
        Assert.Equal(PhysicalAssetRelinkStatus.Cancelled, cancelled.Status);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical.Availability);
    }

    [Fact]
    public async Task CommitRemainsVerifiedWhenCallerCancelsImmediatelyAfterProjectCommit()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        using var cancellation = new CancellationTokenSource();
        var store = new AfterCommitStore { CancelAfterProjectCommit = cancellation };
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), store);
        var (seedWorkspace, _, _) = await CreateMissingAssetAsync(candidate);
        await workspace.OpenAsync(seedWorkspace.Location!.ProjectFilePath);
        var asset = Assert.Single(workspace.Project!.Assets);
        store.Enabled = true;

        var result = await new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new PhysicalAssetRelinkStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate, cancellation.Token);

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Status);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
        var persisted = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(PhysicalAssetAvailability.Available, Assert.Single(persisted.Project.Assets).Physical!.Availability);
    }

    [Fact]
    public async Task CommitRemainsVerifiedWhenAnotherSessionSwitchesImmediatelyAfterCommit()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var store = new AfterCommitStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), store);
        await workspace.CreateAsync(Path.Combine(_root, "first"), "First");
        var expected = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset
        {
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Missing,
                ContentIdentity = expected
            }
        };
        workspace.Project!.AddAsset(asset);
        await workspace.SaveAsync();
        var (_, replacement) = await store.CreateAsync(Path.Combine(_root, "replacement"), "Replacement");
        Task? switchTask = null;
        store.AfterProjectCommit = () => switchTask = workspace.OpenAsync(replacement.ProjectFilePath);
        store.Enabled = true;

        var result = await new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new PhysicalAssetRelinkStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate);
        await switchTask!;

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Status);
        var persisted = await new PortableProjectStore().OpenAsync(Path.Combine(_root, "first", "First.rfp"));
        Assert.Equal(PhysicalAssetAvailability.Available, Assert.Single(persisted.Project.Assets).Physical!.Availability);
        Assert.Equal("Replacement", workspace.Project!.Name);
    }

    [Fact]
    public async Task PreCommitSessionSwitchRollsBackTheStagedCopy()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, _) = await CreateMissingAssetAsync(candidate);
        var replacementStore = new PortableProjectStore();
        var (_, replacement) = await replacementStore.CreateAsync(Path.Combine(_root, "replacement-before-save"), "Replacement");
        var stager = new BlockingStager();
        var service = new PhysicalAssetRelinkService(
            workspace,
            new Sha256ContentHashService(),
            stager,
            new ProjectAssetDependencyAnalyzer());

        var relink = service.RelinkAsync(asset.Id, candidate);
        await stager.Staged.Task;
        await workspace.OpenAsync(replacement.ProjectFilePath);
        stager.Release.TrySetResult();
        var result = await relink;

        Assert.Equal(PhysicalAssetRelinkStatus.Stale, result.Status);
        Assert.Equal("Replacement", workspace.Project!.Name);
        Assert.False(File.Exists(stager.DestinationPath!));
    }

    [Fact]
    public async Task PendingRecoveryRefusesRelinkWithoutReplacingTheRecoveryCandidate()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (seed, _, _) = await CreateMissingAssetAsync(candidate);
        var portable = new PortableProjectStore();
        var opened = await portable.OpenAsync(seed.Location!.ProjectFilePath);
        opened.Project.Name = "Recovery candidate";
        await portable.WriteAsync(opened.Project, opened.Location);
        var workspace = new ProjectWorkspace(portable, new UnusedImporter(), portable);
        await workspace.OpenAsync(seed.Location.ProjectFilePath);
        var asset = Assert.Single(workspace.Project!.Assets);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new PhysicalAssetRelinkStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate));

        Assert.Equal(ProjectWorkspaceState.RecoveryAvailable, workspace.State);
        Assert.True(File.Exists(PortableProjectStore.GetRecoveryFilePath(workspace.Location!)));
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
    }

    [Fact]
    public async Task ConcurrentFailedSavePreservesItsRecoveryCandidateWhenRelinkBecomesIneligible()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var store = new ToggleFailingStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), store);
        await workspace.CreateAsync(Path.Combine(_root, "concurrent-failure"), "Relink project");
        var expected = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset
        {
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Missing,
                ContentIdentity = expected
            }
        };
        workspace.Project!.AddAsset(asset);
        await workspace.SaveAsync();
        var stager = new BlockingStager();
        var relink = new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                stager,
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate);
        await stager.Staged.Task;

        workspace.Project!.Name = "Failed save state";
        store.FailSaves = true;
        await Assert.ThrowsAsync<IOException>(() => workspace.SaveAsync());
        stager.Release.TrySetResult();
        var result = await relink;

        Assert.Equal(PhysicalAssetRelinkStatus.Stale, result.Status);
        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.True(File.Exists(PortableProjectStore.GetRecoveryFilePath(workspace.Location!)));
        Assert.False(File.Exists(stager.DestinationPath!));
    }

    [Fact]
    public async Task CallerCancellationAfterRecoveryWriteRestoresPriorWorkspaceState()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var store = new BlockingProjectSaveStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), store);
        await workspace.CreateAsync(Path.Combine(_root, "cancel-after-recovery"), "Relink project");
        var expected = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset
        {
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Missing,
                ContentIdentity = expected
            }
        };
        workspace.Project!.AddAsset(asset);
        await workspace.SaveAsync();
        store.BlockSaves = true;
        using var cancellation = new CancellationTokenSource();
        var relink = new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                new PhysicalAssetRelinkStager(),
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate, cancellation.Token);
        await store.SaveStarted.Task;
        cancellation.Cancel();
        var result = await relink;

        Assert.Equal(PhysicalAssetRelinkStatus.Cancelled, result.Status);
        Assert.Equal(ProjectWorkspaceState.Saved, workspace.State);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical!.Availability);
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(workspace.Location!)));
    }

    [Fact]
    public async Task DiscardDoesNotDeleteAReplacementOfTheStagedArtifact()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, _) = await CreateMissingAssetAsync(candidate);
        var stager = new PhysicalAssetRelinkStager();
        var staged = await stager.StageAsync(workspace.Location!, asset, candidate);

        await File.WriteAllTextAsync(staged.DestinationPath, "replacement bytes");
        await stager.DiscardAsync(staged);

        Assert.True(File.Exists(staged.DestinationPath));
        Assert.Equal("replacement bytes", await File.ReadAllTextAsync(staged.DestinationPath));
    }

    [Fact]
    public async Task PostCopyCancellationCleansTemporaryDataAndReopensCommittedProject()
    {
        var candidate = await WriteCandidateAsync("expected bytes");
        var (workspace, asset, _) = await CreateMissingAssetAsync(candidate);
        using var cancellation = new CancellationTokenSource();
        var stager = new PhysicalAssetRelinkStager(_ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });

        var result = await new PhysicalAssetRelinkService(
                workspace,
                new Sha256ContentHashService(),
                stager,
                new ProjectAssetDependencyAnalyzer())
            .RelinkAsync(asset.Id, candidate, cancellation.Token);

        Assert.Equal(PhysicalAssetRelinkStatus.Cancelled, result.Status);
        Assert.Equal("assets/videos/source.mp4", asset.Physical!.RelativePath);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical.Availability);
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(asset)));
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(workspace.Location!)));
        Assert.Empty(Directory.EnumerateFiles(workspace.Location!.RootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains(".relink-", StringComparison.Ordinal)));

        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location.ProjectFilePath);
        var persisted = Assert.Single(reopened.Project.Assets);
        Assert.Equal(asset.Id, persisted.Id);
        Assert.Equal("assets/videos/source.mp4", persisted.Physical!.RelativePath);
        Assert.Equal(PhysicalAssetAvailability.Missing, persisted.Physical.Availability);
    }

    private async Task<(ProjectWorkspace Workspace, ProjectAsset Asset, ContentIdentity Expected)> CreateMissingAssetAsync(string candidate)
    {
        var store = new PortableProjectStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter(), store);
        await workspace.CreateAsync(Path.Combine(_root, "project"), "Relink project");
        var expected = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset
        {
            FileName = "source.mp4",
            DisplayName = "Source",
            MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = "assets/videos/source.mp4",
                Availability = PhysicalAssetAvailability.Missing,
                ContentIdentity = expected
            }
        };
        workspace.Project!.AddAsset(asset);
        await workspace.SaveAsync();
        return (workspace, asset, expected);
    }

    private async Task<string> WriteCandidateAsync(string content, string fileName = "candidate.mp4")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}-{fileName}");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private sealed class ThrowingStager : IPhysicalAssetRelinkStager
    {
        public Task<StagedPhysicalAssetRelink> StageAsync(ProjectLocation location, ProjectAsset asset, string candidatePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task DiscardAsync(StagedPhysicalAssetRelink staged, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingStager : IPhysicalAssetRelinkStager
    {
        public Task<StagedPhysicalAssetRelink> StageAsync(ProjectLocation location, ProjectAsset asset, string candidatePath, CancellationToken cancellationToken = default) =>
            throw new IOException("Injected staged copy failure.");

        public Task DiscardAsync(StagedPhysicalAssetRelink staged, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingHashService(Exception exception) : IContentHashService
    {
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) => throw exception;

        public Task<ContentVerificationResult> VerifyAsync(string path, ContentIdentity expected, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class ToggleFailingStore : IProjectStore, IProjectRecoveryStore
    {
        private readonly PortableProjectStore _inner = new();
        public bool FailSaves { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            if (FailSaves) throw new IOException("Injected project save failure.");
            return _inner.SaveAsync(project, location, cancellationToken);
        }

        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.ProbeAsync(location, cancellationToken);

        public Task WriteAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(project, location, cancellationToken);

        public Task DiscardAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.DiscardAsync(location, cancellationToken);
    }

    private sealed class AfterCommitStore : IProjectStore, IProjectRecoveryStore
    {
        private readonly PortableProjectStore _inner = new();
        public bool Enabled { get; set; }
        public CancellationTokenSource? CancelAfterProjectCommit { get; init; }
        public Action? AfterProjectCommit { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public async Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            await _inner.SaveAsync(project, location, cancellationToken);
            if (!Enabled)
                return;

            CancelAfterProjectCommit?.Cancel();
            AfterProjectCommit?.Invoke();
        }

        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.ProbeAsync(location, cancellationToken);

        public Task WriteAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(project, location, cancellationToken);

        public Task DiscardAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.DiscardAsync(location, cancellationToken);
    }

    private sealed class BlockingStager : IPhysicalAssetRelinkStager
    {
        private readonly PhysicalAssetRelinkStager _inner = new();
        public TaskCompletionSource Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? DestinationPath { get; private set; }

        public async Task<StagedPhysicalAssetRelink> StageAsync(
            ProjectLocation location,
            ProjectAsset asset,
            string candidatePath,
            CancellationToken cancellationToken = default)
        {
            var staged = await _inner.StageAsync(location, asset, candidatePath, cancellationToken);
            DestinationPath = staged.DestinationPath;
            Staged.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return staged;
        }

        public Task DiscardAsync(StagedPhysicalAssetRelink staged, CancellationToken cancellationToken = default) =>
            _inner.DiscardAsync(staged, cancellationToken);
    }

    private sealed class BlockingProjectSaveStore : IProjectStore, IProjectRecoveryStore
    {
        private readonly PortableProjectStore _inner = new();
        public bool BlockSaves { get; set; }
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public async Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            if (BlockSaves)
            {
                SaveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            await _inner.SaveAsync(project, location, cancellationToken);
        }

        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.ProbeAsync(location, cancellationToken);

        public Task WriteAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(project, location, cancellationToken);

        public Task DiscardAsync(ProjectLocation location, CancellationToken cancellationToken = default) =>
            _inner.DiscardAsync(location, cancellationToken);
    }
}
