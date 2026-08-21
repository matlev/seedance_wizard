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
    public async Task DeletingSavedClipRemovesItsRecipeAndPrivateBoundaries()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Delete clip");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        source.DurationSeconds = 12;
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        var service = new SavedClipService(workspace);
        var position = new ExactFramePosition(source.Id, new string('a', 64), 0, 450, 1, 100, 135);
        var clip = await service.CreateAsync(
            "Temporary clip",
            source.Id,
            ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.BeforeFrame),
            ClipBoundarySelection.SourceEnd);

        await service.DeleteAsync(clip.Id);

        Assert.Equal(source.Id, Assert.Single(workspace.Project.Assets).Id);
        Assert.Empty(workspace.Project.RecipeRevisions);
        Assert.Empty(workspace.Project.RecipeDrafts);
        Assert.Empty(workspace.Project.Anchors);
        Assert.Empty(workspace.Project.AnchorRevisions);
        var (reopened, _) = await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath);
        Assert.Equal(source.Id, Assert.Single(reopened.Assets).Id);
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

    [Fact]
    public async Task WorkingCompositionEditsCommitOrderedImmutableRevisions()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition editing");
        var first = CreatePhysicalAsset("first.mp4", "assets/videos/first.mp4");
        var second = CreatePhysicalAsset("second.mp4", "assets/videos/second.mp4");
        workspace.Project!.AddAsset(first);
        workspace.Project.AddAsset(second);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(first.Id);

        var added = await service.AddSegmentAsync(second.Id);
        var secondSegmentId = Assert.IsType<CompositionRecipe>(added.Recipe).Segments[1].Id;
        var moved = await service.MoveSegmentAsync(secondSegmentId, -1);

        var movedRecipe = Assert.IsType<CompositionRecipe>(moved.Recipe);
        Assert.Equal([second.Id, first.Id], movedRecipe.Segments.Select(segment => segment.Source.AssetId));
        var historicalInitial = Assert.IsType<CompositionRecipe>(workspace.Project.RecipeRevisions[0].Recipe);
        Assert.Equal(first.Id, Assert.Single(historicalInitial.Segments).Source.AssetId);
        Assert.Equal(3, moved.RevisionNumber);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var reopenedRevision = reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId);
        Assert.Equal(
            [second.Id, first.Id],
            Assert.IsType<CompositionRecipe>(reopenedRevision.Recipe).Segments.Select(segment => segment.Source.AssetId));
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionDirectReorderCommitsOnceAndPersistsFinalOrder()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition direct reorder");
        var first = CreatePhysicalAsset("first.mp4", "assets/videos/first.mp4");
        var second = CreatePhysicalAsset("second.mp4", "assets/videos/second.mp4");
        var third = CreatePhysicalAsset("third.mp4", "assets/videos/third.mp4");
        workspace.Project!.AddAsset(first);
        workspace.Project.AddAsset(second);
        workspace.Project.AddAsset(third);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(first.Id);
        await service.AddSegmentAsync(second.Id);
        var beforeReorder = await service.AddSegmentAsync(third.Id);
        var beforeRecipe = Assert.IsType<CompositionRecipe>(beforeReorder.Recipe);
        var firstSegmentId = beforeRecipe.Segments[0].Id;
        var revisionCount = workspace.Project.RecipeRevisions.Count;

        var reordered = await service.MoveSegmentToIndexAsync(firstSegmentId, 2);

        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);
        Assert.Equal(
            [second.Id, third.Id, first.Id],
            Assert.IsType<CompositionRecipe>(reordered.Recipe).Segments.Select(segment => segment.Source.AssetId));
        Assert.Equal(
            [first.Id, second.Id, third.Id],
            beforeRecipe.Segments.Select(segment => segment.Source.AssetId));

        var noOp = await service.MoveSegmentToIndexAsync(firstSegmentId, 2);
        Assert.Equal(reordered.Id, noOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var reopenedRevision = reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId);
        Assert.Equal(
            [second.Id, third.Id, first.Id],
            Assert.IsType<CompositionRecipe>(reopenedRevision.Recipe).Segments.Select(segment => segment.Source.AssetId));
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionSegmentAudioChangeCommitsOnceAndPersists()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition source audio");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        var composition = await service.CreateInitialAsync(source.Id);
        var initialRevision = workspace.Project.RecipeRevisions.Single();
        var segmentId = Assert.IsType<CompositionRecipe>(initialRevision.Recipe).Segments.Single().Id;
        var revisionCount = workspace.Project.RecipeRevisions.Count;

        var muted = await service.SetSegmentAudioEnabledAsync(segmentId, audioEnabled: false);

        Assert.False(Assert.IsType<CompositionRecipe>(muted.Recipe).Segments.Single().AudioEnabled);
        Assert.True(Assert.IsType<CompositionRecipe>(initialRevision.Recipe).Segments.Single().AudioEnabled);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var noOp = await service.SetSegmentAudioEnabledAsync(segmentId, audioEnabled: false);
        Assert.Equal(muted.Id, noOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var reopenedComposition = reopened.Assets.Single(asset => asset.Id == composition.Id);
        var reopenedRevision = reopened.RecipeRevisions.Single(revision =>
            revision.Id == reopenedComposition.Virtual!.CurrentRecipeRevisionId);
        Assert.False(Assert.IsType<CompositionRecipe>(reopenedRevision.Recipe).Segments.Single().AudioEnabled);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionRemoveRejectsDeletingItsLastSegment()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition minimum");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(source.Id);
        var current = service.GetCurrent();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RemoveSegmentAsync(Assert.Single(current.Recipe.Segments).Id));

        Assert.Contains("at least one", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(service.GetCurrent().Recipe.Segments);
    }

    [Fact]
    public async Task WorkingCompositionPersistsInsertedVideoAndTimedAudio()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition media tracks");
        var first = CreatePhysicalAsset("first.mp4", "assets/videos/first.mp4");
        var inserted = CreatePhysicalAsset("inserted.mp4", "assets/videos/inserted.mp4");
        var audio = CreatePhysicalAsset("music.wav", "assets/audio/music.wav");
        audio.MediaType = MediaType.Audio;
        workspace.Project!.AddAsset(first);
        workspace.Project.AddAsset(inserted);
        workspace.Project.AddAsset(audio);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(first.Id);

        await service.AddSegmentAsync(inserted.Id, insertionIndex: 0);
        var audioRevision = await service.AddAudioClipAsync(audio.Id, TimeSpan.FromSeconds(2.5));
        var audioClipId = Assert.Single(Assert.IsType<CompositionRecipe>(audioRevision.Recipe).AudioClips).Id;

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var recipe = Assert.IsType<CompositionRecipe>(reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId).Recipe);
        Assert.Equal([inserted.Id, first.Id], recipe.Segments.Select(segment => segment.Source.AssetId));
        var audioClip = Assert.Single(recipe.AudioClips);
        Assert.Equal(audio.Id, audioClip.Source.AssetId);
        Assert.Equal(TimeSpan.FromSeconds(2.5), audioClip.TimelineStart);

        var reopenedWorkspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await reopenedWorkspace.OpenAsync(workspace.Location.ProjectFilePath);
        await new WorkingCompositionService(reopenedWorkspace).RemoveItemAsync(audioClipId);
        Assert.Empty(new WorkingCompositionService(reopenedWorkspace).GetCurrent().Recipe.AudioClips);
    }

    [Fact]
    public async Task WorkingCompositionAudioMoveCommitsOnceAndPersists()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition audio move");
        var video = CreatePhysicalAsset("video.mp4", "assets/videos/video.mp4");
        var audio = CreatePhysicalAsset("music.wav", "assets/audio/music.wav");
        audio.MediaType = MediaType.Audio;
        workspace.Project!.AddAsset(video);
        workspace.Project.AddAsset(audio);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(video.Id);
        var added = await service.AddAudioClipAsync(audio.Id, TimeSpan.FromSeconds(1));
        var audioClipId = Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips).Id;
        var revisionCount = workspace.Project.RecipeRevisions.Count;

        var moved = await service.SetAudioClipTimelineStartAsync(audioClipId, TimeSpan.FromSeconds(4.25));

        Assert.Equal(
            TimeSpan.FromSeconds(4.25),
            Assert.Single(Assert.IsType<CompositionRecipe>(moved.Recipe).AudioClips).TimelineStart);
        Assert.Equal(
            TimeSpan.FromSeconds(1),
            Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips).TimelineStart);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var noOp = await service.SetAudioClipTimelineStartAsync(audioClipId, TimeSpan.FromSeconds(4.25));
        Assert.Equal(moved.Id, noOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var subMillisecondNoOp = await service.SetAudioClipTimelineStartAsync(
            audioClipId,
            TimeSpan.FromTicks(TimeSpan.FromSeconds(4.25).Ticks + 4_000));
        Assert.Equal(moved.Id, subMillisecondNoOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var recipe = Assert.IsType<CompositionRecipe>(reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId).Recipe);
        Assert.Equal(TimeSpan.FromSeconds(4.25), Assert.Single(recipe.AudioClips).TimelineStart);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionAudioMixChangeCommitsOnceAndPersists()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition audio mix");
        var video = CreatePhysicalAsset("video.mp4", "assets/videos/video.mp4");
        var audio = CreatePhysicalAsset("music.wav", "assets/audio/music.wav");
        audio.MediaType = MediaType.Audio;
        workspace.Project!.AddAsset(video);
        workspace.Project.AddAsset(audio);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(video.Id);
        var added = await service.AddAudioClipAsync(audio.Id, TimeSpan.Zero);
        var audioClipId = Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips).Id;
        var revisionCount = workspace.Project.RecipeRevisions.Count;

        var changed = await service.SetAudioClipMixAsync(audioClipId, isMuted: true, gainDecibels: -8);

        var changedClip = Assert.Single(Assert.IsType<CompositionRecipe>(changed.Recipe).AudioClips);
        Assert.True(changedClip.IsMuted);
        Assert.Equal(-8, changedClip.GainDecibels);
        var historicalClip = Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips);
        Assert.False(historicalClip.IsMuted);
        Assert.Equal(0, historicalClip.GainDecibels);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var noOp = await service.SetAudioClipMixAsync(audioClipId, isMuted: true, gainDecibels: -8);
        Assert.Equal(changed.Id, noOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var recipe = Assert.IsType<CompositionRecipe>(reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId).Recipe);
        var reopenedClip = Assert.Single(recipe.AudioClips);
        Assert.True(reopenedClip.IsMuted);
        Assert.Equal(-8, reopenedClip.GainDecibels);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionAudioFadesCommitOnceAndPersist()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition audio fades");
        var video = CreatePhysicalAsset("video.mp4", "assets/videos/video.mp4");
        var audio = CreatePhysicalAsset("music.wav", "assets/audio/music.wav");
        audio.MediaType = MediaType.Audio;
        audio.DurationSeconds = 12;
        workspace.Project!.AddAsset(video);
        workspace.Project.AddAsset(audio);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(video.Id);
        var added = await service.AddAudioClipAsync(audio.Id, TimeSpan.Zero);
        var audioClipId = Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips).Id;
        var revisionCount = workspace.Project.RecipeRevisions.Count;

        var faded = await service.SetAudioClipFadesAsync(
            audioClipId,
            TimeSpan.FromSeconds(1.2504),
            TimeSpan.FromSeconds(2.5));

        var fadedClip = Assert.Single(Assert.IsType<CompositionRecipe>(faded.Recipe).AudioClips);
        Assert.Equal(TimeSpan.FromSeconds(1.25), fadedClip.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(2.5), fadedClip.FadeOut);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);
        var historicalClip = Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips);
        Assert.Equal(TimeSpan.Zero, historicalClip.FadeIn);
        Assert.Equal(TimeSpan.Zero, historicalClip.FadeOut);

        var noOp = await service.SetAudioClipFadesAsync(
            audioClipId,
            TimeSpan.FromSeconds(1.25),
            TimeSpan.FromSeconds(2.5));
        Assert.Equal(faded.Id, noOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var recipe = Assert.IsType<CompositionRecipe>(reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId).Recipe);
        var reopenedClip = Assert.Single(recipe.AudioClips);
        Assert.Equal(TimeSpan.FromSeconds(1.25), reopenedClip.FadeIn);
        Assert.Equal(TimeSpan.FromSeconds(2.5), reopenedClip.FadeOut);
        Assert.Empty(ProjectInvariantValidator.Validate(reopened));
    }

    [Fact]
    public async Task WorkingCompositionAudioPanCommitsOnceAndPersists()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Composition audio pan");
        var video = CreatePhysicalAsset("video.mp4", "assets/videos/video.mp4");
        var audio = CreatePhysicalAsset("voice.wav", "assets/audio/voice.wav");
        audio.MediaType = MediaType.Audio;
        workspace.Project!.AddAsset(video);
        workspace.Project.AddAsset(audio);
        await workspace.SaveAsync();
        var service = new WorkingCompositionService(workspace);
        await service.CreateInitialAsync(video.Id);
        var added = await service.AddAudioClipAsync(audio.Id, TimeSpan.Zero);
        var audioClipId = Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips).Id;
        var revisionCount = workspace.Project.RecipeRevisions.Count;

        var panned = await service.SetAudioClipPanAsync(audioClipId, -0.504);

        var pannedClip = Assert.Single(Assert.IsType<CompositionRecipe>(panned.Recipe).AudioClips);
        Assert.Equal(-0.5, pannedClip.Pan);
        Assert.Equal(0, Assert.Single(Assert.IsType<CompositionRecipe>(added.Recipe).AudioClips).Pan);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);
        var noOp = await service.SetAudioClipPanAsync(audioClipId, -0.5);
        Assert.Equal(panned.Id, noOp.Id);
        Assert.Equal(revisionCount + 1, workspace.Project.RecipeRevisions.Count);

        var reopened = (await new PortableProjectStore().OpenAsync(workspace.Location!.ProjectFilePath)).Project;
        var composition = reopened.Assets.Single(asset => asset.Id == reopened.WorkingCompositionAssetId);
        var recipe = Assert.IsType<CompositionRecipe>(reopened.RecipeRevisions.Single(revision =>
            revision.Id == composition.Virtual!.CurrentRecipeRevisionId).Recipe);
        Assert.Equal(-0.5, Assert.Single(recipe.AudioClips).Pan);
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
