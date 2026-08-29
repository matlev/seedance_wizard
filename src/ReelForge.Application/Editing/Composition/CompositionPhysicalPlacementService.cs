using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

/// <summary>
/// Places a verified physical source using independently frozen stream-timing evidence.
/// Media paths are operational inputs only; the committed occurrence retains no path or engine detail.
/// </summary>
public sealed class CompositionPhysicalPlacementService
{
    private static readonly ExactTime Zero = new(0, 1);
    private readonly ProjectWorkspace _workspace;
    private readonly IStreamTimingAssessmentService _timing;
    private readonly IContentHashService _contentHash;
    private readonly ICompositionPlacementDecisionProvider _decisions;
    private readonly CompositionCurrentAccessor _current;
    private readonly TransactionalCompositionRevisionEditor _editor;

    public CompositionPhysicalPlacementService(
        ProjectWorkspace workspace,
        IStreamTimingAssessmentService timing,
        IContentHashService contentHash,
        ICompositionPlacementDecisionProvider decisions)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _timing = timing ?? throw new ArgumentNullException(nameof(timing));
        _contentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _current = new CompositionCurrentAccessor(workspace);
        _editor = new TransactionalCompositionRevisionEditor(workspace, _current);
    }

    public async Task<CompositionPhysicalPlacementResult> PlaceAsync(
        CompositionPhysicalPlacementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var project = _workspace.Project ?? throw new InvalidOperationException("Open a project first.");
        var location = _workspace.Location ?? throw new InvalidOperationException("Open a project first.");
        var source = RequirePhysicalSource(project, request.SourceAssetId);
        var current = _current.GetCurrent();
        ValidateInitialTargetTracks(current.Recipe.Composition, source, request);

        StreamTimingAssessmentResult? videoResult = null;
        StreamTimingAssessmentResult? audioResult = null;
        try
        {
            var path = _workspace.GetAbsoluteAssetPath(source);
            if (source.MediaType == MediaType.Video)
            {
                videoResult = await AssessAsync(source, path, MediaType.Video, cancellationToken).ConfigureAwait(false);
                if (source.Encoding!.Audio is not null)
                    audioResult = await AssessAsync(source, path, MediaType.Audio, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                audioResult = await AssessAsync(source, path, MediaType.Audio, cancellationToken).ConfigureAwait(false);
            }

            var verified = await _contentHash.VerifyAsync(path, source.Physical!.ContentIdentity, cancellationToken).ConfigureAwait(false);
            if (!verified.MatchesExpected)
                return CompositionPhysicalPlacementResult.Blocked("The source file no longer matches this project's verified media. Relink the verified source or import these bytes as new media.", videoResult?.Assessment, audioResult?.Assessment);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Cancelled, "Timing assessment was cancelled before any timeline occurrence was created.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return CompositionPhysicalPlacementResult.Blocked("The verified source file is unavailable for timeline placement. Relink it and try again.", videoResult?.Assessment, audioResult?.Assessment);
        }

        var video = videoResult?.Assessment;
        var audio = audioResult?.Assessment;
        PersistResult assessmentSave;
        try
        {
            assessmentSave = await PersistAssessmentsAsync(project, location, source, video, audio, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Cancelled, "Timing assessment persistence was cancelled; no occurrence was created.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
        }
        if (assessmentSave == PersistResult.Stale)
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Stale, "The active project changed while timing was being assessed.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
        if (assessmentSave == PersistResult.Failed)
            return CompositionPhysicalPlacementResult.Blocked("ReelForge could not save the timing assessment, so nothing was placed.", video, audio);
        if (!ReferenceEquals(_workspace.Project, project) || _workspace.Location != location)
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Stale, "The active project changed while timing was being assessed.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
        if (source.MediaType == MediaType.Video && video!.Readiness == TimingReadiness.Unusable)
            return CompositionPhysicalPlacementResult.Blocked("Video placement is unavailable because ReelForge could not establish a usable stream with a finite timeline span.", video, audio);
        if (source.MediaType == MediaType.Audio && audio!.Readiness == TimingReadiness.Unusable)
            return CompositionPhysicalPlacementResult.Blocked("Audio placement is unavailable because ReelForge could not establish a usable stream with a finite timeline span.", video, audio);

        var usableAudio = source.MediaType == MediaType.Video && audio is { CanPlace: true };
        var unusablePresentAudio = source.MediaType == MediaType.Video && audio?.Readiness == TimingReadiness.Unusable;
        var requiresAcknowledgement = HasUnacknowledgedEstimated(project, video) || HasUnacknowledgedEstimated(project, audio);
        var requiresVideoOnlyApproval = unusablePresentAudio;
        if (requiresAcknowledgement || requiresVideoOnlyApproval)
        {
            CompositionPlacementDecision decision;
            try
            {
                decision = await _decisions.DecideAsync(new CompositionPlacementDecisionRequest(
                    source, video, audio, requiresAcknowledgement, requiresVideoOnlyApproval), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Cancelled, "Placement was cancelled after timing was saved; no occurrence was created.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
            }

            if (decision.Action == CompositionPlacementAction.AttemptRepair)
                return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.RepairRequested, "Repair was requested. The original source was not changed and no occurrence was placed.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
            if (decision.Action == CompositionPlacementAction.Cancel)
                return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Cancelled, "Placement was cancelled; the timing assessment remains available for later use.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
            if (decision.Action != CompositionPlacementAction.Place)
                return CompositionPhysicalPlacementResult.Blocked("ReelForge did not receive a valid placement decision.", video, audio);
            if ((requiresAcknowledgement && !decision.AcknowledgeEstimatedTiming) ||
                (requiresVideoOnlyApproval && !decision.ApproveVideoOnlyWithoutUsableAudio))
                return CompositionPhysicalPlacementResult.Blocked("Placement requires explicit acknowledgement of the reported timing condition.", video, audio);

        }

        if (!ReferenceEquals(_workspace.Project, project) || _workspace.Location != location)
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Stale, "The active project changed before placement could be committed.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);

        try
        {
            var path = _workspace.GetAbsoluteAssetPath(source);
            var verified = await _contentHash.VerifyAsync(path, source.Physical!.ContentIdentity, cancellationToken).ConfigureAwait(false);
            if (!verified.MatchesExpected)
                return CompositionPhysicalPlacementResult.Blocked("The source file changed before placement could be committed. Relink the verified source or import these bytes as new media.", video, audio);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Cancelled, "Placement was cancelled before an occurrence was created.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return CompositionPhysicalPlacementResult.Blocked("The verified source file became unavailable before placement could be committed. Relink it and try again.", video, audio);
        }

        if (!ReferenceEquals(_workspace.Project, project) || _workspace.Location != location)
            return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Stale, "The active project changed before placement could be committed.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);

        var acknowledgementIds = new[] { video, audio }
            .Where(assessment => assessment?.Readiness == TimingReadiness.Estimated && !HasAcknowledgement(project, assessment!.AssessmentId))
            .Select(assessment => assessment!.AssessmentId)
            .ToArray();
        List<TimingAssessmentAcknowledgement>? originalAcknowledgements = null;
        var originalModifiedAt = default(DateTimeOffset);

        var videoItemId = source.MediaType == MediaType.Video ? Guid.NewGuid() : (Guid?)null;
        var audioItemId = source.MediaType == MediaType.Audio || usableAudio ? Guid.NewGuid() : (Guid?)null;
        var linkGroupId = videoItemId.HasValue && audioItemId.HasValue ? Guid.NewGuid() : (Guid?)null;
        var update = await _editor.UpdateIfCurrentAsync(
            project,
            location,
            state =>
            {
                ValidateCommitTargetTracks(state, source, request, usableAudio);
                return AddOccurrences(state, request, source, videoResult, audioResult, videoItemId, audioItemId, linkGroupId);
            },
            () =>
            {
                originalAcknowledgements = project.TimingAssessmentAcknowledgements.ToList();
                originalModifiedAt = project.ModifiedAt;
                foreach (var assessmentId in acknowledgementIds)
                {
                    if (!HasAcknowledgement(project, assessmentId))
                        project.AcknowledgeEstimatedTimingAssessment(assessmentId, DateTimeOffset.UtcNow);
                }
            },
            () =>
            {
                if (originalAcknowledgements is not null)
                    RestoreAcknowledgements(project, originalAcknowledgements, originalModifiedAt);
            },
            cancellationToken).ConfigureAwait(false);
        if (update.Failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(update.Failure).Throw();
        if (!update.Committed)
        {
            return cancellationToken.IsCancellationRequested
                ? new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Cancelled, "Placement was cancelled and no occurrence was created.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness)
                : new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Stale, "The active project changed before placement could be committed.", VideoReadiness: video?.Readiness, AudioReadiness: audio?.Readiness);
        }
        return new CompositionPhysicalPlacementResult(CompositionPhysicalPlacementStatus.Placed, "Placed using the persisted timing assessment.", update.Revision, videoItemId, audioItemId, linkGroupId, video?.Readiness, audio?.Readiness, video?.IssueClassifications, audio?.IssueClassifications);
    }

    private async Task<StreamTimingAssessmentResult> AssessAsync(ProjectAsset source, string path, MediaType type, CancellationToken token)
    {
        var prior = source.TimingAssessments.SingleOrDefault(assessment => assessment.MediaType == type);
        return await _timing.AssessAsync(new StreamTimingAssessmentRequest(path, source.Physical!.ContentIdentity, type, source.Encoding!, prior), token).ConfigureAwait(false);
    }

    private async Task<PersistResult> PersistAssessmentsAsync(VideoProject project, ProjectLocation location, ProjectAsset source, StreamTimingAssessment? video, StreamTimingAssessment? audio, CancellationToken token)
    {
        if ((video is null || source.TimingAssessments.Any(existing => existing.MediaType == MediaType.Video && existing.AssessmentId == video.AssessmentId)) &&
            (audio is null || source.TimingAssessments.Any(existing => existing.MediaType == MediaType.Audio && existing.AssessmentId == audio.AssessmentId)))
            return PersistResult.Committed;

        var original = source.TimingAssessments.ToList();
        var originalModifiedAt = project.ModifiedAt;
        var save = await _workspace.SaveMutationIfCurrentAsync(project, location,
            () =>
            {
                if (video is not null) source.SetTimingAssessment(video);
                if (audio is not null) source.SetTimingAssessment(audio);
                project.Touch();
            },
            () =>
            {
                source.TimingAssessments = original;
                project.ModifiedAt = originalModifiedAt;
                return Task.CompletedTask;
            }, token).ConfigureAwait(false);
        if (!save.Committed && token.IsCancellationRequested)
            throw new OperationCanceledException(token);
        return save.Committed ? PersistResult.Committed : save.Failure is null ? PersistResult.Stale : PersistResult.Failed;
    }

    private static WorkingCompositionState AddOccurrences(WorkingCompositionState state, CompositionPhysicalPlacementRequest request, ProjectAsset source, StreamTimingAssessmentResult? videoResult, StreamTimingAssessmentResult? audioResult, Guid? videoId, Guid? audioId, Guid? linkGroupId)
    {
        var video = videoResult?.Assessment;
        var audio = audioResult?.Assessment;
        var starts = new[] { video, audio }
            .Where(assessment => assessment is { CanPlace: true })
            .Select(assessment => assessment!.SourcePresentationStart ?? Zero)
            .ToArray();
        var earliest = starts.Length == 0 ? Zero : starts.Min()!;
        ExactTime StartFor(StreamTimingAssessment assessment) => request.CompositionStart + (assessment.SourcePresentationStart is null ? Zero : assessment.SourcePresentationStart - earliest);
        var sourceReference = new AssetRevisionReference { AssetId = source.Id };
        return new WorkingCompositionState(
            state.VideoTracks.Select(track => track.Id != request.TargetTrackId ? track : new CompositionVideoTrack(track.Id, track.IsLocked, track.IsVisible,
                track.Items.Concat([new CompositionVideoItem(videoId!.Value, sourceReference, video!.SelectedStreamIndex!.Value, videoResult!.VideoFullRange, video.CreatePlacementPin(), StartFor(video), linkGroupId)]))),
            state.AudioTracks.Select(track =>
            {
                var target = source.MediaType == MediaType.Audio ? request.TargetTrackId : request.AudioTargetTrackId;
                if (track.Id != target) return track;
                return new CompositionAudioTrack(track.Id, track.IsLocked, track.IsMuted, track.Items.Concat([new CompositionAudioItem(audioId!.Value, sourceReference, audio!.SelectedStreamIndex!.Value, audioResult!.AudioFullRange, audio.CreatePlacementPin(), StartFor(audio), linkGroupId)]));
            }));
    }

    private static ProjectAsset RequirePhysicalSource(VideoProject project, Guid sourceId)
    {
        var source = project.Assets.SingleOrDefault(asset => asset.Id == sourceId) ?? throw new InvalidOperationException("The selected source no longer exists.");
        if (source.IsDeleted) throw new InvalidOperationException("A removed project media file cannot be placed.");
        if (source.StorageKind != AssetStorageKind.Physical || source.Physical?.ContentIdentity.Status != ContentHashStatus.Verified || string.IsNullOrWhiteSpace(source.Physical.ContentIdentity.Sha256))
            throw new InvalidOperationException("Timeline placement requires verified physical media.");
        if (source.Encoding is null) throw new InvalidOperationException("The selected media has no inspection metadata for timing assessment.");
        if (source.MediaType is not MediaType.Video and not MediaType.Audio) throw new InvalidOperationException("Only physical video and audio can be placed.");
        return source;
    }

    private static void ValidateInitialTargetTracks(WorkingCompositionState state, ProjectAsset source, CompositionPhysicalPlacementRequest request)
    {
        if (source.MediaType == MediaType.Audio && request.AudioTargetTrackId is not null)
            throw new InvalidOperationException("An audio target track applies only when placing video with a usable audio stream.");
        if (source.MediaType == MediaType.Video)
        {
            var track = state.VideoTracks.SingleOrDefault(track => track.Id == request.TargetTrackId)
                ?? throw new InvalidOperationException("The selected target track does not match the source media type.");
            if (track.IsLocked) throw new InvalidOperationException("Unlock the selected track before placing media.");
        }
        else
        {
            var track = state.AudioTracks.SingleOrDefault(track => track.Id == request.TargetTrackId)
                ?? throw new InvalidOperationException("The selected target track does not match the source media type.");
            if (track.IsLocked) throw new InvalidOperationException("Unlock the selected track before placing media.");
        }
        if (request.AudioTargetTrackId is { } audioId)
        {
            var audioTrack = state.AudioTracks.SingleOrDefault(track => track.Id == audioId)
                ?? throw new InvalidOperationException("The selected audio target track no longer exists.");
            if (audioTrack.IsLocked) throw new InvalidOperationException("Unlock the selected audio track before placing media.");
        }
    }

    private static void ValidateCommitTargetTracks(WorkingCompositionState state, ProjectAsset source, CompositionPhysicalPlacementRequest request, bool usableAudio)
    {
        ValidateInitialTargetTracks(state, source, request);
        if (!usableAudio) return;
        if (request.AudioTargetTrackId is not { } audioId) throw new InvalidOperationException("Choose an audio track before placing this video with audio.");
        var audio = state.AudioTracks.SingleOrDefault(track => track.Id == audioId) ?? throw new InvalidOperationException("The selected audio track no longer exists.");
        if (audio.IsLocked) throw new InvalidOperationException("Unlock the selected audio track before placing media.");
    }

    private static bool HasAcknowledgement(VideoProject project, Guid id) => project.TimingAssessmentAcknowledgements.Any(ack => ack.AssessmentId == id);
    private static bool HasUnacknowledgedEstimated(VideoProject project, StreamTimingAssessment? assessment) => assessment?.Readiness == TimingReadiness.Estimated && !HasAcknowledgement(project, assessment.AssessmentId);
    private static void RestoreAcknowledgements(VideoProject project, List<TimingAssessmentAcknowledgement> acknowledgements, DateTimeOffset modifiedAt)
    {
        project.TimingAssessmentAcknowledgements = acknowledgements;
        project.ModifiedAt = modifiedAt;
    }

    private enum PersistResult { Committed, Stale, Failed }
}
