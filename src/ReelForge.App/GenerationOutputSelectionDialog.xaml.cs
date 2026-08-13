using System.Windows;
using ReelForge.Core;

namespace ReelForge.App;

public partial class GenerationOutputSelectionDialog : Window
{
    public GenerationOutputSelectionDialog(IReadOnlyList<ProjectAsset> outputs)
    {
        InitializeComponent();
        OutputsList.ItemsSource = outputs.Select(asset => new OutputChoice(asset)).ToArray();
        OutputsList.SelectedIndex = 0;
    }

    public ProjectAsset? SelectedOutput => (OutputsList.SelectedItem as OutputChoice)?.Asset;

    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOutput is null) return;
        DialogResult = true;
    }

    private sealed record OutputChoice(ProjectAsset Asset)
    {
        public string DisplayName => Asset.EffectiveDisplayName;
    }
}
