using ReelForge.Core;

namespace ReelForge.Application.Editing.Composition;

/// <summary>Explicit user decision made after physical source timing has been assessed.</summary>
public enum CompositionPlacementAction
{
    Place,
    AttemptRepair,
    Cancel
}

/// <summary>
/// The approval is intentionally explicit: a caller cannot infer acceptance from selecting
/// <see cref="CompositionPlacementAction.Place"/> alone.
/// </summary>
public sealed record CompositionPlacementDecision(
    CompositionPlacementAction Action,
    bool AcknowledgeEstimatedTiming = false,
    bool ApproveVideoOnlyWithoutUsableAudio = false);

public sealed record CompositionPlacementDecisionRequest(
    ProjectAsset Source,
    StreamTimingAssessment? VideoAssessment,
    StreamTimingAssessment? AudioAssessment,
    bool RequiresEstimatedTimingAcknowledgement,
    bool RequiresVideoOnlyApproval);

public interface ICompositionPlacementDecisionProvider
{
    Task<CompositionPlacementDecision> DecideAsync(
        CompositionPlacementDecisionRequest request,
        CancellationToken cancellationToken = default);
}

public enum CompositionPhysicalPlacementStatus
{
    Placed,
    Blocked,
    Cancelled,
    RepairRequested,
    Stale
}

public enum CompositionPhysicalPlacementMode
{
    AtRequestedTime,
    AppendToVideoTrack
}

/// <summary>Targeted request for placing an already-imported physical source.</summary>
public sealed record CompositionPhysicalPlacementRequest(
    Guid SourceAssetId,
    ExactTime CompositionStart,
    Guid TargetTrackId,
    Guid? AudioTargetTrackId = null,
    CompositionPhysicalPlacementMode Mode = CompositionPhysicalPlacementMode.AtRequestedTime)
{
    public void Validate()
    {
        if (SourceAssetId == Guid.Empty)
            throw new ArgumentException("A physical source asset identifier is required.", nameof(SourceAssetId));
        ArgumentNullException.ThrowIfNull(CompositionStart);
        if (CompositionStart < new ExactTime(0, 1))
            throw new ArgumentOutOfRangeException(nameof(CompositionStart), "Composition start must be nonnegative.");
        if (TargetTrackId == Guid.Empty)
            throw new ArgumentException("A target composition track identifier is required.", nameof(TargetTrackId));
        if (AudioTargetTrackId == Guid.Empty)
            throw new ArgumentException("An audio target track identifier cannot be empty.", nameof(AudioTargetTrackId));
        if (!Enum.IsDefined(Mode))
            throw new ArgumentOutOfRangeException(nameof(Mode), "A supported placement mode is required.");
    }
}

public sealed record CompositionPhysicalPlacementResult(
    CompositionPhysicalPlacementStatus Status,
    string Detail,
    RecipeRevision? Revision = null,
    Guid? VideoItemId = null,
    Guid? AudioItemId = null,
    Guid? LinkGroupId = null,
    TimingReadiness? VideoReadiness = null,
    TimingReadiness? AudioReadiness = null,
    IReadOnlyList<TimingIssueClassification>? VideoIssues = null,
    IReadOnlyList<TimingIssueClassification>? AudioIssues = null)
{
    public static CompositionPhysicalPlacementResult Blocked(
        string detail,
        StreamTimingAssessment? video = null,
        StreamTimingAssessment? audio = null) => new(
            CompositionPhysicalPlacementStatus.Blocked,
            detail,
            VideoReadiness: video?.Readiness,
            AudioReadiness: audio?.Readiness,
            VideoIssues: video?.IssueClassifications,
            AudioIssues: audio?.IssueClassifications);
}
