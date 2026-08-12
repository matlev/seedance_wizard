using System.Windows;

namespace ReelForge.App;

public partial class AssetNameDialog : Window
{
    public AssetNameDialog(string currentName)
    {
        InitializeComponent();
        AssetNameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            AssetNameTextBox.Focus();
            AssetNameTextBox.SelectAll();
        };
    }

    public string AssetName => AssetNameTextBox.Text.Trim();

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AssetNameTextBox.Text))
        {
            ValidationText.Text = "Enter an asset name.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
