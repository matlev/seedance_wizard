using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class PhaseTwoDomainTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(),
        "ReelForge phase two tests",
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

    [Fact]
    public void CommittingAnchorChangesCurrentRevisionWithoutMutatingHistory()
    {
        var source = CreatePhysicalAsset();
        var anchor = new FrameAnchor { DisplayLabel = "Interesting look" };
        var project = new VideoProject { Assets = [source], Anchors = [anchor] };

        var first = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id, new string('b', 64), 0, 30, 1, 30, 30));
        var pinned = new AnchorRevisionReference { AnchorId = anchor.Id, AnchorRevisionId = first.Id };
        var second = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            source.Id, new string('b', 64), 0, 45, 1, 30, 45));

        Assert.Equal(second.Id, anchor.CurrentRevisionId);
        Assert.Equal(first.Id, second.PreviousRevisionId);
        Assert.Equal(1, first.RevisionNumber);
        Assert.Equal(2, second.RevisionNumber);
        Assert.Equal(first.Id, pinned.AnchorRevisionId);
        Assert.Equal(30, first.PresentationTimestamp);
        Assert.Empty(ProjectInvariantValidator.Validate(project));
    }

    [Fact]
    public void ExactAnchorRevisionRequiresCompleteMediaNativePosition()
    {
        var source = CreatePhysicalAsset();
        var revision = new FrameAnchorRevision
        {
            AnchorId = Guid.NewGuid(),
            RevisionNumber = 1,
            SourceAssetId = source.Id,
            VideoStreamIndex = -1,
            PresentationTimestamp = 15,
            TimeBaseNumerator = 1,
            TimeBaseDenominator = 30
        };
        var anchor = new FrameAnchor { Id = revision.AnchorId, CurrentRevisionId = revision.Id };
        var project = new VideoProject { Assets = [source], Anchors = [anchor], AnchorRevisions = [revision] };

        var errors = ProjectInvariantValidator.Validate(project);

        Assert.Contains(errors, error => error.Contains("invalid presentation timing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepeatedLogicalReferenceIsDistinguishedByStableOccurrenceId()
    {
        var source = CreatePhysicalAsset();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var generation = new GenerationRecord
        {
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "fake",
                ModelVersion = "fake-v1",
                Prompt = "Use one source in two roles",
                References = Array.AsReadOnly(new[]
                {
                    new GenerationReferenceSnapshot
                    {
                        ReferenceId = firstId,
                        ObjectKind = GenerationReferenceObjectKind.Asset,
                        LogicalObjectId = source.Id,
                        Role = GenerationReferenceRole.Character
                    },
                    new GenerationReferenceSnapshot
                    {
                        ReferenceId = secondId,
                        ObjectKind = GenerationReferenceObjectKind.Asset,
                        LogicalObjectId = source.Id,
                        Role = GenerationReferenceRole.Motion
                    }
                })
            }
        };
        var project = new VideoProject { Assets = [source], Generations = [generation] };

        Assert.DoesNotContain(ProjectInvariantValidator.Validate(project),
            error => error.Contains("reference ID", StringComparison.OrdinalIgnoreCase));

        var duplicateGeneration = new GenerationRecord
        {
            RequestSnapshot = new GenerationRequestSnapshot
            {
                ProviderId = "fake",
                ModelVersion = "fake-v1",
                Prompt = "duplicate occurrence",
                References = Array.AsReadOnly(generation.RequestSnapshot.References.Select(reference =>
                    new GenerationReferenceSnapshot
                    {
                        ReferenceId = firstId,
                        ObjectKind = reference.ObjectKind,
                        LogicalObjectId = reference.LogicalObjectId,
                        Role = reference.Role
                    }).ToArray())
            }
        };
        var duplicateProject = new VideoProject
        {
            Assets = [source],
            Generations = [duplicateGeneration]
        };

        Assert.Contains(ProjectInvariantValidator.Validate(duplicateProject),
            error => error.Contains("duplicate reference ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemovingReferencedAnchorArchivesItWhileUnreferencedAnchorIsDeleted()
    {
        var source = CreatePhysicalAsset();
        var referenced = new FrameAnchor();
        var disposable = new FrameAnchor();
        var virtualAsset = new ProjectAsset
        {
            MediaType = MediaType.Image,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState()
        };
        var project = new VideoProject
        {
            Assets = [source, virtualAsset],
            Anchors = [referenced, disposable]
        };
        var referencedRevision = project.CommitAnchorRevision(referenced.Id, new ExactFramePosition(
            source.Id, new string('b', 64), 0, 30, 1, 30, 30));
        project.CommitAnchorRevision(disposable.Id, new ExactFramePosition(
            source.Id, new string('b', 64), 0, 60, 1, 30, 60));
        project.CommitRecipe(virtualAsset.Id, new ExtractFrameRecipe
        {
            Source = new AssetRevisionReference { AssetId = source.Id },
            Anchor = new AnchorRevisionReference
            {
                AnchorId = referenced.Id,
                AnchorRevisionId = referencedRevision.Id
            }
        });

        var referencedResult = project.RemoveOrArchiveAnchor(referenced.Id);
        var disposableResult = project.RemoveOrArchiveAnchor(disposable.Id);

        Assert.Equal(AnchorRemovalDisposition.Archived, referencedResult);
        Assert.True(referenced.IsArchived);
        Assert.Contains(project.AnchorRevisions, revision => revision.Id == referencedRevision.Id);
        Assert.Equal(AnchorRemovalDisposition.Removed, disposableResult);
        Assert.DoesNotContain(project.Anchors, anchor => anchor.Id == disposable.Id);
        Assert.DoesNotContain(project.AnchorRevisions, revision => revision.AnchorId == disposable.Id);
        Assert.Empty(ProjectInvariantValidator.Validate(project));
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
