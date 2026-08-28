using System.IO;
using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.App.Views.ProjectMedia;

/// <summary>
/// Owns the user-facing Project Media command policy. Selection and preview
/// lifecycle remain owned by the shell; callers provide the selected item.
/// </summary>
public sealed class ProjectMediaCommandCoordinator
{
    private readonly ProjectMediaOperationsCoordinator _operations;
    private readonly IProjectMediaCommandHost _host;

    public ProjectMediaCommandCoordinator(
        ProjectMediaOperationsCoordinator operations,
        IProjectMediaCommandHost host)
    {
        _operations = operations;
        _host = host;
    }

    public Task HandleAsync(ProjectMediaAction action, ProjectMediaListItem? selectedItem) =>
        action switch
        {
            ProjectMediaAction.Rename => RenameAsync(selectedItem),
            ProjectMediaAction.Relink => RelinkAsync(selectedItem),
            ProjectMediaAction.Export => ExportAsync(selectedItem),
            ProjectMediaAction.ExtractAudio => ExtractAudioAsync(selectedItem),
            ProjectMediaAction.Copy => CopyAsync(selectedItem),
            ProjectMediaAction.Move => MoveAsync(selectedItem),
            ProjectMediaAction.Delete => DeleteAsync(selectedItem),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown Project Media action.")
        };

    private async Task RenameAsync(ProjectMediaListItem? selectedItem)
    {
        var asset = selectedItem?.Asset;
        var renameKind = ProjectMediaRenamePolicy.GetKind(asset);
        if (asset is null || renameKind == ProjectMediaRenameKind.None) return;

        var requestedName = renameKind == ProjectMediaRenameKind.PhysicalFile
            ? _host.PromptPhysicalFileName(asset.FileName)
            : _host.PromptSavedClipDisplayName(asset.EffectiveDisplayName);
        if (requestedName is null) return;

        await _host.RunUiActionAsync(
            renameKind == ProjectMediaRenameKind.PhysicalFile
                ? $"Renaming {asset.FileName}…"
                : $"Renaming {asset.EffectiveDisplayName}…",
            async () =>
            {
                await _operations.RenameAsync(asset, requestedName);
                _host.RefreshProjectMedia(asset.Id);
                _host.UpdateAssetInspector(asset);
                _host.SetStatus(renameKind == ProjectMediaRenameKind.PhysicalFile
                    ? $"Renamed stored media file to {asset.FileName}."
                    : $"Renamed Saved Clip to {asset.EffectiveDisplayName}.");
            });
    }

    private async Task RelinkAsync(ProjectMediaListItem? selectedItem)
    {
        if (!_host.HasOpenProject ||
            selectedItem?.Asset is not { StorageKind: AssetStorageKind.Physical, Physical: not null } asset) return;

        var candidatePath = _host.PromptRelinkCandidate(asset);
        if (candidatePath is null) return;

        await _host.RunUiActionAsync($"Verifying replacement for {asset.FileName}…", async () =>
        {
            var result = await _operations.RelinkPhysicalAssetAsync(asset, candidatePath);
            switch (result.Status)
            {
                case PhysicalAssetRelinkStatus.Verified:
                    _host.RefreshProjectMedia(asset.Id);
                    _host.UpdateAssetInspector(asset);
                    _host.SetStatus($"Relinked {asset.FileName}; its SHA-256 identity was verified.");
                    return;
                case PhysicalAssetRelinkStatus.Missing:
                    ShowRelinkInformation(
                        "The selected relink file is no longer available. Choose an accessible copy of the original media.",
                        "Relink source",
                        result);
                    return;
                case PhysicalAssetRelinkStatus.Inaccessible:
                    ShowRelinkInformation(
                        "ReelForge could not read the selected relink file. Check its location and permissions, then try again.",
                        "Relink source",
                        result);
                    return;
                case PhysicalAssetRelinkStatus.Mismatched:
                    ShowRelinkInformation(
                        "The selected file does not match this asset's recorded SHA-256 identity. It was not relinked. " +
                        "If you want to use these different bytes, import them as new media using Import.",
                        "Relink refused",
                        result);
                    return;
                case PhysicalAssetRelinkStatus.Cancelled:
                    _host.SetStatus($"Relinking {asset.FileName} was cancelled; the project was unchanged.");
                    return;
                case PhysicalAssetRelinkStatus.Stale:
                    ShowRelinkInformation(
                        "The project changed while relinking, so the verified copy was not adopted. Try again from the current project state.",
                        "Relink source",
                        result);
                    return;
                case PhysicalAssetRelinkStatus.Failed:
                    ShowRelinkInformation(
                        "ReelForge could not safely complete the relink. The existing project reference was retained.",
                        "Relink source",
                        result);
                    return;
                default:
                    throw new InvalidOperationException($"Unknown relink status '{result.Status}'.");
            }
        });
    }

