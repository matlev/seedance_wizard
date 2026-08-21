namespace ReelForge.Application;

public enum FileNameCollisionStyle
{
    Parenthesized,
    Hyphenated
}

public static class CollisionFreeDestinationPolicy
{
    public static string GetAvailablePath(
        string directory,
        string fileName,
        FileNameCollisionStyle collisionStyle = FileNameCollisionStyle.Parenthesized)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var safeFileName = MediaFileNamePolicy.ValidateLeafFileName(fileName, nameof(fileName));
        var candidate = Path.Combine(directory, safeFileName);
        var stem = Path.GetFileNameWithoutExtension(safeFileName);
        var extension = Path.GetExtension(safeFileName);
        var suffix = 2;

        while (File.Exists(candidate))
        {
            var nextName = collisionStyle switch
            {
                FileNameCollisionStyle.Parenthesized => $"{stem} ({suffix}){extension}",
                FileNameCollisionStyle.Hyphenated => $"{stem}-{suffix}{extension}",
                _ => throw new ArgumentOutOfRangeException(nameof(collisionStyle), collisionStyle, null)
            };
            candidate = Path.Combine(directory, nextName);
            suffix++;
        }

        return candidate;
    }
}
