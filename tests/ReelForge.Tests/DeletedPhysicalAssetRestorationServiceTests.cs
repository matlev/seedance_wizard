using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class DeletedPhysicalAssetRestorationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReelForge restoration tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProbeReturnsEveryExactDeletedMatchWithoutSelectingOne()
    {
        var candidate = await WriteAsync("same bytes");
        var workspace = await CreateWorkspaceAsync();
        var first = await AddDeletedAsync(workspace, candidate, "first.mp4");
        var second = await AddDeletedAsync(workspace, candidate, "second.mp4");
        await workspace.SaveAsync();

        var probe = await Service(workspace).ProbeAsync(candidate, MediaType.Video);

        Assert.Equal(DeletedPhysicalAssetProbeStatus.Verified, probe.Status);
        Assert.Equal([first.Id, second.Id], probe.Matches.Select(match => match.AssetId));
        Assert.All(probe.Matches, match => Assert.False(match.DependencyReport.IsInUse));
        Assert.True(first.IsDeleted);
        Assert.True(second.IsDeleted);
    }

    [Fact]
    public async Task ProbeExcludesUnverifiedAndDifferentMediaTypes()
    {
        var candidate = await WriteAsync("same bytes");
        var workspace = await CreateWorkspaceAsync();
        var unverified = await AddDeletedAsync(workspace, candidate, "unverified.mp4");
        unverified.Physical!.ContentIdentity.Status = ContentHashStatus.Pending;
        await AddDeletedAsync(workspace, candidate, "audio.m4a", MediaType.Audio);

        var probe = await Service(workspace).ProbeAsync(candidate, MediaType.Video);

        Assert.Empty(probe.Matches);
    }

    [Fact]
    public async Task ProbeSkipsHashingWhenNoDeletedIdentityCanMatch()
    {
        var candidate = await WriteAsync("ordinary import");
        var workspace = await CreateWorkspaceAsync();

        var probe = await Service(workspace, new NeverHashService()).ProbeAsync(candidate, MediaType.Video);

        Assert.Equal(DeletedPhysicalAssetProbeStatus.NotApplicable, probe.Status);
        Assert.Empty(probe.Matches);
    }

    [Fact]
    public async Task ExternalRestorePreservesTombstoneIdentityAndHistoryAfterReopen()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        deleted.Provenance = new AssetProvenance { Operation = "original import", SourceAssetIds = [Guid.NewGuid()] };
        deleted.ProviderReferences["historic"] = new ProviderAssetReference { Value = "reference" };
        var derived = new ProjectAsset
        {
            FileName = "saved clip", DisplayName = "saved clip", MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual, Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        workspace.Project!.AddAsset(derived);
        workspace.Project.CommitRecipe(derived.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = deleted.Id }, Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 1 }
        });
        await workspace.SaveAsync();

        var result = await Service(workspace).RestoreExternalAsync(deleted.Id, candidate);

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Relink.Status);
        Assert.False(deleted.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Available, deleted.Physical!.Availability);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(deleted)));
        var reopened = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        var persisted = reopened.Project.Assets.Single(asset => asset.Id == deleted.Id);
        Assert.False(persisted.IsDeleted);
        Assert.Equal("original import", persisted.Provenance!.Operation);
        Assert.Equal("reference", persisted.ProviderReferences["historic"].Value);
        Assert.Equal(deleted.Id, Assert.IsType<TrimRecipe>(Assert.Single(reopened.Project.RecipeRevisions).Recipe).Source.AssetId);
    }

    [Fact]
    public async Task CancelledExternalRestoreLeavesTombstoneUnchanged()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        await workspace.SaveAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await Service(workspace).RestoreExternalAsync(deleted.Id, candidate, cancellation.Token);

        Assert.Equal(PhysicalAssetRelinkStatus.Cancelled, result.Relink.Status);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Missing, deleted.Physical!.Availability);
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(deleted)));
    }

    [Fact]
    public async Task MismatchedExternalBytesRefuseRestoration()
    {
        var expected = await WriteAsync("expected bytes");
        var different = await WriteAsync("different bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, expected, "deleted.mp4");
        await workspace.SaveAsync();

        var result = await Service(workspace).RestoreExternalAsync(deleted.Id, different);

        Assert.Equal(PhysicalAssetRelinkStatus.Mismatched, result.Relink.Status);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Missing, deleted.Physical!.Availability);
    }

    [Fact]
    public async Task OrdinaryRelinkCannotImplicitlyRestoreDeletedAsset()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var hash = new Sha256ContentHashService();
        var analyzer = new ProjectAssetDependencyAnalyzer();
        var relink = new PhysicalAssetRelinkService(
            workspace,
            hash,
            new PhysicalAssetRelinkStager(),
            analyzer);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => relink.RelinkAsync(deleted.Id, candidate));

        Assert.Contains("explicitly restored", exception.Message, StringComparison.Ordinal);
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task UnusedMatchingDonorIsFoldedIntoSelectedTombstone()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var donor = await AddActiveAsync(workspace, candidate, "donor.mp4");
        await workspace.SaveAsync();

        var result = await Service(workspace).RestoreFromActiveDonorAsync(deleted.Id, donor.Id);

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Relink.Status);
        Assert.True(result.DonorWasFolded);
        Assert.DoesNotContain(workspace.Project!.Assets, asset => asset.Id == donor.Id);
        Assert.False(deleted.IsDeleted);
        Assert.Equal("assets/videos/deleted.mp4", deleted.Physical!.RelativePath);
        Assert.True(File.Exists(workspace.GetAbsoluteAssetPath(deleted)));
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(donor)));
    }

    [Fact]
    public async Task FailedFoldCommitRestoresTombstoneAndDonor()
    {
        var candidate = await WriteAsync("matching bytes");
        var store = new ToggleFailingStore();
        var workspace = await CreateWorkspaceAsync(store);
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var donor = await AddActiveAsync(workspace, candidate, "donor.mp4");
        await workspace.SaveAsync();
        store.FailSaves = true;

        var result = await Service(workspace).RestoreFromActiveDonorAsync(deleted.Id, donor.Id);

        Assert.Equal(PhysicalAssetRelinkStatus.Failed, result.Relink.Status);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Missing, deleted.Physical!.Availability);
        Assert.Contains(workspace.Project!.Assets, asset => ReferenceEquals(asset, donor));
        Assert.Equal("assets/videos/donor.mp4", donor.Physical!.RelativePath);
    }

    [Fact]
    public async Task ChangedDonorDuringStagingIsRefusedWithoutMakingItsBytesAuthoritative()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var donor = await AddActiveAsync(workspace, candidate, "donor.mp4");
        await workspace.SaveAsync();
        var donorPath = workspace.GetAbsoluteAssetPath(donor);
        var stager = new CallbackStager(() => File.WriteAllTextAsync(donorPath, "replaced donor bytes"));

        var result = await Service(workspace, stager: stager)
            .RestoreFromActiveDonorAsync(deleted.Id, donor.Id);

        Assert.Equal(PhysicalAssetRelinkStatus.Mismatched, result.Relink.Status);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(PhysicalAssetAvailability.Missing, deleted.Physical!.Availability);
        Assert.Contains(workspace.Project!.Assets, asset => asset.Id == donor.Id);
        Assert.Equal("replaced donor bytes", await File.ReadAllTextAsync(donorPath));
        Assert.False(File.Exists(workspace.GetAbsoluteAssetPath(deleted)));
    }

    [Fact]
    public async Task FoldRetiresOnlyTheVerifiedDonorBytes()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var donor = await AddActiveAsync(workspace, candidate, "donor.mp4");
        await workspace.SaveAsync();
        var donorPath = workspace.GetAbsoluteAssetPath(donor);
        var stager = new AfterStageCallbackStager(() => File.WriteAllTextAsync(donorPath, "new donor bytes"));

        var result = await Service(workspace, stager: stager)
            .RestoreFromActiveDonorAsync(deleted.Id, donor.Id);

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Relink.Status);
        Assert.True(result.DonorWasFolded);
        Assert.False(deleted.IsDeleted);
        Assert.Equal("matching bytes", await File.ReadAllTextAsync(workspace.GetAbsoluteAssetPath(deleted)));
        Assert.True(File.Exists(donorPath));
        Assert.Equal("new donor bytes", await File.ReadAllTextAsync(donorPath));
    }

    [Fact]
    public async Task MissingOrCancelledDonorRestoreReturnsARecoverableStatusWithoutMutation()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var donor = await AddActiveAsync(workspace, candidate, "donor.mp4");
        await workspace.SaveAsync();
        File.Delete(workspace.GetAbsoluteAssetPath(donor));

        var missing = await Service(workspace).RestoreFromActiveDonorAsync(deleted.Id, donor.Id);

        Assert.Equal(PhysicalAssetRelinkStatus.Missing, missing.Relink.Status);
        Assert.True(deleted.IsDeleted);
        Assert.Contains(workspace.Project!.Assets, asset => asset.Id == donor.Id);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await Service(workspace).RestoreFromActiveDonorAsync(deleted.Id, donor.Id, cancellation.Token);

        Assert.Equal(PhysicalAssetRelinkStatus.Cancelled, cancelled.Relink.Status);
        Assert.True(deleted.IsDeleted);
        Assert.Contains(workspace.Project!.Assets, asset => asset.Id == donor.Id);
    }

    [Fact]
    public async Task ReferencedMatchingDonorIsRetainedAndCopiedToRestoreTombstone()
    {
        var candidate = await WriteAsync("matching bytes");
        var workspace = await CreateWorkspaceAsync();
        var deleted = await AddDeletedAsync(workspace, candidate, "deleted.mp4");
        var donor = await AddActiveAsync(workspace, candidate, "donor.mp4");
        var derived = new ProjectAsset { FileName = "saved", MediaType = MediaType.Video, StorageKind = AssetStorageKind.Virtual,
            Physical = null, Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip } };
        workspace.Project!.AddAsset(derived);
        workspace.Project.CommitRecipe(derived.Id, new TrimRecipe { Source = new AssetRevisionReference { AssetId = donor.Id },
            Start = RecipeBoundary.SourceStart, End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 1 } });
        await workspace.SaveAsync();

        var result = await Service(workspace).RestoreFromActiveDonorAsync(deleted.Id, donor.Id);

        Assert.Equal(PhysicalAssetRelinkStatus.Verified, result.Relink.Status);
        Assert.False(result.DonorWasFolded);
        Assert.Contains(workspace.Project.Assets, asset => asset.Id == donor.Id);
        Assert.False(deleted.IsDeleted);
        Assert.NotEqual(donor.Physical!.RelativePath, deleted.Physical!.RelativePath);
    }

    private static DeletedPhysicalAssetRestorationService Service(
        ProjectWorkspace workspace,
        IContentHashService? hashService = null,
        IPhysicalAssetRelinkStager? stager = null)
    {
        var hash = hashService ?? new Sha256ContentHashService();
        var analyzer = new ProjectAssetDependencyAnalyzer();
        stager ??= new PhysicalAssetRelinkStager();
        return new DeletedPhysicalAssetRestorationService(workspace, hash,
            new PhysicalAssetRelinkService(workspace, hash, stager, analyzer), stager, analyzer);
    }

    private Task<ProjectWorkspace> CreateWorkspaceAsync() => CreateWorkspaceAsync(new PortableProjectStore());

    private async Task<ProjectWorkspace> CreateWorkspaceAsync(IProjectStore store)
    {
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.CreateAsync(Path.Combine(_root, "project"), "Restoration");
        return workspace;
    }

    private static async Task<ProjectAsset> AddDeletedAsync(ProjectWorkspace workspace, string candidate, string name, MediaType type = MediaType.Video)
    {
        var identity = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset { FileName = name, DisplayName = name, MediaType = type, IsDeleted = true,
            Physical = new PhysicalAssetStorage { RelativePath = $"assets/{(type == MediaType.Audio ? "audio" : "videos")}/{name}",
                Availability = PhysicalAssetAvailability.Missing, ContentIdentity = identity } };
        workspace.Project!.AddAsset(asset);
        return asset;
    }

    private static async Task<ProjectAsset> AddActiveAsync(ProjectWorkspace workspace, string candidate, string name)
    {
        var identity = await new Sha256ContentHashService().ComputeAsync(candidate);
        var asset = new ProjectAsset { FileName = name, DisplayName = name, MediaType = MediaType.Video,
            Physical = new PhysicalAssetStorage { RelativePath = $"assets/videos/{name}", Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = identity } };
        workspace.Project!.AddAsset(asset);
        var path = workspace.GetAbsoluteAssetPath(asset);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(candidate, path);
        return asset;
    }

    private async Task<string> WriteAsync(string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.mp4");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }

    private sealed class ToggleFailingStore : IProjectStore
    {
        private readonly PortableProjectStore _inner = new();
        public bool FailSaves { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name,
            CancellationToken cancellationToken = default) => _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath,
            CancellationToken cancellationToken = default) => _inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            if (FailSaves) throw new IOException("Injected project save failure.");
            return _inner.SaveAsync(project, location, cancellationToken);
        }
    }

    private sealed class NeverHashService : IContentHashService
    {
        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The candidate should not be hashed without an eligible tombstone.");

        public Task<ContentVerificationResult> VerifyAsync(
            string path,
            ContentIdentity expected,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The candidate should not be verified without an eligible tombstone.");
    }

    private sealed class CallbackStager(Func<Task> beforeStageAsync) : IPhysicalAssetRelinkStager
    {
        private readonly PhysicalAssetRelinkStager _inner = new();

        public async Task<StagedPhysicalAssetRelink> StageAsync(
            ProjectLocation location,
            ProjectAsset asset,
            string candidatePath,
            CancellationToken cancellationToken = default)
        {
            await beforeStageAsync().ConfigureAwait(false);
            return await _inner.StageAsync(location, asset, candidatePath, cancellationToken).ConfigureAwait(false);
        }

        public Task DiscardAsync(StagedPhysicalAssetRelink staged, CancellationToken cancellationToken = default) =>
            _inner.DiscardAsync(staged, cancellationToken);
    }

    private sealed class AfterStageCallbackStager(Func<Task> afterStageAsync) : IPhysicalAssetRelinkStager
    {
        private readonly PhysicalAssetRelinkStager _inner = new();

        public async Task<StagedPhysicalAssetRelink> StageAsync(
            ProjectLocation location,
            ProjectAsset asset,
            string candidatePath,
            CancellationToken cancellationToken = default)
        {
            var staged = await _inner.StageAsync(location, asset, candidatePath, cancellationToken).ConfigureAwait(false);
            await afterStageAsync().ConfigureAwait(false);
            return staged;
        }

        public Task DiscardAsync(StagedPhysicalAssetRelink staged, CancellationToken cancellationToken = default) =>
            _inner.DiscardAsync(staged, cancellationToken);
    }
}
