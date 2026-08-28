using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;
using System.Text.Json.Nodes;

namespace ReelForge.Tests;

public sealed class ProjectRecoveryLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge tests", Guid.NewGuid().ToString("N"));
    private static readonly System.Text.Json.JsonSerializerOptions RecoverySerializerOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
        };

    [Fact]
    public async Task OpenDiscoversRecoveryWithoutActivatingCommittedProjectAndAcceptRequiresSave()
    {
        var store = new PortableProjectStore();
        var (committed, location) = await store.CreateAsync(_root, "Recovery demo");
        var recovered = new VideoProject { Id = committed.Id, Name = "Recovered working copy" };
        await store.WriteAsync(recovered, location);

        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.RecoveryAvailable, workspace.State);
        Assert.Equal("Recovery demo", workspace.Project!.Name);
        Assert.NotNull(workspace.RecoveryCandidate);

        await workspace.AcceptRecoveryAsync();

        Assert.Equal(ProjectWorkspaceState.Recovered, workspace.State);
        Assert.Equal("Recovered working copy", workspace.Project!.Name);
        Assert.Equal("Recovery demo", (await store.OpenAsync(location.ProjectFilePath)).Project.Name);
        Assert.True(File.Exists(PortableProjectStore.GetRecoveryFilePath(location)));

        await workspace.SaveAsync();

        Assert.Equal(ProjectWorkspaceState.Saved, workspace.State);
        Assert.Equal("Recovered working copy", (await store.OpenAsync(location.ProjectFilePath)).Project.Name);
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(location)));
    }

    [Fact]
    public async Task DiscardRecoveryLeavesCommittedProjectAndRetiresCandidate()
    {
        var store = new PortableProjectStore();
        var (committed, location) = await store.CreateAsync(_root, "Discard demo");
        await store.WriteAsync(new VideoProject { Id = committed.Id, Name = "Discard me" }, location);
        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(location.ProjectFilePath);

        await workspace.DiscardRecoveryAsync();

        Assert.Equal(ProjectWorkspaceState.Clean, workspace.State);
        Assert.Equal("Discard demo", workspace.Project!.Name);
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(location)));
    }

    [Fact]
    public async Task InvalidRecoveryFailsClosedWhileCommittedProjectRemainsAvailable()
    {
        var store = new PortableProjectStore();
        var (_, location) = await store.CreateAsync(_root, "Invalid demo");
        await File.WriteAllTextAsync(PortableProjectStore.GetRecoveryFilePath(location), "{ invalid");
        var workspace = CreateWorkspace(store);

        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.NotNull(workspace.FailureDetail);
        Assert.Equal("Invalid demo", workspace.Project!.Name);
        Assert.Null(workspace.RecoveryCandidate);
    }

    [Fact]
    public async Task RecoveryForAnotherProjectFailsClosed()
    {
        var store = new PortableProjectStore();
        var (_, location) = await store.CreateAsync(_root, "Project identity");
        await store.WriteAsync(new VideoProject { Name = "Another project" }, location);
        var workspace = CreateWorkspace(store);

        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.Contains("different project", workspace.FailureDetail!);
        Assert.Null(workspace.RecoveryCandidate);
    }

    [Fact]
    public async Task CandidateIdenticalToCommittedProjectIsRetiredWithoutPrompting()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Already committed");
        await store.WriteAsync(project, location);

        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.Clean, workspace.State);
        Assert.Null(workspace.RecoveryCandidate);
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(location)));
    }

    [Fact]
    public async Task CandidateWithTamperedPayloadHashFailsClosed()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Tampered hash");
        project.Name = "Pending recovery";
        await store.WriteAsync(project, location);
        var recoveryPath = PortableProjectStore.GetRecoveryFilePath(location);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(recoveryPath))!.AsObject();
        envelope["projectPayloadSha256"] = new string('0', 64);
        await File.WriteAllTextAsync(recoveryPath, envelope.ToJsonString());

        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.Contains("invalid", workspace.FailureDetail!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Tampered hash", workspace.Project!.Name);
    }

    [Fact]
    public async Task CandidateBasedOnAnOlderCommittedRepresentationFailsClosed()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Stale base");
        project.Name = "Recovery from first save";
        await store.WriteAsync(project, location);
        project.Name = "Newer committed project";
        await store.SaveAsync(project, location);

        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.Contains("stale", workspace.FailureDetail!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Newer committed project", workspace.Project!.Name);
    }

    [Fact]
    public async Task CandidateRequiresTheExactCommittedFileRepresentationItWasBasedOn()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Exact base identity");
        project.Name = "Candidate based on original bytes";
        await store.WriteAsync(project, location);
        var committed = await File.ReadAllTextAsync(location.ProjectFilePath);
        await File.WriteAllTextAsync(location.ProjectFilePath, committed + Environment.NewLine);

        var probe = await store.ProbeAsync(location);

        Assert.Null(probe.Candidate);
        Assert.Contains("stale", probe.FailureDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelfConsistentAlteredRecoveryEnvelopeStillFailsClosedWhenItsBaseIsStale()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Altered envelope");
        project.Name = "Original candidate";
        await store.WriteAsync(project, location);
        var recoveryPath = PortableProjectStore.GetRecoveryFilePath(location);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(recoveryPath))!.AsObject();
        envelope["project"]!.AsObject()["name"] = "Altered candidate";
        var candidate = System.Text.Json.JsonSerializer.Deserialize<ProjectFileDto>(
            envelope["project"]!.ToJsonString(),
            RecoverySerializerOptions)!;
        var candidateBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(candidate, RecoverySerializerOptions);
        envelope["projectPayloadSha256"] = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(candidateBytes)).ToLowerInvariant();
        await File.WriteAllTextAsync(recoveryPath, envelope.ToJsonString());
        project.Name = "Newer committed project";
        await store.SaveAsync(project, location);

        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(location.ProjectFilePath);

        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.Contains("stale", workspace.FailureDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoveryCandidateProjectsPhysicalMediaDegradation()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Degraded candidate");
        project.AddAsset(CreatePhysicalAsset("missing.mp4", "assets/videos/missing.mp4"));
        await store.WriteAsync(project, location);

        var probe = await store.ProbeAsync(location);

        Assert.NotNull(probe.Candidate);
        Assert.True(probe.Candidate!.IsDegraded);
        Assert.NotNull(probe.Candidate.DegradationDetail);
        Assert.Equal(PhysicalAssetAvailability.Missing, probe.Candidate.Project.Assets.Single().Physical!.Availability);
    }

    [Fact]
    public async Task MainSaveFailureRetainsRecoveryAndMarksWorkspaceFailed()
    {
        var portable = new PortableProjectStore();
        var (project, location) = await portable.CreateAsync(_root, "Interrupted save");
        var workspace = CreateWorkspace(new FailingSaveStore(portable), portable);
        await workspace.OpenAsync(location.ProjectFilePath);
        workspace.Project!.Name = "Unsaved replacement";

        await Assert.ThrowsAsync<IOException>(() => workspace.SaveAsync());

        Assert.Equal(ProjectWorkspaceState.Failed, workspace.State);
        Assert.True(File.Exists(PortableProjectStore.GetRecoveryFilePath(location)));
        Assert.Equal("Interrupted save", (await portable.OpenAsync(location.ProjectFilePath)).Project.Name);
        var afterRestart = CreateWorkspace(portable);
        await afterRestart.OpenAsync(location.ProjectFilePath);
        Assert.Equal(ProjectWorkspaceState.RecoveryAvailable, afterRestart.State);
        Assert.Equal("Unsaved replacement", afterRestart.RecoveryCandidate!.Project.Name);
    }

    [Fact]
    public async Task SaveTransitionsThroughDirtyAndSavingBeforeItCommits()
    {
        var portable = new PortableProjectStore();
        var (_, location) = await portable.CreateAsync(_root, "Transitions");
        var states = new List<ProjectWorkspaceState>();
        ProjectWorkspace? workspace = null;
        var recovery = new ObservingRecoveryStore(portable, () => states.Add(workspace!.State));
        var savingStore = new ObservingSaveStore(portable, () => states.Add(workspace!.State));
        workspace = new ProjectWorkspace(savingStore, new UnusedImporter(), recovery);
        await workspace!.OpenAsync(location.ProjectFilePath);

        await workspace.SaveAsync();

        Assert.Equal([ProjectWorkspaceState.Dirty, ProjectWorkspaceState.Saving], states);
        Assert.Equal(ProjectWorkspaceState.Saved, workspace.State);
    }

    [Fact]
    public async Task ProjectSwitchWaitsForInFlightSaveAndPublishesReplacementWorkspaceState()
    {
        var portable = new PortableProjectStore();
        var (_, firstLocation) = await portable.CreateAsync(Path.Combine(_root, "first"), "First");
        var (_, secondLocation) = await portable.CreateAsync(Path.Combine(_root, "second"), "Second");
        var blockingRecovery = new BlockingRecoveryStore(portable);
        var workspace = new ProjectWorkspace(portable, new UnusedImporter(), blockingRecovery);
        await workspace.OpenAsync(firstLocation.ProjectFilePath);
        var firstProject = workspace.Project!;
        var firstSession = workspace.Location!;
        firstProject.Name = "Unsaved first-project change";

        var staleSave = workspace.SaveIfCurrentAsync(firstProject, firstSession);
        await blockingRecovery.WriteStarted.Task;
        var openSecond = workspace.OpenAsync(secondLocation.ProjectFilePath);
        await Task.Delay(50);
        Assert.False(openSecond.IsCompleted);
        blockingRecovery.ReleaseWrite.TrySetResult();

        await staleSave;
        await openSecond;
        Assert.Equal("Unsaved first-project change", (await portable.OpenAsync(firstLocation.ProjectFilePath)).Project.Name);
        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(firstLocation)));
        Assert.Equal("Second", workspace.Project!.Name);
        Assert.Equal(ProjectWorkspaceState.Clean, workspace.State);
        Assert.Null(workspace.RecoveryCandidate);
        Assert.Null(workspace.FailureDetail);
    }

    [Fact]
    public async Task OverlappingSaveCannotDeleteTheNewerRecoveryCandidate()
    {
        var portable = new PortableProjectStore();
        var (_, location) = await portable.CreateAsync(_root, "Overlapping saves");
        var recovery = new BlockingFirstDiscardRecoveryStore(portable);
        var workspace = new ProjectWorkspace(new FailNamedSaveStore(portable, "Second pending"), new UnusedImporter(), recovery);
        await workspace.OpenAsync(location.ProjectFilePath);
        workspace.Project!.Name = "First committed";

        var firstSave = workspace.SaveAsync();
        await recovery.FirstDiscardStarted.Task;
        workspace.Project.Name = "Second pending";
        var secondSave = workspace.SaveAsync();
        await Task.Delay(50);
        recovery.ReleaseFirstDiscard.TrySetResult();

        await firstSave;
        await Assert.ThrowsAsync<IOException>(() => secondSave);
        Assert.Equal("First committed", (await portable.OpenAsync(location.ProjectFilePath)).Project.Name);
        var probe = await portable.ProbeAsync(location);
        Assert.Equal("Second pending", probe.Candidate!.Project.Name);
    }

    [Fact]
    public async Task SessionSwitchCannotInterleaveWithTheFinalAtomicReplacement()
    {
        var portable = new PortableProjectStore();
        var (_, firstLocation) = await portable.CreateAsync(Path.Combine(_root, "commit-first"), "First");
        var (_, secondLocation) = await portable.CreateAsync(Path.Combine(_root, "commit-second"), "Second");
        var blockingStore = new BlockingCommitProjectStore(portable);
        var workspace = new ProjectWorkspace(blockingStore, new UnusedImporter());
        await workspace.OpenAsync(firstLocation.ProjectFilePath);
        workspace.Project!.Name = "Committed before switch";

        var save = workspace.SaveAsync();
        await blockingStore.CommitEntered.Task;
        var open = workspace.OpenAsync(secondLocation.ProjectFilePath);
        await Task.Delay(50);
        Assert.False(open.IsCompleted);
        blockingStore.ReleaseCommit.TrySetResult();

        await Task.WhenAll(save, open);
        Assert.Equal("Committed before switch", (await portable.OpenAsync(firstLocation.ProjectFilePath)).Project.Name);
        Assert.Equal("Second", workspace.Project!.Name);
    }

    [Fact]
    public async Task OpenWaitsForDetachedCommitAndCannotPublishItsStalePreCommitSnapshot()
    {
        var portable = new PortableProjectStore();
        var (_, location) = await portable.CreateAsync(_root, "Before background update");
        var blockingStore = new BlockingCommitProjectStore(portable);
        var coordinator = new ProjectSaveCoordinator();
        var backgroundWorkspace = new ProjectWorkspace(
            blockingStore, new UnusedImporter(), portable, coordinator);
        var activeWorkspace = new ProjectWorkspace(
            blockingStore, new UnusedImporter(), portable, coordinator);

        var backgroundUpdate = backgroundWorkspace.UpdateDetachedAsync(
            location.ProjectFilePath,
            (project, _) => project.Name = "Background update committed");
        await blockingStore.CommitEntered.Task;
        var open = activeWorkspace.OpenAsync(location.ProjectFilePath);
        await Task.Delay(50);
        Assert.False(open.IsCompleted);
        blockingStore.ReleaseCommit.TrySetResult();

        await backgroundUpdate;
        await open;
        Assert.Equal("Background update committed", activeWorkspace.Project!.Name);
        activeWorkspace.Project.CurrentGenerationDraft = new GenerationDraft { Prompt = "Unrelated user edit" };
        await activeWorkspace.SaveAsync();
        var (reopened, _) = await portable.OpenAsync(location.ProjectFilePath);
        Assert.Equal("Background update committed", reopened.Name);
        Assert.Equal("Unrelated user edit", reopened.CurrentGenerationDraft!.Prompt);
    }

    [Fact]
    public async Task OverlappingOpensPublishInRequestOrder()
    {
        var store = new OrderedOpenProjectStore(_root);
        var workspace = new ProjectWorkspace(store, new UnusedImporter());

        var firstOpen = workspace.OpenAsync(Path.Combine(_root, "first.rfp"));
        await store.FirstOpenStarted.Task;
        var secondOpen = workspace.OpenAsync(Path.Combine(_root, "second.rfp"));
        await Task.Delay(50);
        Assert.False(store.SecondOpenStarted.Task.IsCompleted);
        store.ReleaseFirstOpen.TrySetResult();
        await store.SecondOpenStarted.Task;
        store.ReleaseSecondOpen.TrySetResult();

        await Task.WhenAll(firstOpen, secondOpen);
        Assert.Equal("second", workspace.Project!.Name);
    }

    [Fact]
    public async Task DetachedBackgroundSaveReplacesAndRetiresAnOlderRecoveryCandidate()
    {
        var store = new PortableProjectStore();
        var (backgroundProject, location) = await store.CreateAsync(_root, "Background project");
        backgroundProject.Name = "Older recovery candidate";
        await store.WriteAsync(backgroundProject, location);
        backgroundProject.Name = "Background finalization";
        var workspace = CreateWorkspace(store);

        await workspace.UpdateDetachedAsync(
            location.ProjectFilePath,
            (latest, _) => latest.Name = backgroundProject.Name);

        Assert.False(File.Exists(PortableProjectStore.GetRecoveryFilePath(location)));
        var reopened = CreateWorkspace(store);
        await reopened.OpenAsync(location.ProjectFilePath);
        Assert.Equal(ProjectWorkspaceState.Clean, reopened.State);
        Assert.Equal("Background finalization", reopened.Project!.Name);
    }

    [Fact]
    public async Task BackgroundFinalizationMergesWithAnUnrelatedActiveProjectEdit()
    {
        var store = new PortableProjectStore();
        var (background, backgroundLocation) = await store.CreateAsync(Path.Combine(_root, "background"), "Background");
        var generation = new GenerationRecord { Status = GenerationStatus.Running };
        background.Generations.Add(generation);
        await store.SaveAsync(background, backgroundLocation);
        var (_, activeLocation) = await store.CreateAsync(Path.Combine(_root, "active"), "Active");
        var workspace = CreateWorkspace(store);
        await workspace.OpenAsync(activeLocation.ProjectFilePath);
        var ingestion = new BlockingGeneratedOutputIngestion();
        var finalizer = new GenerationJobFinalizer(workspace, store, ingestion);

        var finalization = finalizer.FinalizeAsync(new TrackedGenerationJob
        {
            GenerationId = generation.Id,
            ProjectFilePath = backgroundLocation.ProjectFilePath,
            Status = GenerationStatus.Succeeded,
            Outputs = [new ProviderGenerationOutput("test://generated-output")]
        });
        await ingestion.Started.Task;
        await workspace.OpenAsync(backgroundLocation.ProjectFilePath);
        workspace.Project!.Name = "User edit preserved";
        await workspace.SaveAsync();
        ingestion.Release.TrySetResult();

        await finalization;
        var (reopened, _) = await store.OpenAsync(backgroundLocation.ProjectFilePath);
        Assert.Equal("User edit preserved", reopened.Name);
        var finalized = reopened.Generations.Single(candidate => candidate.Id == generation.Id);
        Assert.Equal(GenerationStatus.Succeeded, finalized.Status);
        Assert.Equal(OutputIngestionStatus.Succeeded, finalized.IngestionStatus);
        Assert.Single(finalized.OutputAssetIds);
        Assert.Contains(reopened.Assets, asset => asset.Id == finalized.OutputAssetIds[0]);
    }

    [Fact]
    public async Task FinalPostIngestionSaveFailurePreservesSuccessfulOutputInRecovery()
    {
        var portable = new PortableProjectStore();
        var (project, location) = await portable.CreateAsync(_root, "Final save recovery");
        var generation = new GenerationRecord { Status = GenerationStatus.Running };
        project.Generations.Add(generation);
        await portable.SaveAsync(project, location);
        var workspace = new ProjectWorkspace(
            new FailSucceededGenerationSaveStore(portable),
            new UnusedImporter(),
            portable);
        await workspace.OpenAsync(location.ProjectFilePath);
        var ingestion = new BlockingGeneratedOutputIngestion();
        ingestion.Release.TrySetResult();
        var finalizer = new GenerationJobFinalizer(workspace, portable, ingestion);

        await Assert.ThrowsAsync<IOException>(() => finalizer.FinalizeAsync(new TrackedGenerationJob
        {
            GenerationId = generation.Id,
            ProjectFilePath = location.ProjectFilePath,
            Status = GenerationStatus.Succeeded,
            Outputs = [new ProviderGenerationOutput("test://generated-output")]
        }));

        var probe = await portable.ProbeAsync(location);
        var recoveredGeneration = probe.Candidate!.Project.Generations.Single(candidate => candidate.Id == generation.Id);
        Assert.Equal(GenerationStatus.Succeeded, recoveredGeneration.Status);
        Assert.Equal(OutputIngestionStatus.Succeeded, recoveredGeneration.IngestionStatus);
        Assert.Null(recoveredGeneration.Error);
        var outputId = Assert.Single(recoveredGeneration.OutputAssetIds);
        Assert.Contains(probe.Candidate.Project.Assets, asset => asset.Id == outputId);
    }

    [Fact]
    public async Task RecoveryEnvelopePreservesExistingProjectMeaning()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_root, "Identity demo");
        var asset = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        asset.Provenance = new AssetProvenance
        {
            Operation = "import",
            SourceAssetIds = [Guid.NewGuid()]
        };
        asset.ProviderReferences["provider"] = new ProviderAssetReference
        {
            Value = "historic-reference",
            SourceContentHash = new string('a', 64)
        };
        project.AddAsset(asset);
        var derived = new ProjectAsset
        {
            DisplayName = "Trim",
            FileName = "Trim",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Origin = AssetOrigin.EditorDerived,
            Physical = null,
            Virtual = new VirtualAssetState()
        };
        project.AddAsset(derived);
        var recipe = project.CommitRecipe(derived.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = asset.Id },
            Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 3 }
        });
        var anchor = new FrameAnchor { DisplayLabel = "Anchor" };
        project.Anchors.Add(anchor);
        var anchorRevision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            asset.Id, new string('a', 64), 0, 0, 1, 90_000, 0));
        project.CurrentGenerationDraft = new GenerationDraft { Prompt = "Keep provenance" };
        project.Generations.Add(new GenerationRecord
        {
            Status = GenerationStatus.Succeeded,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "test-provider",
                ModelVersion = "test-model",
                Prompt = "Keep snapshot",
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
                        ContentHash = new string('a', 64),
                        Anchor = new FrameAnchorReferenceSnapshot
                        {
                            AnchorRevisionId = anchorRevision.Id,
                            SourceAssetId = asset.Id,
                            SourceContentHash = new string('a', 64),
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
        await store.WriteAsync(project, location);

        var probe = await store.ProbeAsync(location);
        var recovered = probe.Candidate!.Project;

        Assert.Equal(project.Id, recovered.Id);
        Assert.Equal(asset.Id, recovered.Assets.Single(candidate => candidate.Id == asset.Id).Id);
        Assert.Equal(recipe.Id, Assert.Single(recovered.RecipeRevisions).Id);
        Assert.Equal(anchorRevision.Id, Assert.Single(recovered.AnchorRevisions).Id);
        Assert.Equal(anchor.Id, Assert.Single(recovered.Anchors).Id);
        var snapshot = Assert.Single(recovered.Generations).RequestSnapshot;
        Assert.Equal("Keep snapshot", snapshot.Prompt);
        Assert.Equal(anchorRevision.Id, Assert.Single(snapshot.References).Anchor!.AnchorRevisionId);
        var reopenedAsset = recovered.Assets.Single(candidate => candidate.Id == asset.Id);
        Assert.Equal(asset.Provenance.SourceAssetIds, reopenedAsset.Provenance!.SourceAssetIds);
        Assert.Equal("historic-reference", reopenedAsset.ProviderReferences["provider"].Value);
        Assert.Equal("Keep provenance", recovered.CurrentGenerationDraft!.Prompt);
    }

    private static ProjectWorkspace CreateWorkspace(IProjectStore store, IProjectRecoveryStore? recovery = null) =>
        new(store, new UnusedImporter(), recovery ?? store as IProjectRecoveryStore);

    private static bool CommitUnconditionally(Action commit)
    {
        commit();
        return true;
    }

    private static ProjectAsset CreatePhysicalAsset(string fileName, string relativePath) => new()
    {
        DisplayName = fileName,
        FileName = fileName,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Origin = AssetOrigin.Imported,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = relativePath,
            ContentIdentity = new ContentIdentity
            {
                Sha256 = new string('a', 64),
                Status = ContentHashStatus.Verified,
                LengthBytes = 42
            }
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockingGeneratedOutputIngestion : IGeneratedOutputIngestionService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ProjectAsset>> IngestAsync(
            ProjectLocation location,
            Guid generationId,
            IReadOnlyList<ProviderGenerationOutput> outputs,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return
            [
                new ProjectAsset
                {
                    FileName = "generated.mp4",
                    DisplayName = "generated.mp4",
                    MediaType = MediaType.Video,
                    Origin = AssetOrigin.Generated,
                    Provenance = new AssetProvenance
                    {
                        Operation = "generation-output",
                        GenerationId = generationId
                    },
                    Physical = new PhysicalAssetStorage
                    {
                        RelativePath = "generated/generated.mp4",
                        Durability = PhysicalAssetDurability.Generated,
                        Availability = PhysicalAssetAvailability.Available,
                        ContentIdentity = new ContentIdentity
                        {
                            Sha256 = new string('b', 64),
                            Status = ContentHashStatus.Verified
                        }
                    }
                }
            ];
        }
    }

    private sealed class FailingSaveStore(PortableProjectStore inner) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default) => inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default) => throw new IOException("Simulated interrupted main replacement.");
    }

    private sealed class FailSucceededGenerationSaveStore(PortableProjectStore inner) :
        IProjectStore,
        IProjectCommitGuardedStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default) => inner.OpenAsync(projectFilePath, cancellationToken);

        public async Task SaveAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            _ = await SaveIfAsync(project, location, CommitUnconditionally, cancellationToken).ConfigureAwait(false);
        }

        public Task<bool> SaveIfAsync(VideoProject project, ProjectLocation location, Func<Action, bool> tryCommit,
            CancellationToken cancellationToken = default) =>
            project.Generations.Any(candidate => candidate.IngestionStatus == OutputIngestionStatus.Succeeded)
                ? Task.FromException<bool>(new IOException("Simulated final post-ingestion save failure."))
                : inner.SaveIfAsync(project, location, tryCommit, cancellationToken);
    }

    private sealed class FailNamedSaveStore(PortableProjectStore inner, string failingName) :
        IProjectStore,
        IProjectCommitGuardedStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default) => inner.OpenAsync(projectFilePath, cancellationToken);

        public async Task SaveAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            _ = await SaveIfAsync(project, location, CommitUnconditionally, cancellationToken).ConfigureAwait(false);
        }

        public Task<bool> SaveIfAsync(VideoProject project, ProjectLocation location, Func<Action, bool> tryCommit,
            CancellationToken cancellationToken = default) =>
            project.Name == failingName
                ? Task.FromException<bool>(new IOException("Simulated interrupted overlapping save."))
                : inner.SaveIfAsync(project, location, tryCommit, cancellationToken);
    }

    private sealed class BlockingCommitProjectStore(PortableProjectStore inner) :
        IProjectStore,
        IProjectCommitGuardedStore
    {
        public TaskCompletionSource CommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default) => inner.OpenAsync(projectFilePath, cancellationToken);

        public async Task SaveAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            _ = await SaveIfAsync(project, location, CommitUnconditionally, cancellationToken).ConfigureAwait(false);
        }

        public Task<bool> SaveIfAsync(VideoProject project, ProjectLocation location, Func<Action, bool> tryCommit,
            CancellationToken cancellationToken = default) => inner.SaveIfAsync(
            project,
            location,
            commit => tryCommit(() =>
            {
                CommitEntered.TrySetResult();
                ReleaseCommit.Task.GetAwaiter().GetResult();
                commit();
            }),
            cancellationToken);
    }

    private sealed class OrderedOpenProjectStore(string root) : IProjectStore
    {
        private int _openCount;

        public TaskCompletionSource FirstOpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondOpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecondOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default)
        {
            var sequence = Interlocked.Increment(ref _openCount);
            var started = sequence == 1 ? FirstOpenStarted : SecondOpenStarted;
            var release = sequence == 1 ? ReleaseFirstOpen : ReleaseSecondOpen;
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            var name = Path.GetFileNameWithoutExtension(projectFilePath);
            return (new VideoProject { Name = name }, new ProjectLocation(root, projectFilePath));
        }

        public Task SaveAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class BlockingFirstDiscardRecoveryStore(PortableProjectStore inner) :
        IProjectRecoveryStore,
        IProjectRecoveryCommitGuardedStore
    {
        private int _discardCount;

        public TaskCompletionSource FirstDiscardStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstDiscard { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.ProbeAsync(location, cancellationToken);

        public Task WriteAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.WriteAsync(project, location, cancellationToken);

        public Task<bool> WriteIfAsync(VideoProject project, ProjectLocation location, Func<Action, bool> tryCommit,
            CancellationToken cancellationToken = default) =>
            inner.WriteIfAsync(project, location, tryCommit, cancellationToken);

        public async Task DiscardAsync(ProjectLocation location, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _discardCount) == 1)
            {
                FirstDiscardStarted.TrySetResult();
                await ReleaseFirstDiscard.Task.WaitAsync(cancellationToken);
            }
            await inner.DiscardAsync(location, cancellationToken);
        }
    }

    private sealed class ObservingSaveStore(PortableProjectStore inner, Action observed) : IProjectStore
    {
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default) => inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            observed();
            return inner.SaveAsync(project, location, cancellationToken);
        }
    }

    private sealed class ObservingRecoveryStore(PortableProjectStore inner, Action observed) : IProjectRecoveryStore
    {
        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.ProbeAsync(location, cancellationToken);

        public Task WriteAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            observed();
            return inner.WriteAsync(project, location, cancellationToken);
        }

        public Task DiscardAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.DiscardAsync(location, cancellationToken);
    }

    private sealed class BlockingRecoveryStore(PortableProjectStore inner) :
        IProjectRecoveryStore,
        IProjectRecoveryCommitGuardedStore
    {
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ProjectRecoveryProbe> ProbeAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.ProbeAsync(location, cancellationToken);

        public async Task WriteAsync(VideoProject project, ProjectLocation location,
            CancellationToken cancellationToken = default)
        {
            _ = await WriteIfAsync(
                project,
                location,
                static commit =>
                {
                    commit();
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> WriteIfAsync(
            VideoProject project,
            ProjectLocation location,
            Func<Action, bool> tryCommit,
            CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            await ReleaseWrite.Task.WaitAsync(cancellationToken);
            return await inner.WriteIfAsync(project, location, tryCommit, cancellationToken);
        }

        public Task DiscardAsync(ProjectLocation location,
            CancellationToken cancellationToken = default) => inner.DiscardAsync(location, cancellationToken);
    }
}
