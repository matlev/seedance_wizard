using System.Collections.Concurrent;

namespace ReelForge.Infrastructure;

internal static class MediaCacheLeaseRegistry
{
    private static readonly ConcurrentDictionary<string, int> Leases =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Acquire(string path)
    {
        var normalized = Path.GetFullPath(path);
        Leases.AddOrUpdate(normalized, 1, static (_, count) => checked(count + 1));
        return normalized;
    }

    public static bool IsLeased(string path) => Leases.ContainsKey(Path.GetFullPath(path));

    public static void Release(string path)
    {
        var normalized = Path.GetFullPath(path);
        while (Leases.TryGetValue(normalized, out var count))
        {
            if (count <= 1)
            {
                if (Leases.TryRemove(new KeyValuePair<string, int>(normalized, count))) return;
            }
            else if (Leases.TryUpdate(normalized, count - 1, count))
            {
                return;
            }
        }
    }
}
