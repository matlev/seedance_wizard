using System.Text.Json;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PortableProjectStoreTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateSaveOpenRoundTripsCurrentProjectFormat()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Portable demo");
        project.AddAsset(CreatePhysicalAsset("clip one.mp4", "assets/videos/clip one.mp4"));
        var parentGenerationId = Guid.NewGuid();
        project.CurrentGenerationDraft = new GenerationDraft
        {
            ProviderId = "atlascloud",
            ModelVersion = "bytedance/seedance-2.5/text-to-video",
            Prompt = "Editable prompt",
            ParentGenerationId = parentGenerationId,
            RelationshipType = GenerationRelationshipType.VariantOf
        };
        project.Generations.Add(new GenerationRecord
        {
            Id = parentGenerationId,
            Status = GenerationStatus.Succeeded,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "fake.seedance",
                ModelVersion = "development-v1",
                Prompt = "A lantern drifting through fog",
                Mode = GenerationMode.TextToVideo,
                DurationSeconds = 15,
                AspectRatio = "16:9",
                Resolution = "720p"
            }
        });

        await store.SaveAsync(project, location);
        var (reopened, reopenedLocation) = await store.OpenAsync(location.ProjectFilePath);

        Assert.Equal(project.Id, reopened.Id);
        Assert.Equal("Portable demo", reopened.Name);
        Assert.Equal("clip one.mp4", Assert.Single(reopened.Assets).FileName);
        Assert.Equal(ContentHashStatus.Verified, reopened.Assets[0].Physical?.ContentIdentity.Status);
        Assert.Equal("A lantern drifting through fog", Assert.Single(reopened.Generations).RequestSnapshot.Prompt);
        Assert.Equal("Editable prompt", reopened.CurrentGenerationDraft?.Prompt);
        Assert.Equal(parentGenerationId, reopened.CurrentGenerationDraft?.ParentGenerationId);
        Assert.Equal(GenerationRelationshipType.VariantOf, reopened.CurrentGenerationDraft?.RelationshipType);
        Assert.Equal(Path.GetFullPath(_temporaryRoot), reopenedLocation.RootDirectory);
        Assert.Equal("Portable demo.rfp", Path.GetFileName(reopenedLocation.ProjectFilePath));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(location.ProjectFilePath));
        Assert.Equal(2, json.RootElement.GetProperty("formatVersion").GetInt32());
        AssertProjectFoldersExist(location.ProjectFilePath);
    }

    [Fact]
    public async Task ObsoleteDevelopmentFormatIsRejectedWithoutRewritingTheFile()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var projectPath = Path.Combine(_temporaryRoot, "Obsolete.rfp");
        const string obsolete = """{"schemaVersion":3,"name":"obsolete"}""";
        await File.WriteAllTextAsync(projectPath, obsolete);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PortableProjectStore().OpenAsync(projectPath));

        Assert.Contains("obsolete development format", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(obsolete, await File.ReadAllTextAsync(projectPath));
    }

    [Fact]
    public async Task CurrentFormatRoundTripPreservesImmutableAnchorRevisionsAndReferenceOccurrences()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Saved frames");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        project.AddAsset(source);
        var anchor = new FrameAnchor { DisplayLabel = "Hero glance" };
        project.Anchors.Add(anchor);
        var first = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id, new string('a', 64), 0, 90_000, 1, 90_000, 30));
        var second = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id, new string('a', 64), 0, 180_000, 1, 90_000, 60));
        var firstReferenceId = Guid.NewGuid();
        var secondReferenceId = Guid.NewGuid();
        project.Generations.Add(new GenerationRecord
        {
            Status = GenerationStatus.Failed,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "fake",
                ModelVersion = "fake-v1",
                Prompt = "Use the earlier saved frame twice",
                References = Array.AsReadOnly(new[]
                {
                    CreateAnchorSnapshot(firstReferenceId, anchor.Id, first, GenerationReferenceRole.StartFrame),
                    CreateAnchorSnapshot(secondReferenceId, anchor.Id, first, GenerationReferenceRole.Character)
                })
            }
        });

        await store.SaveAsync(project, location);
        var (reopened, _) = await store.OpenAsync(location.ProjectFilePath);

        Assert.Equal(second.Id, Assert.Single(reopened.Anchors).CurrentRevisionId);
        Assert.Equal(first.Id, reopened.AnchorRevisions.Single(revision => revision.RevisionNumber == 1).Id);
        Assert.Equal(first.Id, reopened.AnchorRevisions.Single(revision => revision.Id == second.Id).PreviousRevisionId);
        var references = Assert.Single(reopened.Generations).RequestSnapshot.References;
        Assert.Equal([firstReferenceId, secondReferenceId], references.Select(reference => reference.ReferenceId));
        Assert.All(references, reference => Assert.Equal(first.Id, reference.Anchor?.AnchorRevisionId));
    }

    [Fact]
    public async Task CommittedRecipeRevisionsRemainPinnedAcrossRoundTrip()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Recipe history");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        project.AddAsset(source);
        var virtualAsset = new ProjectAsset
        {
            DisplayName = "Trim",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Origin = AssetOrigin.EditorDerived,
            Physical = null,
            Virtual = new VirtualAssetState()
        };
        project.AddAsset(virtualAsset);
        var first = project.CommitRecipe(virtualAsset.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 4 }
        });
        var second = project.CommitRecipe(virtualAsset.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Start = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 1 },
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 4 }
        });
        project.Generations.Add(new GenerationRecord
        {
            Status = GenerationStatus.Failed,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "fake.seedance",
                ModelVersion = "development-v1",
                Prompt = "Use the original trim",
                Mode = GenerationMode.ReferenceToVideo,
                DurationSeconds = 4,
                AspectRatio = "16:9",
                Resolution = "720p",
                References = Array.AsReadOnly(new[]
                {
                    new GenerationReferenceSnapshot
                    {
                        ObjectKind = GenerationReferenceObjectKind.Asset,
                        LogicalObjectId = virtualAsset.Id,
                        RecipeRevisionId = first.Id,
                        Role = GenerationReferenceRole.Motion
                    }
                })
            }
        });

        await store.SaveAsync(project, location);
        var (reopened, _) = await store.OpenAsync(location.ProjectFilePath);

        Assert.Equal(second.Id, reopened.Assets.Single(asset => asset.Id == virtualAsset.Id).Virtual?.CurrentRecipeRevisionId);
        Assert.Equal(first.Id, reopened.Generations.Single().RequestSnapshot.References.Single().RecipeRevisionId);
        Assert.Equal(first.Id, reopened.RecipeRevisions.Single(revision => revision.Id == second.Id).PreviousRevisionId);
        var original = Assert.IsType<TrimRecipe>(reopened.RecipeRevisions.Single(revision => revision.Id == first.Id).Recipe);
        Assert.Equal(RecipeBoundaryKind.SourceStart, original.Start.Kind);
    }

    [Fact]
    public async Task SavedClipPersistsExactHiddenBoundariesWithoutCreatingSavedFrames()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Clip boundaries");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        source.DurationSeconds = 12;
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        var position = new ExactFramePosition(source.Id, new string('a', 64), 0, 450, 1, 100, 135);

        var clip = await new SavedClipService(workspace).CreateAsync(
            "Favorite moment",
            source.Id,
            ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.BeforeFrame),
            ClipBoundarySelection.SourceEnd);

        var hiddenAnchor = Assert.Single(workspace.Project.Anchors);
        Assert.True(hiddenAnchor.IsArchived);
        Assert.Equal(VirtualAssetKind.SavedClip, clip.Virtual?.Kind);
        Assert.Equal(7.5, clip.Virtual?.ExpectedMediaProperties?.DurationSeconds);
        var revision = Assert.Single(workspace.Project.RecipeRevisions);
        var recipe = Assert.IsType<TrimRecipe>(revision.Recipe);
        Assert.Equal(hiddenAnchor.Id, recipe.Start.Anchor?.AnchorId);
        Assert.Equal(AnchorBoundaryEdge.BeforeFrame, recipe.Start.Edge);
        Assert.Equal(RecipeBoundaryKind.SourceEnd, recipe.End.Kind);

        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(VirtualAssetKind.SavedClip, reopened.Assets.Single(asset => asset.Id == clip.Id).Virtual?.Kind);
        Assert.True(Assert.Single(reopened.Anchors).IsArchived);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionPersistsPinnedInitialSegmentWithoutLegacyTimelineState()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition shell");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();

        var composition = await new WorkingCompositionService(workspace).CreateInitialAsync(source.Id);

        Assert.Equal(composition.Id, workspace.Project.WorkingCompositionAssetId);
        Assert.Equal(VirtualAssetKind.Composition, composition.Virtual?.Kind);
        var revision = Assert.Single(workspace.Project.RecipeRevisions);
        var recipe = Assert.IsType<CompositionRecipe>(revision.Recipe);
        var segment = Assert.Single(recipe.Segments);
        Assert.Equal(source.Id, segment.Source.AssetId);
        Assert.Null(segment.Source.RecipeRevisionId);
        Assert.Equal(RecipeBoundaryKind.SourceStart, segment.Start.Kind);
        Assert.Equal(RecipeBoundaryKind.SourceEnd, segment.End.Kind);
        var draft = Assert.Single(workspace.Project.RecipeDrafts);
        Assert.Equal(revision.Id, draft.BasedOnRevisionId);
        Assert.NotSame(recipe, draft.EditableRecipe);

        var json = await File.ReadAllTextAsync(workspace.Location!.ProjectFilePath);
        Assert.DoesNotContain("timeline", json, StringComparison.OrdinalIgnoreCase);
        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location.ProjectFilePath);
        Assert.Equal(composition.Id, reopened.WorkingCompositionAssetId);
        Assert.IsType<CompositionRecipe>(Assert.Single(reopened.RecipeRevisions).Recipe);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    private static ProjectAsset CreatePhysicalAsset(
        string fileName,
        string relativePath,
        AssetOrigin origin = AssetOrigin.Imported) => new()
    {
        DisplayName = fileName,
        FileName = fileName,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Origin = origin,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = relativePath,
            Durability = origin == AssetOrigin.Generated ? PhysicalAssetDurability.Generated : PhysicalAssetDurability.Source,
            ContentIdentity = new ContentIdentity
            {
                Sha256 = new string('a', 64),
                Status = ContentHashStatus.Verified,
                LengthBytes = 42
            }
        }
    };

    private static GenerationReferenceSnapshot CreateAnchorSnapshot(
        Guid referenceId,
        Guid anchorId,
        FrameAnchorRevision revision,
        GenerationReferenceRole role) => new()
    {
        ReferenceId = referenceId,
        ObjectKind = GenerationReferenceObjectKind.FrameAnchor,
        LogicalObjectId = anchorId,
        ContentHash = revision.SourceContentHash,
        Role = role,
        Anchor = new FrameAnchorReferenceSnapshot
        {
            AnchorRevisionId = revision.Id,
            SourceAssetId = revision.SourceAssetId,
            SourceContentHash = revision.SourceContentHash,
            VideoStreamIndex = revision.VideoStreamIndex,
            PresentationTimestamp = revision.PresentationTimestamp,
            TimeBaseNumerator = revision.TimeBaseNumerator,
            TimeBaseDenominator = revision.TimeBaseDenominator,
            FrameNumber = revision.FrameNumber
        }
    };

    private void AssertProjectFoldersExist(string projectFilePath)
    {
        Assert.True(File.Exists(projectFilePath));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "assets", "images")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "assets", "videos")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "assets", "audio")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "generated")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "exports")));
        Assert.True(Directory.Exists(Path.Combine(_temporaryRoot, "cache")));
    }

    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(
            ProjectLocation location,
            IEnumerable<string> sourcePaths,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not import assets.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
