namespace ReelForge.Application;

/// <summary>
/// Owns a same-directory temporary file and exposes one atomic move into its final path.
/// Uncommitted temporary files are deleted when the scope ends.
/// </summary>
public sealed class AtomicFileCommit : IDisposable
{
    private bool _disposed;

    private AtomicFileCommit(string temporaryPath, string destinationPath)
    {
        TemporaryPath = temporaryPath;
        DestinationPath = destinationPath;
    }

    public string TemporaryPath { get; }
    public string DestinationPath { get; }
    public bool IsCommitted { get; private set; }

    public static AtomicFileCommit Create(
        string destinationPath,
        string operation,
        string temporaryExtension = ".tmp")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (operation != Path.GetFileName(operation) ||
            operation.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The file operation name must be a valid path component.", nameof(operation));
        }

        if (!temporaryExtension.StartsWith('.'))
            throw new ArgumentException("The temporary extension must begin with a period.", nameof(temporaryExtension));

        var finalPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new ArgumentException("The destination path must have a parent directory.", nameof(destinationPath));
        var temporaryName = $".{Path.GetFileName(finalPath)}.{operation}-{Guid.NewGuid():N}{temporaryExtension}";
        return new AtomicFileCommit(Path.Combine(directory, temporaryName), finalPath);
    }

    public void Commit(bool overwrite = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsCommitted) throw new InvalidOperationException("This file has already been committed.");
        if (!File.Exists(TemporaryPath))
            throw new FileNotFoundException("The temporary file does not exist and cannot be committed.", TemporaryPath);

        File.Move(TemporaryPath, DestinationPath, overwrite);
        IsCommitted = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath);
    }
}
