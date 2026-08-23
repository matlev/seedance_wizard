using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using ReelForge.App.Bootstrap;
using ReelForge.Application;
using ReelForge.Core;
using ReelForge.Infrastructure;

namespace ReelForge.App.Views.Generation;

/// <summary>Owns Generation workspace UI state, provider selection, and durable draft autosave.</summary>
internal sealed class GenerationWorkspaceCoordinator : IDisposable
{
    private readonly ApplicationRuntime _runtime;
    private readonly ProjectWorkspace _workspace;
    private readonly GenerationPanel _panel;
    private readonly ExpandedPromptEditor _promptEditor;
    private readonly ObservableCollection<GenerationReferenceChoice> _referenceChoices;
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _suppressAutosave;
    private bool _disposed;
    private long _autosaveVersion;
    private VideoProject? _scheduledAutosaveProject;
    private Guid? _scheduledAutosaveProjectId;

    public GenerationWorkspaceCoordinator(ApplicationRuntime runtime, ProjectWorkspace workspace, GenerationPanel panel,
        ExpandedPromptEditor promptEditor, ObservableCollection<GenerationReferenceChoice> referenceChoices)
    {
        _runtime = runtime;
        _workspace = workspace;
        _panel = panel;
        _promptEditor = promptEditor;
        _referenceChoices = referenceChoices;
        _panel.SetReferences(referenceChoices);
        _panel.ProviderChanged += Panel_ProviderChanged;
        _panel.DraftChanged += Panel_DraftChanged;
        _panel.ExpandPromptRequested += Panel_ExpandPromptRequested;
        _panel.ReferenceSelected += Panel_ReferenceSelected;
        _promptEditor.PromptChanged += PromptEditor_PromptChanged;
        _promptEditor.Closed += PromptEditor_Closed;
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        RefreshProviders(null);
    }

    public event EventHandler<GenerationReferenceSelectionRequestedEventArgs>? ReferenceSelectionRequested;
    public IVideoGenerationProvider CurrentProvider { get; private set; } = new FakeVideoGenerationProvider();
    public GenerationWorkflow CurrentWorkflow { get; private set; } = null!;
    public IProviderAssetPreparationService? CurrentPreparation { get; private set; }
    public IReadOnlyList<GenerationProviderChoice> ProviderChoices { get; private set; } = [];
    public ObservableCollection<GenerationReferenceChoice> ReferenceChoices => _referenceChoices;

    public void RefreshProviders(string? preferredProviderId)
    {
        InvalidateAutosave();
        var providerRuntime = _runtime.RefreshProviders(preferredProviderId);
        ProviderChoices = providerRuntime.Choices;
        CurrentPreparation = providerRuntime.PreparationService;
        CurrentWorkflow = providerRuntime.Workflow;
        CurrentProvider = providerRuntime.SelectedProvider;
        var selected = ProviderChoices.First(choice => ReferenceEquals(choice.Provider, CurrentProvider));
        WithAutosaveSuppressed(() =>
        {
            _panel.SetProviders(ProviderChoices, selected);
            _panel.ConfigureProvider(CurrentProvider);
        });
    }

    public GenerationDraft CaptureDraft() => GenerationDraftMapper.Capture(
        _panel,
        CurrentProvider,
        _workspace.Project?.CurrentGenerationDraft,
        _referenceChoices);

    public void LoadDraft(GenerationDraft draft)
    {
        InvalidateAutosave();
        WithAutosaveSuppressed(() =>
        {
            var providerChoice = ProviderChoices.FirstOrDefault(choice => choice.Provider.Capabilities.ProviderId == draft.ProviderId);
            if (providerChoice is not null)
            {
                CurrentProvider = providerChoice.Provider;
                _panel.SelectProvider(providerChoice);
                _panel.ConfigureProvider(CurrentProvider);
            }
            GenerationDraftMapper.Load(_panel, draft, _referenceChoices);
        });
    }

    public void Reset()
    {
        InvalidateAutosave();
        _promptEditor.CloseEditor(notify: false);
        WithAutosaveSuppressed(() =>
        {
            _referenceChoices.Clear();
            _panel.Prompt = string.Empty;
            _panel.Status = string.Empty;
            _panel.SetLineage("New root generation");
        });
    }

