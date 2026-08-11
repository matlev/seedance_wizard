using System.Collections.ObjectModel;
using ReelForge.Core;

namespace ReelForge.Application;

public sealed class GenerationWorkflow
{
    private readonly ProjectWorkspace _workspace;
    private readonly IMediaMaterializer _materializer;
    private readonly IProviderAssetPreparationService? _providerPreparation;
    private readonly IGeneratedOutputIngestionService _outputIngestion;

    public GenerationWorkflow(
        ProjectWorkspace workspace,
        IMediaMaterializer materializer,
        IGeneratedOutputIngestionService outputIngestion,
        IProviderAssetPreparationService? providerPreparation = null)
    {
        _workspace = workspace;
        _materializer = materializer;
        _outputIngestion = outputIngestion;
        _providerPreparation = providerPreparation;
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
        EnsureProjectOpen();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(draft);
        options ??= new GenerationWorkflowOptions();

        if (provider.CostBehavior == GenerationProviderCostBehavior.PotentiallyBillable)
        {
            if (authorization is null)
                throw new InvalidOperationException("Potentially billable generation requires explicit user confirmation.");
            authorization.Demand(provider.Capabilities.ProviderId, allowNetworkIsolatedTest: true);
        }

        var snapshot = CreateSnapshot(provider, draft, _workspace.Project!);
        var request = CreateProviderRequest(snapshot);
        var validationErrors = provider.Capabilities.Validate(request, _workspace.Project!.Assets);
        if (validationErrors.Count > 0) throw new GenerationValidationException(validationErrors);
        ValidateLineage(draft, _workspace.Project);

        var record = new GenerationRecord
        {
            RequestSnapshot = snapshot,
            RequestedAt = DateTimeOffset.UtcNow,
            Status = GenerationStatus.Queued,
            IngestionStatus = OutputIngestionStatus.NotRequired,
            ParentGenerationId = draft.ParentGenerationId,
            RelationshipType = draft.RelationshipType
        };
        _workspace.Project.Generations.Add(record);
        _workspace.Project.CurrentGenerationDraft = null;
        await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            await PrepareReferencesAsync(provider, request, snapshot, authorization, record, progress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new GenerationWorkflowProgress(record.Status, record.IngestionStatus, "Submitting generation job…"));
            var submission = await provider
                .SubmitAsync(request, _workspace.Project.Assets, authorization, cancellationToken)
                .ConfigureAwait(false);
            record.ProviderJobId = submission.ProviderJobId;
            record.Status = submission.Status;
            MergeMetadata(record.ResponseMetadata, submission.ResponseMetadata);
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);

