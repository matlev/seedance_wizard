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
    public async Task CreateSaveOpenRoundTripsSchemaTwoProject()
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

        Assert.Equal(2, project.SchemaVersion);
        Assert.NotNull(location.Migration);
        Assert.Equal(1, location.Migration.FromVersion);
        Assert.True(File.Exists(location.Migration.BackupPath));
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(location.Migration.BackupPath));
        using (var migratedJson = JsonDocument.Parse(await File.ReadAllTextAsync(projectPath)))
            Assert.Equal(2, migratedJson.RootElement.GetProperty("schemaVersion").GetInt32());

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
