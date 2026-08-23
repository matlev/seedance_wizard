using ReelForge.Core;

namespace ReelForge.Application;

internal sealed class GenerationMonitor
{
    private readonly ProjectWorkspace _workspace;
    private readonly IGeneratedOutputIngestionService _outputIngestion;

    public GenerationMonitor(
        ProjectWorkspace workspace,
        IGeneratedOutputIngestionService outputIngestion)
    {
        _workspace = workspace;
        _outputIngestion = outputIngestion;
    }

    public async Task<GenerationRecord> MonitorAsync(
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
            foreach (var pair in job.ResponseMetadata) record.ResponseMetadata[pair.Key] = pair.Value;
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
                return await IngestSucceededOutputAsync(record, job.Outputs, progress, cancellationToken)
                    .ConfigureAwait(false);

            await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<GenerationRecord> IngestSucceededOutputAsync(
        GenerationRecord record,
        IReadOnlyList<ProviderGenerationOutput> outputs,
        IProgress<GenerationWorkflowProgress>? progress,
        CancellationToken cancellationToken)
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
                .IngestAsync(_workspace.Location!, record.Id, outputs, cancellationToken)
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
}
