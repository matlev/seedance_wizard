using System.Globalization;
using System.Windows.Data;

namespace ReelForge.App;

public sealed class PromptPreviewConverter : IValueConverter
{
    private const int MaximumCharacters = 50;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var prompt = value as string ?? string.Empty;
        var singleLine = string.Join(' ', prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= MaximumCharacters
            ? singleLine
            : $"{singleLine[..MaximumCharacters]}…";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
