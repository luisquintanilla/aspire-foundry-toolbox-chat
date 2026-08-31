using ModelContextProtocol.Client;

namespace FoundryToolbox.Client;

internal sealed class FoundryToolboxConnection : IAsyncDisposable
{
    private readonly object _leaseLock = new();
    private readonly Lazy<Task> _disposeTask;
    private TaskCompletionSource? _drained;
    private int _activeInvocations;
    private bool _retired;

    internal FoundryToolboxConnection(
        McpClient client,
        IReadOnlyList<McpClientTool> tools)
    {
        Client = client;
        Tools = tools;
        _disposeTask = new Lazy<Task>(
            DisposeCoreAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal McpClient Client { get; }

    internal IReadOnlyList<McpClientTool> Tools { get; }

    internal IDisposable AcquireInvocationLease()
    {
        lock (_leaseLock)
        {
            ObjectDisposedException.ThrowIf(
                _retired,
                nameof(FoundryToolboxConnection));
            _activeInvocations++;
            return new InvocationLease(this);
        }
    }

    public ValueTask DisposeAsync() => new(_disposeTask.Value);

    private async Task DisposeCoreAsync()
    {
        Task? drainTask;
        lock (_leaseLock)
        {
            _retired = true;
            drainTask = _activeInvocations == 0
                ? null
                : (_drained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }

        if (drainTask is not null)
        {
            await drainTask.ConfigureAwait(false);
        }

        await Client.DisposeAsync().ConfigureAwait(false);
    }

    private void ReleaseInvocationLease()
    {
        lock (_leaseLock)
        {
            _activeInvocations--;
            if (_retired && _activeInvocations == 0)
            {
                _drained?.TrySetResult();
            }
        }
    }

    private sealed class InvocationLease(
        FoundryToolboxConnection connection) : IDisposable
    {
        private FoundryToolboxConnection? _connection = connection;

        public void Dispose() =>
            Interlocked.Exchange(ref _connection, null)?
                .ReleaseInvocationLease();
    }
}
