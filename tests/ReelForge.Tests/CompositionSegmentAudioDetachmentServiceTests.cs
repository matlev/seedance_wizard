using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.Tests;

public sealed class CompositionSegmentAudioDetachmentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ReelForge-detach-audio-{Guid.NewGuid():N}");

    [Fact]
    public async Task DetachExtractsTheExactLinkedAudioRangeAndRebindsTheOccurrenceWithoutDoubling()
    {
        var fixture = await CreateFixtureAsync();
        var engine = new RecordingExtractionEngine();
        var service = CreateService(fixture.Workspace, fixture.SourcePath, engine);

        var result = await service.DetachAsync(fixture.VideoItemId, "spoken line.m4a");

        Assert.False(service.CanDetach(fixture.VideoItemId));
        Assert.Equal(1, engine.CallCount);
        Assert.Equal(1, engine.AudioStreamIndex);
        Assert.Equal(96_000, engine.Range!.Start.SampleFrameOffset);
        Assert.Equal(144_000, engine.Range.End.SampleFrameOffset);
        Assert.Equal(MediaType.Audio, result.AudioAsset.MediaType);
        Assert.Equal(AssetOrigin.ExtractedAudio, result.AudioAsset.Origin);
        Assert.Equal("detach-audio", result.AudioAsset.Provenance!.Operation);
        Assert.Equal(fixture.Source.Id, Assert.Single(result.AudioAsset.Provenance.SourceAssetIds));
        Assert.Equal("96000", result.AudioAsset.Provenance.Parameters["startSample"]);
        Assert.Equal(fixture.VideoItemId.ToString("D"), result.AudioAsset.Provenance.Parameters["videoItemId"]);
        Assert.Equal(fixture.AudioItemId.ToString("D"), result.AudioAsset.Provenance.Parameters["audioItemId"]);
        Assert.True(File.Exists(fixture.Workspace.GetAbsoluteAssetPath(result.AudioAsset)));

        var current = GetCurrent(fixture.Workspace.Project!);
        var video = Assert.Single(current.Composition.VideoTracks.Single().Items);
        var audio = Assert.Single(current.Composition.AudioTracks.Single().Items);
        Assert.Equal(fixture.VideoItemId, video.Id);
        Assert.Equal(fixture.AudioItemId, audio.Id);
        Assert.Null(video.LinkGroupId);
        Assert.Null(audio.LinkGroupId);
        Assert.Equal(result.AudioAsset.Id, audio.Source.AssetId);
        Assert.Equal(4, audio.CompositionStart.ToDoubleSeconds());
        Assert.Equal(-3, audio.GainDecibels);
        Assert.Equal(0.25, audio.Pan);
        Assert.True(audio.IsMuted);
        Assert.Equal(0, audio.SourceRange!.Start.SampleFrameOffset);
        Assert.Equal(48_000, audio.SourceRange.End.SampleFrameOffset);
        Assert.Equal(TimingReadiness.Exact, audio.TimingAssessment.Readiness);
        Assert.Equal(result.AudioAsset.Physical!.ContentIdentity.Sha256, audio.TimingAssessment.SourceContentHash, ignoreCase: true);
        Assert.Empty(ProjectInvariantValidator.Validate(fixture.Workspace.Project!));
    }

    [Fact]
    public async Task EligibilityRequiresLinkedExactUnlockedOccurrences()
    {
        var fixture = await CreateFixtureAsync(audioLocked: true);
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new RecordingExtractionEngine());

        Assert.False(service.CanDetach(fixture.VideoItemId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a"));
    }

    [Fact]
    public async Task EligibilityRefusesEstimatedLinkedAudio()
    {
        var fixture = await CreateFixtureAsync(audioEstimated: true);
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new RecordingExtractionEngine());

        Assert.False(service.CanDetach(fixture.VideoItemId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a"));
    }

    [Fact]
    public async Task EligibilityAllowsEstimatedVideoWhenTheLinkedAudioRangeIsExact()
    {
        var fixture = await CreateFixtureAsync(videoEstimated: true);
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new RecordingExtractionEngine());

        Assert.True(service.CanDetach(fixture.VideoItemId));
        await service.DetachAsync(fixture.VideoItemId, "line.m4a");
    }

    [Fact]
    public async Task ExtractionFailureLeavesProjectAndDestinationUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new ThrowingExtractionEngine());
        var originalAssets = fixture.Workspace.Project!.Assets.Count;
        var originalRevision = fixture.Workspace.Project.Assets.Single(asset => asset.Id == fixture.Workspace.Project.WorkingCompositionAssetId).Virtual!.CurrentRecipeRevisionId;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a"));

        Assert.Equal(originalAssets, fixture.Workspace.Project.Assets.Count);
        Assert.Equal(originalRevision, fixture.Workspace.Project.Assets.Single(asset => asset.Id == fixture.Workspace.Project.WorkingCompositionAssetId).Virtual!.CurrentRecipeRevisionId);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Workspace.Location!.RootDirectory, "assets", "audio")));
    }

    [Fact]
    public async Task OutputWithoutMatchingExactTimingIsRefusedBeforePublication()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(
            fixture.Workspace,
            fixture.SourcePath,
            new RecordingExtractionEngine(),
            new EstimatedOutputTimingAssessment());
        var originalAssets = fixture.Workspace.Project!.Assets.Count;

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a"));

        Assert.Equal(originalAssets, fixture.Workspace.Project.Assets.Count);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Workspace.Location!.RootDirectory, "assets", "audio")));
    }

    [Fact]
    public async Task DestinationCollisionDoesNotDeleteTheForeignFile()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new CollidingExtractionEngine("line.m4a"));
        var destination = Path.Combine(fixture.Workspace.Location!.RootDirectory, "assets", "audio", "line.m4a");

        await Assert.ThrowsAsync<IOException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a"));

        Assert.Equal(new byte[] { 9, 9, 9 }, await File.ReadAllBytesAsync(destination));
        Assert.DoesNotContain(fixture.Workspace.Project!.Assets, asset => asset.FileName == "line.m4a");
    }

    [Fact]
    public async Task SaveFailureRollsBackTheAssetRevisionAndCommittedOutput()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Store.FailSaves = true;
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new RecordingExtractionEngine());
        var originalAssets = fixture.Workspace.Project!.Assets.Count;
        var originalRevision = fixture.Workspace.Project.Assets.Single(asset => asset.Id == fixture.Workspace.Project.WorkingCompositionAssetId).Virtual!.CurrentRecipeRevisionId;

        await Assert.ThrowsAsync<IOException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a"));

        Assert.Equal(originalAssets, fixture.Workspace.Project.Assets.Count);
        Assert.Equal(originalRevision, fixture.Workspace.Project.Assets.Single(asset => asset.Id == fixture.Workspace.Project.WorkingCompositionAssetId).Virtual!.CurrentRecipeRevisionId);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Workspace.Location!.RootDirectory, "assets", "audio")));
    }

    [Fact]
    public async Task CancellationLeavesProjectUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        var service = CreateService(fixture.Workspace, fixture.SourcePath, new RecordingExtractionEngine());
        var originalAssets = fixture.Workspace.Project!.Assets.Count;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DetachAsync(fixture.VideoItemId, "line.m4a", cancellation.Token));

        Assert.Equal(originalAssets, fixture.Workspace.Project.Assets.Count);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.Workspace.Location!.RootDirectory, "assets", "audio")));
    }

    private static CompositionSegmentAudioDetachmentService CreateService(
        ProjectWorkspace workspace,
        string sourcePath,
        IAudioExtractionEngine engine,
        IStreamTimingAssessmentService? timingAssessment = null) =>
        new(
            workspace,
            new StubMaterializer(sourcePath),
            engine,
            new Sha256ContentHashService(),
            new OutputInspector(),
            timingAssessment ?? new ExactOutputTimingAssessment());

    private async Task<Fixture> CreateFixtureAsync(
        bool audioLocked = false,
        bool audioEstimated = false,
        bool videoEstimated = false)
    {
        Directory.CreateDirectory(_root);
        var store = new ToggleSaveStore();
        var workspace = new ProjectWorkspace(store, new UnusedImporter());
        await workspace.CreateAsync(_root, "Detach audio");
        var sourcePath = Path.Combine(workspace.Location!.RootDirectory, "assets", "videos", "source.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3]);
        var hash = (await new Sha256ContentHashService().ComputeAsync(sourcePath)).Sha256!;
        var source = new ProjectAsset
        {
            DisplayName = "source.mp4", FileName = "source.mp4", MediaType = MediaType.Video, StorageKind = AssetStorageKind.Physical,
            Encoding = new MediaEncodingMetadata { Video = new VideoStreamMetadata { StreamIndex = 0, Codec = "h264" }, Audio = new AudioStreamMetadata { StreamIndex = 1, Codec = "aac", SampleRate = 48_000, Channels = 2 } },
            Physical = new PhysicalAssetStorage { RelativePath = "assets/videos/source.mp4", Availability = PhysicalAssetAvailability.Available, ContentIdentity = new ContentIdentity { Sha256 = hash, Status = ContentHashStatus.Verified } }
        };
        workspace.Project!.AddAsset(source);
        var composition = new ProjectAsset { DisplayName = "Working Composition", FileName = "working-composition", MediaType = MediaType.Video, StorageKind = AssetStorageKind.Virtual, Virtual = new VirtualAssetState { Kind = VirtualAssetKind.Composition }, Physical = null };
        workspace.Project.AddAsset(composition);
        workspace.Project.WorkingCompositionAssetId = composition.Id;
        var videoId = Guid.NewGuid();
        var audioId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var sourceReference = new AssetRevisionReference { AssetId = source.Id };
        var duration = new ExactTime(1, 1);
        var state = new WorkingCompositionState(
            [new CompositionVideoTrack(Guid.NewGuid(), false, true, [new CompositionVideoItem(videoId, sourceReference, 0, videoEstimated ? null : new VideoSourceRange(new VideoPresentationTime(0, 1, 30), new VideoPresentationTime(30, 1, 30)), Pin(hash, MediaType.Video, 0, duration, videoEstimated), new ExactTime(4, 1), linkId)])],
            [new CompositionAudioTrack(Guid.NewGuid(), audioLocked, false, [new CompositionAudioItem(audioId, sourceReference, 1, new AudioSourceRange(new AudioSampleTime(96_000, 48_000), new AudioSampleTime(144_000, 48_000)), Pin(hash, MediaType.Audio, 1, duration, audioEstimated), new ExactTime(4, 1), linkId, isMuted: true, gainDecibels: -3, pan: 0.25, fadeIn: new ExactTime(1, 10), fadeOut: new ExactTime(1, 5))])]);
        workspace.Project.CommitRecipe(composition.Id, new CompositionRecipe { Composition = state });
        await workspace.SaveAsync();
        return new Fixture(workspace, store, source, sourcePath, videoId, audioId);
    }

    private static StreamTimingAssessmentPin Pin(string hash, MediaType type, int stream, ExactTime duration, bool estimated = false) => new(new StreamTimingAssessment(
        Guid.NewGuid(),
        hash,
        type,
        stream,
        estimated ? TimingReadiness.Estimated : TimingReadiness.Exact,
        true,
        duration,
        estimated
            ? [type == MediaType.Audio
                ? TimingIssueClassification.UnresolvedAudioSampleBoundary
                : TimingIssueClassification.DiscontinuousTimestamps]
            : [],
        new ExactTime(0, 1)));
    private static CompositionRecipe GetCurrent(VideoProject project)
    {
        var composition = project.Assets.Single(asset => asset.Id == project.WorkingCompositionAssetId);
        return (CompositionRecipe)project.RecipeRevisions.Single(revision => revision.Id == composition.Virtual!.CurrentRecipeRevisionId).Recipe;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    private sealed record Fixture(ProjectWorkspace Workspace, ToggleSaveStore Store, ProjectAsset Source, string SourcePath, Guid VideoItemId, Guid AudioItemId);

    private sealed class StubMaterializer(string sourcePath) : IMediaMaterializer
    {
        public Task<MaterializedMediaLease> MaterializeAsync(VideoProject project, ProjectLocation location, MaterializationRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new MaterializedMediaLease(sourcePath, new ContentIdentity { Sha256 = new string('A', 64), Status = ContentHashStatus.Verified }, null, true));
    }
    private sealed class RecordingExtractionEngine : IAudioExtractionEngine
    {
        public int CallCount { get; private set; }
        public int AudioStreamIndex { get; private set; }
        public AudioSourceRange? Range { get; private set; }
        public Task ExtractToM4aAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task ExtractExactRangeToM4aAsync(string inputPath, string outputPath, int audioStreamIndex, AudioSourceRange sourceRange, CancellationToken cancellationToken = default)
        {
            CallCount++; AudioStreamIndex = audioStreamIndex; Range = sourceRange;
            await File.WriteAllBytesAsync(outputPath, [4, 5, 6, 7], cancellationToken);
        }
    }
    private sealed class ThrowingExtractionEngine : IAudioExtractionEngine
    {
        public Task ExtractToM4aAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExtractExactRangeToM4aAsync(string inputPath, string outputPath, int audioStreamIndex, AudioSourceRange sourceRange, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Extraction failed.");
    }
    private sealed class CollidingExtractionEngine(string destinationFileName) : IAudioExtractionEngine
    {
        public Task ExtractToM4aAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task ExtractExactRangeToM4aAsync(string inputPath, string outputPath, int audioStreamIndex, AudioSourceRange sourceRange, CancellationToken cancellationToken = default)
        {
            await File.WriteAllBytesAsync(outputPath, [4, 5, 6, 7], cancellationToken);
            var destination = Path.Combine(Path.GetDirectoryName(outputPath)!, destinationFileName);
            await File.WriteAllBytesAsync(destination, [9, 9, 9], cancellationToken);
        }
    }
    private sealed class OutputInspector : IMediaInspectionService
    {
        public Task<MediaEncodingMetadata> InspectAsync(string mediaPath, CancellationToken cancellationToken = default) => Task.FromResult(new MediaEncodingMetadata { ContainerFormat = "mov,mp4,m4a,3gp,3g2,mj2", Audio = new AudioStreamMetadata { StreamIndex = 0, Codec = "aac", SampleRate = 48_000, Channels = 2 } });
    }
    private sealed class ExactOutputTimingAssessment : IStreamTimingAssessmentService
    {
        public Task<StreamTimingAssessmentResult> AssessAsync(StreamTimingAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampleRate = request.SelectedStream.SampleRate!.Value;
            var range = new AudioSourceRange(new AudioSampleTime(0, sampleRate), new AudioSampleTime(sampleRate, sampleRate));
            var assessment = new StreamTimingAssessment(
                Guid.NewGuid(), request.SourceContentHash, MediaType.Audio, request.SelectedStream.StreamIndex,
                TimingReadiness.Exact, true, range.Duration, [], new ExactTime(0, 1));
            return Task.FromResult(new StreamTimingAssessmentResult(assessment, audioFullRange: range));
        }
    }
    private sealed class EstimatedOutputTimingAssessment : IStreamTimingAssessmentService
    {
        public Task<StreamTimingAssessmentResult> AssessAsync(StreamTimingAssessmentRequest request, CancellationToken cancellationToken = default)
        {
            var assessment = new StreamTimingAssessment(
                Guid.NewGuid(), request.SourceContentHash, MediaType.Audio, request.SelectedStream.StreamIndex,
                TimingReadiness.Estimated, true, new ExactTime(1, 1),
                [TimingIssueClassification.UnresolvedAudioPrimingOrPadding], new ExactTime(0, 1));
            return Task.FromResult(new StreamTimingAssessmentResult(assessment));
        }
    }
    private sealed class UnusedImporter : IAssetImportService
    {
        public Task<IReadOnlyList<ProjectAsset>> ImportAsync(ProjectLocation location, IEnumerable<string> sourcePaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ToggleSaveStore : IProjectStore
    {
        private readonly PortableProjectStore _inner = new();
        public bool FailSaves { get; set; }
        public Task<(VideoProject Project, ProjectLocation Location)> CreateAsync(string rootDirectory, string name, CancellationToken cancellationToken = default) => _inner.CreateAsync(rootDirectory, name, cancellationToken);
        public Task<(VideoProject Project, ProjectLocation Location)> OpenAsync(string projectFilePath, CancellationToken cancellationToken = default) => _inner.OpenAsync(projectFilePath, cancellationToken);
        public Task SaveAsync(VideoProject project, ProjectLocation location, CancellationToken cancellationToken = default)
        {
            if (FailSaves) throw new IOException("Save failed.");
            return _inner.SaveAsync(project, location, cancellationToken);
        }
    }
}
