using System.Collections.ObjectModel;
using System.Windows.Controls;
using ReelForge.Core;

namespace ReelForge.App.Views.Generation;

public sealed class GenerationSelectedEventArgs(GenerationRecord generation) : EventArgs
{
    public GenerationRecord Generation { get; } = generation;
}

public partial class GenerationHistoryPanel : UserControl
{
    private readonly ObservableCollection<GenerationRecord> _generations = [];

    public GenerationHistoryPanel()
    {
        InitializeComponent();
        GenerationsList.ItemsSource = _generations;
    }

    public event EventHandler<GenerationSelectedEventArgs>? GenerationSelected;

    public GenerationRecord? SelectedGeneration => GenerationsList.SelectedItem as GenerationRecord;

    public void SetGenerations(IEnumerable<GenerationRecord> generations)
    {
        var selectedId = SelectedGeneration?.Id;
        _generations.Clear();
        foreach (var generation in generations) _generations.Add(generation);
        if (selectedId is { } id) SelectGeneration(id);
    }

    public void SelectGeneration(Guid generationId) =>
        GenerationsList.SelectedItem = _generations.FirstOrDefault(item => item.Id == generationId);

    public void ClearSelection() => GenerationsList.SelectedItem = null;

    private void GenerationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedGeneration is { } generation)
            GenerationSelected?.Invoke(this, new GenerationSelectedEventArgs(generation));
    }
}