            if (provider is IAsyncVideoGenerationProvider asyncProvider)
            {
                return await MonitorAndIngestAsync(asyncProvider, record, options, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (record.Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled)
                record.CompletedAt = DateTimeOffset.UtcNow;
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
        catch (OperationCanceledException) when (record.ProviderJobId is not null)
        {
            record.ResponseMetadata["localMonitoring"] = "stopped-by-user";
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            progress?.Report(new GenerationWorkflowProgress(
                record.Status,
                record.IngestionStatus,
                "Local monitoring stopped. The remote job was not cancelled."));
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
        catch (Exception exception)
        {
            ApplyFailure(record, exception);
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
            return record;
        }
    }

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
            return await MonitorAndIngestAsync(
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
        GenerationRelationshipType relationshipType) => new()
    {
        ProviderId = source.RequestSnapshot.ProviderId,
        ModelVersion = source.RequestSnapshot.ModelVersion,
        Prompt = source.RequestSnapshot.Prompt,
        Mode = source.RequestSnapshot.Mode,
        DurationSeconds = source.RequestSnapshot.DurationSeconds,
        AspectRatio = source.RequestSnapshot.AspectRatio,
        Resolution = source.RequestSnapshot.Resolution,
        References = source.RequestSnapshot.References.Select(reference => new GenerationReferenceDraft
        {
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            Role = reference.Role,
            Order = reference.Order,
            Label = reference.Label,
            Notes = reference.Notes
        }).ToList(),
        ProviderParameters = new Dictionary<string, string>(source.RequestSnapshot.ProviderParameters, StringComparer.Ordinal),
        ParentGenerationId = source.Id,
        RelationshipType = relationshipType,
        ModifiedAt = DateTimeOffset.UtcNow
    };

    private async Task PrepareReferencesAsync(
        IVideoGenerationProvider provider,
        GenerationRequest request,
        GenerationRequestSnapshot snapshot,
        GenerationSubmissionAuthorization? authorization,
        GenerationRecord record,
        IProgress<GenerationWorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (snapshot.References.Count == 0 || provider.CostBehavior == GenerationProviderCostBehavior.NoCharge)
            return;
        if (_providerPreparation is null || authorization is null)
            throw new InvalidOperationException("This provider requires a configured reference preparation service.");

        foreach (var reference in snapshot.References.OrderBy(reference => reference.Order ?? int.MaxValue))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.ObjectKind != GenerationReferenceObjectKind.Asset)
                throw new NotSupportedException("Frame-anchor provider preparation arrives in Phase 2C.");

            var asset = _workspace.Project!.Assets.Single(candidate => candidate.Id == reference.LogicalObjectId);
            if (TryGetReusableProviderReference(provider.Capabilities.ProviderId, asset, reference, out var reusableReference))
            {
                request.ProviderReferenceOverrides[reference.LogicalObjectId] = reusableReference;
                record.ResponseMetadata[$"reference.{reference.LogicalObjectId:N}.preparation"] = "reused-provider-reference";
                continue;
            }

            progress?.Report(new GenerationWorkflowProgress(
                record.Status,
                record.IngestionStatus,
                "Verifying and preparing a logical reference…"));
            await using var media = await _materializer.MaterializeAsync(
                    _workspace.Project!,
                    _workspace.Location!,
                    new MaterializationRequest(
                        reference.LogicalObjectId,
                        reference.RecipeRevisionId,
                        MaterializationPurpose.ProviderUpload,
                        MaterializationRetentionPreference.Ephemeral),
                    cancellationToken)
                .ConfigureAwait(false);
            var prepared = await _providerPreparation
                .PrepareAsync(provider.Capabilities.ProviderId, reference, media, authorization, cancellationToken)
                .ConfigureAwait(false);
            request.ProviderReferenceOverrides[reference.LogicalObjectId] = prepared.ProviderRepresentation;
            record.ResponseMetadata[$"reference.{reference.LogicalObjectId:N}.preparation"] =
                prepared.Receipt?.ProviderScope ?? "prepared";
        }

        await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static bool TryGetReusableProviderReference(
        string providerId,
        ProjectAsset asset,
        GenerationReferenceSnapshot logicalReference,
        out string providerRepresentation)
    {
        providerRepresentation = string.Empty;
        if (!asset.ProviderReferences.TryGetValue(providerId, out var reference) ||
            string.IsNullOrWhiteSpace(reference.Value) ||
            reference.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow ||
            reference.SourceContentHash is { } sourceHash &&
            !sourceHash.Equals(logicalReference.ContentHash, StringComparison.OrdinalIgnoreCase) ||
            reference.SourceRecipeRevisionId is { } revisionId &&
            revisionId != logicalReference.RecipeRevisionId)
        {
            return false;
        }

        providerRepresentation = reference.Value;
        return true;
    }

    private async Task<GenerationRecord> MonitorAndIngestAsync(
        IAsyncVideoGenerationProvider provider,
        GenerationRecord record,
        GenerationWorkflowOptions options,
        IProgress<GenerationWorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - started >= options.PollTimeout)
            {
                record.ResponseMetadata["localMonitoring"] = "timed-out";
                await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                progress?.Report(new GenerationWorkflowProgress(
                    record.Status,
                    record.IngestionStatus,
                    "Local monitoring timed out. The remote job remains available to resume."));
                return record;
            }

            var job = await provider.GetJobAsync(record.ProviderJobId!, cancellationToken).ConfigureAwait(false);
            record.Status = job.Status;
            record.Error = job.Error;
            MergeMetadata(record.ResponseMetadata, job.ResponseMetadata);
            record.ResponseMetadata["localMonitoring"] = "active";
            progress?.Report(new GenerationWorkflowProgress(
                record.Status,
                record.IngestionStatus,
                $"Remote job: {record.Status}"));
            await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);

            if (job.Status is GenerationStatus.Failed or GenerationStatus.Cancelled)
            {
                record.CompletedAt = DateTimeOffset.UtcNow;
                await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                return record;
            }

            if (job.Status == GenerationStatus.Succeeded)
            {
                record.CompletedAt = DateTimeOffset.UtcNow;
                record.IngestionStatus = OutputIngestionStatus.Pending;
                await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                progress?.Report(new GenerationWorkflowProgress(
                    record.Status,
                    record.IngestionStatus,
                    "Remote generation completed. Downloading and verifying output…"));
                try
                {
                    record.IngestionStatus = OutputIngestionStatus.Running;
                    await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                    var assets = await _outputIngestion
                        .IngestAsync(_workspace.Location!, record.Id, job.Outputs, cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var asset in assets)
                    {
                        _workspace.Project!.AddAsset(asset);
                        record.OutputAssetIds.Add(asset.Id);
                    }
                    record.IngestionStatus = OutputIngestionStatus.Succeeded;
                    record.Error = null;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    record.IngestionStatus = OutputIngestionStatus.Failed;
                    record.Error = new GenerationError
                    {
                        ProviderCode = "local_ingestion_failed",
                        Message = exception.Message,
                        TechnicalDetails = exception.GetType().FullName
                    };
                }

                await _workspace.SaveAsync(CancellationToken.None).ConfigureAwait(false);
                progress?.Report(new GenerationWorkflowProgress(
                    record.Status,
                    record.IngestionStatus,
                    record.IngestionStatus == OutputIngestionStatus.Succeeded
                        ? "Output downloaded, verified, and added to the project."
                        : "Remote generation completed, but local output ingestion failed."));
                return record;
            }

