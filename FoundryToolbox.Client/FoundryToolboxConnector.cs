using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace FoundryToolbox.Client;

internal sealed class FoundryToolboxConnector(
    FoundryToolboxClientOptions options,
    HttpClient httpClient,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger<FoundryToolboxConnector> _logger =
        loggerFactory.CreateLogger<FoundryToolboxConnector>();
    private readonly ConcurrentDictionary<Task, LateAttempt> _lateAttempts = new();

    internal async Task<FoundryToolboxConnection> OpenAsync(
        CancellationToken callerCancellation,
        CancellationToken shutdownToken)
    {
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellation,
                shutdownToken);
        var operationToken = operationCancellation.Token;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= options.MaximumDiscoveryAttempts; attempt++)
        {
            EnsureLateAttemptCapacity();
            FoundryToolboxTelemetry.DiscoveryAttempts.Add(1);
            using var activity = FoundryToolboxTelemetry.ActivitySource.StartActivity(
                "foundry.toolbox.discover",
                ActivityKind.Client);
            activity?.SetTag("server.address", options.Endpoint.Host);
            activity?.SetTag("foundry.toolbox.discovery.attempt", attempt);

            try
            {
                var connection = await OpenAttemptAsync(
                    callerCancellation,
                    shutdownToken).ConfigureAwait(false);
                activity?.SetTag("foundry.toolbox.tool.count", connection.Tools.Count);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return connection;
            }
            catch (Exception ex) when (
                IsTransientDiscoveryFailure(ex, operationToken))
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                lastError = ex;
                if (attempt == options.MaximumDiscoveryAttempts)
                {
                    break;
                }

                var delay = TimeSpan.FromTicks(
                    options.InitialDiscoveryRetryDelay.Ticks *
                    (1L << Math.Min(attempt - 1, 6)));
                await Task.Delay(delay, operationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                throw;
            }
        }

        throw new InvalidOperationException(
            "Foundry Toolbox discovery exhausted its retry budget.",
            lastError);
    }

    private async Task<FoundryToolboxConnection> OpenAttemptAsync(
        CancellationToken callerCancellation,
        CancellationToken shutdownToken)
    {
        // Keep observing a canceled handshake so any late MCP session is disposed
        // before the shared HTTP transport closes.
        var attemptCancellation = new CancellationTokenSource();
        var attempt = OpenOnceAsync(attemptCancellation.Token);
        var cancellationReason = new CancellationReason();
        using var waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellation,
                shutdownToken);
        using var callerRegistration = callerCancellation.Register(
            static state => ((CancellationReason)state!).TrySet(
                AttemptCancellationReason.Caller),
            cancellationReason);
        using var shutdownRegistration = shutdownToken.Register(
            static state => ((CancellationReason)state!).TrySet(
                AttemptCancellationReason.Shutdown),
            cancellationReason);

        try
        {
            var connection = await attempt
                .WaitAsync(
                    options.DiscoveryTimeout,
                    waitCancellation.Token)
                .ConfigureAwait(false);
            attemptCancellation.Dispose();
            return connection;
        }
        catch (TimeoutException ex)
        {
            ReleaseAttemptResources(
                attempt,
                attemptCancellation);
            throw new TimeoutException(
                $"Foundry Toolbox discovery exceeded the {options.DiscoveryTimeout} attempt timeout.",
                ex);
        }
        catch (OperationCanceledException)
        {
            var reason = cancellationReason.Value;
            ReleaseAttemptResources(
                attempt,
                attemptCancellation);
            throw reason switch
            {
                AttemptCancellationReason.Caller =>
                    new OperationCanceledException(callerCancellation),
                AttemptCancellationReason.Shutdown =>
                    new OperationCanceledException(shutdownToken),
                _ => new OperationCanceledException(),
            };
        }
        catch
        {
            ReleaseAttemptResources(
                attempt,
                attemptCancellation);
            throw;
        }
    }

    private void ReleaseAttemptResources(
        Task<FoundryToolboxConnection> attempt,
        CancellationTokenSource attemptCancellation)
    {
        try
        {
            attemptCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (attempt.IsCompleted && !attempt.IsCompletedSuccessfully)
        {
            _ = attempt.Exception;
            attemptCancellation.Dispose();
            return;
        }

        TransferLateAttempt(
            attempt,
            attemptCancellation);
    }

    private void TransferLateAttempt(
        Task<FoundryToolboxConnection> attempt,
        CancellationTokenSource attemptCancellation)
    {
        var cleanup = ObserveLateAttemptAsync(attempt, attemptCancellation);
        _lateAttempts.TryAdd(
            cleanup,
            new LateAttempt(attemptCancellation));
        _ = cleanup.ContinueWith(
            static (completed, state) =>
                ((ConcurrentDictionary<Task, LateAttempt>)state!)
                    .TryRemove(completed, out _),
            _lateAttempts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ObserveLateAttemptAsync(
        Task<FoundryToolboxConnection> attempt,
        CancellationTokenSource attemptCancellation)
    {
        try
        {
            var connection = await attempt.ConfigureAwait(false);
            await FoundryToolboxDisposal.DisposeAsync(
                connection,
                options.ConnectionDisposalTimeout).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "A timed-out Foundry Toolbox discovery attempt completed with an error.");
        }
        finally
        {
            attemptCancellation.Dispose();
        }
    }

    internal async Task WaitForLateAttemptsAsync(TimeSpan timeout)
    {
        var attempts = _lateAttempts.ToArray();
        if (attempts.Length == 0)
        {
            return;
        }

        foreach (var attempt in attempts)
        {
            try
            {
                attempt.Value.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        try
        {
            await Task.WhenAll(attempts.Select(attempt => attempt.Key))
                .WaitAsync(timeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "Timed-out Foundry Toolbox discovery attempts did not finish cleanup before HTTP transport shutdown.");
        }
    }

    private sealed record LateAttempt(
        CancellationTokenSource Cancellation);

    private void EnsureLateAttemptCapacity()
    {
        if (_lateAttempts.Count >= options.MaximumDiscoveryAttempts)
        {
            throw new InvalidOperationException(
                "Foundry Toolbox discovery cannot start while the maximum number " +
                "of timed-out connection attempts are still cleaning up.");
        }
    }

    private enum AttemptCancellationReason
    {
        None,
        Caller,
        Shutdown,
    }

    private sealed class CancellationReason
    {
        private int _value;

        internal AttemptCancellationReason Value =>
            (AttemptCancellationReason)Volatile.Read(ref _value);

        internal void TrySet(AttemptCancellationReason reason) =>
            Interlocked.CompareExchange(
                ref _value,
                (int)reason,
                (int)AttemptCancellationReason.None);
    }

    private async Task<FoundryToolboxConnection> OpenOnceAsync(
        CancellationToken cancellationToken)
    {
        var endpointInfo = options.Validate();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = options.Endpoint,
                Name = endpointInfo.ToolboxName,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient,
            loggerFactory,
            ownsHttpClient: false);

        McpClient? client = null;
        try
        {
            client = await McpClient.CreateAsync(
                transport,
                clientOptions: null,
                loggerFactory,
                cancellationToken).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(
                options: null,
                cancellationToken).ConfigureAwait(false);
            return new FoundryToolboxConnection(client, tools.ToArray());
        }
        catch
        {
            try
            {
                await FoundryToolboxDisposal.DisposeAsync(
                    client is not null ? client : transport,
                    options.ConnectionDisposalTimeout).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                _logger.LogWarning(
                    cleanupException,
                    "Foundry Toolbox connection cleanup did not complete normally.");
            }

            throw;
        }
    }

    private static bool IsTransientDiscoveryFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested ||
            exception is OperationCanceledException ||
            FoundryToolboxConsent.TryParse(exception, out _))
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException httpException)
            {
                return httpException.StatusCode is null ||
                    httpException.StatusCode is HttpStatusCode.TooManyRequests
                        or HttpStatusCode.RequestTimeout
                        or HttpStatusCode.InternalServerError
                        or HttpStatusCode.BadGateway
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.GatewayTimeout;
            }

            if (current is IOException or TimeoutException)
            {
                return true;
            }

            if (current is McpProtocolException)
            {
                return false;
            }
        }

        return false;
    }
}