    public void SetSubmissionEnabled(bool enabled) => _panel.IsSubmissionEnabled = enabled;
    public void SetProviderEnabled(bool enabled) => _panel.IsProviderEnabled = enabled;
    public void SetStatus(string text) => _panel.Status = text;

    private void Panel_ProviderChanged(object? sender, GenerationProviderChangedEventArgs e)
    {
        CurrentProvider = e.Choice.Provider;
        WithAutosaveSuppressed(() => _panel.ConfigureProvider(CurrentProvider));
        ScheduleAutosave();
    }

    private void Panel_DraftChanged(object? sender, EventArgs e)
    {
        if (_promptEditor.IsOpen) _promptEditor.UpdatePrompt(_panel.Prompt);
        ScheduleAutosave();
    }
    private void Panel_ExpandPromptRequested(object? sender, EventArgs e) => _promptEditor.Open(_panel.Prompt);
    private void PromptEditor_PromptChanged(object? sender, PromptTextChangedEventArgs e) => _panel.Prompt = e.Prompt;
    private void PromptEditor_Closed(object? sender, EventArgs e) => _panel.FocusPromptAtEnd();

    private void Panel_ReferenceSelected(object? sender, GenerationReferenceSelectedEventArgs e)
    {
        ReferenceSelectionRequested?.Invoke(
            this,
            new GenerationReferenceSelectionRequestedEventArgs(e.Choice.ObjectKind, e.Choice.LogicalObjectId));
    }

    private void ScheduleAutosave()
    {
        if (_suppressAutosave || _disposed || _workspace.Project is null) return;

        _autosaveVersion++;
        _scheduledAutosaveProject = _workspace.Project;
        _scheduledAutosaveProjectId = _workspace.Project.Id;
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        if (_suppressAutosave || _disposed ||
            _scheduledAutosaveProject is not { } project ||
            _scheduledAutosaveProjectId is not { } projectId ||
            !IsCurrentProject(project, projectId)) return;

        var version = _autosaveVersion;
        var workflow = CurrentWorkflow;
        var draft = CaptureDraft();
        try
        {
            await workflow.SaveDraftAsync(draft);
            if (IsAutosaveCurrent(project, projectId, version))
                _panel.Status = "Draft autosaved.";
        }
        catch (Exception exception)
        {
            if (IsAutosaveCurrent(project, projectId, version))
                _panel.Status = $"Draft autosave failed: {exception.Message}";
        }
    }

    private bool IsAutosaveCurrent(VideoProject project, Guid projectId, long version) =>
        !_disposed && version == _autosaveVersion && IsCurrentProject(project, projectId);

    private bool IsCurrentProject(VideoProject project, Guid projectId) =>
        ReferenceEquals(_workspace.Project, project) && _workspace.Project?.Id == projectId;

    private void InvalidateAutosave()
    {
        _autosaveVersion++;
        _scheduledAutosaveProject = null;
        _scheduledAutosaveProjectId = null;
        _autosaveTimer.Stop();
    }

    private void WithAutosaveSuppressed(Action action)
    {
        var previous = _suppressAutosave;
        _suppressAutosave = true;
        try
        {
            action();
        }
        finally
        {
            _suppressAutosave = previous;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvalidateAutosave();
        _autosaveTimer.Tick -= AutosaveTimer_Tick;
        _panel.ProviderChanged -= Panel_ProviderChanged;
        _panel.DraftChanged -= Panel_DraftChanged;
        _panel.ExpandPromptRequested -= Panel_ExpandPromptRequested;
        _panel.ReferenceSelected -= Panel_ReferenceSelected;
        _promptEditor.PromptChanged -= PromptEditor_PromptChanged;
        _promptEditor.Closed -= PromptEditor_Closed;
    }
}

internal sealed class GenerationReferenceSelectionRequestedEventArgs(GenerationReferenceObjectKind objectKind, Guid logicalObjectId) : EventArgs
{
    public GenerationReferenceObjectKind ObjectKind { get; } = objectKind;
    public Guid LogicalObjectId { get; } = logicalObjectId;
}
