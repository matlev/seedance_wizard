using System.Windows;
using ReelForge.App.Views.Editing;
using ReelForge.Application.Editing.Composition;
using ReelForge.Core;

namespace ReelForge.App.Views.Dialogs;

public partial class TimingPlacementDialog : Window
{
    private readonly CompositionPlacementDecisionRequest _request;

    public TimingPlacementDialog(CompositionPlacementDecisionRequest request)
    {
        InitializeComponent();
        _request = request ?? throw new ArgumentNullException(nameof(request));
        VideoOnlyText.Visibility = request.RequiresVideoOnlyApproval
            ? Visibility.Visible
            : Visibility.Collapsed;
        PrimaryButton.Content = request.RequiresVideoOnlyApproval
            ? "Place Video Only"
            : "Place Anyway";
        DetailText.Text = string.Join("\n", new[]
        {
            Describe(request.VideoAssessment),
            Describe(request.AudioAssessment)
        }.Where(detail => detail is not null));
    }

    public CompositionPlacementDecision Decision { get; private set; } =
        new(CompositionPlacementAction.Cancel);

    private void Place_Click(object sender, RoutedEventArgs e)
    {
        Decision = new CompositionPlacementDecision(
            CompositionPlacementAction.Place,
            AcknowledgeEstimatedTiming: _request.RequiresEstimatedTimingAcknowledgement,
            ApproveVideoOnlyWithoutUsableAudio: _request.RequiresVideoOnlyApproval);
        DialogResult = true;
    }

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        Decision = new CompositionPlacementDecision(CompositionPlacementAction.AttemptRepair);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Decision = new CompositionPlacementDecision(CompositionPlacementAction.Cancel);
        DialogResult = false;
    }

    private static string? Describe(StreamTimingAssessment? assessment)
    {
        if (assessment is null || assessment.Readiness == TimingReadiness.Exact)
            return null;
        var issues = assessment.IssueClassifications.Count == 0
            ? string.Empty
            : $" — {string.Join(", ", assessment.IssueClassifications.Select(TimingWarningPresentation.FormatIssue))}";
        return $"{assessment.MediaType}: {assessment.Readiness}{issues}";
    }
}
