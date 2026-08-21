namespace ReelForge.Application;

public static class MediaFileNamePolicy
{
    public static string ValidateLeafFileName(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        var fileName = value.Trim();
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.EndsWith(' ') ||
            fileName.EndsWith('.'))
        {
            throw new ArgumentException(
                "Enter a valid filename without a folder path.",
                parameterName);
        }

        return fileName;
    }

    public static string ValidateRequiredExtension(
        string value,
        string requiredExtension,
        string subject,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredExtension);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var fileName = ValidateLeafFileName(value, parameterName);
        if (!Path.GetExtension(fileName).Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{subject} must keep the {requiredExtension} file type.",
                parameterName);
        }

        return fileName;
    }
}
