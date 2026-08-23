using ReelForge.Infrastructure;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Coordinates project-media import work while leaving file dialogs and WPF drag/drop
/// presentation with the shell.
/// </summary>
public sealed class MediaImportCoordinator
{
    private readonly ProjectMediaOperationsCoordinator _operations;
    private readonly IMediaImportCoordinatorHost _host;

    public MediaImportCoordinator(
        ProjectMediaOperationsCoordinator operations,
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
                    var imported = await _operations.ImportAsync(input.FilePaths);
                    _host.RefreshProjectMedia();
                    _host.SetStatus(input.SkippedCount == 0
                        ? $"Imported {imported.Count} asset(s)."
                        : $"Imported {imported.Count} asset(s); skipped {input.SkippedCount} unsupported item(s).");
                });
        }
        finally
        {
            IsBusy = false;
            _host.SetProjectActionsEnabled(true);
        }
    }
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
}
