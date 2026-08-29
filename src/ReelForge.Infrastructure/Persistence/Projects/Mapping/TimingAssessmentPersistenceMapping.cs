using ReelForge.Core;

namespace ReelForge.Infrastructure;

internal static partial class ProjectPersistenceMapper
{
    private static StreamTimingAssessmentDto ToDto(StreamTimingAssessment source) => new()
    {
        SchemaIdentity = source.SchemaIdentity,
        AssessmentId = source.AssessmentId,
        SourceContentHash = source.SourceContentHash,
        MediaType = source.MediaType,
        SelectedStreamIndex = source.SelectedStreamIndex,
        Readiness = source.Readiness,
        HasUsableSequentialDecodePath = source.HasUsableSequentialDecodePath,
        TimelineDuration = ToDto(source.TimelineDuration),
        SourcePresentationStart = ToDto(source.SourcePresentationStart),
        IssueClassifications = source.IssueClassifications.ToList()
    };

    private static StreamTimingAssessment FromDto(StreamTimingAssessmentDto source)
    {
        if (source is null)
            throw new InvalidDataException("A timing assessment entry cannot be null.");
        try
        {
            return new StreamTimingAssessment(
                source.AssessmentId,
                source.SchemaIdentity,
                source.SourceContentHash,
                source.MediaType,
                source.SelectedStreamIndex,
                source.Readiness,
                source.HasUsableSequentialDecodePath,
                FromDto(source.TimelineDuration),
                source.IssueClassifications ?? throw new InvalidDataException("Timing assessment issue classifications are required."),
                FromDto(source.SourcePresentationStart));
        }
        catch (InvalidDataException) { throw; }
        catch (ArgumentException exception) { throw new InvalidDataException("The timing assessment payload is invalid.", exception); }
        catch (OverflowException exception) { throw new InvalidDataException("The timing assessment exact-time payload is invalid.", exception); }
    }

    private static ExactTimeDto? ToDto(ExactTime? source) => source is null ? null : new()
    {
        Numerator = source.Numerator,
        Denominator = source.Denominator
    };

    private static ExactTime? FromDto(ExactTimeDto? source)
    {
        if (source is null) return null;
        try { return new ExactTime(source.Numerator, source.Denominator); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("The timing assessment exact-time denominator is invalid.", exception); }
        catch (OverflowException exception) { throw new InvalidDataException("The timing assessment exact-time value is invalid.", exception); }
    }

    private static TimingAssessmentAcknowledgementDto ToDto(TimingAssessmentAcknowledgement source) => new()
    {
        AssessmentId = source.AssessmentId,
        AcknowledgedAt = source.AcknowledgedAt
    };

    private static TimingAssessmentAcknowledgement FromDto(TimingAssessmentAcknowledgementDto source)
    {
        if (source is null)
            throw new InvalidDataException("A timing assessment acknowledgement entry cannot be null.");
        try { return new TimingAssessmentAcknowledgement(source.AssessmentId, source.AcknowledgedAt); }
        catch (ArgumentException exception) { throw new InvalidDataException("The timing assessment acknowledgement payload is invalid.", exception); }
    }
}