            await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static GenerationRequestSnapshot CreateSnapshot(
        IVideoGenerationProvider provider,
        GenerationDraft draft,
        VideoProject project)
    {
        if (!string.IsNullOrWhiteSpace(draft.ProviderId) &&
            !draft.ProviderId.Equals(provider.Capabilities.ProviderId, StringComparison.Ordinal))
            throw new InvalidOperationException("The draft provider does not match the selected provider.");

        var references = draft.References
            .Select((reference, index) => CreateReferenceSnapshot(reference, index, project))
            .OrderBy(reference => reference.Order ?? int.MaxValue)
            .ToArray();
        return new GenerationRequestSnapshot
        {
            ProviderId = provider.Capabilities.ProviderId,
            ModelVersion = provider.Capabilities.ModelVersion,
            Mode = draft.Mode,
            Prompt = draft.Prompt,
            DurationSeconds = draft.DurationSeconds,
            AspectRatio = draft.AspectRatio,
            Resolution = draft.Resolution,
            References = Array.AsReadOnly(references),
            ProviderParameters = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(draft.ProviderParameters, StringComparer.Ordinal))
        };
    }

    private static GenerationReferenceSnapshot CreateReferenceSnapshot(
        GenerationReferenceDraft reference,
        int index,
        VideoProject project)
    {
        if (reference.ObjectKind == GenerationReferenceObjectKind.FrameAnchor)
        {
            if (project.Anchors.All(anchor => anchor.Id != reference.LogicalObjectId))
                throw new InvalidOperationException($"Frame anchor '{reference.LogicalObjectId}' no longer exists.");
            return new GenerationReferenceSnapshot
            {
                ObjectKind = reference.ObjectKind,
                LogicalObjectId = reference.LogicalObjectId,
                Role = reference.Role,
                Order = reference.Order ?? index,
                Label = reference.Label,
                Notes = reference.Notes
            };
        }

        var asset = project.Assets.SingleOrDefault(candidate => candidate.Id == reference.LogicalObjectId)
            ?? throw new InvalidOperationException($"Reference asset '{reference.LogicalObjectId}' no longer exists.");
        if (asset.StorageKind == AssetStorageKind.Physical &&
            (asset.Physical?.ContentIdentity.Status != ContentHashStatus.Verified ||
             string.IsNullOrWhiteSpace(asset.Physical.ContentIdentity.Sha256)))
        {
            throw new InvalidOperationException(
                $"'{asset.EffectiveDisplayName}' must have a verified SHA-256 identity before submission.");
        }

        return new GenerationReferenceSnapshot
        {
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
            RecipeRevisionId = asset.Virtual?.CurrentRecipeRevisionId,
            ContentHash = asset.Physical?.ContentIdentity.Sha256,
            Role = reference.Role,
            Order = reference.Order ?? index,
            Label = reference.Label,
            Notes = reference.Notes
        };
    }

    private static GenerationRequest CreateProviderRequest(GenerationRequestSnapshot snapshot) => new()
    {
        Prompt = snapshot.Prompt,
        Mode = snapshot.Mode,
        DurationSeconds = snapshot.DurationSeconds,
        AspectRatio = snapshot.AspectRatio,
        Resolution = snapshot.Resolution,
        ReferenceAssetIds = snapshot.References
            .Where(reference => reference.ObjectKind == GenerationReferenceObjectKind.Asset)
            .Select(reference => reference.LogicalObjectId)
            .ToList(),
        ProviderParameters = new Dictionary<string, string>(snapshot.ProviderParameters, StringComparer.Ordinal)
    };

    private static void ValidateLineage(GenerationDraft draft, VideoProject project)
    {
        if (draft.ParentGenerationId.HasValue != draft.RelationshipType.HasValue)
            throw new InvalidOperationException("A derived generation must pair its parent and relationship type.");
        if (draft.ParentGenerationId is { } parentId && project.Generations.All(candidate => candidate.Id != parentId))
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
        IDictionary<string, string> destination,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var pair in source) destination[pair.Key] = pair.Value;
    }

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
            ObjectKind = reference.ObjectKind,
            LogicalObjectId = reference.LogicalObjectId,
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
