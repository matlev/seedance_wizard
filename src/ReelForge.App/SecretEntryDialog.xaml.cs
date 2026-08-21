using System.Windows;
using ReelForge.Application;

namespace ReelForge.App;

public partial class SecretEntryDialog : Window
{
    public SecretEntryDialog(ConfigurationRequirement requirement)
    {
        InitializeComponent();
        HeadingText.Text = $"Update {requirement.DisplayName}";
        SecretPasswordBox.Focus();
    }

    public string TakeSecret()
    {
        var value = SecretPasswordBox.Password;
        SecretPasswordBox.Clear();
        return value;
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SecretPasswordBox.Password))
        {
            ValidationText.Text = "Enter the complete updated credential.";
            return;
        }

        DialogResult = true;
    }
}
