using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

/// <summary>
/// Stages a portable project's durable files beside its requested destination,
/// then publishes the complete staged directory with one same-volume move.
/// Cache and recovery data are deliberately not project-clone inputs.
/// </summary>
public sealed class PortableProjectCloneFileSystem : IProjectCloneFileSystem
{
    private const int BufferSize = 81920;
    private static readonly Regex AtomicTemporaryFileName = new(
        @"^\..+\.[^.]+-[0-9a-f]{32}\.[^.]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RenameTemporaryFileName = new(
        @"^\.reelforge-rename-[0-9a-f]{32}\.[^.]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, string> _ownedStagingDirectories =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<ProjectCloneStaging> StageDurableContentAsync(
        ProjectLocation sourceLocation,
        string destinationParentDirectory,
        string cloneName,
        IProgress<ProjectCloneProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceLocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationParentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cloneName);

        cancellationToken.ThrowIfCancellationRequested();
        var sourceRoot = Path.GetFullPath(sourceLocation.RootDirectory);
        var sourceProjectFile = Path.GetFullPath(sourceLocation.ProjectFilePath);
        var destinationParent = Path.GetFullPath(destinationParentDirectory);
        var trimmedCloneName = cloneName.Trim();
        var projectFileName = PortableProjectStore.GetProjectFileName(trimmedCloneName);
        var finalRoot = Path.GetFullPath(Path.Combine(destinationParent, trimmedCloneName));
        var finalLocation = new ProjectLocation(finalRoot, Path.Combine(finalRoot, projectFileName));

        ValidateLocations(sourceRoot, sourceProjectFile, destinationParent, finalRoot);
        progress?.Report(new ProjectCloneProgress(ProjectClonePhase.Scanning));

        var durableContent = ScanDurableContent(sourceRoot, sourceProjectFile, cancellationToken);
        var files = durableContent.Files;
        var totalBytes = files.Sum(file => file.Length);
        progress?.Report(new ProjectCloneProgress(
            ProjectClonePhase.Scanning,
            0,
            files.Count,
            0,
            totalBytes));

        var stagingRoot = Path.Combine(
            destinationParent,
            ProjectCloneArtifactPolicy.CreateStagingDirectoryName(trimmedCloneName));
        var stagingLocation = new ProjectLocation(stagingRoot, Path.Combine(stagingRoot, projectFileName));
        Directory.CreateDirectory(stagingRoot);
        _ownedStagingDirectories.TryAdd(stagingRoot, destinationParent);

        try
        {
            foreach (var relativeDirectory in durableContent.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(stagingRoot, relativeDirectory));
            }

