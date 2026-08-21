using ReelForge.Application;
using ReelForge.Core;

namespace ReelForge.Infrastructure;

public sealed class PhysicalAssetFileRenameService
{
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static async Task RenameAsync(
        ProjectWorkspace workspace,
        ProjectAsset asset,
        string newFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(asset);
        if (workspace.Project is null || workspace.Location is null)
            throw new InvalidOperationException("Create or open a project first.");
        if (asset.StorageKind != AssetStorageKind.Physical || asset.Physical is null)
            throw new InvalidOperationException("Only physical assets have a stored media filename.");

        var validatedFileName = ValidateFileName(asset.FileName, newFileName);
        if (validatedFileName.Equals(asset.FileName, StringComparison.Ordinal)) return;

        var sourcePath = workspace.GetAbsoluteAssetPath(asset);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The stored media file could not be found.", sourcePath);

        var targetPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)
                ?? throw new InvalidOperationException("The stored media file must have a parent folder."),
            validatedFileName);
        var caseOnlyRename = sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && File.Exists(targetPath))
            throw new IOException($"A file named '{validatedFileName}' already exists in this media folder.");

        var oldFileName = asset.FileName;
        var oldDisplayName = asset.DisplayName;
        var oldRelativePath = asset.Physical.RelativePath;
        MoveFile(sourcePath, targetPath, caseOnlyRename);
        try
        {
            asset.FileName = validatedFileName;
            asset.DisplayName = validatedFileName;
            asset.Physical.RelativePath = ProjectPathPolicy.GetRelativePath(workspace.Location, targetPath);
            await workspace.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception saveException)
        {
            asset.FileName = oldFileName;
            asset.DisplayName = oldDisplayName;
            asset.Physical.RelativePath = oldRelativePath;
            try
            {
                MoveFile(targetPath, sourcePath, caseOnlyRename);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Saving the renamed filename failed, and the stored media file could not be moved back to its original name.",
                    saveException,
                    rollbackException);
            }
            throw;
        }
    }

    public static string ValidateFileName(string currentFileName, string requestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedFileName);
        var trimmed = requestedFileName.Trim();
        if (!Path.GetFileName(trimmed).Equals(trimmed, StringComparison.Ordinal) ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.EndsWith(' ') ||
            trimmed.EndsWith('.'))
            throw new ArgumentException("Enter a valid Windows filename without a folder path.", nameof(requestedFileName));

        var currentExtension = Path.GetExtension(currentFileName);
        var requestedExtension = Path.GetExtension(trimmed);
        if (!requestedExtension.Equals(currentExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"The file type cannot be changed. Keep the '{currentExtension}' extension.",
                nameof(requestedFileName));

        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(trimmed)))
            throw new ArgumentException("Enter a filename before the extension.", nameof(requestedFileName));
        if (ReservedWindowsNames.Contains(Path.GetFileNameWithoutExtension(trimmed)))
            throw new ArgumentException("That filename is reserved by Windows.", nameof(requestedFileName));
        return trimmed;
    }

    private static void MoveFile(string sourcePath, string targetPath, bool caseOnlyRename)
    {
        if (!caseOnlyRename)
        {
            File.Move(sourcePath, targetPath, overwrite: false);
            return;
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".reelforge-rename-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Move(sourcePath, temporaryPath, overwrite: false);
        try
        {
            File.Move(temporaryPath, targetPath, overwrite: false);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Move(temporaryPath, sourcePath, overwrite: false);
            throw;
        }
    }
}