    private void ShowRelinkInformation(string message, string title, PhysicalAssetRelinkResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : $"\n\nDetails: {result.Detail}";
        var dependencies = result.DependencyReport.IsInUse
            ? $"\n\nExisting project references retained:\n• {string.Join("\n• ", result.DependencyReport.DisplayDescriptions)}"
            : string.Empty;
        _host.ShowInformation(message + detail + dependencies, title);
        _host.SetStatus(message);
    }

    private async Task ExportAsync(ProjectMediaListItem? item)
    {
        if (item is null || !_host.HasOpenProject) return;

        if (item.Anchor is { } anchor && item.AnchorRevision is { } anchorRevision)
        {
            var destination = _host.PromptExportPath(new ProjectMediaExportRequest(
                "Export Saved Frame", "PNG image|*.png", ".png", $"{MakeSafeFileName(item.DisplayName)}.png"));
            if (destination is null) return;
            await _host.RunUiActionAsync("Exporting Saved Frame…", async () =>
            {
                var path = await _operations.ExportSavedFrameAsync(anchor, anchorRevision, destination);
                _host.SetStatus($"Exported Saved Frame to {path}.");
            });
            return;
        }

        if (item.Asset is not { StorageKind: AssetStorageKind.Virtual, MediaType: MediaType.Video } asset ||
            asset.Virtual?.CurrentRecipeRevisionId is not { } recipeRevisionId)
        {
            _host.ShowInformation("Choose a Saved Frame, Saved Clip, or Working Composition to export rendered media.", "Export");
            return;
        }

        var videoDestination = _host.PromptExportPath(new ProjectMediaExportRequest(
            $"Export {asset.EffectiveDisplayName}", "MP4 video|*.mp4", ".mp4", $"{MakeSafeFileName(asset.EffectiveDisplayName)}.mp4"));
        if (videoDestination is null) return;
        await _host.RunUiActionAsync($"Exporting {asset.EffectiveDisplayName}…", async () =>
        {
            var path = await _operations.ExportVirtualVideoAsync(asset, recipeRevisionId, videoDestination);
            _host.SetStatus($"Exported {asset.EffectiveDisplayName} to {path}.");
        });
    }

    private async Task ExtractAudioAsync(ProjectMediaListItem? selectedItem)
    {
        if (selectedItem?.Asset is not { MediaType: MediaType.Video } source) return;
        var recipeRevisionId = source.Virtual?.Kind == VirtualAssetKind.SavedClip
            ? source.Virtual.CurrentRecipeRevisionId
            : null;
        if (source.StorageKind == AssetStorageKind.Virtual && recipeRevisionId is null)
        {
            _host.ShowInformation("This Saved Clip does not have a committed recipe revision.", "Extract audio");
            return;
        }

        var stem = MakeSafeFileName(Path.GetFileNameWithoutExtension(source.EffectiveDisplayName));
        var requestedName = _host.PromptAudioExtractionFileName($"{stem} audio.m4a");
        if (requestedName is null) return;
        await _host.RunUiActionAsync($"Extracting audio from {source.EffectiveDisplayName}…", async () =>
        {
            var extracted = await _operations.ExtractAudioAsync(source, recipeRevisionId, requestedName);
            _host.RefreshProjectMedia(extracted.Id);
            _host.SetStatus($"Extracted audio as {extracted.FileName}.");
        });
    }

    private async Task DeleteAsync(ProjectMediaListItem? selectedItem)
    {
        if (selectedItem is null || !_host.HasOpenProject) return;
        if (selectedItem.Anchor is { } anchor)
        {
            await _host.DeleteSavedFrameAsync(anchor, selectedItem.DisplayName);
            return;
        }
        if (selectedItem.Asset is not { } asset) return;
        if (asset.Virtual?.Kind == VirtualAssetKind.SavedClip)
        {
            if (!_host.Confirm(
                    $"Delete Saved Clip '{asset.EffectiveDisplayName}' from this project?\n\n" +
                    "Its non-destructive recipe and private boundaries will be removed. The source video is unchanged.",
                    "Delete Saved Clip")) return;
            await _host.RunUiActionAsync($"Deleting {asset.EffectiveDisplayName}…", async () =>
            {
                await _operations.DeleteSavedClipAsync(asset.Id);
                _host.ClearSelectionAndPreview();
                _host.RefreshProjectMedia();
                _host.SetStatus($"Deleted Saved Clip '{asset.EffectiveDisplayName}'. The source video was unchanged.");
            });
            return;
        }
        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            _host.ShowInformation("This virtual project item cannot be deleted from Project Media yet.", "Delete project media");
            return;
        }

