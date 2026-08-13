using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class MaterializationTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "ReelForge materialization target tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AssetTargetReturnsVerifiedDurableSource()
    {
        var (project, location, asset) = await CreateProjectSourceAsync();
        var materializer = new PhysicalAssetMaterializer();

        await using var lease = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(asset.Id),
                MaterializationPurpose.Preview));

        Assert.True(lease.IsDurableSource);
        Assert.Equal(Path.Combine(_root, "assets", "videos", "source.mp4"), lease.Path);
        Assert.Equal(ContentHashStatus.Verified, lease.ContentIdentity.Status);
        Assert.Equal(lease.ContentIdentity.Sha256, asset.Physical?.ContentIdentity.Sha256);
    }

    [Fact]
    public async Task AnchorTargetPinsRevisionAndVerifiesSourceBeforeExtraction()
    {
        var (project, location, asset) = await CreateProjectSourceAsync();
        var materializer = new PhysicalAssetMaterializer();
        await using (var source = await materializer.MaterializeAsync(
                         project,
                         location,
                         new MaterializationRequest(
                             new AssetMaterializationTarget(asset.Id),
                             MaterializationPurpose.Preview)))
        {
            var anchor = new FrameAnchor();
            project.Anchors.Add(anchor);
            var revision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
                asset.Id, source.ContentIdentity.Sha256!, 0, 30, 1, 30, 30));

            var exception = await Assert.ThrowsAsync<MediaToolUnavailableException>(() => materializer.MaterializeAsync(
                project,
                location,
                new MaterializationRequest(
                    new AnchorMaterializationTarget(anchor.Id, revision.Id),
                    MaterializationPurpose.FrameExtraction)));

            Assert.Contains("FFmpeg", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<(VideoProject Project, ProjectLocation Location, ProjectAsset Asset)> CreateProjectSourceAsync()
    {
        var relativePath = Path.Combine("assets", "videos", "source.mp4");
        var absolutePath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllBytesAsync(absolutePath, [1, 2, 3, 4, 5]);
        var asset = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = relativePath,
                ContentIdentity = new ContentIdentity { Status = ContentHashStatus.Pending }
            }
        };
        var project = new VideoProject { Assets = [asset] };
        return (project, new ProjectLocation(_root, Path.Combine(_root, "Test.rfp")), asset);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
