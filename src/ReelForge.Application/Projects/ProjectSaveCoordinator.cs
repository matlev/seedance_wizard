using System.Diagnostics.CodeAnalysis;

namespace ReelForge.Application;

/// <summary>
/// Serializes the complete project recovery-and-commit transaction across active and isolated
/// workspaces that share the coordinator.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification =
    "The coordination gate is held for the application lifetime and owns no external resource.")]
public sealed class ProjectSaveCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
