using System.IO;
using ReelForge.Application;
using ReelForge.App.Views.Dialogs;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Coordinates project-media import work while leaving file dialogs and WPF drag/drop
/// presentation with the shell.
/// </summary>
public sealed class MediaImportCoordinator
{
    private readonly IMediaImportOperations _operations;
    private readonly IMediaImportCoordinatorHost _host;

    public MediaImportCoordinator(
        IMediaImportOperations operations,
        IMediaImportCoordinatorHost host)
    {
        _operations = operations;
        _host = host;
    }

    public bool IsBusy { get; private set; }

    public bool CanBeginImport => _host.HasOpenProject && !IsBusy;

    public bool CanImport(MediaImportInput input) =>
        CanBeginImport && input.FilePaths.Count > 0;

    public async Task ImportAsync(MediaImportInput input)
    {
        if (!CanImport(input)) return;

        IsBusy = true;
        _host.SetProjectActionsEnabled(false);
        try
        {
            await _host.RunUiActionAsync(
                $"Importing {input.FilePaths.Count} asset(s)…",
                async () =>
                {
                    var plan = new List<ImportPlanItem>();
                    var reservedMissingAssetIds = new HashSet<Guid>();
                    var reservedDeletedAssetIds = new HashSet<Guid>();
                    var uniquePaths = input.FilePaths
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    foreach (var path in uniquePaths)
                    {
                        var mediaType = AssetImportService.DetermineMediaType(path);
                        var missingProbe = await _operations.ProbeMissingRelinkAsync(path, mediaType);
                        var availableMissingMatches = missingProbe.Matches
                            .Where(match => !reservedMissingAssetIds.Contains(match.AssetId))
                            .ToArray();
                        if (missingProbe.Status == MissingPhysicalAssetProbeStatus.Verified &&
                            availableMissingMatches.Length == 1)
                        {
                            reservedMissingAssetIds.Add(availableMissingMatches[0].AssetId);
                            plan.Add(new ImportPlanItem(path, availableMissingMatches[0].AssetId, null));
                            continue;
                        }

                        if (missingProbe.Status == MissingPhysicalAssetProbeStatus.Verified &&
                            availableMissingMatches.Length > 1)
                        {
                            var missingChoice = _host.PromptMissingSourceRelink(Path.GetFileName(path), availableMissingMatches);
                            if (missingChoice.Kind == MissingSourceRelinkChoiceKind.Cancel)
                            {
                                _host.SetStatus("Import cancelled; the project was unchanged.");
                                return;
                            }

                            if (missingChoice.Kind == MissingSourceRelinkChoiceKind.Relink)
                            {
                                if (missingChoice.MissingAssetId is not { } missingAssetId ||
                                    !availableMissingMatches.Any(match => match.AssetId == missingAssetId))
                                    throw new InvalidOperationException("A missing source must be selected before relinking.");
                                reservedMissingAssetIds.Add(missingAssetId);
                                plan.Add(new ImportPlanItem(path, missingAssetId, null));
                                continue;
                            }
                            // Import-as-new deliberately falls through to the ordinary import plan.
                            plan.Add(new ImportPlanItem(path, null, null));
                            continue;
                        }

                        var deletedMatches = missingProbe.Status == MissingPhysicalAssetProbeStatus.Verified &&
                                             missingProbe.CandidateIdentity is not null
                            ? _operations.FindDeletedRestoreMatches(missingProbe.CandidateIdentity, mediaType)
                            : (await _operations.ProbeDeletedRestoreAsync(path, mediaType)).Matches;
                        var availableMatches = deletedMatches
                            .Where(match => !reservedDeletedAssetIds.Contains(match.AssetId))
                            .ToArray();
                        if (availableMatches.Length == 0)
                        {
                            // Different paths with equal bytes may still be deliberate separate imports once a tombstone is reserved.
                            plan.Add(new ImportPlanItem(path, null, null));
                            continue;
                        }

                        var choice = _host.PromptDeletedSourceRestore(Path.GetFileName(path), availableMatches, allowImportAsNew: true);
                        if (choice.Kind == DeletedSourceRestoreChoiceKind.Cancel)
                        {
                            _host.SetStatus("Import cancelled; the project was unchanged.");
                            return;
                        }
                        var selectedDeletedAssetId = choice.Kind == DeletedSourceRestoreChoiceKind.Restore
                            ? choice.DeletedAssetId
                            : null;
                        if (choice.Kind == DeletedSourceRestoreChoiceKind.Restore && selectedDeletedAssetId is null)
                            throw new InvalidOperationException("A deleted source must be selected before restoration.");
                        if (selectedDeletedAssetId is { } selectedId &&
                            !availableMatches.Any(match => match.AssetId == selectedId))
                            throw new InvalidOperationException("The selected deleted source is no longer available for restoration.");
                        if (selectedDeletedAssetId is { } id) reservedDeletedAssetIds.Add(id);
                        plan.Add(new ImportPlanItem(path, null, selectedDeletedAssetId));
                    }

                    var relinked = 0;
                    foreach (var item in plan.Where(item => item.MissingAssetId is not null))
                    {
                        var result = await _operations.RelinkMissingExternalAsync(item.MissingAssetId!.Value, item.Path);
                        if (result.Status != PhysicalAssetRelinkStatus.Verified)
                            throw new InvalidOperationException(
                                $"The missing source could not be relinked: {result.Status}. {result.Detail}");
                        relinked++;
                    }

                    var restored = 0;
                    foreach (var item in plan.Where(item => item.DeletedAssetId is not null))
                    {
                        var result = await _operations.RestoreDeletedExternalAsync(item.DeletedAssetId!.Value, item.Path);
                        if (result.Relink.Status != PhysicalAssetRelinkStatus.Verified)
                            throw new InvalidOperationException(
                                $"The selected source could not be restored: {result.Relink.Status}. {result.Relink.Detail}");
                        restored++;
                    }

                    var ordinaryPaths = plan.Where(item => item.MissingAssetId is null && item.DeletedAssetId is null)
                        .Select(item => item.Path).ToArray();
                    var imported = ordinaryPaths.Length == 0
                        ? []
                        : await _operations.ImportAsync(ordinaryPaths);
                    _host.RefreshProjectMedia();
                    var statusParts = new List<string>();
                    if (relinked > 0) statusParts.Add($"Relinked {relinked} missing source(s)");
                    if (restored > 0)
                        statusParts.Add($"{(statusParts.Count == 0 ? "Restored" : "restored")} {restored} deleted source(s)");
                    if (imported.Count > 0 || statusParts.Count == 0)
                        statusParts.Add($"{(statusParts.Count == 0 ? "Imported" : "imported")} {imported.Count} asset(s)");
                    var status = string.Join(" and ", statusParts) + ".";
                    _host.SetStatus(input.SkippedCount == 0 ? status : $"{status} Skipped {input.SkippedCount} unsupported item(s).");
                });
        }
        finally
        {
            IsBusy = false;
            _host.SetProjectActionsEnabled(true);
        }
    }
}

