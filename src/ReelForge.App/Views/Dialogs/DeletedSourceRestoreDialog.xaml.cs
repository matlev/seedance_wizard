using System.Windows;
using ReelForge.Application;

namespace ReelForge.App.Views.Dialogs;

public enum DeletedSourceRestoreChoiceKind { Cancel, Restore, ImportAsNew }

public sealed record DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind Kind, Guid? DeletedAssetId = null);

public sealed class DeletedSourceRestoreDialogItem
{
    public DeletedSourceRestoreDialogItem(DeletedPhysicalAssetRestoreMatch match) => Match = match;
    public DeletedPhysicalAssetRestoreMatch Match { get; }
    public string DisplayText => Match.DependencyReport.IsInUse
        ? $"{Match.DisplayName} — {string.Join(", ", Match.DependencyReport.DisplayDescriptions)}"
        : $"{Match.DisplayName} — no current project references";
}

public partial class DeletedSourceRestoreDialog : Window
{
    private readonly bool _allowImportAsNew;

    public DeletedSourceRestoreDialog(
        string candidateName,
        IReadOnlyList<DeletedPhysicalAssetRestoreMatch> matches,
        bool allowImportAsNew)
    {
        InitializeComponent();
        _allowImportAsNew = allowImportAsNew;
        DescriptionText.Text = allowImportAsNew
            ? $"'{candidateName}' has the same verified SHA-256 identity as deleted project media. Choose the original source to restore, or keep this as a new import."
            : $"'{candidateName}' can restore one of these deleted project sources. Choose the original source identity to restore.";
        MatchesList.ItemsSource = matches.Select(match => new DeletedSourceRestoreDialogItem(match)).ToArray();
        MatchesList.SelectedIndex = matches.Count == 1 ? 0 : -1;
        ImportAsNewButton.Visibility = allowImportAsNew ? Visibility.Visible : Visibility.Collapsed;
    }

    public DeletedSourceRestoreChoice Choice { get; private set; } = new(DeletedSourceRestoreChoiceKind.Cancel);

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (MatchesList.SelectedItem is not DeletedSourceRestoreDialogItem item)
        {
            MessageBox.Show(this, "Choose the deleted source identity to restore.", "Restore deleted source",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Choice = new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.Restore, item.Match.AssetId);
        DialogResult = true;
    }

    private void ImportAsNew_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowImportAsNew) return;
        Choice = new DeletedSourceRestoreChoice(DeletedSourceRestoreChoiceKind.ImportAsNew);
        DialogResult = true;
    }
}
