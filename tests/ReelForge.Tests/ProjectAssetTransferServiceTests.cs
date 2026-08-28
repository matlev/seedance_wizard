using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class ProjectAssetTransferServiceTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge transfer tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GeneratedAssetCanBeCopiedWithoutBreakingItsSourceGeneration()
    {
        var sourceMedia = Path.Combine(_temporaryRoot, "incoming", "generated-clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceMedia)!);
        await File.WriteAllTextAsync(sourceMedia, "generated media bytes");
        var store = new PortableProjectStore();
        var importer = new AssetImportService(new StubInspector());
        var sourceWorkspace = new ProjectWorkspace(store, importer);
        await sourceWorkspace.CreateAsync(Path.Combine(_temporaryRoot, "project1"), "project1");
        var sourceAsset = Assert.Single(await sourceWorkspace.ImportAssetsAsync([sourceMedia]));
        var generation = new GenerationRecord
        {
            Status = GenerationStatus.Succeeded,
            IngestionStatus = OutputIngestionStatus.Succeeded,
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "test.provider",
                ModelVersion = "test-model",
                Prompt = "A generated clip",
                Mode = GenerationMode.TextToVideo,
                DurationSeconds = 5,
                AspectRatio = "16:9",
                Resolution = "720p"
            },
            OutputAssetIds = [sourceAsset.Id]
        };
        sourceAsset.Origin = AssetOrigin.Generated;
        sourceAsset.Provenance = new AssetProvenance
        {
            Operation = "generation-output",
            GenerationId = generation.Id
        };
        sourceWorkspace.Project!.Generations.Add(generation);
        await sourceWorkspace.SaveAsync();
        var sourcePath = sourceWorkspace.GetAbsoluteAssetPath(sourceAsset);

        var targetWorkspace = new ProjectWorkspace(store, importer);
        await targetWorkspace.CreateAsync(
            Path.Combine(_temporaryRoot, "project_super_amazing_and_wonderful"),
            "project_super_amazing_and_wonderful");
        var result = await new ProjectAssetTransferService(store, importer).CopyToProjectAsync(
            sourceWorkspace,
            sourceAsset,
            targetWorkspace.Location!.ProjectFilePath);

        Assert.Equal("project_super_amazing_and_wonderful", result.TargetProjectName);
        Assert.NotEqual(sourceAsset.Id, result.CopiedAsset.Id);
        Assert.Equal(sourceAsset.Physical!.ContentIdentity.Sha256, result.CopiedAsset.Physical!.ContentIdentity.Sha256);
        Assert.Equal("copied-from-project", result.CopiedAsset.Provenance?.Operation);
        Assert.Null(result.CopiedAsset.Provenance?.GenerationId);
        Assert.Equal(sourceWorkspace.Project.Id.ToString("D"), result.CopiedAsset.Provenance?.Parameters["sourceProjectId"]);
        Assert.Equal(sourceAsset.Id.ToString("D"), result.CopiedAsset.Provenance?.Parameters["sourceAssetId"]);
        Assert.DoesNotContain("materializedContentHash", result.CopiedAsset.Provenance!.Parameters.Keys);
        Assert.True(File.Exists(sourcePath));
        Assert.Contains(sourceAsset.Id, generation.OutputAssetIds);

        var (reopenedTarget, reopenedTargetLocation) = await store.OpenAsync(targetWorkspace.Location.ProjectFilePath);
        var targetAsset = Assert.Single(reopenedTarget.Assets);
        Assert.True(File.Exists(Path.Combine(
            reopenedTargetLocation.RootDirectory,
            targetAsset.Physical!.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task RepeatedCopiesReceiveCollisionSafeFilenames()
    {
        var sourceMedia = Path.Combine(_temporaryRoot, "collision-incoming", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceMedia)!);
        await File.WriteAllTextAsync(sourceMedia, "same bytes");
        var store = new PortableProjectStore();
        var importer = new AssetImportService(new StubInspector());
        var source = new ProjectWorkspace(store, importer);
        await source.CreateAsync(Path.Combine(_temporaryRoot, "collision-source"), "Collision source");
        var asset = Assert.Single(await source.ImportAssetsAsync([sourceMedia]));
        var target = new ProjectWorkspace(store, importer);
        await target.CreateAsync(Path.Combine(_temporaryRoot, "collision-target"), "Collision target");
        var service = new ProjectAssetTransferService(store, importer);

        var first = await service.CopyToProjectAsync(source, asset, target.Location!.ProjectFilePath);
        var second = await service.CopyToProjectAsync(source, asset, target.Location.ProjectFilePath);

        Assert.Equal("clip.mp4", first.CopiedAsset.FileName);
        Assert.Equal("clip (2).mp4", second.CopiedAsset.FileName);
        var (reopened, _) = await store.OpenAsync(target.Location.ProjectFilePath);
        Assert.Equal(2, reopened.Assets.Count);
    }

    [Fact]
    public async Task CopyReservesAnActiveTargetPathWhoseFileIsMissing()
    {
        var sourceMedia = Path.Combine(_temporaryRoot, "missing-target-source", "clip.mp4");
        var targetMedia = Path.Combine(_temporaryRoot, "missing-target-existing", "clip.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceMedia)!);
        Directory.CreateDirectory(Path.GetDirectoryName(targetMedia)!);
        await File.WriteAllTextAsync(sourceMedia, "source bytes");
        await File.WriteAllTextAsync(targetMedia, "target bytes");
        var store = new PortableProjectStore();
        var importer = new AssetImportService(new StubInspector());
        var source = new ProjectWorkspace(store, importer);
        await source.CreateAsync(Path.Combine(_temporaryRoot, "missing-target-source-project"), "Source");
        var sourceAsset = Assert.Single(await source.ImportAssetsAsync([sourceMedia]));
        var target = new ProjectWorkspace(store, importer);
        await target.CreateAsync(Path.Combine(_temporaryRoot, "missing-target-project"), "Target");
        var targetAsset = Assert.Single(await target.ImportAssetsAsync([targetMedia]));
        File.Delete(target.GetAbsoluteAssetPath(targetAsset));

        var copied = await new ProjectAssetTransferService(store, importer).CopyToProjectAsync(
            source, sourceAsset, target.Location!.ProjectFilePath);

        Assert.Equal("clip (2).mp4", copied.CopiedAsset.FileName);
        var (reopened, _) = await store.OpenAsync(target.Location.ProjectFilePath);
        Assert.Equal(
            2,
            reopened.Assets
                .Where(asset => !asset.IsDeleted && asset.Physical is not null)
                .Select(asset => asset.Physical!.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class StubInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaEncodingMetadata
            {
                ContainerFormat = "mp4",
                DurationSeconds = 5,
                Video = new VideoStreamMetadata { Width = 1280, Height = 720 }
            });
    }
}
