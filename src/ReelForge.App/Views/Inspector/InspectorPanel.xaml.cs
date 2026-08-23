using System.Windows.Controls;

namespace ReelForge.App.Views.Inspector;

public partial class InspectorPanel : UserControl
{
    public const string EmptyState = "Select an asset or generation to inspect its details and history.";

    public InspectorPanel()
    {
        InitializeComponent();
        Reset();
    }

    public string Text
    {
        get => InspectorText.Text;
        set => InspectorText.Text = value;
    }

    public void Reset() => Text = EmptyState;
}
