using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class CompositionSegmentSplitServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ReelForge-split-{Guid.NewGuid():N}");

    [Fact]
    public async Task SplitsPhysicalSegmentAtNearestDecodedFrameAndPersistsExactBoundary()
    {
        var workspace = await CreateWorkspaceAsync();
        var sourceHash = new string('a', 64);
        var source = AddPhysicalVideo(workspace.Project!, "source.mp4", sourceHash, 10);
        var composition = await new WorkingCompositionService(workspace).CreateInitialAsync(source.Id);
        var initialSegment = new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single();
        await new WorkingCompositionService(workspace).SetSegmentAudioEnabledAsync(initialSegment.Id, false);
        var originalRevision = composition.Virtual!.CurrentRecipeRevisionId;
        var originalSegment = new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single();
        var frames = new StubExactFrameService([
            new VideoPresentationFrame(0, 95, 1, 25, 95),
            new VideoPresentationFrame(0, 100, 1, 25, 100),
            new VideoPresentationFrame(0, 105, 1, 25, 105)
        ]);
        var service = new CompositionSegmentSplitService(
            workspace,
            new StubMaterializer(Path.Combine(_root, "source.mp4"), sourceHash, 10),
            frames);

        var result = await service.SplitAsync(originalSegment.Id, TimeSpan.FromSeconds(4.02));

        var (_, revision, recipe) = new WorkingCompositionService(workspace).GetCurrent();
        Assert.NotEqual(originalRevision, revision.Id);
        Assert.Equal(2, recipe.Segments.Count);
        Assert.Equal(originalSegment.Id, recipe.Segments[0].Id);
        Assert.Equal(result.TrailingSegmentId, recipe.Segments[1].Id);
        Assert.Equal(result.LeadingClipAssetId, recipe.Segments[0].Source.AssetId);
        Assert.Equal(result.TrailingClipAssetId, recipe.Segments[1].Source.AssetId);
        Assert.All(recipe.Segments, segment =>
        {
            Assert.Equal(RecipeBoundaryKind.SourceStart, segment.Start.Kind);
            Assert.Equal(RecipeBoundaryKind.SourceEnd, segment.End.Kind);
            Assert.False(segment.AudioEnabled);
        });
        var splitClips = workspace.Project!.Assets
            .Where(asset => asset.Virtual?.Kind == VirtualAssetKind.SavedClip)
            .ToArray();
        Assert.Equal(2, splitClips.Length);
        Assert.Equal(2, splitClips.Select(asset => asset.DisplayName).Distinct().Count());
        var leadingRecipe = Assert.IsType<TrimRecipe>(workspace.Project.RecipeRevisions.Single(revision =>
            revision.Id == recipe.Segments[0].Source.RecipeRevisionId).Recipe);
        var trailingRecipe = Assert.IsType<TrimRecipe>(workspace.Project.RecipeRevisions.Single(revision =>
            revision.Id == recipe.Segments[1].Source.RecipeRevisionId).Recipe);
        Assert.Equal(leadingRecipe.End, trailingRecipe.Start);
        Assert.Equal(AnchorBoundaryEdge.BeforeFrame, leadingRecipe.End.Edge);
        var anchor = Assert.Single(workspace.Project!.Anchors);
        Assert.True(anchor.IsArchived);
        var anchorRevision = Assert.Single(workspace.Project.AnchorRevisions);
        Assert.Equal(source.Id, anchorRevision.SourceAssetId);
        Assert.Null(anchorRevision.SourceRecipeRevisionId);
        Assert.Equal(100, anchorRevision.PresentationTimestamp);
        Assert.Equal(4, splitClips.Single(asset => asset.Id == result.LeadingClipAssetId)
            .Virtual!.ExpectedMediaProperties!.DurationSeconds!.Value, precision: 6);
        Assert.Equal(6, splitClips.Single(asset => asset.Id == result.TrailingClipAssetId)
            .Virtual!.ExpectedMediaProperties!.DurationSeconds!.Value, precision: 6);
        Assert.Empty(ProjectInvariantValidator.Validate(workspace.Project));

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        Assert.Equal(2, ((CompositionRecipe)reopened.RecipeRevisions.Single(candidate =>
            candidate.Id == reopened.Assets.Single(asset => asset.Id == composition.Id).Virtual!.CurrentRecipeRevisionId).Recipe)
            .Segments.Count);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task SplitsPinnedSavedClipWithoutBakingItIntoPhysicalProjectMedia()
    {
        var workspace = await CreateWorkspaceAsync();
        var project = workspace.Project!;
        var physical = AddPhysicalVideo(project, "source.mp4", new string('a', 64), 12);
        var clip = new ProjectAsset
        {
            DisplayName = "Saved clip",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata
                {
                    ContainerFormat = "matroska,webm",
                    DurationSeconds = 6,
                    Video = new VideoStreamMetadata { Codec = "h264", Width = 1920, Height = 1080 },
                    Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 }
                }
            }
        };
        project.AddAsset(clip);
        var clipRevision = project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = physical.Id }
        });
        await workspace.SaveAsync();
        await new WorkingCompositionService(workspace).CreateInitialAsync(clip.Id);
        var originalSegment = new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single();
        var renderedHash = new string('b', 64);
        var service = new CompositionSegmentSplitService(
            workspace,
            new StubMaterializer(Path.Combine(_root, "saved-clip.mp4"), renderedHash, 6),
            new StubExactFrameService([
                new VideoPresentationFrame(0, 48, 1, 24, 48),
                new VideoPresentationFrame(0, 49, 1, 24, 49)
            ]));

        var result = await service.SplitAsync(originalSegment.Id, TimeSpan.FromSeconds(2.03));

        var (_, _, recipe) = new WorkingCompositionService(workspace).GetCurrent();
        var leadingClipRevision = project.RecipeRevisions.Single(revision =>
            revision.Id == recipe.Segments[0].Source.RecipeRevisionId);
        var trailingClipRevision = project.RecipeRevisions.Single(revision =>
            revision.Id == recipe.Segments[1].Source.RecipeRevisionId);
        var leadingRecipe = Assert.IsType<TrimRecipe>(leadingClipRevision.Recipe);
        var trailingRecipe = Assert.IsType<TrimRecipe>(trailingClipRevision.Recipe);
        var boundaryRevisionId = leadingRecipe.End.Anchor!.AnchorRevisionId;
        var boundary = project.AnchorRevisions.Single(revision => revision.Id == boundaryRevisionId);
        Assert.Equal(clip.Id, boundary.SourceAssetId);
        Assert.Equal(clipRevision.Id, boundary.SourceRecipeRevisionId);
        Assert.Equal(renderedHash, boundary.SourceContentHash);
        Assert.Equal(clip.Id, leadingRecipe.Source.AssetId);
        Assert.Equal(clipRevision.Id, leadingRecipe.Source.RecipeRevisionId);
        Assert.Equal(clip.Id, trailingRecipe.Source.AssetId);
        Assert.Equal(clipRevision.Id, trailingRecipe.Source.RecipeRevisionId);
        Assert.Equal(result.LeadingClipAssetId, recipe.Segments[0].Source.AssetId);
        Assert.Equal(result.TrailingClipAssetId, recipe.Segments[1].Source.AssetId);
        var snappedCutSeconds = 49d / 24;
        var leadingProperties = project.Assets.Single(asset => asset.Id == result.LeadingClipAssetId)
            .Virtual!.ExpectedMediaProperties!;
        var trailingProperties = project.Assets.Single(asset => asset.Id == result.TrailingClipAssetId)
            .Virtual!.ExpectedMediaProperties!;
        Assert.Equal(snappedCutSeconds, leadingProperties.DurationSeconds!.Value, precision: 6);
        Assert.Equal(6 - snappedCutSeconds, trailingProperties.DurationSeconds!.Value, precision: 6);
        Assert.Equal("mp4", leadingProperties.ContainerFormat);
        Assert.Equal("h264", trailingProperties.Video!.Codec);
        Assert.Null(trailingProperties.Video.PixelFormat);
        Assert.Equal("aac", leadingProperties.Audio!.Codec);
        Assert.Null(leadingProperties.Audio.SampleRate);
        Assert.NotSame(clip.Virtual!.ExpectedMediaProperties, leadingProperties);
        Assert.NotSame(clip.Virtual.ExpectedMediaProperties!.Video, leadingProperties.Video);
        Assert.NotSame(clip.Virtual.ExpectedMediaProperties.Audio, trailingProperties.Audio);
        Assert.Equal(5, project.Assets.Count);
        Assert.DoesNotContain(project.Assets, asset => asset.Origin == AssetOrigin.Exported);
        Assert.Empty(ProjectInvariantValidator.Validate(project));

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var reopenedBoundary = reopened.AnchorRevisions.Single(revision => revision.Id == boundary.Id);
        Assert.Equal(clip.Id, reopenedBoundary.SourceAssetId);
        Assert.Equal(clipRevision.Id, reopenedBoundary.SourceRecipeRevisionId);
        Assert.Equal(renderedHash, reopenedBoundary.SourceContentHash);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task SplitAfterSelectedFrameIncludesThatFrameInLeadingClip()
    {
        var workspace = await CreateWorkspaceAsync();
        var sourceHash = new string('c', 64);
        var source = AddPhysicalVideo(workspace.Project!, "after-source.mp4", sourceHash, 10);
        await new WorkingCompositionService(workspace).CreateInitialAsync(source.Id);
        var originalSegment = new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single();
        var service = new CompositionSegmentSplitService(
            workspace,
            new StubMaterializer(Path.Combine(_root, "after-source.mp4"), sourceHash, 10),
            new StubExactFrameService([
                new VideoPresentationFrame(0, 95, 1, 25, 95),
                new VideoPresentationFrame(0, 100, 1, 25, 100),
                new VideoPresentationFrame(0, 105, 1, 25, 105)
            ]));

        var result = await service.SplitAsync(
            originalSegment.Id,
            TimeSpan.FromSeconds(4.02),
            AnchorBoundaryEdge.AfterFrame);

        var (_, _, recipe) = new WorkingCompositionService(workspace).GetCurrent();
        var leadingRecipe = Assert.IsType<TrimRecipe>(workspace.Project!.RecipeRevisions.Single(revision =>
            revision.Id == recipe.Segments[0].Source.RecipeRevisionId).Recipe);
        var trailingRecipe = Assert.IsType<TrimRecipe>(workspace.Project.RecipeRevisions.Single(revision =>
            revision.Id == recipe.Segments[1].Source.RecipeRevisionId).Recipe);
        Assert.Equal(AnchorBoundaryEdge.AfterFrame, leadingRecipe.End.Edge);
        Assert.Equal(leadingRecipe.End, trailingRecipe.Start);
        Assert.Equal(100, workspace.Project.AnchorRevisions.Single().PresentationTimestamp);
        Assert.Equal(4.2, workspace.Project.Assets.Single(asset => asset.Id == result.LeadingClipAssetId)
            .Virtual!.ExpectedMediaProperties!.DurationSeconds!.Value, precision: 6);
        Assert.Equal(5.8, workspace.Project.Assets.Single(asset => asset.Id == result.TrailingClipAssetId)
            .Virtual!.ExpectedMediaProperties!.DurationSeconds!.Value, precision: 6);
        Assert.Empty(ProjectInvariantValidator.Validate(workspace.Project));
    }

    [Fact]
    public async Task SplitClipsDeclareConservativeOutputMetadataAndUseExactChildDurations()
    {
        var workspace = await CreateWorkspaceAsync();
        var project = workspace.Project!;
        var source = AddPhysicalVideo(project, "metadata-source.mp4", new string('c', 64), 10);
        source.Encoding = new MediaEncodingMetadata
        {
            ContainerFormat = "mov,mp4,m4a,3gp,3g2,mj2",
            DurationSeconds = 10,
            Video = new VideoStreamMetadata { Codec = "h264", Width = 1920, Height = 1080 },
            Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2 }
        };
        await new WorkingCompositionService(workspace).CreateInitialAsync(source.Id);
        var segment = new WorkingCompositionService(workspace).GetCurrent().Recipe.Segments.Single();
        var result = await new CompositionSegmentSplitService(
                workspace,
                new StubMaterializer(Path.Combine(_root, "metadata-source.mp4"), new string('c', 64), 10),
                new StubExactFrameService([
                    new VideoPresentationFrame(0, 95, 1, 25, 95),
                    new VideoPresentationFrame(0, 100, 1, 25, 100),
                    new VideoPresentationFrame(0, 105, 1, 25, 105)
                ]))
            .SplitAsync(segment.Id, TimeSpan.FromSeconds(4.02));

        var leading = project.Assets.Single(asset => asset.Id == result.LeadingClipAssetId)
            .Virtual!.ExpectedMediaProperties!;
        var trailing = project.Assets.Single(asset => asset.Id == result.TrailingClipAssetId)
            .Virtual!.ExpectedMediaProperties!;
        Assert.Equal("mp4", leading.ContainerFormat);
        Assert.Equal(4, leading.DurationSeconds);
        Assert.Equal(6, trailing.DurationSeconds);
        Assert.Equal("h264", leading.Video!.Codec);
        Assert.Null(leading.Video.PixelFormat);
        Assert.Equal("aac", trailing.Audio!.Codec);
        Assert.Null(leading.Audio!.SampleRate);
        Assert.NotSame(source.Encoding, leading);
        Assert.NotSame(source.Encoding!.Video, leading.Video);
        Assert.NotSame(source.Encoding.Audio, trailing.Audio);

    }

    [Fact]
    public async Task SaveFailureRollsBackAllSplitMutationState()
    {
        var store = new ToggleFailingProjectStore();
        var workspace = await CreateWorkspaceAsync(store);
        var project = workspace.Project!;
        var source = AddPhysicalVideo(project, "rollback-source.mp4", new string('d', 64), 10);
        var compositionService = new WorkingCompositionService(workspace);
        var composition = await compositionService.CreateInitialAsync(source.Id);
        var before = compositionService.GetCurrent();
        var draft = Assert.Single(project.RecipeDrafts);
        var originalModifiedAt = project.ModifiedAt;
        var originalAssetCount = project.Assets.Count;
        var originalAnchorCount = project.Anchors.Count;
        var originalAnchorRevisionCount = project.AnchorRevisions.Count;
        var originalRecipeRevisionCount = project.RecipeRevisions.Count;
        var originalSources = composition.Provenance!.SourceAssetIds.ToArray();
        var originalDraftBasedOnRevisionId = draft.BasedOnRevisionId;
        var originalDraftRecipe = draft.EditableRecipe;
        var originalDraftModifiedAt = draft.ModifiedAt;
        store.FailSaves = true;
        var service = new CompositionSegmentSplitService(
            workspace,
            new StubMaterializer(Path.Combine(_root, "rollback-source.mp4"), new string('d', 64), 10),
            new StubExactFrameService([
                new VideoPresentationFrame(0, 95, 1, 25, 95),
                new VideoPresentationFrame(0, 100, 1, 25, 100),
                new VideoPresentationFrame(0, 105, 1, 25, 105)
            ]));

        await Assert.ThrowsAsync<IOException>(() =>
            service.SplitAsync(before.Recipe.Segments.Single().Id, TimeSpan.FromSeconds(4.02)));

        var after = compositionService.GetCurrent();
        Assert.Equal(originalModifiedAt, project.ModifiedAt);
        Assert.Equal(originalAssetCount, project.Assets.Count);
        Assert.Equal(originalAnchorCount, project.Anchors.Count);
        Assert.Equal(originalAnchorRevisionCount, project.AnchorRevisions.Count);
        Assert.Equal(originalRecipeRevisionCount, project.RecipeRevisions.Count);
        Assert.Same(before.Asset, after.Asset);
        Assert.Same(before.Revision, after.Revision);
        Assert.Same(before.Recipe, after.Recipe);
        Assert.Equal(originalDraftBasedOnRevisionId, draft.BasedOnRevisionId);
        Assert.Same(originalDraftRecipe, draft.EditableRecipe);
        Assert.Equal(originalDraftModifiedAt, draft.ModifiedAt);
        Assert.Equal(originalSources, composition.Provenance.SourceAssetIds);
        Assert.DoesNotContain(project.Assets, asset => asset.Virtual?.Kind == VirtualAssetKind.SavedClip);
    }

    private async Task<ProjectWorkspace> CreateWorkspaceAsync(IProjectStore? store = null)
    {
        Directory.CreateDirectory(_root);
        var workspace = new ProjectWorkspace(store ?? new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_root, "Split Tests");
        return workspace;
    }

    private ProjectAsset AddPhysicalVideo(VideoProject project, string fileName, string hash, double duration)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, [1, 2, 3]);
        var asset = new ProjectAsset
        {
            DisplayName = fileName,
            FileName = fileName,
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            DurationSeconds = duration,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = fileName,
                ContentIdentity = new ContentIdentity { Sha256 = hash, Status = ContentHashStatus.Verified }
            }
        };
        project.AddAsset(asset);
        return asset;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubMaterializer(string path, string hash, double duration) : IMediaMaterializer
    {
        public Task<MaterializedMediaLease> MaterializeAsync(
            VideoProject project,
            ProjectLocation location,
            MaterializationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MaterializedMediaLease(
                path,
                new ContentIdentity { Sha256 = hash, Status = ContentHashStatus.Verified },
                new MediaEncodingMetadata { DurationSeconds = duration },
                isDurableSource: false));
    }

    private sealed class StubExactFrameService(IReadOnlyList<VideoPresentationFrame> frames) : IExactVideoFrameService
    {
        public Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) => Task.FromResult(frames);

        public Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
            string mediaPath,
            double centerSeconds,
            double radiusSeconds = 2,
            CancellationToken cancellationToken = default) => Task.FromResult(frames);

        public Task<MaterializedMediaLease> ExtractAsync(
            string mediaPath,
            string sourceContentHash,
            FrameAnchorRevision revision,
            MaterializationPurpose purpose,
            string? profile = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ToggleFailingProjectStore : IProjectStore
    {
        private readonly PortableProjectStore _inner = new();

        public bool FailSaves { get; set; }

        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(
            string rootDirectory,
            string name,
            CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(rootDirectory, name, cancellationToken);

        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(
            string projectFilePath,
            CancellationToken cancellationToken = default) =>
            _inner.OpenAsync(projectFilePath, cancellationToken);

        public Task SaveAsync(
            VideoProject project,
            ProjectLocation location,
            CancellationToken cancellationToken = default) =>
            FailSaves
                ? Task.FromException(new IOException("Simulated project save failure."))
                : _inner.SaveAsync(project, location, cancellationToken);
    }
}
