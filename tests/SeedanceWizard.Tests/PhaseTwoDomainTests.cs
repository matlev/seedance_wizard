using SeedanceWizard.Core;
using SeedanceWizard.Infrastructure;

namespace SeedanceWizard.Tests;

public sealed class PhaseTwoDomainTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "Seedance Wizard phase two tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void GenerationLineageAllowsOneParentAndRejectsCycles()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var project = new VideoProject
        {
            Generations =
            [
                CreateGeneration(firstId, secondId, GenerationRelationshipType.BasedOn),
                CreateGeneration(secondId, firstId, GenerationRelationshipType.VariantOf)
            ]
        };

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains("lineage cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainVideoCannotReferenceVirtualAsset()
    {
        var virtualAsset = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState()
        };
        var project = new VideoProject { Assets = [virtualAsset], MainVideoAssetId = virtualAsset.Id };

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains("main video", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sha256ServiceUsesCanonicalByteFingerprintIndependentlyOfName()
    {
        Directory.CreateDirectory(_temporaryRoot);
        var first = Path.Combine(_temporaryRoot, "friendly name.txt");
        var renamed = Path.Combine(_temporaryRoot, "renamed.txt");
        await File.WriteAllTextAsync(first, "abc");
        File.Copy(first, renamed);
        var service = new Sha256ContentHashService();

        var firstIdentity = await service.ComputeAsync(first);
        var renamedIdentity = await service.ComputeAsync(renamed);
        await File.WriteAllTextAsync(renamed, "different bytes");
        var changedIdentity = await service.VerifyAsync(renamed, firstIdentity);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", firstIdentity.Sha256);
        Assert.Equal(firstIdentity.Sha256, renamedIdentity.Sha256);
        Assert.False(changedIdentity.MatchesExpected);
        Assert.NotEqual(firstIdentity.Sha256, changedIdentity.Observed.Sha256);
        Assert.Equal(ContentHashStatus.Mismatch, changedIdentity.Observed.Status);
        Assert.Equal(ContentHashStatus.Verified, firstIdentity.Status);
    }

    [Fact]
    public void VirtualGenerationReferenceMustPinExactRevision()
    {
        var source = CreatePhysicalAsset();
        var virtualAsset = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState()
        };
        var project = new VideoProject { Assets = [source, virtualAsset] };
        project.CommitRecipe(virtualAsset.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id }
        });
        project.Generations.Add(new GenerationRecord
        {
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "fake",
                ModelVersion = "fake",
                Prompt = "missing pin",
                References = Array.AsReadOnly(new[]
                {
                    new GenerationReferenceSnapshot
                    {
                        ObjectKind = GenerationReferenceObjectKind.Asset,
                        LogicalObjectId = virtualAsset.Id
                    }
                })
            }
        });

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains("pin an exact revision", StringComparison.OrdinalIgnoreCase));
    }

    private static GenerationRecord CreateGeneration(
        Guid id,
        Guid parentId,
        GenerationRelationshipType relationshipType) => new()
    {
        Id = id,
        ParentGenerationId = parentId,
        RelationshipType = relationshipType,
        RequestSnapshot = new GenerationRequestSnapshot { ProviderId = "fake", ModelVersion = "fake", Prompt = "test" }
    };

    private static ProjectAsset CreatePhysicalAsset() => new()
    {
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = "assets/videos/source.mp4",
            ContentIdentity = new ContentIdentity
            {
                Sha256 = new string('b', 64),
                Status = ContentHashStatus.Verified
            }
        },
        Virtual = null
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }
}
