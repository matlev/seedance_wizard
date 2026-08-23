namespace ReelForge.Application;

/// <summary>
/// Coordinates cancelable asynchronous work where only the newest request may publish a result.
/// Each operation owns and ultimately disposes its cancellation source.
/// </summary>
internal sealed class LatestOperationSequence : IDisposable
{
    private readonly object _sync = new();
    private long _generation;
    private Operation? _active;
    private bool _disposed;

    public Operation Begin(CancellationToken cancellationToken = default)
    {
        Operation? superseded;
        Operation operation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            superseded = _active;
            operation = new Operation(
                this,
                ++_generation,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _active = operation;
        }

        superseded?.Cancel();
        return operation;
    }

    public void Invalidate()
    {
        Operation? superseded;
        lock (_sync)
        {
            if (_disposed) return;
            _generation++;
            superseded = _active;
            _active = null;
        }

        superseded?.Cancel();
    }

    public void Dispose()
    {
        Operation? active;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            active = _active;
            _active = null;
        }

        // The operation may still be unwinding. It owns final CTS disposal.
        active?.Cancel();
    }

    private bool IsCurrent(Operation operation)
    {
        lock (_sync)
        {
            return !_disposed && ReferenceEquals(operation, _active);
        }
    }

    private void Complete(Operation operation)
    {
        lock (_sync)
        {
            if (ReferenceEquals(operation, _active)) _active = null;
        }
    }

    internal sealed class Operation : IDisposable
    {
        private readonly object _sync = new();
        private LatestOperationSequence? _owner;
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationToken _cancellationToken;
        private bool _disposed;

        internal Operation(
            LatestOperationSequence owner,
            long generation,
            CancellationTokenSource cancellation)
        {
            _owner = owner;
            Generation = generation;
            _cancellation = cancellation;
            _cancellationToken = cancellation.Token;
        }

        public long Generation { get; }
        public CancellationToken CancellationToken => _cancellationToken;
        public bool IsCurrent
        {
            get
            {
                lock (_sync)
                {
                    return !_disposed &&
                           !_cancellationToken.IsCancellationRequested &&
                           _owner?.IsCurrent(this) == true;
                }
            }
        }

        internal void Cancel()
        {
            lock (_sync)
            {
                if (!_disposed) _cancellation.Cancel();
            }
        }

        public void Dispose()
        {
            LatestOperationSequence? owner;
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                owner = _owner;
                _owner = null;
                _cancellation.Dispose();
            }

            owner?.Complete(this);
        }
    }
}