        var usage = _operations.AnalyzeDependencies(asset);
        if (usage.IsInUse)
        {
            _host.ShowInformation(
                $"'{asset.EffectiveDisplayName}' cannot be deleted because it is still used by:\n\n• {string.Join("\n• ", usage.DisplayDescriptions)}",
                "Asset is in use");
            return;
        }
        if (!_host.Confirm(
                $"Delete '{asset.EffectiveDisplayName}' from this project and remove its stored media file?\n\nThis cannot be undone.",
                "Delete asset")) return;
        await _host.RunUiActionAsync($"Deleting {asset.EffectiveDisplayName}…", async () =>
        {
            await _operations.DeletePhysicalAssetAsync(asset.Id);
            _host.ClearSelectionAndPreview();
            _host.RefreshProjectMedia();
            _host.SetStatus($"Deleted {asset.EffectiveDisplayName}.");
        });
    }

    private async Task MoveAsync(ProjectMediaListItem? selectedItem)
    {
        if (!_host.HasOpenProject ||
            selectedItem?.Anchor is not null ||
            selectedItem?.Asset is not { StorageKind: AssetStorageKind.Physical } asset) return;
        var targetProjectFile = _host.ChooseTransferTargetProject();
        if (targetProjectFile is null) return;
        await _host.RunUiActionAsync($"Moving {asset.EffectiveDisplayName}…", async () =>
        {
            var result = await _operations.MovePhysicalAssetToProjectAsync(asset, targetProjectFile);
            if (result.SourceRemoved)
            {
                _host.ClearSelectionAndPreview();
                _host.RefreshProjectMedia();
                _host.SetStatus($"Moved {asset.FileName} to {result.CopyResult.TargetProjectName}.");
                return;
            }

            _host.SetStatus($"Copied {asset.FileName} to {result.CopyResult.TargetProjectName}; the source remains because project history references it.");
            _host.ShowInformation(
                $"'{asset.FileName}' is now available in '{result.CopyResult.TargetProjectName}'.\n\n" +
                "ReelForge retained the source copy because removing it would break:\n\n" +
                $"• {string.Join("\n• ", result.DependencyReport.DisplayDescriptions)}",
                "Asset transferred; source retained");
        });
    }

    private async Task CopyAsync(ProjectMediaListItem? selectedItem)
    {
        if (selectedItem is null || !_host.HasOpenProject) return;
        var targetProjectFile = _host.ChooseTransferTargetProject();
        if (targetProjectFile is null) return;

        if (selectedItem.Anchor is { } anchor && selectedItem.AnchorRevision is { } revision)
        {
            await _host.RunUiActionAsync($"Copying {selectedItem.DisplayName}…", async () =>
            {
                var result = await _operations.CopySavedFrameToProjectAsync(anchor, revision, selectedItem.DisplayName, targetProjectFile);
                _host.SetStatus($"Copied {selectedItem.DisplayName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.");
            });
            return;
        }

        if (selectedItem.Asset is not { } asset) return;
        if (asset.StorageKind == AssetStorageKind.Virtual)
        {
            if (asset.MediaType != MediaType.Video ||
                asset.Virtual?.Kind is not (VirtualAssetKind.SavedClip or VirtualAssetKind.Composition) ||
                asset.Virtual.CurrentRecipeRevisionId is not { } recipeRevisionId) return;
            await _host.RunUiActionAsync($"Copying {asset.EffectiveDisplayName}…", async () =>
            {
                var result = await _operations.CopyVirtualVideoToProjectAsync(asset, recipeRevisionId, asset.EffectiveDisplayName, targetProjectFile);
                _host.SetStatus($"Copied {asset.EffectiveDisplayName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.");
            });
            return;
        }
        if (asset.StorageKind != AssetStorageKind.Physical) return;
        await _host.RunUiActionAsync($"Copying {asset.FileName}…", async () =>
        {
            var result = await _operations.CopyPhysicalAssetToProjectAsync(asset, targetProjectFile);
            _host.SetStatus($"Copied {asset.FileName} to {result.TargetProjectName} as {result.CopiedAsset.FileName}.");
        });
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Working Composition" : safe;
    }
}

public sealed record ProjectMediaExportRequest(string Title, string Filter, string DefaultExtension, string FileName);

public interface IProjectMediaCommandHost
{
    bool HasOpenProject { get; }
    Task RunUiActionAsync(string status, Func<Task> action);
    string? PromptPhysicalFileName(string fileName);
    string? PromptRelinkCandidate(ProjectAsset asset);
    string? PromptSavedClipDisplayName(string displayName);
    string? PromptExportPath(ProjectMediaExportRequest request);
    string? PromptAudioExtractionFileName(string suggestedFileName);
    bool Confirm(string message, string title);
    void ShowInformation(string message, string title);
    string? ChooseTransferTargetProject();
    void SetStatus(string status);
    void RefreshProjectMedia(Guid? selectedAssetId = null);
    void ClearSelectionAndPreview();
    void UpdateAssetInspector(ProjectAsset asset);
    Task DeleteSavedFrameAsync(FrameAnchor anchor, string displayLabel);
}
