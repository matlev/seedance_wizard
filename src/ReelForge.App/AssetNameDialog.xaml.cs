using System.IO;
using System.Windows;
using ReelForge.Infrastructure;

namespace ReelForge.App;

public partial class AssetNameDialog : Window
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".heic", ".heif", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp",
        ".avi", ".m4v", ".mkv", ".mov", ".mp4", ".webm", ".wmv",
        ".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma"
    };
    private readonly string _extension;

    public AssetNameDialog(
        string currentFileName,
        string? title = null,
        string? heading = null,
        string? description = null,
        string? confirmLabel = null)
    {
        InitializeComponent();
        if (title is not null) Title = title;
        if (heading is not null) HeadingText.Text = heading;
        if (description is not null) DescriptionText.Text = description;
        if (confirmLabel is not null) ConfirmButton.Content = confirmLabel;
        _extension = Path.GetExtension(currentFileName);
        FileNameStemTextBox.Text = Path.GetFileNameWithoutExtension(currentFileName);
        ExtensionText.Text = $"{_extension} (file type locked)";
        Loaded += (_, _) =>
        {
            FileNameStemTextBox.Focus();
            FileNameStemTextBox.SelectAll();
        };
    }

    public string FileName => $"{FileNameStemTextBox.Text.Trim()}{_extension}";

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        var stem = FileNameStemTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(stem))
        {
            ShowValidation("Enter a filename.");
            return;
        }
        if (stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stem.EndsWith(' ') || stem.EndsWith('.'))
        {
            ShowValidation("Enter a valid Windows filename without a folder path.");
            return;
        }
        var typedExtension = Path.GetExtension(stem);
        if (MediaExtensions.Contains(typedExtension))
        {
            ShowValidation($"The file type is locked as {_extension}. Remove '{typedExtension}' from the filename field.");
            return;
        }

        try
        {
            PhysicalAssetFileRenameService.ValidateFileName($"current{_extension}", FileName);
        }
        catch (ArgumentException exception)
        {
            ShowValidation(exception.Message);
            return;
        }

        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
