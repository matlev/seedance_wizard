using System.Windows;
using ReelForge.Application;

namespace ReelForge.App.Views.Dialogs;

public enum MissingSourceRelinkChoiceKind { Cancel, Relink, ImportAsNew }

public sealed record MissingSourceRelinkChoice(MissingSourceRelinkChoiceKind Kind, Guid? MissingAssetId = null);

public sealed class MissingSourceRelinkDialogItem
{
    public MissingSourceRelinkDialogItem(MissingPhysicalAssetRelinkMatch match) => Match = match;
    public MissingPhysicalAssetRelinkMatch Match { get; }
    public string DisplayText => Match.DependencyReport.IsInUse
        ? $"{Match.DisplayName} — {string.Join(", ", Match.DependencyReport.DisplayDescriptions)}"
        : $"{Match.DisplayName} — no current project references";
}

public partial class MissingSourceRelinkDialog : Window
{
    public MissingSourceRelinkDialog(
        string candidateName,
        IReadOnlyList<MissingPhysicalAssetRelinkMatch> matches)
    {
        InitializeComponent();
        DescriptionText.Text = $"'{candidateName}' matches more than one missing project source. Choose the source to relink, or keep this as a new import.";
        MatchesList.ItemsSource = matches.Select(match => new MissingSourceRelinkDialogItem(match)).ToArray();
        MatchesList.SelectedIndex = -1;
    }

    public MissingSourceRelinkChoice Choice { get; private set; } = new(MissingSourceRelinkChoiceKind.Cancel);

    private void Relink_Click(object sender, RoutedEventArgs e)
    {
        if (MatchesList.SelectedItem is not MissingSourceRelinkDialogItem item)
        {
            MessageBox.Show(this, "Choose the missing source to relink.", "Relink missing source",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Choice = new MissingSourceRelinkChoice(MissingSourceRelinkChoiceKind.Relink, item.Match.AssetId);
        DialogResult = true;
    }

    private void ImportAsNew_Click(object sender, RoutedEventArgs e)
    {
        Choice = new MissingSourceRelinkChoice(MissingSourceRelinkChoiceKind.ImportAsNew);
        DialogResult = true;
    }
}
