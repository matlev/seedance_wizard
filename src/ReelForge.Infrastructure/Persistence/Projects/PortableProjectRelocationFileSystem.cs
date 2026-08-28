using System.Collections.Concurrent;
using ReelForge.Application;

namespace ReelForge.Infrastructure;

/// <summary>
/// Relocates complete portable project folders. Same-volume moves use the filesystem rename;
/// cross-volume moves copy to an unpublished sibling staging folder before publication.
/// </summary>
public sealed class PortableProjectRelocationFileSystem : IProjectRelocationFileSystem
{
    private const int BufferSize = 81920;
    private readonly ConcurrentDictionary<string, string> _ownedStagingDirectories =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<ProjectRelocationPlan> PrepareAsync(
        ProjectLocation sourceLocation,
        string destinationRootDirectory,
        IProgress<ProjectRelocationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceLocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRootDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceLocation.RootDirectory));
        var sourceFile = Path.GetFullPath(sourceLocation.ProjectFilePath);
        var finalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRootDirectory));
        var finalFile = Path.Combine(finalRoot, Path.GetFileName(sourceFile));
        ValidateLocations(sourceRoot, sourceFile, finalRoot);
        var finalLocation = new ProjectLocation(finalRoot, finalFile);
        if (IsSameVolume(sourceRoot, finalRoot))
        {
            // Directory.Move would otherwise preserve an unexpected reparse point without this
            // operation ever having verified it. Relocation fails closed for both routes.
            _ = ScanContent(sourceRoot, cancellationToken);
            return new ProjectRelocationPlan(sourceLocation, finalLocation, null, false, 0, 0);
        }

        var parent = Path.GetDirectoryName(finalRoot)
            ?? throw new InvalidOperationException("The relocation destination must have a parent folder.");
        var stagingRoot = Path.Combine(parent, $".{Path.GetFileName(finalRoot)}.move-{Guid.NewGuid():N}");
        var stagingLocation = new ProjectLocation(stagingRoot, Path.Combine(stagingRoot, Path.GetFileName(sourceFile)));
        Directory.CreateDirectory(stagingRoot);
        _ownedStagingDirectories.TryAdd(stagingRoot, parent);
        try
        {
            progress?.Report(new ProjectRelocationProgress(ProjectRelocationPhase.Scanning));
            var content = ScanContent(sourceRoot, cancellationToken);
            var totalBytes = content.Files.Sum(file => file.Length);
            progress?.Report(new ProjectRelocationProgress(
                ProjectRelocationPhase.Scanning, 0, content.Files.Count, 0, totalBytes));
            foreach (var directory in content.Directories)
                Directory.CreateDirectory(Path.Combine(stagingRoot, directory));

            var copiedCount = 0;
            long copiedBytes = 0;
            foreach (var file in content.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(stagingRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(file.FullPath, destination, file.RelativePath, copiedCount, content.Files.Count,
                    copiedBytes, totalBytes, progress, cancellationToken).ConfigureAwait(false);
                copiedCount++;
                copiedBytes += file.Length;
                progress?.Report(new ProjectRelocationProgress(ProjectRelocationPhase.Copying, copiedCount,
                    content.Files.Count, copiedBytes, totalBytes, file.RelativePath));
            }
            return new ProjectRelocationPlan(sourceLocation, finalLocation, stagingLocation, true, copiedCount, copiedBytes);
        }
        catch
        {
            await DeleteOwnedStagingAsync(stagingRoot).ConfigureAwait(false);
            throw;
        }
    }

    public Task PublishAsync(ProjectRelocationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(plan.FinalLocation.RootDirectory) || File.Exists(plan.FinalLocation.RootDirectory))
            throw new IOException($"A project already exists at '{plan.FinalLocation.RootDirectory}'.");

        if (!plan.UsesStaging)
        {
            ValidateSourceForRemoval(plan.SourceLocation);
            Directory.Move(plan.SourceLocation.RootDirectory, plan.FinalLocation.RootDirectory);
            return Task.CompletedTask;
        }

        var stagingRoot = Path.GetFullPath(plan.StagingLocation!.RootDirectory);
        if (!_ownedStagingDirectories.TryRemove(stagingRoot, out var parent) ||
            !PathsEqual(parent, Path.GetDirectoryName(plan.FinalLocation.RootDirectory)!))
            throw new InvalidOperationException("The relocation staging directory is not owned by this operation.");
        if (!Directory.Exists(stagingRoot))
            throw new DirectoryNotFoundException("The relocation staging directory no longer exists.");
        Directory.Move(stagingRoot, plan.FinalLocation.RootDirectory);
        return Task.CompletedTask;
    }

    public Task RemoveSourceAsync(ProjectRelocationPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (!plan.UsesStaging)
            return Task.CompletedTask;
        ValidateSourceForRemoval(plan.SourceLocation);
        Directory.Delete(plan.SourceLocation.RootDirectory, recursive: true);
        return Task.CompletedTask;
    }

    public Task RollbackAsync(ProjectRelocationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.UsesStaging
            ? DeleteOwnedStagingAsync(Path.GetFullPath(plan.StagingLocation!.RootDirectory))
            : Task.CompletedTask;
    }

    private static void ValidateLocations(string sourceRoot, string sourceFile, string finalRoot)
    {
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"The source project folder was not found: '{sourceRoot}'.");
        if (!File.Exists(sourceFile))
            throw new FileNotFoundException("The source project file was not found.", sourceFile);
        if (!PathsEqual(Path.GetDirectoryName(sourceFile)!, sourceRoot))
            throw new InvalidDataException("The source project file must be stored in its project folder.");
        var destinationParent = Path.GetDirectoryName(finalRoot);
        if (destinationParent is null || !Directory.Exists(destinationParent))
            throw new DirectoryNotFoundException("The selected project location's parent folder was not found.");
        if (PathsEqual(sourceRoot, finalRoot) || IsSameOrChild(finalRoot, sourceRoot))
            throw new InvalidOperationException("Choose a project location outside the current project folder.");
        if (Directory.Exists(finalRoot) || File.Exists(finalRoot))
            throw new IOException($"A project already exists at '{finalRoot}'.");
        EnsureNoLinksInPath(sourceRoot);
        EnsureNoLinksInPath(destinationParent);
        if (IsLink(sourceFile))
            throw new IOException("Projects containing linked folders or files cannot be moved.");
    }

    private static Content ScanContent(string sourceRoot, CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();
        var directories = new List<string>();
        ScanDirectory(sourceRoot, sourceRoot, files, directories, cancellationToken);
        return new Content(files, directories);
    }

    private static void ScanDirectory(string root, string directory, List<SourceFile> files, List<string> directories,
        CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(entry);
            if (IsLink(fullPath))
                throw new IOException($"Projects containing linked folders or files cannot be moved: '{fullPath}'.");
            if (Directory.Exists(fullPath))
            {
                var relative = Path.GetRelativePath(root, fullPath);
                directories.Add(relative);
                ScanDirectory(root, fullPath, files, directories, cancellationToken);
            }
            else if (File.Exists(fullPath))
            {
                var relative = Path.GetRelativePath(root, fullPath);
                if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
                    throw new IOException($"A project file is outside the project folder: '{fullPath}'.");
                files.Add(new SourceFile(fullPath, relative, new FileInfo(fullPath).Length));
            }
            else
                throw new IOException($"The project contains an unsupported filesystem entry: '{fullPath}'.");
        }
    }

    private static async Task CopyFileAsync(string source, string destination, string relative, int copied, int total,
        long copiedBefore, long totalBytes, IProgress<ProjectRelocationProgress>? progress, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long copiedThisFile = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            copiedThisFile += read;
            progress?.Report(new ProjectRelocationProgress(ProjectRelocationPhase.Copying, copied, total,
                copiedBefore + copiedThisFile, totalBytes, relative));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteOwnedStagingAsync(string stagingRoot)
    {
        if (!_ownedStagingDirectories.TryRemove(stagingRoot, out var parent)) return;
        if (!PathsEqual(Path.GetDirectoryName(stagingRoot)!, parent))
            throw new InvalidOperationException("Refusing to remove relocation staging outside its destination.");
        if (Directory.Exists(stagingRoot))
            await Task.Run(() => Directory.Delete(stagingRoot, recursive: true)).ConfigureAwait(false);
    }

    private static void ValidateSourceForRemoval(ProjectLocation source)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.RootDirectory));
        var file = Path.GetFullPath(source.ProjectFilePath);
        if (!Directory.Exists(root) || !File.Exists(file) || !PathsEqual(Path.GetDirectoryName(file)!, root))
            throw new IOException("The original project folder changed before relocation could be completed.");
        EnsureNoLinksInPath(root);
    }

    private static bool IsSameVolume(string first, string second)
    {
        var firstRoot = Path.GetPathRoot(Path.GetFullPath(first));
        var secondRoot = Path.GetPathRoot(Path.GetFullPath(second));
        return firstRoot is not null && secondRoot is not null && PathsEqual(firstRoot, secondRoot);
    }

    private static void EnsureNoLinksInPath(string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.GetPathRoot(fullPath) ?? throw new IOException($"Cannot resolve path '{path}'.");
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, fullPath).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
                throw new IOException($"Linked filesystem paths cannot be used for project relocation: '{current}'.");
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new IOException($"Could not verify whether '{path}' is a linked filesystem entry.", exception);
        }
    }

    private static bool IsSameOrChild(string candidate, string parent)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return PathsEqual(normalizedCandidate, normalizedParent) ||
               normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private sealed record SourceFile(string FullPath, string RelativePath, long Length);
    private sealed record Content(IReadOnlyList<SourceFile> Files, IReadOnlyList<string> Directories);
}