            var copiedFiles = 0;
            long copiedBytes = 0;
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = Path.Combine(stagingRoot, file.RelativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidOperationException("A staged clone file must have a parent directory.");
                Directory.CreateDirectory(destinationDirectory);

                await CopyFileAsync(
                    file.FullPath,
                    destinationPath,
                    file.RelativePath,
                    copiedFiles,
                    files.Count,
                    copiedBytes,
                    totalBytes,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                copiedFiles++;
                copiedBytes += file.Length;
                progress?.Report(new ProjectCloneProgress(
                    ProjectClonePhase.Copying,
                    copiedFiles,
                    files.Count,
                    copiedBytes,
                    totalBytes,
                    file.RelativePath));
            }

            return new ProjectCloneStaging(stagingLocation, finalLocation, copiedFiles, copiedBytes);
        }
        catch (Exception copyException)
        {
            try
            {
                await DeleteOwnedStagingAsync(stagingRoot).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"Cloning failed and the staging folder '{stagingRoot}' could not be removed.",
                    copyException,
                    cleanupException);
            }
            throw;
        }
    }

    public Task PublishAsync(ProjectCloneStaging staging, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staging);
        cancellationToken.ThrowIfCancellationRequested();

        var stagingRoot = Path.GetFullPath(staging.StagingLocation.RootDirectory);
        var finalRoot = Path.GetFullPath(staging.FinalLocation.RootDirectory);
        if (!IsOwnedStaging(stagingRoot, finalRoot))
            throw new InvalidOperationException("The clone staging directory is not owned by this operation.");
        if (!Directory.Exists(stagingRoot))
            throw new DirectoryNotFoundException("The clone staging directory no longer exists.");
        if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
            throw new IOException($"A project already exists at '{finalRoot}'.");

        Directory.Move(stagingRoot, finalRoot);
        _ownedStagingDirectories.TryRemove(stagingRoot, out _);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(ProjectCloneStaging staging)
    {
        ArgumentNullException.ThrowIfNull(staging);
        return DeleteOwnedStagingAsync(Path.GetFullPath(staging.StagingLocation.RootDirectory));
    }

    private static void ValidateLocations(
        string sourceRoot,
        string sourceProjectFile,
        string destinationParent,
        string finalRoot)
    {
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"The source project folder was not found: '{sourceRoot}'.");
        if (!File.Exists(sourceProjectFile))
            throw new FileNotFoundException("The source project file was not found.", sourceProjectFile);
        if (!IsDirectChild(sourceRoot, sourceProjectFile))
            throw new InvalidDataException("The source project file must be stored in its project folder.");
        if (!Directory.Exists(destinationParent))
            throw new DirectoryNotFoundException($"The clone destination was not found: '{destinationParent}'.");
        if (IsFileSystemLink(sourceProjectFile))
            throw new IOException("Projects containing linked folders or files cannot be cloned.");
        var resolvedSourceRoot = ResolveDirectoryLinks(sourceRoot);
        var resolvedDestinationParent = ResolveDirectoryLinks(destinationParent);
        if (IsSameOrChildPath(resolvedDestinationParent, resolvedSourceRoot))
            throw new InvalidOperationException("Choose a clone location outside the source project folder.");
        if (!IsDirectChild(destinationParent, finalRoot))
            throw new InvalidOperationException("The clone project folder must be directly inside the selected destination.");
        if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
            throw new IOException($"A project already exists at '{finalRoot}'.");
    }

    private static DurableContent ScanDurableContent(
        string sourceRoot,
        string sourceProjectFile,
        CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();
        var directories = new List<string>();
        var recoveryFile = PortableProjectStore.GetRecoveryFilePath(new ProjectLocation(sourceRoot, sourceProjectFile));
        ScanDirectory(sourceRoot, sourceRoot, sourceProjectFile, recoveryFile, files, directories, cancellationToken);
        return new DurableContent(files, directories);
    }

    private static void ScanDirectory(
        string sourceRoot,
        string directory,
        string sourceProjectFile,
        string recoveryFile,
        ICollection<SourceFile> files,
        ICollection<string> directories,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(entry);
            if (IsFileSystemLink(fullPath))
                throw new IOException($"Projects containing linked folders or files cannot be cloned: '{fullPath}'.");

            if (Directory.Exists(fullPath))
            {
                var relativeDirectory = Path.GetRelativePath(sourceRoot, fullPath);
                if (relativeDirectory.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                    ProjectCloneArtifactPolicy.IsStagingDirectoryName(Path.GetFileName(fullPath)))
                    continue;
                directories.Add(relativeDirectory);
                ScanDirectory(sourceRoot, fullPath, sourceProjectFile, recoveryFile, files, directories, cancellationToken);
                continue;
            }

            if (!File.Exists(fullPath))
                throw new IOException($"The project contains an unsupported filesystem entry: '{fullPath}'.");
            if (fullPath.Equals(sourceProjectFile, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Equals(recoveryFile, StringComparison.OrdinalIgnoreCase) ||
                IsTemporaryArtifact(Path.GetFileName(fullPath)))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, fullPath);
            if (Path.IsPathRooted(relativePath) || relativePath.StartsWith("..", StringComparison.Ordinal))
                throw new IOException($"A project file is outside the project folder: '{fullPath}'.");
            files.Add(new SourceFile(fullPath, relativePath, new FileInfo(fullPath).Length));
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        string relativePath,
        int copiedFileCount,
        int totalFileCount,
        long copiedBytesBeforeFile,
        long totalBytes,
        IProgress<ProjectCloneProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        long copiedThisFile = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copiedThisFile += read;
            progress?.Report(new ProjectCloneProgress(
                ProjectClonePhase.Copying,
                copiedFileCount,
                totalFileCount,
                copiedBytesBeforeFile + copiedThisFile,
                totalBytes,
                relativePath));
        }
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteOwnedStagingAsync(string stagingRoot)
    {
        if (!_ownedStagingDirectories.TryRemove(stagingRoot, out var destinationParent))
            return;
        if (!IsDirectChild(destinationParent, stagingRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to remove clone staging folder outside its destination: '{stagingRoot}'.");
        }

        try
        {
            if (Directory.Exists(stagingRoot))
                await Task.Run(() => Directory.Delete(stagingRoot, recursive: true)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not remove clone staging folder '{stagingRoot}'.", exception);
        }
    }

    private bool IsOwnedStaging(string stagingRoot, string finalRoot)
    {
        var stagingParent = Path.GetDirectoryName(stagingRoot);
        var finalParent = Path.GetDirectoryName(finalRoot);
        return stagingParent is not null && finalParent is not null &&
               stagingParent.Equals(finalParent, StringComparison.OrdinalIgnoreCase) &&
               _ownedStagingDirectories.TryGetValue(stagingRoot, out var registeredParent) &&
               registeredParent.Equals(stagingParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemporaryArtifact(string fileName) =>
        AtomicTemporaryFileName.IsMatch(fileName) || RenameTemporaryFileName.IsMatch(fileName);

    private static bool IsFileSystemLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            return info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new IOException($"Could not verify whether '{path}' is a linked filesystem entry.", exception);
        }
    }

    private static string ResolveDirectoryLinks(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"The path '{fullPath}' has no filesystem root.");
        var current = root;
        var relative = Path.GetRelativePath(root, fullPath);
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            string? linkTarget;
            try
            {
                linkTarget = info.LinkTarget;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new IOException($"Could not inspect clone path component '{current}'.", exception);
            }

            if (linkTarget is null) continue;
            FileSystemInfo resolved;
            try
            {
                resolved = info.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException($"Linked clone path component '{current}' has no accessible target.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new IOException($"Could not resolve linked clone path component '{current}'.", exception);
            }
            current = Path.GetFullPath(resolved.FullName);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return normalizedCandidate.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectChild(string parent, string candidate)
    {
        var candidateParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)));
        return candidateParent is not null &&
               candidateParent.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SourceFile(string FullPath, string RelativePath, long Length);

    private sealed record DurableContent(
        IReadOnlyList<SourceFile> Files,
        IReadOnlyList<string> Directories);
}
