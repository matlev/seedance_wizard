using System.Windows;

namespace ReelForge.App.Views.Dialogs;

public partial class DisplayNameDialog : Window
{
    public DisplayNameDialog(string currentDisplayName)
    {
        InitializeComponent();
        DisplayNameTextBox.Text = currentDisplayName;
        Loaded += (_, _) =>
        {
            DisplayNameTextBox.Focus();
            DisplayNameTextBox.SelectAll();
        };
    }

    public string DisplayName => DisplayNameTextBox.Text.Trim();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ValidationText.Text = "Enter a Saved Clip name.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