internal sealed record ImportPlanItem(string Path, Guid? MissingAssetId, Guid? DeletedAssetId);

/// <summary>Narrow import orchestration seam; physical mutations remain owned by the operations coordinator.</summary>
public interface IMediaImportOperations
{
    Task<MissingPhysicalAssetRelinkProbe> ProbeMissingRelinkAsync(
        string candidatePath, MediaType mediaType, CancellationToken cancellationToken = default);
    IReadOnlyList<DeletedPhysicalAssetRestoreMatch> FindDeletedRestoreMatches(
        ContentIdentity identity, MediaType mediaType);
    Task<PhysicalAssetRelinkResult> RelinkMissingExternalAsync(
        Guid missingAssetId, string candidatePath, CancellationToken cancellationToken = default);
    Task<DeletedPhysicalAssetRestoreProbe> ProbeDeletedRestoreAsync(
        string candidatePath, MediaType mediaType, CancellationToken cancellationToken = default);
    Task<DeletedPhysicalAssetRestoreResult> RestoreDeletedExternalAsync(
        Guid deletedAssetId, string candidatePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectAsset>> ImportAsync(
        IReadOnlyCollection<string> sourcePaths, CancellationToken cancellationToken = default);
}

/// <summary>
/// A provider-neutral import candidate set. External drop paths are validated by
/// the shell before being analyzed, so this type never receives WPF drag data.
/// </summary>
public sealed record MediaImportInput(IReadOnlyList<string> FilePaths, int SkippedCount)
{
    /// <summary>
    /// Preserves the native file-dialog path. The dialog defaults to supported-media
    /// filters, but its explicit All files option remains an operation concern.
    /// </summary>
    public static MediaImportInput FromDialogSelection(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        return new MediaImportInput(filePaths, SkippedCount: 0);
    }

    /// <summary>
    /// Filters an external file drop after the shell has applied file-existence
    /// validation. Unsupported paths are counted for overlay and completion text.
    /// </summary>
    public static MediaImportInput AnalyzeExternalDrop(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var candidates = filePaths.ToArray();
        var supported = candidates
            .Where(AssetImportService.IsSupportedMediaFile)
            .ToArray();
        return new MediaImportInput(supported, candidates.Length - supported.Length);
    }
}

public interface IMediaImportCoordinatorHost
{
    bool HasOpenProject { get; }
    Task RunUiActionAsync(string status, Func<Task> action);
    void SetProjectActionsEnabled(bool enabled);
    void RefreshProjectMedia();
    void SetStatus(string status);
    DeletedSourceRestoreChoice PromptDeletedSourceRestore(
        string candidateName,
        IReadOnlyList<DeletedPhysicalAssetRestoreMatch> matches,
        bool allowImportAsNew);
    MissingSourceRelinkChoice PromptMissingSourceRelink(
        string candidateName,
        IReadOnlyList<MissingPhysicalAssetRelinkMatch> matches);
}
