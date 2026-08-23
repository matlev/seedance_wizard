using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ReelForge.App.Views.Generation;

public sealed class PromptTextChangedEventArgs(string prompt) : EventArgs
{
    public string Prompt { get; } = prompt;
}

public partial class ExpandedPromptEditor : UserControl
{
    private bool _updatingText;

    public ExpandedPromptEditor() => InitializeComponent();

    public event EventHandler<PromptTextChangedEventArgs>? PromptChanged;
    public event EventHandler? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Open(string prompt)
    {
        UpdatePrompt(prompt);
        Visibility = Visibility.Visible;
        PromptTextBox.Focus();
        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
    }

    public void UpdatePrompt(string prompt)
    {
        if (string.Equals(prompt, PromptTextBox.Text, StringComparison.Ordinal)) return;
        _updatingText = true;
        try
        {
            PromptTextBox.Text = prompt;
        }
        finally
        {
            _updatingText = false;
        }
    }

    public void CloseEditor(bool notify = true)
    {
        Visibility = Visibility.Collapsed;
        if (notify) Closed?.Invoke(this, EventArgs.Empty);
    }

    private void PromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_updatingText)
            PromptChanged?.Invoke(this, new PromptTextChangedEventArgs(PromptTextBox.Text));
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseEditor();
        e.Handled = true;
    }
}
