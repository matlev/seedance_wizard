using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Tests;

public sealed class RecipeRenderPlannerTests
{
    [Fact]
    public void NestedVirtualSourcesProducePinnedDeterministicPlan()
    {
        var physical = PhysicalVideo();
        var inner = VirtualVideo("Inner clip");
        var outer = VirtualVideo("Outer clip");
        var project = new VideoProject { Assets = [physical, inner, outer] };
        var innerRevision = project.CommitRecipe(inner.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = physical.Id },
            Start = Timestamp(1),
            End = Timestamp(7)
        });
        var outerRevision = project.CommitRecipe(outer.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference
            {
                AssetId = inner.Id,
                RecipeRevisionId = innerRevision.Id
            },
            Start = Timestamp(2),
            End = Timestamp(4)
        });

        var first = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview,
            "preview");
        var second = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview,
            "preview");
        var upload = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.ProviderUpload,
            "preview");
        physical.Physical!.ContentIdentity.Sha256 = new string('b', 64);
        var changedSource = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview,
            "preview");

        var outerNode = Assert.IsType<TrimRenderPlanNode>(first.Root);
        var innerNode = Assert.IsType<TrimRenderPlanNode>(outerNode.Source);
        Assert.IsType<PhysicalSourceRenderPlanNode>(innerNode.Source);
        Assert.Equal(outerRevision.Id, first.TargetRecipeRevisionId);
        Assert.Equal(first.PlanHash, second.PlanHash);
        Assert.NotEqual(first.PlanHash, upload.PlanHash);
        Assert.NotEqual(first.PlanHash, changedSource.PlanHash);
    }

    [Fact]
    public void VirtualDependencyWithoutPinnedRevisionIsRejected()
    {
        var physical = PhysicalVideo();
        var inner = VirtualVideo("Inner clip");
        var outer = VirtualVideo("Outer clip");
        var project = new VideoProject { Assets = [physical, inner, outer] };
        project.CommitRecipe(inner.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = physical.Id }
        });
        var outerRevision = project.CommitRecipe(outer.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = inner.Id }
        });

        var exception = Assert.Throws<InvalidDataException>(() => RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview));

        Assert.Contains("pin an exact recipe revision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecipeDependencyCycleIsRejectedDuringPlanning()
    {
        var first = VirtualVideo("First");
        var second = VirtualVideo("Second");
        var firstRevisionId = Guid.NewGuid();
        var secondRevisionId = Guid.NewGuid();
        first.Virtual!.CurrentRecipeRevisionId = firstRevisionId;
        second.Virtual!.CurrentRecipeRevisionId = secondRevisionId;
        var project = new VideoProject
        {
            Assets = [first, second],
            RecipeRevisions =
            [
                new RecipeRevision
                {
                    Id = firstRevisionId,
                    VirtualAssetId = first.Id,
                    RevisionNumber = 1,
                    Recipe = new TrimRecipe
                    {
                        Source = new AssetRevisionReference
                        {
                            AssetId = second.Id,
                            RecipeRevisionId = secondRevisionId
                        }
                    }
                },
                new RecipeRevision
                {
                    Id = secondRevisionId,
                    VirtualAssetId = second.Id,
                    RevisionNumber = 1,
                    Recipe = new TrimRecipe
                    {
                        Source = new AssetRevisionReference
                        {
                            AssetId = first.Id,
                            RecipeRevisionId = firstRevisionId
                        }
                    }
                }
            ]
        };

        var exception = Assert.Throws<InvalidDataException>(() => RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(first.Id, firstRevisionId),
            MaterializationPurpose.Preview));

        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompositionPlanCarriesCompatibilityDecisionWithoutRendering()
    {
        var first = PhysicalVideo();
        first.Encoding = Encoding(1280);
        var second = PhysicalVideo();
        second.Encoding = Encoding(1920);
        var composition = VirtualVideo("Composition");
        composition.Virtual!.Kind = VirtualAssetKind.Composition;
        var project = new VideoProject { Assets = [first, second, composition] };
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = first.Id } },
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = second.Id } }
            ]
        });

        var plan = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(composition.Id, revision.Id),
            MaterializationPurpose.Preview);

        var node = Assert.IsType<CompositionRenderPlanNode>(plan.Root);
        Assert.Equal(CompositionCompatibilityDecision.RequiresNormalization, node.Compatibility.Decision);
        Assert.Contains(node.Compatibility.Issues, issue => issue.Property.Contains("width", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositionPlanPinsTimedAudioClipsIntoItsHash()
    {
        var video = PhysicalVideo();
        var audio = PhysicalAudio();
        var composition = VirtualVideo("Composition");
        composition.Virtual!.Kind = VirtualAssetKind.Composition;
        var project = new VideoProject { Assets = [video, audio, composition] };
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = video.Id } }
            ],
            AudioClips =
            [
                new CompositionAudioClip
                {
                    Source = new AssetRevisionReference { AssetId = audio.Id },
                    TimelineStartTicks = TimeSpan.FromSeconds(3).Ticks,
                    IsMuted = true,
                    GainDecibels = -7
                }
            ]
        });

        var plan = RecipeRenderPlanner.Plan(
            project,
            new AssetMaterializationTarget(composition.Id, revision.Id),
            MaterializationPurpose.Preview);
        var node = Assert.IsType<CompositionRenderPlanNode>(plan.Root);

        var clip = Assert.Single(node.AudioClips);
        Assert.Equal(audio.Id, clip.Source.AssetId);
        Assert.Equal(TimeSpan.FromSeconds(3).Ticks, clip.TimelineStartTicks);
        Assert.True(clip.IsMuted);
        Assert.Equal(-7, clip.GainDecibels);
        Assert.NotEqual(node.Segments[0].SegmentHash, clip.ClipHash);
    }

    private static ProjectAsset PhysicalVideo() => new()
    {
        DisplayName = "Source",
        FileName = "source.mp4",
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = Path.Combine("assets", "videos", "source.mp4"),
            ContentIdentity = new ContentIdentity
            {
                Status = ContentHashStatus.Verified,
                Sha256 = new string('a', 64)
            }
        }
    };

    private static ProjectAsset VirtualVideo(string name) => new()
    {
        DisplayName = name,
        MediaType = MediaType.Video,
        StorageKind = AssetStorageKind.Virtual,
        Physical = null,
        Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
    };

    private static ProjectAsset PhysicalAudio() => new()
    {
        DisplayName = "Music",
        FileName = "music.wav",
        MediaType = MediaType.Audio,
        StorageKind = AssetStorageKind.Physical,
        Physical = new PhysicalAssetStorage
        {
            RelativePath = Path.Combine("assets", "audio", "music.wav"),
            ContentIdentity = new ContentIdentity
            {
                Status = ContentHashStatus.Verified,
                Sha256 = new string('c', 64)
            }
        }
    };

    private static RecipeBoundary Timestamp(double seconds) => new()
    {
        Kind = RecipeBoundaryKind.Timestamp,
        TimestampSeconds = seconds
    };

    private static MediaEncodingMetadata Encoding(int width) => new()
    {
        Video = new VideoStreamMetadata
        {
            Codec = "h264",
            Width = width,
            Height = 720,
            PixelFormat = "yuv420p",
            FrameRate = "30/1"
        },
        Audio = new AudioStreamMetadata
        {
            Codec = "aac",
            SampleRate = 48000,
            Channels = 2,
            ChannelLayout = "stereo"
        }
    };
}
