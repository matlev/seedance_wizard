using ReelForge.Core;

namespace ReelForge.Infrastructure;

/// <summary>Portable DTOs for current physical-stream timing evidence; no runtime/tool terminology.</summary>
internal sealed class StreamTimingAssessmentDto
{
    public string SchemaIdentity { get; set; } = string.Empty;
    public Guid AssessmentId { get; set; }
    public string SourceContentHash { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public int? SelectedStreamIndex { get; set; }
    public TimingReadiness Readiness { get; set; }
    public bool HasUsableSequentialDecodePath { get; set; }
    public ExactTimeDto? TimelineDuration { get; set; }
    public ExactTimeDto? SourcePresentationStart { get; set; }
    public List<TimingIssueClassification> IssueClassifications { get; set; } = [];
}

/// <summary>A rational number of seconds, reusable by later persisted timing pins.</summary>
internal sealed class ExactTimeDto
{
    public long Numerator { get; set; }
    public long Denominator { get; set; }
}

internal sealed class TimingAssessmentAcknowledgementDto
{
    public Guid AssessmentId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
}
