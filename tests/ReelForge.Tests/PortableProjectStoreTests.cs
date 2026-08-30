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
            ModelVersion = "bytedance/seedance-2.5",
            Prompt = "Editable prompt",
            Mode = GenerationMode.VideoEdit,
            DurationSeconds = -1,
            AspectRatio = "adaptive",
            ParentGenerationId = parentGenerationId,
            RelationshipType = GenerationRelationshipType.VariantOf
        };
        project.Generations.Add(new GenerationRecord
        {
            Id = parentGenerationId,
            Status = GenerationStatus.Succeeded,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "atlascloud",
                ModelVersion = "bytedance/seedance-2.5",
                Prompt = "Change the lighting while preserving the source geometry",
                Mode = GenerationMode.VideoEdit,
                DurationSeconds = -1,
                AspectRatio = "adaptive",
                Resolution = "720p"
            }
        });

        await store.SaveAsync(project, location);
        var (reopened, reopenedLocation) = await store.OpenAsync(location.ProjectFilePath);

        Assert.Equal(project.Id, reopened.Id);
        Assert.Equal("Portable demo", reopened.Name);
        Assert.Equal("clip one.mp4", Assert.Single(reopened.Assets).FileName);
        Assert.Equal(ContentHashStatus.Verified, reopened.Assets[0].Physical?.ContentIdentity.Status);
        var reopenedGeneration = Assert.Single(reopened.Generations).RequestSnapshot;
        Assert.Equal("Change the lighting while preserving the source geometry", reopenedGeneration.Prompt);
        Assert.Equal(GenerationMode.VideoEdit, reopenedGeneration.Mode);
        Assert.Equal(-1, reopenedGeneration.DurationSeconds);
        Assert.Equal("adaptive", reopenedGeneration.AspectRatio);
        Assert.Equal("Editable prompt", reopened.CurrentGenerationDraft?.Prompt);
        Assert.Equal(GenerationMode.VideoEdit, reopened.CurrentGenerationDraft?.Mode);
        Assert.Equal(-1, reopened.CurrentGenerationDraft?.DurationSeconds);
        Assert.Equal("adaptive", reopened.CurrentGenerationDraft?.AspectRatio);
        Assert.Equal(parentGenerationId, reopened.CurrentGenerationDraft?.ParentGenerationId);
        Assert.Equal(GenerationRelationshipType.VariantOf, reopened.CurrentGenerationDraft?.RelationshipType);
        Assert.Equal(Path.GetFullPath(_temporaryRoot), reopenedLocation.RootDirectory);
        Assert.Equal("Portable demo.rfp", Path.GetFileName(reopenedLocation.ProjectFilePath));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(location.ProjectFilePath));
        Assert.Equal(5, json.RootElement.GetProperty("formatVersion").GetInt32());
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
    public async Task PreviousDevelopmentFormatIsRejectedClearly()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var projectPath = Path.Combine(_temporaryRoot, "Version two.rfp");
        await File.WriteAllTextAsync(projectPath, """{"formatVersion":2,"name":"old"}""");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PortableProjectStore().OpenAsync(projectPath));

        Assert.Contains("unsupported development format 2", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("requires format 5", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoveryPayloadUsesTheCurrentNestedProjectFormat()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Recovery format");
        project.Name = "Unsaved title";

        await store.WriteAsync(project, location);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(PortableProjectStore.GetRecoveryFilePath(location)));
        Assert.Equal(5, json.RootElement.GetProperty("project").GetProperty("formatVersion").GetInt32());
    }

    [Fact]
    public async Task CurrentFormatRoundTripPreservesImmutableAnchorRevisionsAndReferenceOccurrences()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Saved frames");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        source.Encoding = new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 0 },
            Audio = new AudioStreamMetadata { StreamIndex = 0 }
        };
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
    public async Task SavedClipDeclaresConservativeTranscodedOutputMetadata()
    {
        var workspace = new ProjectWorkspace(new PortableProjectStore(), new UnusedImporter());
        await workspace.CreateAsync(_temporaryRoot, "Clip metadata");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        source.DurationSeconds = 12;
        source.Encoding = new MediaEncodingMetadata
        {
            ContainerFormat = "matroska,webm",
            DurationSeconds = 12,
            SizeBytes = 1234,
            BitRate = 5678,
            Video = new VideoStreamMetadata { Codec = "h264", Width = 1920, Height = 1080, FrameRate = "24/1" },
            Audio = new AudioStreamMetadata { Codec = "aac", SampleRate = 48000, Channels = 2, ChannelLayout = "stereo" }
        };
        workspace.Project!.AddAsset(source);
        await workspace.SaveAsync();
        var position = new ExactFramePosition(source.Id, new string('a', 64), 0, 96, 1, 24, 96);

        var clip = await new SavedClipService(workspace).CreateAsync(
            "Favorite moment",
            source.Id,
            ClipBoundarySelection.AtFrame(position, AnchorBoundaryEdge.BeforeFrame),
            ClipBoundarySelection.SourceEnd);

        var expected = clip.Virtual!.ExpectedMediaProperties!;
        Assert.Equal("mp4", expected.ContainerFormat);
        Assert.Equal(8, expected.DurationSeconds);
        Assert.Null(expected.SizeBytes);
        Assert.Null(expected.BitRate);
        Assert.Equal("h264", expected.Video!.Codec);
        Assert.Equal(1920, expected.Video.Width);
        Assert.Equal(1080, expected.Video.Height);
        Assert.Equal("24/1", expected.Video.FrameRate);
        Assert.Null(expected.Video.PixelFormat);
        Assert.Null(expected.Video.CodecProfile);
        Assert.Null(expected.Video.TimeBase);
        Assert.Null(expected.Video.CodecLevel);
        Assert.Equal("aac", expected.Audio!.Codec);
        Assert.Null(expected.Audio.SampleRate);
        Assert.Null(expected.Audio.Channels);
        Assert.Null(expected.Audio.ChannelLayout);
        Assert.NotSame(source.Encoding, expected);
        Assert.NotSame(source.Encoding!.Video, expected.Video);
        Assert.NotSame(source.Encoding.Audio, expected.Audio);
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
    public async Task CandidateFormatRoundTripPreservesMultitrackIdentityControlsAndTimingPins()
    {
        var store = new PortableProjectStore();
        var (project, location) = await store.CreateAsync(_temporaryRoot, "Candidate composition");
        var source = CreatePhysicalAsset("source.mp4", "assets/videos/source.mp4");
        source.Encoding = new MediaEncodingMetadata
        {
            Video = new VideoStreamMetadata { StreamIndex = 0 },
            Audio = new AudioStreamMetadata { StreamIndex = 0 }
        };
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            FileName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Origin = AssetOrigin.EditorDerived,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(source);
        project.AddAsset(composition);
        project.WorkingCompositionAssetId = composition.Id;
        var videoTrackId = Guid.NewGuid();
        var emptyVideoTrackId = Guid.NewGuid();
        var emptyAudioTrackId = Guid.NewGuid();
        var audioTrackId = Guid.NewGuid();
        var videoItemId = Guid.NewGuid();
        var audioItemId = Guid.NewGuid();
        var linkGroupId = Guid.NewGuid();
        var sourceReference = new AssetRevisionReference { AssetId = source.Id };
        project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState(
            [
                new CompositionVideoTrack(videoTrackId, isLocked: true, isVisible: false,
                [
                    new CompositionVideoItem(videoItemId, sourceReference, 0,
                        new VideoSourceRange(new VideoPresentationTime(90_000, 1, 90_000), new VideoPresentationTime(270_000, 1, 90_000)),
                        new StreamTimingAssessmentPin(new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Video, 0, TimingReadiness.Exact, true, new ExactTime(2, 1), [], new ExactTime(0, 1))),
                        new ExactTime(1, 2), linkGroupId)
                ], name: "Primary video"),
                new CompositionVideoTrack(emptyVideoTrackId, isLocked: false, isVisible: true, [], name: "Video B-roll")
            ],
            [
                new CompositionAudioTrack(emptyAudioTrackId, isLocked: true, isMuted: false, [], name: "Room tone"),
                new CompositionAudioTrack(audioTrackId, isLocked: false, isMuted: true,
                [
                    new CompositionAudioItem(audioItemId, sourceReference, 0, null,
                        new StreamTimingAssessmentPin(new StreamTimingAssessment(Guid.NewGuid(), new string('a', 64), MediaType.Audio, 0, TimingReadiness.Estimated, true, new ExactTime(2, 1), [TimingIssueClassification.DiscontinuousTimestamps], new ExactTime(0, 1))),
                        new ExactTime(3, 4), linkGroupId, true, -6, 0.25, new ExactTime(1, 4), new ExactTime(1, 2))
                ], name: "Dialogue")
            ])
        });
        await store.SaveAsync(project, location);
        var (reopened, _) = await store.OpenAsync(location.ProjectFilePath);
        var state = Assert.IsType<CompositionRecipe>(Assert.Single(reopened.RecipeRevisions).Recipe).Composition;
        Assert.Equal([videoTrackId, emptyVideoTrackId], state.VideoTracks.Select(track => track.Id));
        Assert.Equal([emptyAudioTrackId, audioTrackId], state.AudioTracks.Select(track => track.Id));
        Assert.Equal(["Primary video", "Video B-roll"], state.VideoTracks.Select(track => track.Name));
        Assert.Equal(["Room tone", "Dialogue"], state.AudioTracks.Select(track => track.Name));
        Assert.False(state.VideoTracks[0].IsVisible);
        Assert.True(state.VideoTracks[0].IsLocked);
        Assert.True(state.VideoTracks[1].IsVisible);
        Assert.Empty(state.VideoTracks[1].Items);
        Assert.True(state.AudioTracks[0].IsLocked);
        Assert.False(state.AudioTracks[0].IsMuted);
        Assert.Empty(state.AudioTracks[0].Items);
        Assert.True(state.AudioTracks[1].IsMuted);
        var video = Assert.Single(state.VideoTracks[0].Items);
        Assert.Equal(videoItemId, video.Id);
        Assert.Equal(linkGroupId, video.LinkGroupId);
        Assert.Equal(new ExactTime(1, 2), video.CompositionStart);
        Assert.Equal(new ExactTime(2, 1), video.SourceRange!.Duration);
        var audio = Assert.Single(state.AudioTracks[1].Items);
        Assert.Equal(audioItemId, audio.Id);
        Assert.Equal(linkGroupId, audio.LinkGroupId);
        Assert.Equal(new ExactTime(3, 4), audio.CompositionStart);
        Assert.Null(audio.SourceRange);
        Assert.Equal(TimingReadiness.Estimated, audio.TimingAssessment.Readiness);
        Assert.Equal([TimingIssueClassification.DiscontinuousTimestamps], audio.TimingAssessment.IssueClassifications);
        Assert.Equal(0, audio.SelectedStreamIndex);
        Assert.True(audio.IsMuted);
        Assert.Equal(-6, audio.GainDecibels);
        Assert.Equal(0.25, audio.Pan);
        Assert.Equal(new ExactTime(1, 4), audio.FadeIn);
        Assert.Equal(new ExactTime(1, 2), audio.FadeOut);
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
