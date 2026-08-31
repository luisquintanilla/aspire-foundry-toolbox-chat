namespace FoundryToolbox.Client;

internal static class FoundryToolboxDisposal
{
    internal static async Task DisposeAsync(
        IAsyncDisposable target,
        TimeSpan timeout)
    {
        var disposal = target.DisposeAsync().AsTask();
        try
        {
            await disposal.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _ = disposal.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously |
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            throw;
        }
    }
}
