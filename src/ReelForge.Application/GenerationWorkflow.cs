using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed class GenerationWorkflow
{
    private readonly ProjectWorkspace _workspace;
    private readonly GenerationSubmissionService _submissionService;
    private readonly GenerationMonitor _monitor;

    public GenerationWorkflow(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IGeneratedOutputIngestionService outputIngestion,
        IProviderAssetPreparationService? providerPreparation = null)
    {
        _workspace = workspace;
        var referencePreparer = new GenerationReferencePreparer(workspace, materializer, providerPreparation);
        _submissionService = new GenerationSubmissionService(workspace, referencePreparer);
        _monitor = new GenerationMonitor(workspace, outputIngestion);
    }

    public async Task SaveDraftAsync(GenerationDraft draft, CancellationToken cancellationToken = default)
    {
        EnsureProjectOpen();
        ArgumentNullException.ThrowIfNull(draft);
        draft.ModifiedAt = DateTimeOffset.UtcNow;
        _workspace.Project!.CurrentGenerationDraft = CloneDraft(draft);
        await _workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationRecord> RunAsync(
        IVideoGenerationProvider provider,
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization,
        GenerationWorkflowOptions? options = null,
        IProgress<GenerationWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var record = await SubmitAsync(provider, draft, authorization, progress, cancellationToken)
            .ConfigureAwait(false);
        if (provider is not IAsyncVideoGenerationProvider asyncProvider ||
            string.IsNullOrWhiteSpace(record.ProviderJobId) ||
            record.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
            return record;

        return await ResumeMonitoringAsync(
                asyncProvider,
                record,
                options ?? new GenerationWorkflowOptions(),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<GenerationRecord> SubmitAsync(
        IVideoGenerationProvider provider,
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization,
        IProgress<GenerationWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var record = await QueueAsync(provider, draft, authorization, cancellationToken).ConfigureAwait(false);
        return await SubmitQueuedAsync(provider, record, authorization, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationRecord> QueueAsync(
        IVideoGenerationProvider provider,
        GenerationDraft draft,
        GenerationSubmissionAuthorization? authorization,
        CancellationToken cancellationToken = default)
        => await _submissionService.QueueAsync(provider, draft, authorization, cancellationToken)
            .ConfigureAwait(false);

    public async Task<GenerationRecord> SubmitQueuedAsync(
        IVideoGenerationProvider provider,
        GenerationRecord record,
        GenerationSubmissionAuthorization? authorization,
        IProgress<GenerationWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await _submissionService.SubmitQueuedAsync(
                provider, record, authorization, progress, cancellationToken)
            .ConfigureAwait(false);

    public async Task<GenerationRecord> ResumeMonitoringAsync(
        IAsyncVideoGenerationProvider provider,
        GenerationRecord record,
        GenerationWorkflowOptions? options = null,
        IProgress<GenerationWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectOpen();
        if (string.IsNullOrWhiteSpace(record.ProviderJobId))
            throw new InvalidOperationException("The selected generation has no remote job ID to monitor.");
        if (!record.RequestSnapshot.ProviderId.Equals(provider.Capabilities.ProviderId, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected provider does not own this generation job.");
        if (record.Status is GenerationStatus.Failed or GenerationStatus.Cancelled ||
            record.IngestionStatus == OutputIngestionStatus.Succeeded)
            return record;

        try
        {
            return await _monitor.MonitorAsync(
                    provider,
                    record,
                    options ?? new GenerationWorkflowOptions(),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            record.ResponseMetadata["localMonitoring"] = "stopped-by-user";
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch (Exception exception)
        {
            record.ResponseMetadata["localMonitoring"] = "error";
            record.Error = new GenerationError
            {
                ProviderCode = "local_monitoring_failed",
                Message = exception.Message,
                TechnicalDetails = exception.GetType().FullName
            };
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
    }

    public static GenerationDraft CreateDerivedDraft(
        GenerationRecord source,
        GenerationRelationshipType relationshipType) =>
        GenerationRequestFactory.CreateDerivedDraft(source, relationshipType);

    private static GenerationDraft CloneDraft(GenerationDraft source) => new()
    {
        ProviderId = source.ProviderId,
        ModelVersion = source.ModelVersion,
        Prompt = source.Prompt,
        Mode = source.Mode,
        DurationSeconds = source.DurationSeconds,
        AspectRatio = source.AspectRatio,
        Resolution = source.Resolution,
        References = source.References.Select(reference => new GenerationReferenceDraft
        {
            ReferenceId = reference.ReferenceId,
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            AnchorRevisionId = reference.AnchorRevisionId,
            Role = reference.Role,
            Order = reference.Order,
            Label = reference.Label,
            Notes = reference.Notes
        }).ToList(),
        ProviderParameters = new Dictionary<string, string>(source.ProviderParameters, StringComparer.Ordinal),
        ParentGenerationId = source.ParentGenerationId,
        RelationshipType = source.RelationshipType,
        ModifiedAt = source.ModifiedAt
    };

    private void EnsureProjectOpen()
    {
        if (_workspace.Project is null || _workspace.Location is null)
            throw new InvalidOperationException("Create or open a project first.");
    }
}
