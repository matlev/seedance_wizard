namespace ReelForge.Application;

public static class ExactFrameContactWindow
{
    public const int DefaultVisibleFrameCount = 9;

    public static IReadOnlyList<VideoPresentationFrame> Select(
        IReadOnlyList<VideoPresentationFrame> indexedFrames,
        double centerSeconds,
        int visibleFrameCount = DefaultVisibleFrameCount)
    {
        ArgumentNullException.ThrowIfNull(indexedFrames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(visibleFrameCount);
        if (indexedFrames.Count == 0) return [];

        var centerIndex = FindNearestIndex(indexedFrames, centerSeconds);
        var start = Math.Max(0, Math.Min(
            centerIndex - visibleFrameCount / 2,
            indexedFrames.Count - visibleFrameCount));
        var count = Math.Min(visibleFrameCount, indexedFrames.Count - start);
        return indexedFrames.Skip(start).Take(count).ToArray();
    }

    public static int FindNearestIndex(
        IReadOnlyList<VideoPresentationFrame> indexedFrames,
        double timestampSeconds)
    {
        ArgumentNullException.ThrowIfNull(indexedFrames);
        if (indexedFrames.Count == 0) return -1;

        var low = 0;
        var high = indexedFrames.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var value = indexedFrames[middle].TimestampSeconds;
            if (value < timestampSeconds) low = middle + 1;
            else if (value > timestampSeconds) high = middle - 1;
            else return middle;
        }

        if (low <= 0) return 0;
        if (low >= indexedFrames.Count) return indexedFrames.Count - 1;
        return Math.Abs(indexedFrames[low - 1].TimestampSeconds - timestampSeconds) <=
               Math.Abs(indexedFrames[low].TimestampSeconds - timestampSeconds)
            ? low - 1
            : low;
    }
}
