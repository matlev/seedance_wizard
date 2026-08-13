using System.Text.Json;
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
    public async Task CreateSaveOpenRoundTripsSchemaThreeProject()
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

        Assert.Equal(VideoProject.CurrentSchemaVersion, reopened.SchemaVersion);
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
        Assert.Null(reopenedLocation.Migration);
        AssertProjectFoldersExist(location.ProjectFilePath);
    }

    [Fact]
    public async Task OpeningVersionOneCreatesBackupAndMigratesMetadataOnly()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var assetId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var projectPath = Path.Combine(_temporaryRoot, PortableProjectStore.LegacyProjectFileName);
        var legacyJson = $$"""
            {
              "schemaVersion": 1,
              "id": "{{Guid.NewGuid()}}",
              "name": "Legacy project",
              "createdAt": "2026-08-01T00:00:00+00:00",
              "modifiedAt": "2026-08-02T00:00:00+00:00",
              "mainVideoAssetId": "{{assetId}}",
              "assets": [{
                "id": "{{assetId}}",
                "fileName": "legacy.mp4",
                "relativePath": "assets/videos/legacy.mp4",
                "mediaType": "video",
                "origin": "generated",
                "createdAt": "2026-08-01T00:00:00+00:00",
                "providerReferences": { "atlascloud": "legacy-provider-ref" }
              }],
              "generations": [
                {
                  "id": "{{parentId}}",
                  "providerId": "legacy.provider",
                  "modelVersion": "legacy-v1",
                  "request": {
                    "prompt": "parent",
                    "mode": "textToVideo",
                    "durationSeconds": 4,
                    "aspectRatio": "16:9",
                    "resolution": "720p",
                    "referenceAssetIds": [],
                    "providerParameters": {}
                  },
                  "requestedAt": "2026-08-01T00:00:00+00:00",
                  "status": "succeeded",
                  "outputAssetId": "{{assetId}}",
                  "responseMetadata": {}
                },
                {
                  "id": "{{childId}}",
                  "providerId": "legacy.provider",
                  "modelVersion": "legacy-v1",
                  "request": {
                    "prompt": "child",
                    "mode": "referenceToVideo",
                    "durationSeconds": 4,
                    "aspectRatio": "16:9",
                    "resolution": "720p",
                    "referenceAssetIds": ["{{assetId}}"],
                    "providerParameters": {}
                  },
                  "requestedAt": "2026-08-02T00:00:00+00:00",
                  "status": "failed",
                  "responseMetadata": {},
                  "parentGenerationId": "{{parentId}}"
                }
              ],
              "timeline": { "clips": [] }
            }
            """;
        await File.WriteAllTextAsync(projectPath, legacyJson);

        var (project, location) = await new PortableProjectStore().OpenAsync(projectPath);

        Assert.Equal(VideoProject.CurrentSchemaVersion, project.SchemaVersion);
        Assert.NotNull(location.Migration);
        Assert.Equal(1, location.Migration.FromVersion);
        Assert.True(File.Exists(location.Migration.BackupPath));
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(location.Migration.BackupPath));
        using (var migratedJson = JsonDocument.Parse(await File.ReadAllTextAsync(projectPath)))
            Assert.Equal(VideoProject.CurrentSchemaVersion, migratedJson.RootElement.GetProperty("schemaVersion").GetInt32());

        var asset = Assert.Single(project.Assets);
        Assert.Equal(AssetStorageKind.Physical, asset.StorageKind);
        Assert.Equal(ContentHashStatus.Pending, asset.Physical?.ContentIdentity.Status);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical?.Availability);
        Assert.Equal("legacy-provider-ref", asset.ProviderReferences["atlascloud"].Value);
        Assert.Equal(assetId, Assert.Single(project.Generations[0].OutputAssetIds));
        Assert.Equal(parentId, project.Generations[1].ParentGenerationId);
        Assert.Equal(GenerationRelationshipType.BasedOn, project.Generations[1].RelationshipType);
        Assert.Equal(assetId, Assert.Single(project.Generations[1].RequestSnapshot.References).LogicalObjectId);
        Assert.Equal(parentId, asset.Provenance?.GenerationId);
    }

    [Fact]
    public async Task SchemaThreeRoundTripPreservesImmutableAnchorRevisionsAndReferenceOccurrences()
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
    public async Task OpeningVersionTwoCreatesLegacyAnchorRevisionWithoutInventingBoundaryEdge()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var sourceId = Guid.NewGuid();
        var virtualId = Guid.NewGuid();
        var anchorId = Guid.NewGuid();
        var recipeRevisionId = Guid.NewGuid();
        var generationId = Guid.NewGuid();
        var projectPath = Path.Combine(_temporaryRoot, "Legacy anchors.rfp");
        var legacyJson = $$"""
            {
              "schemaVersion": 2,
              "id": "{{Guid.NewGuid()}}",
              "name": "Legacy anchors",
              "createdAt": "2026-08-01T00:00:00+00:00",
              "modifiedAt": "2026-08-02T00:00:00+00:00",
              "assets": [
                {
                  "id": "{{sourceId}}",
                  "displayName": "source.mp4",
                  "fileName": "source.mp4",
                  "mediaType": "video",
                  "storageKind": "physical",
                  "origin": "imported",
                  "createdAt": "2026-08-01T00:00:00+00:00",
                  "physical": {
                    "relativePath": "assets/videos/source.mp4",
                    "durability": "source",
                    "contentIdentity": { "algorithm": "SHA-256", "sha256": "{{new string('c', 64)}}", "status": "verified" }
                  },
                  "providerReferences": {}
                },
                {
                  "id": "{{virtualId}}",
                  "displayName": "legacy trim",
                  "fileName": "legacy trim",
                  "mediaType": "video",
                  "storageKind": "virtual",
                  "origin": "editorDerived",
                  "createdAt": "2026-08-01T00:00:00+00:00",
                  "virtual": { "currentRecipeRevisionId": "{{recipeRevisionId}}" },
                  "providerReferences": {}
                }
              ],
              "recipeRevisions": [{
                "id": "{{recipeRevisionId}}",
                "virtualAssetId": "{{virtualId}}",
                "revisionNumber": 1,
                "createdAt": "2026-08-01T00:00:00+00:00",
                "recipe": {
                  "type": "trim",
                  "recipeSchemaVersion": 1,
                  "source": { "assetId": "{{sourceId}}" },
                  "start": { "kind": "sourceStart" },
                  "end": { "kind": "anchor", "anchorId": "{{anchorId}}" }
                }
              }],
              "anchors": [{
                "id": "{{anchorId}}",
                "assetId": "{{sourceId}}",
                "frameNumber": 38,
                "timestampSeconds": 1.25,
                "timeBase": "1/30",
                "label": "Legacy saved frame",
                "notes": "Preserve me"
              }],
              "currentGenerationDraft": {
                "prompt": "legacy draft",
                "mode": "referenceToVideo",
                "durationSeconds": 5,
                "aspectRatio": "16:9",
                "resolution": "720p",
                "references": [{ "objectKind": "frameAnchor", "logicalObjectId": "{{anchorId}}", "role": "startFrame" }],
                "providerParameters": {},
                "modifiedAt": "2026-08-02T00:00:00+00:00"
              },
              "generations": [{
                "id": "{{generationId}}",
                "requestSnapshot": {
                  "providerId": "legacy.provider",
                  "modelVersion": "legacy-v2",
                  "mode": "referenceToVideo",
                  "prompt": "legacy history",
                  "durationSeconds": 5,
                  "aspectRatio": "16:9",
                  "resolution": "720p",
                  "references": [{
                    "objectKind": "frameAnchor",
                    "logicalObjectId": "{{anchorId}}",
                    "contentHash": "{{new string('c', 64)}}",
                    "role": "startFrame"
                  }],
                  "providerParameters": {}
                },
                "requestedAt": "2026-08-02T00:00:00+00:00",
                "status": "failed",
                "ingestionStatus": "notRequired",
                "responseMetadata": {}
              }],
              "timeline": { "clips": [] }
            }
            """;
        await File.WriteAllTextAsync(projectPath, legacyJson);

        var (project, location) = await new PortableProjectStore().OpenAsync(projectPath);

        Assert.Equal(VideoProject.CurrentSchemaVersion, project.SchemaVersion);
        Assert.Equal(2, location.Migration?.FromVersion);
        Assert.True(File.Exists(location.Migration?.BackupPath));
        var anchor = Assert.Single(project.Anchors);
        var revision = Assert.Single(project.AnchorRevisions);
        Assert.Equal(anchorId, anchor.Id);
        Assert.Equal(revision.Id, anchor.CurrentRevisionId);
        Assert.Equal(AnchorTimingPrecision.LegacyTimestampSeconds, revision.TimingPrecision);
        Assert.Equal(1.25, revision.LegacyTimestampSeconds);
        Assert.Null(revision.PresentationTimestamp);
        var recipe = Assert.IsType<TrimRecipe>(Assert.Single(project.RecipeRevisions).Recipe);
        Assert.Equal(revision.Id, recipe.End.Anchor?.AnchorRevisionId);
        Assert.Equal(AnchorBoundaryEdge.LegacyUnspecified, recipe.End.Edge);
        Assert.NotEqual(Guid.Empty, Assert.Single(project.CurrentGenerationDraft?.References!).ReferenceId);
        var historicalReference = Assert.Single(Assert.Single(project.Generations).RequestSnapshot.References);
        Assert.NotEqual(Guid.Empty, historicalReference.ReferenceId);
        Assert.Equal(revision.Id, historicalReference.Anchor?.AnchorRevisionId);
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
    public void FirstGeneratedPhysicalVideoBecomesMainVideo()
    {
        var project = new VideoProject();
        var importedVideo = CreatePhysicalAsset("imported.mp4", "assets/videos/imported.mp4");
        var generatedVideo = CreatePhysicalAsset("generated.mp4", "generated/generated.mp4", AssetOrigin.Generated);

        project.AddAsset(importedVideo);
        project.AddAsset(generatedVideo);

        Assert.Equal(generatedVideo.Id, project.MainVideoAssetId);
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
            TimingPrecision = revision.TimingPrecision,
            PresentationTimestamp = revision.PresentationTimestamp,
            TimeBaseNumerator = revision.TimeBaseNumerator,
            TimeBaseDenominator = revision.TimeBaseDenominator,
            LegacyTimestampSeconds = revision.LegacyTimestampSeconds,
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

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
