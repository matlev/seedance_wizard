using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.Application;

/// <summary>
/// Compatibility surface for exact-occurrence audio detach. The legacy command derived a
/// floating timeline start and therefore must not create media or mutate a composition.
/// </summary>
public sealed class CompositionSegmentAudioDetachmentService
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly IAudioExtractionEngine _extractionEngine;
    private readonly IContentHashService _contentHashService;
    private readonly IMediaInspectionService _mediaInspector;
    private readonly IStreamTimingAssessmentService _timingAssessment;
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionSegmentAudioDetachmentService(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IAudioExtractionEngine extractionEngine,
        IContentHashService contentHashService,
        IMediaInspectionService mediaInspector,
        IStreamTimingAssessmentService timingAssessment)
    {
        _workspace = workspace;
        _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));
        _extractionEngine = extractionEngine ?? throw new ArgumentNullException(nameof(extractionEngine));
        _contentHashService = contentHashService ?? throw new ArgumentNullException(nameof(contentHashService));
        _mediaInspector = mediaInspector ?? throw new ArgumentNullException(nameof(mediaInspector));
        _timingAssessment = timingAssessment ?? throw new ArgumentNullException(nameof(timingAssessment));
        _current = new CompositionCurrentAccessor(workspace);
        _editor = new TransactionalCompositionRevisionEditor(workspace, _current);
    }

    /// <summary>Returns whether this linked video occurrence can be detached without weakening exact timing meaning.</summary>
    public bool CanDetach(Guid videoItemId)
    {
        try
        {
            var project = _current.Project;
            var (_, _, recipe) = _current.GetCurrent();
            if (!TryGetEligibility(recipe.Composition, videoItemId, out var eligibility))
                return false;
            var source = project.Assets.SingleOrDefault(asset => asset.Id == eligibility.Video.Source.AssetId);
            return source is not null && IsVerifiedPhysicalSource(source);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public async Task<DetachedCompositionAudioResult> DetachAsync(
        Guid segmentId,
        string requestedFileName,
        CancellationToken cancellationToken = default)
    {
        var project = _current.Project;
        var location = _workspace.Location ?? throw new InvalidOperationException("The open project has no location.");
        var (compositionAsset, compositionRevision, recipe) = _current.GetCurrent();
        if (!TryGetEligibility(recipe.Composition, segmentId, out var eligibility))
            throw new InvalidOperationException("Detach audio requires a linked video occurrence and one exact unlocked audio occurrence.");

        var source = project.Assets.SingleOrDefault(asset => asset.Id == eligibility.Video.Source.AssetId)
            ?? throw new InvalidOperationException("The linked source media no longer exists.");
        RequireVerifiedPhysicalSource(source);
        var fileName = MediaFileNamePolicy.ValidateRequiredExtension(
            requestedFileName, ".m4a", "Detached audio", nameof(requestedFileName));
        var audioDirectory = Path.GetFullPath(Path.Combine(location.RootDirectory, "assets", "audio"));
        Directory.CreateDirectory(audioDirectory);
        var finalPath = CollisionFreeDestinationPolicy.GetAvailablePath(audioDirectory, fileName);
        using var fileCommit = AtomicFileCommit.Create(finalPath, "detach-audio", ".m4a");
        ProjectAsset? detached = null;
        ContentIdentity? publishedIdentity = null;
        var committed = false;
        try
        {
            await using (var media = await _materializer.MaterializeAsync(
                             project,
                             location,
                             new MaterializationRequest(
                                 new AssetMaterializationTarget(eligibility.Video.Source.AssetId, eligibility.Video.Source.RecipeRevisionId),
                                 MaterializationPurpose.FinalExport,
                             MaterializationRetentionPreference.NormalCache),
                             cancellationToken).ConfigureAwait(false))
            {
                var verified = await _contentHashService.VerifyAsync(
                    media.Path, source.Physical!.ContentIdentity, cancellationToken).ConfigureAwait(false);
                if (!verified.MatchesExpected)
                    throw new InvalidOperationException("The linked source bytes no longer match the verified project media identity. Relink the source before detaching audio.");
                await _extractionEngine.ExtractExactRangeToM4aAsync(
                    media.Path,
                    fileCommit.TemporaryPath,
                    eligibility.Audio.SelectedStreamIndex,
                    eligibility.Audio.SourceRange!,
                    cancellationToken).ConfigureAwait(false);
            }

            var encoding = await _mediaInspector.InspectAsync(fileCommit.TemporaryPath, cancellationToken).ConfigureAwait(false);
            ValidateExtractedEncoding(encoding, eligibility.Audio.SourceRange!);
            var identity = await _contentHashService.ComputeAsync(fileCommit.TemporaryPath, cancellationToken).ConfigureAwait(false);
            var outputTiming = await _timingAssessment.AssessAsync(
                new StreamTimingAssessmentRequest(
                    fileCommit.TemporaryPath,
                    identity,
                    MediaType.Audio,
                    encoding),
                cancellationToken).ConfigureAwait(false);
            ValidateOutputTiming(outputTiming, eligibility.Audio.SourceRange!);
            fileCommit.Commit();
            publishedIdentity = identity;

            detached = CreateDetachedAsset(
                source,
                location,
                finalPath,
                identity,
                encoding,
                compositionAsset.Id,
                compositionRevision.Id,
                eligibility,
                outputTiming);
            var update = await _editor.UpdateIfCurrentAsync(
                project,
                location,
                state => RebindDetachedOccurrence(state, eligibility, detached, outputTiming.AudioFullRange!),
                () => project.AddAsset(detached),
                () => project.Assets.Remove(detached),
                cancellationToken).ConfigureAwait(false);
            if (update.Failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(update.Failure).Throw();
            if (!update.Committed)
                throw new OperationCanceledException("Audio detachment did not commit because the active project changed or the operation was cancelled.", cancellationToken);

            committed = true;
            return new DetachedCompositionAudioResult(detached, update.Revision!, eligibility.Audio.Id,
                TimeSpan.FromSeconds(eligibility.Audio.CompositionStart.ToDoubleSeconds()));
        }
        finally
        {
            if (!committed && fileCommit.IsCommitted && publishedIdentity is not null)
                await DeleteCommittedOutputIfOwnedAsync(finalPath, publishedIdentity).ConfigureAwait(false);
        }
    }

    private static bool TryGetEligibility(WorkingCompositionState state, Guid videoItemId, out DetachmentEligibility eligibility)
    {
        eligibility = default!;
        var videoTrack = state.VideoTracks.SingleOrDefault(track => track.Items.Any(item => item.Id == videoItemId));
        var video = videoTrack?.Items.Single(item => item.Id == videoItemId);
        if (videoTrack is null || video is null || video.LinkGroupId is not { } linkGroupId || videoTrack.IsLocked)
            return false;
        var matches = state.AudioTracks
            .SelectMany(track => track.Items.Where(item => item.LinkGroupId == linkGroupId).Select(item => (Track: track, Item: item)))
            .ToArray();
        if (matches.Length != 1 || matches[0].Track.IsLocked)
            return false;
        var audio = matches[0].Item;
        if (audio.TimingAssessment.Readiness != TimingReadiness.Exact || audio.SourceRange is null ||
            video.Source != audio.Source)
            return false;
        eligibility = new DetachmentEligibility(video, audio);
        return true;
    }

    private static void RequireVerifiedPhysicalSource(ProjectAsset source)
    {
        if (!IsVerifiedPhysicalSource(source))
            throw new InvalidOperationException("Detach audio requires verified physical source media.");
    }

    private static bool IsVerifiedPhysicalSource(ProjectAsset source) =>
        !source.IsDeleted &&
        source.StorageKind == AssetStorageKind.Physical &&
        source.Physical?.ContentIdentity is { Status: ContentHashStatus.Verified, Sha256: { Length: 64 } };

    private static void ValidateExtractedEncoding(MediaEncodingMetadata encoding, AudioSourceRange range)
    {
        if (encoding.Audio is not { StreamIndex: { } streamIndex, SampleRate: { } sampleRate } ||
            encoding.Video is not null || streamIndex < 0 || sampleRate != range.Start.SampleRate)
            throw new InvalidDataException("Detached audio is not an inspectable audio-only file with the required sample rate.");
    }

    private static void ValidateOutputTiming(StreamTimingAssessmentResult outputTiming, AudioSourceRange requestedRange)
    {
        if (outputTiming.Assessment.Readiness != TimingReadiness.Exact ||
            outputTiming.AudioFullRange is not { } outputRange ||
            outputRange.Duration != requestedRange.Duration)
        {
            throw new InvalidDataException(
                "ReelForge could not establish an exact detached-audio span matching the selected occurrence. The original occurrence was not changed.");
        }
    }

    private static ProjectAsset CreateDetachedAsset(
        ProjectAsset source,
        ProjectLocation location,
        string finalPath,
        ContentIdentity identity,
        MediaEncodingMetadata encoding,
        Guid compositionAssetId,
        Guid compositionRevisionId,
        DetachmentEligibility eligibility,
        StreamTimingAssessmentResult outputTiming)
    {
        if (identity.Status != ContentHashStatus.Verified || string.IsNullOrWhiteSpace(identity.Sha256))
            throw new InvalidDataException("Detached audio did not produce a verified SHA-256 content identity.");

        var audio = eligibility.Audio;
        var range = audio.SourceRange!;
        var asset = new ProjectAsset
        {
            DisplayName = Path.GetFileName(finalPath),
            FileName = Path.GetFileName(finalPath),
            MediaType = MediaType.Audio,
            StorageKind = AssetStorageKind.Physical,
            Origin = AssetOrigin.ExtractedAudio,
            DurationSeconds = range.Duration.ToDoubleSeconds(),
            Encoding = encoding,
            Provenance = new AssetProvenance
            {
                Operation = "detach-audio",
                SourceAssetIds = [source.Id],
                SourceRecipeRevisionId = audio.Source.RecipeRevisionId,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["format"] = "m4a",
                    ["audioStreamIndex"] = audio.SelectedStreamIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["startSample"] = range.Start.SampleFrameOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["endSample"] = range.End.SampleFrameOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["sampleRate"] = range.Start.SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["compositionAssetId"] = compositionAssetId.ToString("D"),
                    ["compositionRevisionId"] = compositionRevisionId.ToString("D"),
                    ["videoItemId"] = eligibility.Video.Id.ToString("D"),
                    ["audioItemId"] = audio.Id.ToString("D"),
                    ["linkGroupId"] = eligibility.Video.LinkGroupId!.Value.ToString("D")
                }
            },
            Physical = new PhysicalAssetStorage
            {
                RelativePath = ProjectPathPolicy.GetRelativePath(location, finalPath),
                Durability = PhysicalAssetDurability.Promoted,
                Availability = PhysicalAssetAvailability.Available,
                ContentIdentity = identity
            },
            Virtual = null
        };
        asset.SetTimingAssessment(outputTiming.Assessment);
        return asset;
    }

    private static WorkingCompositionState RebindDetachedOccurrence(
        WorkingCompositionState state,
        DetachmentEligibility captured,
        ProjectAsset detached,
        AudioSourceRange detachedRange)
    {
        if (!TryGetEligibility(state, captured.Video.Id, out var eligibility) ||
            !MatchesCapturedOccurrence(eligibility, captured))
            throw new InvalidOperationException("The linked timeline occurrences changed before audio could be detached.");
        var originalAudio = eligibility.Audio;
        var detachedAssessment = detached.TimingAssessments.Single(assessment => assessment.MediaType == MediaType.Audio);
        var replacementAudio = new CompositionAudioItem(
            originalAudio.Id,
            new AssetRevisionReference { AssetId = detached.Id },
            detachedAssessment.SelectedStreamIndex!.Value,
            detachedRange,
            detachedAssessment.CreatePlacementPin(),
            originalAudio.CompositionStart,
            null,
            originalAudio.IsMuted,
            originalAudio.GainDecibels,
            originalAudio.Pan,
            originalAudio.FadeIn,
            originalAudio.FadeOut);
        return new WorkingCompositionState(
            state.VideoTracks.Select(track => new CompositionVideoTrack(track.Id, track.IsLocked, track.IsVisible,
                track.Items.Select(item => item.Id == captured.Video.Id
                    ? new CompositionVideoItem(item.Id, item.Source, item.SelectedStreamIndex, item.SourceRange, item.TimingAssessment, item.CompositionStart, null)
                    : item), track.Name)),
            state.AudioTracks.Select(track => new CompositionAudioTrack(track.Id, track.IsLocked, track.IsMuted,
                track.Items.Select(item => item.Id == captured.Audio.Id ? replacementAudio : item), track.Name)));
    }

    private static bool MatchesCapturedOccurrence(DetachmentEligibility current, DetachmentEligibility captured)
    {
        var currentRange = current.Audio.SourceRange!;
        var capturedRange = captured.Audio.SourceRange!;
        return current.Video.Source == captured.Video.Source &&
               current.Video.LinkGroupId == captured.Video.LinkGroupId &&
               current.Audio.Id == captured.Audio.Id &&
               current.Audio.Source == captured.Audio.Source &&
               current.Audio.SelectedStreamIndex == captured.Audio.SelectedStreamIndex &&
               current.Audio.LinkGroupId == captured.Audio.LinkGroupId &&
               current.Audio.TimingAssessment.AssessmentId == captured.Audio.TimingAssessment.AssessmentId &&
               current.Audio.CompositionStart == captured.Audio.CompositionStart &&
               currentRange.Start.SampleFrameOffset == capturedRange.Start.SampleFrameOffset &&
               currentRange.End.SampleFrameOffset == capturedRange.End.SampleFrameOffset &&
               currentRange.Start.SampleRate == capturedRange.Start.SampleRate;
    }

    private async Task DeleteCommittedOutputIfOwnedAsync(string path, ContentIdentity expectedIdentity)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var verification = await _contentHashService.VerifyAsync(path, expectedIdentity, CancellationToken.None)
                .ConfigureAwait(false);
            if (verification.MatchesExpected)
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup must not hide the original cancellation or save failure. A path that cannot
            // be verified as this operation's output is preserved instead of risking unrelated data.
        }
    }

    private sealed record DetachmentEligibility(CompositionVideoItem Video, CompositionAudioItem Audio);
}

public sealed record DetachedCompositionAudioResult(
    ProjectAsset AudioAsset,
    RecipeRevision CompositionRevision,
    Guid AudioClipId,
    TimeSpan TimelineStart);
