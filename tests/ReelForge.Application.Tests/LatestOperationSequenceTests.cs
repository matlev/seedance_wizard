using ReelForge.Application;

namespace ReelForge.Tests;

public sealed class LatestOperationSequenceTests
{
    [Fact]
    public async Task SupersedingOperationCancelsConsumableTokenUntilOwnerDisposesIt()
    {
        using var sequence = new LatestOperationSequence();
        using var first = sequence.Begin();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = first.CancellationToken.Register(cancellationObserved.SetResult);

        using var second = sequence.Begin();

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.Throws<OperationCanceledException>(first.CancellationToken.ThrowIfCancellationRequested);
    }

    [Fact]
    public void ExternalCancellationMakesActiveOperationNonCurrent()
    {
        using var sequence = new LatestOperationSequence();
        using var cancellation = new CancellationTokenSource();
        using var operation = sequence.Begin(cancellation.Token);

        cancellation.Cancel();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.False(operation.IsCurrent);
    }

    [Fact]
    public void InvalidationCancelsTokenWithoutDisposingOperationOwnedSource()
    {
        using var sequence = new LatestOperationSequence();
        using var operation = sequence.Begin();

        sequence.Invalidate();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.False(operation.IsCurrent);
        Assert.Throws<OperationCanceledException>(operation.CancellationToken.ThrowIfCancellationRequested);
    }

    [Fact]
    public void SequenceDisposalCancelsTokenWithoutDisposingOperationOwnedSource()
    {
        var sequence = new LatestOperationSequence();
        using var operation = sequence.Begin();

        sequence.Dispose();

        Assert.True(operation.CancellationToken.IsCancellationRequested);
        Assert.False(operation.IsCurrent);
        Assert.Throws<OperationCanceledException>(operation.CancellationToken.ThrowIfCancellationRequested);
    }

    [Fact]
    public void OldCompletionCannotClearNewOperation()
    {
        using var sequence = new LatestOperationSequence();
        var first = sequence.Begin();
        using var second = sequence.Begin();

        first.Dispose();

        Assert.True(second.IsCurrent);
    }

    [Fact]
    public async Task DelayedCrossSegmentWorkOnlyPublishesLatestTarget()
    {
        using var sequence = new LatestOperationSequence();
        var published = new List<int>();
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = SimulateOpenAsync(sequence.Begin(), 1, firstRelease.Task, published);
        var second = SimulateOpenAsync(sequence.Begin(), 2, secondRelease.Task, published);
        var final = SimulateOpenAsync(sequence.Begin(), 3, finalRelease.Task, published);

        secondRelease.SetResult();
        firstRelease.SetResult();
        finalRelease.SetResult();
        await Task.WhenAll(first, second, final);

        Assert.Equal([3], published);
    }

    [Fact]
    public async Task NewSameSegmentSeekInvalidatesOlderDelayedOpen()
    {
        using var sequence = new LatestOperationSequence();
        var published = new List<int>();
        var delayedOpenRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var delayedOpen = SimulateOpenAsync(sequence.Begin(), 2, delayedOpenRelease.Task, published);
        using (var sameSegmentSeek = sequence.Begin())
        {
            if (sameSegmentSeek.IsCurrent) published.Add(1);
        }

        delayedOpenRelease.SetResult();
        await delayedOpen;

        Assert.Equal([1], published);
    }

    private static async Task SimulateOpenAsync(
        LatestOperationSequence.Operation operation,
        int segment,
        Task materialization,
        List<int> published)
    {
        using (operation)
        {
            // Simulate a materializer which cannot stop immediately when cancellation is requested.
            await materialization;
            if (operation.IsCurrent) published.Add(segment);
        }
    }
}
