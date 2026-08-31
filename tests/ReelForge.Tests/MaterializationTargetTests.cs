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
    public async Task DeletedPhysicalSourceIsRejectedBeforeHashingAndDoesNotBecomeAvailableWhenBytesReappear()
    {
        var (project, location, asset) = await CreateProjectSourceAsync();
        asset.Physical!.ContentIdentity = await new Sha256ContentHashService().ComputeAsync(
            Path.Combine(_root, asset.Physical.RelativePath));
        asset.IsDeleted = true;
        asset.Physical.Availability = PhysicalAssetAvailability.Missing;
        var hashService = new CountingHashService();
        var materializer = new PhysicalAssetMaterializer(hashService);

        var assetException = await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(new AssetMaterializationTarget(asset.Id), MaterializationPurpose.Preview)));

        Assert.Equal("'source.mp4' was deleted from the project and cannot be materialized.", assetException.Message);
        Assert.Equal(0, hashService.CallCount);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical.Availability);

        var anchor = new FrameAnchor();
        project.Anchors.Add(anchor);
        var revision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            asset.Id, asset.Physical.ContentIdentity.Sha256!, 0, 1, 1, 24, 1));

        var anchorException = await Assert.ThrowsAsync<InvalidOperationException>(() => materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AnchorMaterializationTarget(anchor.Id, revision.Id),
                MaterializationPurpose.FrameExtraction)));

        Assert.Equal(assetException.Message, anchorException.Message);
        Assert.Equal(0, hashService.CallCount);
        Assert.Equal(PhysicalAssetAvailability.Missing, asset.Physical.Availability);
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

        Assert.True(await materializer.HasCachedRepresentationAsync(project, request.Target));

        using var restartedMaterializer = new RecipeMediaMaterializer(
            "ffmpeg.exe", runner, frames, Path.Combine(_root, "cache"));
        Assert.True(await restartedMaterializer.HasCachedRepresentationAsync(project, request.Target));
        await using var cached = await restartedMaterializer.OpenCachedRepresentationAsync(project, request.Target);
        Assert.NotNull(cached);
        Assert.Equal(first.Path, cached.Path);
        Assert.Equal(1, runner.TrimCount);

        File.Delete(first.Path);
        Assert.False(await restartedMaterializer.HasCachedRepresentationAsync(project, request.Target));
        Assert.Equal(1, runner.TrimCount);
    }





    [Fact]
    public async Task PersistencePreferenceCopiesSavedFrameIntoProjectMediaFolder()
    {
        var (project, location, sourceAsset) = await CreateProjectSourceAsync();
        sourceAsset.Physical!.ContentIdentity = await new Sha256ContentHashService().ComputeAsync(
            Path.Combine(location.RootDirectory, sourceAsset.Physical.RelativePath));
        var anchor = new FrameAnchor { DisplayLabel = "Expression" };
        project.Anchors.Add(anchor);
        var revision = project.CommitAnchorRevision(anchor.Id, new ExactFramePosition(
            sourceAsset.Id,
            sourceAsset.Physical.ContentIdentity.Sha256!,
            0,
            30,
            1,
            30,
            30));
        var extractedPath = Path.Combine(_root, "cache", "frame.png");
        Directory.CreateDirectory(Path.GetDirectoryName(extractedPath)!);
        await File.WriteAllBytesAsync(extractedPath, [7, 8, 9]);
        using var materializer = new RecipeMediaMaterializer(
            "ffmpeg.exe",
            new TrimRunner(),
            new StubExactFrameService([], extractedPath),
            Path.Combine(_root, "cache"),
            persistModifiedMediaOnDisk: true);

        await using var frame = await materializer.MaterializeAsync(
            project,
            location,
            new MaterializationRequest(
                new AnchorMaterializationTarget(anchor.Id, revision.Id),
                MaterializationPurpose.Preview));

        Assert.StartsWith(
            Path.Combine(location.RootDirectory, "assets", "modified", "frames"),
            frame.Path,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", frame.Path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(frame.Path));
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
    public async Task CompositionRevisionIdentityKeepsEarlierCachedRepresentationDistinctFromCurrentRevision()
    {
        var project = new VideoProject();
        var sourceHash = new string('d', 64);
        var duration = new ExactTime(1, 1);
        var source = new ProjectAsset
        {
            DisplayName = "source.mp4",
            FileName = "source.mp4",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Physical,
            Encoding = new MediaEncodingMetadata
            {
                Video = new VideoStreamMetadata { StreamIndex = 0, Codec = "h264" },
                Audio = new AudioStreamMetadata { StreamIndex = 1, Codec = "aac", SampleRate = 48_000, Channels = 2 }
            },
            Physical = new PhysicalAssetStorage
            {
                RelativePath = Path.Combine("assets", "videos", "source.mp4"),
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = new ContentIdentity { Sha256 = sourceHash, Status = ContentHashStatus.Verified }
            },
            TimingAssessments =
            [
                ExactAssessment(sourceHash, MediaType.Video, 0, duration),
                ExactAssessment(sourceHash, MediaType.Audio, 1, duration)
            ]
        };
        var composition = new ProjectAsset
        {
            DisplayName = "Working Composition",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(source);
        project.AddAsset(composition);
        project.WorkingCompositionAssetId = composition.Id;

        var firstRevision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = ExactLinkedComposition(source.Id, "Primary video", "Primary audio")
        });
        var cacheRoot = Path.Combine(_root, "cache", "composition-identity");
        var cachedRepresentation = Path.Combine(cacheRoot, "first-revision.mp4");
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllBytesAsync(cachedRepresentation, [1]);
        using var index = new CachedProjectMediaRepresentationIndex(cacheRoot);
        var firstTarget = new AssetMaterializationTarget(composition.Id, firstRevision.Id);
        await index.RecordAsync(project, firstTarget, cachedRepresentation, CancellationToken.None);

        var secondRevision = project.CommitRecipe(composition.Id, new CompositionRecipe
        {
            Composition = ExactLinkedComposition(source.Id, "Replacement video", "Replacement audio")
        });

        Assert.NotEqual(firstRevision.Id, secondRevision.Id);
        Assert.NotEqual(
            ((CompositionRecipe)firstRevision.Recipe).Composition.VideoTracks.Single().Id,
            ((CompositionRecipe)secondRevision.Recipe).Composition.VideoTracks.Single().Id);
        Assert.Empty(ProjectInvariantValidator.Validate(project));
        Assert.True(await index.HasCachedRepresentationAsync(project, firstTarget, CancellationToken.None));
        Assert.Equal(cachedRepresentation, await index.FindCachedRepresentationPathAsync(
            project, firstTarget, CancellationToken.None));
        Assert.False(await index.HasCachedRepresentationAsync(
            project, new AssetMaterializationTarget(composition.Id, secondRevision.Id), CancellationToken.None));
        Assert.Null(await index.FindCachedRepresentationPathAsync(
            project, new AssetMaterializationTarget(composition.Id, secondRevision.Id), CancellationToken.None));
        Assert.False(await index.HasCachedRepresentationAsync(
            project, new AssetMaterializationTarget(composition.Id), CancellationToken.None));
        Assert.Null(await index.FindCachedRepresentationPathAsync(
            project, new AssetMaterializationTarget(composition.Id), CancellationToken.None));
    }

    [Fact]
    public async Task AssetTargetDoesNotAddressRecipeRevisionOwnedByAnotherVirtualAsset()
    {
        var project = new VideoProject();
        var target = new ProjectAsset
        {
            DisplayName = "Target",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        var other = new ProjectAsset
        {
            DisplayName = "Other",
            MediaType = MediaType.Video,
            StorageKind = AssetStorageKind.Virtual,
            Physical = null,
            Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }
        };
        project.AddAsset(target);
        project.AddAsset(other);
        project.CommitRecipe(target.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState([], [])
        });
        var otherRevision = project.CommitRecipe(other.Id, new CompositionRecipe
        {
            Composition = new WorkingCompositionState([], [])
        });
        var cacheRoot = Path.Combine(_root, "cache", "cross-asset-revision");
        var cachedRepresentation = Path.Combine(cacheRoot, "other-revision.mp4");
        Directory.CreateDirectory(cacheRoot);
        await File.WriteAllBytesAsync(cachedRepresentation, [1]);
        using var index = new CachedProjectMediaRepresentationIndex(cacheRoot);
        var invalidTarget = new AssetMaterializationTarget(target.Id, otherRevision.Id);

        await index.RecordAsync(project, invalidTarget, cachedRepresentation, CancellationToken.None);

        Assert.False(await index.HasCachedRepresentationAsync(project, invalidTarget, CancellationToken.None));
        Assert.Null(await index.FindCachedRepresentationPathAsync(project, invalidTarget, CancellationToken.None));
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

    private static WorkingCompositionState ExactLinkedComposition(Guid sourceAssetId, string videoTrackName, string audioTrackName)
    {
        var source = new AssetRevisionReference { AssetId = sourceAssetId };
        var linkGroupId = Guid.NewGuid();
        var contentHash = new string('d', 64);
        var duration = new ExactTime(1, 1);
        return new WorkingCompositionState(
        [
            new CompositionVideoTrack(Guid.NewGuid(), false, true,
            [
                new CompositionVideoItem(Guid.NewGuid(), source, 0,
                    new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30)),
                    ExactPin(contentHash, MediaType.Video, 0, duration), new ExactTime(0, 1), linkGroupId)
            ], videoTrackName)
        ],
        [
            new CompositionAudioTrack(Guid.NewGuid(), false, false,
            [
                new CompositionAudioItem(Guid.NewGuid(), source, 1,
                    new AudioSourceRange(new AudioSampleTime(0, 48_000), new AudioSampleTime(48_000, 48_000)),
                    ExactPin(contentHash, MediaType.Audio, 1, duration), new ExactTime(0, 1), linkGroupId)
            ], audioTrackName)
        ]);
    }

    private static StreamTimingAssessmentPin ExactPin(
        string contentHash,
        MediaType mediaType,
        int streamIndex,
        ExactTime duration) => new(ExactAssessment(contentHash, mediaType, streamIndex, duration));

    private static StreamTimingAssessment ExactAssessment(
        string contentHash,
        MediaType mediaType,
        int streamIndex,
        ExactTime duration) => new(
        Guid.NewGuid(), contentHash, mediaType, streamIndex, TimingReadiness.Exact, true, duration, [], new ExactTime(0, 1));

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

    private sealed class StubExactFrameService(
        IReadOnlyList<VideoPresentationFrame> frames,
        string? extractedPath = null) : IExactVideoFrameService
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
            extractedPath is null
                ? throw new NotSupportedException()
                : Task.FromResult(new MaterializedMediaLease(
                    extractedPath,
                    new ContentIdentity
                    {
                        Sha256 = new string('c', 64),
                        Status = ContentHashStatus.Verified
                    },
                    null,
                    isDurableSource: false));
    }

    private sealed class CountingHashService : IContentHashService
    {
        public int CallCount { get; private set; }

        public Task<ContentIdentity> ComputeAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("A deleted source must not be hashed.");
        }

        public Task<ContentVerificationResult> VerifyAsync(
            string path,
            ContentIdentity expected,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("A deleted source must not be verified.");
        }
    }

    private sealed class StubMediaInspector(MediaEncodingMetadata encoding) : IMediaInspectionService
    {
        public int InspectionCount { get; private set; }
        public string? LastInspectedPath { get; private set; }

        public Task<MediaEncodingMetadata> InspectAsync(
            string mediaPath,
            CancellationToken cancellationToken = default)
        {
            InspectionCount++;
            LastInspectedPath = mediaPath;
            return Task.FromResult(encoding);
        }
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
