using ReelForge.App.Views.Editing;

namespace ReelForge.App.Tests;

#pragma warning disable CA1707 // Test names describe behavior with readable clauses.
public sealed class CompositionAuditionCancellationPolicyTests
{
    [Fact]
    public void CancelledCapturedSelection_IsTreatedAsAStaleAuditionOperation()
    {
        using var selectionCancellation = new CancellationTokenSource();
        selectionCancellation.Cancel();

        Assert.True(CompositionAuditionController.IsStaleSelectionOperation(
            true,
            () => true,
            selectionCancellation.Token));
    }

    [Fact]
    public void CurrentUncancelledSelection_IsNotTreatedAsStale()
    {
        Assert.False(CompositionAuditionController.IsStaleSelectionOperation(
            true,
            () => true,
            CancellationToken.None));
    }

    [Fact]
    public void ReplacedSelection_IsTreatedAsAStaleAuditionOperation()
    {
        Assert.True(CompositionAuditionController.IsStaleSelectionOperation(
            true,
            () => false,
            CancellationToken.None));
    }
}
