using ReelForge.Core;

namespace ReelForge.Application;

internal sealed class GenerationSubmissionService
{
    private readonly ProjectWorkspace _workspace;
    private readonly GenerationReferencePreparer _referencePreparer;

    public GenerationSubmissionService(
        ProjectWorkspace workspace,
        GenerationReferencePreparer referencePreparer)
    {
        _workspace = workspace;
        _referencePreparer = referencePreparer;
    }

    public async Task<GenerationRecord> QueueAsync(
        IVideoGenerationProvider provider,
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization,
        CancellationToken cancellationToken)
    {
        var project = RequireProject();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(draft);
        DemandAuthorization(provider, authorization);

        var snapshot = GenerationRequestFactory.CreateSnapshot(provider, draft, project);
        var request = GenerationRequestFactory.CreateProviderRequest(snapshot, project.Assets);
        var validationErrors = provider.Capabilities.Validate(request, project.Assets);
        if (validationErrors.Count > 0) throw new GenerationValidationException(validationErrors);
        ValidateLineage(draft, project);

        var record = new GenerationRecord
        {
            RequestSnapshot = snapshot,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = GenerationStatus.Queued,
            IngestionStatus = OutputIngestionStatus.NotRequired,
            ParentGenerationId = draft.ParentGenerationId,
            RelationshipType = draft.RelationshipType
        };
        project.Generations.Add(record);
        project.CurrentGenerationDraft = null;
        await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
        return record;
    }

    public async Task<GenerationRecord> SubmitQueuedAsync(
        IVideoGenerationProvider provider,
        GenerationRecord record,
        GenerationSubmissionAuthorization? authorization,
        IProgress<GenerationWorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        var project = RequireProject();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(record);
        if (project.Generations.All(candidate => candidate.Id != record.Id))
            throw new InvalidOperationException("The queued generation does not belong to the open project.");
        if (!record.RequestSnapshot.ProviderId.Equals(provider.Capabilities.ProviderId, StringComparison.Ordinal))
            throw new InvalidOperationException("The queued generation provider does not match the selected provider.");
        if (!string.IsNullOrWhiteSpace(record.ProviderJobId) || record.Status != GenerationStatus.Queued)
            throw new InvalidOperationException("Only an unsubmitted queued generation can be sent.");
        DemandAuthorization(provider, authorization);

        var snapshot = record.RequestSnapshot;
        var request = GenerationRequestFactory.CreateProviderRequest(snapshot, project.Assets);
        try
        {
            await _referencePreparer.PrepareAsync(
                    provider, request, snapshot, authorization, record, progress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new GenerationWorkflowProgress(
                record.Status,
                record.IngestionStatus,
                "Submitting generation job…"));
            var submission = await provider
                .SubmitAsync(request, project.Assets, authorization, cancellationToken)
                .ConfigureAwait(false);
            record.ProviderJobId = submission.ProviderJobId;
            record.Status = submission.Status;
            MergeMetadata(record.ResponseMetadata, submission.ResponseMetadata);
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);

            if (record.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
                record.CompletedAt = DateTimeOffset.UtcNow;
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch (OperationCanceledException) when (record.ProviderJobId is not null)
        {
            record.ResponseMetadata["submission"] = "accepted-before-local-cancellation";
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch (OperationCanceledException)
        {
            record.Status = GenerationStatus.Cancelled;
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.Error = new GenerationError
            {
                ProviderCode = "cancelled_remote_state_unknown",
                Message = "Generation was cancelled before a remote job ID was received; remote acceptance is unknown."
            };
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch (Exception exception) when (record.ProviderJobId is not null)
        {
            record.ResponseMetadata["submissionPersistence"] = "error-after-acceptance";
            record.Error = new GenerationError
            {
                ProviderCode = "submission_persistence_failed",
                Message = exception.Message,
                TechnicalDetails = exception.GetType().FullName
            };
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch (Exception exception)
        {
            ApplyFailure(record, exception);
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
    }

    private VideoProject RequireProject() =>
        _workspace.Project is not null && _workspace.Location is not null
            ? _workspace.Project
            : throw new InvalidOperationException("Create or open a project first.");

    private static void DemandAuthorization(
        IVideoGenerationProvider provider,
        GenerationSubmissionAuthorization? authorization)
    {
        if (provider.CostBehavior != GenerationProviderCostBehavior.PotentiallyBillable) return;
        if (authorization is null)
            throw new InvalidOperationException("Potentially billable generation requires explicit user confirmation.");
        authorization.Demand(provider.Capabilities.ProviderId, allowNetworkIsolatedTest: true);
    }

    private static void ValidateLineage(GenerationDraft draft, VideoProject project)
    {
        if (draft.ParentGenerationId.HasValue != draft.RelationshipType.HasValue)
            throw new InvalidOperationException("A derived generation must pair its parent and relationship type.");
        if (draft.ParentGenerationId is { } parentId &&
            project.Generations.All(candidate => candidate.Id != parentId))
            throw new InvalidOperationException("The draft's parent generation no longer exists.");
    }

    private static void ApplyFailure(GenerationRecord record, Exception exception)
    {
        record.Status = GenerationStatus.Failed;
        record.CompletedAt = DateTimeOffset.UtcNow;
        record.Error = exception is VideoGenerationProviderException providerException
            ? new GenerationError
            {
                HttpStatus = providerException.HttpStatus,
                ProviderCode = providerException.ProviderCode,
                Message = providerException.Message,
                TechnicalDetails = providerException.TechnicalDetails ?? providerException.ToString()
            }
            : new GenerationError
            {
                ProviderCode = "generation_workflow_failed",
                Message = exception.Message,
                TechnicalDetails = exception.ToString()
            };
    }

    private static void MergeMetadata(
        Dictionary<string, string> destination,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var pair in source) destination[pair.Key] = pair.Value;
    }
}
