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

    [Fact]
    public async Task SavedClipMaterializationResolvesAfterFrameAndReusesDeterministicCache()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        sourceAsset.DurationSeconds = 8;
        sourceAsset.Physical!.ContentIdentity = await new Sha256ContentHashService()
            .ComputeAsync(Path.Combine(_root, sourceAsset.Physical.RelativePath));
        var anchor = new FrameAnchor { IsArchived = true };
        project.Anchors.Add(anchor);
        var anchorRevision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id,
            sourceAsset.Physical.ContentIdentity.Sha256!,
            0,
            30,
            1,
            10,
            30));
        var clip = new ProjectAsset
        {
            DisplayName = "Opening",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.SavedClip }
        };
        project.AddAsset(clip);
        var recipeRevision = project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = sourceAsset.Id },
            Start = RecipeBoundary.SourceStart,
            End = new RecipeBoundary
            {
                Kind = RecipeBoundaryKind.Anchor,
                Anchor = new AnchorRevisionReference
                {
                    AnchorId = anchor.Id,
                    AnchorRevisionId = anchorRevision.Id
                },
                Edge = AnchorBoundaryEdge.AfterFrame
            }
        });
        var runner = new TrimRunner();
        var frames = new StubExactFrameService([
            new VideoPresentationFrame(0, 30, 1, 10),
            new VideoPresentationFrame(0, 31, 1, 10)
        ]);
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, frames, Path.Combine(_root, "cache"));
        var request = new MaterializationRequest(
            new AssetMaterializationTarget(clip.Id, recipeRevision.Id),
            MaterializationPurpose.Preview);

        await using var first = await materializer.MaterializeAsync(project, location, request);
        await using var second = await materializer.MaterializeAsync(project, location, request);

        Assert.False(first.IsDurableSource);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(1, runner.TrimCount);
        Assert.Equal(1, frames.WindowIndexCount);
        Assert.Contains("3.1", runner.TrimRequest!.Arguments);
    }

    [Fact]
    public async Task InitialWorkingCompositionPreviewsItsPinnedSingleSourceDirectly()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(composition);
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment
                {
                    Source = new AssetRevisionReference { AssetId = sourceAsset.Id },
                    Start = RecipeBoundary.SourceStart,
                    End = RecipeBoundary.SourceEnd
                }
            ]
        });
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", new TrimRunner(), new StubExactFrameService([]), Path.Combine(_root, "cache"));

        await using var preview = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(composition.Id, revision.Id),
                MaterializationPurpose.Preview));

        Assert.True(preview.IsDurableSource);
        Assert.EndsWith("source.mp4", preview.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NestedSavedClipsMaterializeRecursivelyAndReuseEachCacheLevel()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        sourceAsset.DurationSeconds = 8;
        sourceAsset.Physical!.ContentIdentity = await new Sha256ContentHashService()
            .ComputeAsync(Path.Combine(_root, sourceAsset.Physical.RelativePath));
        var inner = new ProjectAsset
        {
            DisplayName = "Inner",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata { DurationSeconds = 4 }
            }
        };
        var outer = new ProjectAsset
        {
            DisplayName = "Outer",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata { DurationSeconds = 2 }
            }
        };
        project.AddAsset(inner);
        project.AddAsset(outer);
        var innerRevision = project.CommitRecipe(inner.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = sourceAsset.Id },
            Start = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 1 },
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 5 }
        });
        var outerRevision = project.CommitRecipe(outer.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference
            {
                AssetId = inner.Id,
                RecipeRevisionId = innerRevision.Id
            },
            Start = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 1 },
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 3 }
        });
        var runner = new TrimRunner();
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, new StubExactFrameService([]), Path.Combine(_root, "cache"));
        var request = new MaterializationRequest(
            new AssetMaterializationTarget(outer.Id, outerRevision.Id),
            MaterializationPurpose.Preview);

        await using var first = await materializer.MaterializeAsync(project, location, request);
        await using var second = await materializer.MaterializeAsync(project, location, request);

        Assert.False(first.IsDurableSource);
        Assert.Equal(first.Path, second.Path);
        Assert.Equal(2, runner.TrimCount);
    }

    [Fact]
    public async Task WorkingCompositionResolvesItsPinnedSavedClipSource()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        sourceAsset.DurationSeconds = 8;
        sourceAsset.Physical!.ContentIdentity = await new Sha256ContentHashService()
            .ComputeAsync(Path.Combine(_root, sourceAsset.Physical.RelativePath));
        var clip = new ProjectAsset
        {
            DisplayName = "Pinned clip",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState
            {
                Kind = VirtualAssetKind.SavedClip,
                ExpectedMediaProperties = new MediaEncodingMetadata { DurationSeconds = 3 }
            }
        };
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(clip);
        project.AddAsset(composition);
        var clipRevision = project.CommitRecipe(clip.Id, new TrimRecipe
        {
            Source = new AssetRevisionReference { AssetId = sourceAsset.Id },
            Start = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 2 },
            End = new RecipeBoundary { Kind = RecipeBoundaryKind.Timestamp, TimestampSeconds = 5 }
        });
        var compositionRevision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment
                {
                    Source = new AssetRevisionReference
                    {
                        AssetId = clip.Id,
                        RecipeRevisionId = clipRevision.Id
                    },
                    Start = RecipeBoundary.SourceStart,
                    End = RecipeBoundary.SourceEnd
                }
            ]
        });
        var runner = new TrimRunner();
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, new StubExactFrameService([]), Path.Combine(_root, "cache"));

        await using var preview = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(composition.Id, compositionRevision.Id),
                MaterializationPurpose.Preview));

        Assert.False(preview.IsDurableSource);
        Assert.Equal(1, runner.TrimCount);
    }

    [Fact]
    public async Task CompatibleCompositionConcatsPhysicalSegmentsAndReusesCache()
    {
        var (project, location, firstSource) = await CreateProjectSourceAsync();
        var firstPath = Path.Combine(_root, firstSource.Physical!.RelativePath);
        firstSource.Physical.ContentIdentity = await new Sha256ContentHashService().ComputeAsync(firstPath);
        firstSource.Encoding = CompatibleEncoding();
        var secondRelativePath = Path.Combine("assets", "videos", "second.mp4");
        var secondPath = Path.Combine(_root, secondRelativePath);
        await File.WriteAllBytesAsync(secondPath, [6, 7, 8, 9]);
        var secondSource = new ProjectAsset
        {
            DisplayName = "second.mp4",
            FileName = "second.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Encoding = CompatibleEncoding(),
            Physical = new PhysicalAssetStorage
            {
                RelativePath = secondRelativePath,
                ContentIdentity = await new Sha256ContentHashService().ComputeAsync(secondPath)
            }
        };
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(secondSource);
        project.AddAsset(composition);
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = firstSource.Id } },
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = secondSource.Id } }
            ]
        });
        var runner = new TrimRunner();
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, new StubExactFrameService([]), Path.Combine(_root, "cache"));
        var request = new MaterializationRequest(
            new AssetMaterializationTarget(composition.Id, revision.Id),
            MaterializationPurpose.Preview);

        await using var first = await materializer.MaterializeAsync(project, location, request);
        await using var second = await materializer.MaterializeAsync(project, location, request);

        Assert.Equal(first.Path, second.Path);
        Assert.Equal(1, runner.ConcatCount);
        Assert.Equal(0, runner.TrimCount);
        Assert.Contains("-filter_complex", runner.ConcatRequest!.Arguments);
    }

    [Fact]
    public async Task IncompatibleCompositionNormalizesAndCachesConcat()
    {
        var (project, location, firstSource) = await CreateProjectSourceAsync();
        var firstPath = Path.Combine(_root, firstSource.Physical!.RelativePath);
        firstSource.Physical.ContentIdentity = await new Sha256ContentHashService().ComputeAsync(firstPath);
        firstSource.Encoding = CompatibleEncoding();
        var secondRelativePath = Path.Combine("assets", "videos", "wide.mp4");
        var secondPath = Path.Combine(_root, secondRelativePath);
        await File.WriteAllBytesAsync(secondPath, [10, 11, 12]);
        var incompatibleEncoding = CompatibleEncoding();
        incompatibleEncoding.Video!.Width = 1920;
        var secondSource = new ProjectAsset
        {
            DisplayName = "wide.mp4",
            FileName = "wide.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Encoding = incompatibleEncoding,
            Physical = new PhysicalAssetStorage
            {
                RelativePath = secondRelativePath,
                ContentIdentity = await new Sha256ContentHashService().ComputeAsync(secondPath)
            }
        };
        var composition = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(secondSource);
        project.AddAsset(composition);
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = firstSource.Id } },
                new CompositionSegment
                {
                    Source = new AssetRevisionReference { AssetId = secondSource.Id },
                    AudioEnabled = false
                }
            ]
        });
        var runner = new TrimRunner();
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, new StubExactFrameService([]), Path.Combine(_root, "cache"));

        await using var preview = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(composition.Id, revision.Id),
                MaterializationPurpose.Preview));

        Assert.Equal(1, runner.ConcatCount);
        var graph = runner.ConcatRequest!.Arguments[runner.ConcatRequest.Arguments.ToList().IndexOf("-filter_complex") + 1];
        Assert.Contains("scale=1920:720", graph, StringComparison.Ordinal);
        Assert.Contains("anullsrc=r=48000:cl=stereo", graph, StringComparison.Ordinal);
        Assert.True(File.Exists(preview.Path));
    }

    [Fact]
    public async Task FailedCompositionConcatRemovesPartialCacheArtifact()
    {
        var (project, location, firstSource) = await CreateProjectSourceAsync();
        var firstPath = Path.Combine(_root, firstSource.Physical!.RelativePath);
        firstSource.Physical.ContentIdentity = await new Sha256ContentHashService().ComputeAsync(firstPath);
        firstSource.Encoding = CompatibleEncoding();
        var secondRelativePath = Path.Combine("assets", "videos", "second-failure.mp4");
        var secondPath = Path.Combine(_root, secondRelativePath);
        await File.WriteAllBytesAsync(secondPath, [20, 21, 22]);
        var secondSource = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Encoding = CompatibleEncoding(),
            Physical = new PhysicalAssetStorage
            {
                RelativePath = secondRelativePath,
                ContentIdentity = await new Sha256ContentHashService().ComputeAsync(secondPath)
            }
        };
        var composition = new ProjectAsset
        {
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(secondSource);
        project.AddAsset(composition);
        var revision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Segments =
            [
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = firstSource.Id } },
                new CompositionSegment { Source = new AssetRevisionReference { AssetId = secondSource.Id } }
            ]
        });
        var runner = new TrimRunner { FailConcat = true };
        var cacheRoot = Path.Combine(_root, "cache");
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, new StubExactFrameService([]), cacheRoot);

        await Assert.ThrowsAsync<ExternalProcessException>(() => materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AssetMaterializationTarget(composition.Id, revision.Id),
                MaterializationPurpose.Preview)));

        var compositionCache = Path.Combine(cacheRoot, "compositions");
        Assert.False(Directory.Exists(compositionCache) &&
                     Directory.EnumerateFiles(compositionCache, "*.tmp.mp4").Any());
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

    private static MediaEncodingMetadata CompatibleEncoding() => new()
    {
        ContainerFormat = "mp4",
        DurationSeconds = 4,
        Video = new VideoStreamMetadata
        {
            Codec = "h264",
            Width = 1280,
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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class StubExactFrameService(IReadOnlyList<VideoPresentationFrame> frames) : IExactVideoFrameService
    {
        public int WindowIndexCount { get; private set; }

        public Task<IReadOnlyList<VideoPresentationFrame>> IndexAsync(
            string mediaPath,
            CancellationToken cancellationToken = default) => Task.FromResult(frames);

        public Task<IReadOnlyList<VideoPresentationFrame>> IndexWindowAsync(
            string mediaPath,
            double centerSeconds,
            double radiusSeconds = 2,
            CancellationToken cancellationToken = default)
        {
            WindowIndexCount++;
            return Task.FromResult(frames);
        }

        public Task<MaterializedMediaLease> ExtractAsync(
            string mediaPath,
            string sourceContentHash,
            FrameAnchorRevision revision,
            MaterializationPurpose purpose,
            string? profile = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TrimRunner : IExternalProcessRunner
    {
        public int TrimCount { get; private set; }
        public int ConcatCount { get; private set; }
        public bool FailConcat { get; init; }
        public ExternalProcessRequest? TrimRequest { get; private set; }
        public ExternalProcessRequest? ConcatRequest { get; private set; }

        public async Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request.Arguments.SequenceEqual(["-version"]))
                return new ExternalProcessResult(0, "ffmpeg version test-1\n", string.Empty);
            if (request.Arguments.Contains("-filter_complex"))
            {
                ConcatCount++;
                ConcatRequest = request;
                if (FailConcat)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(request.Arguments[^1])!);
                    await File.WriteAllBytesAsync(request.Arguments[^1], [99], cancellationToken);
                    return new ExternalProcessResult(1, string.Empty, "simulated concat failure");
                }
            }
            else
            {
                TrimCount++;
                TrimRequest = request;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(request.Arguments[^1])!);
            await File.WriteAllBytesAsync(request.Arguments[^1], [1, 2, 3], cancellationToken);
            return new ExternalProcessResult(0, string.Empty, string.Empty);
        }
    }
}
