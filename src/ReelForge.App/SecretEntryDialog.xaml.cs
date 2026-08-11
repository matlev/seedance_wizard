using System.Windows;
using ReelForge.Application;

namespace ReelForge.App;

public partial class SecretEntryDialog : Window
{
    public SecretEntryDialog(ConfigurationRequirement requirement)
    {
        InitializeComponent();
        HeadingText.Text = $"Replace {requirement.DisplayName}";
        SecretPasswordBox.Focus();
    }

    public string TakeSecret()
    {
        var value = SecretPasswordBox.Password;
        SecretPasswordBox.Clear();
        return value;
    }

    private void Store_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SecretPasswordBox.Password))
        {
            ValidationText.Text = "Enter the complete replacement credential.";
            return;
        }

        DialogResult = true;
    }
}
