using Azure.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace FoundryToolbox.Client;

public sealed class FoundryToolboxClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _stateTransitionLock = new();
    private readonly HttpClient _httpClient;
    private readonly FoundryToolboxConnector _connector;
    private readonly ILogger<FoundryToolboxClient> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly TimeSpan _connectionDisposalTimeout;
    private readonly Lazy<Task> _disposeTask;
    private readonly Func<Task>? _beforePublish;
    private ClientState _state = ClientState.Initial;
    private int _disposeState;

    public FoundryToolboxClient(
        FoundryToolboxClientOptions options,
        TokenCredential credential,
        HttpMessageHandler? innerHandler = null,
        ILoggerFactory? loggerFactory = null)
        : this(
            options,
            credential,
            innerHandler,
            loggerFactory,
            beforePublish: null)
    {
    }

    internal FoundryToolboxClient(
        FoundryToolboxClientOptions options,
        TokenCredential credential,
        HttpMessageHandler? innerHandler,
        ILoggerFactory? loggerFactory,
        Func<Task>? beforePublish)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);
        _ = options.Validate();

        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<FoundryToolboxClient>();
        _connectionDisposalTimeout = options.ConnectionDisposalTimeout;

        var authenticationHandler =
            new FoundryToolboxAuthenticationHandler(credential, options)
            {
                InnerHandler = innerHandler ?? new SocketsHttpHandler(),
            };
        _httpClient = new HttpClient(authenticationHandler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _connector = new FoundryToolboxConnector(
            options,
            _httpClient,
            loggerFactory);
        _disposeTask = new Lazy<Task>(
            DisposeCoreAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _beforePublish = beforePublish;
    }

    public FoundryToolboxReadiness Readiness =>
        Volatile.Read(ref _state).Readiness;

    public Exception? LastError => Volatile.Read(ref _state).LastError;

    public Task<IReadOnlyList<AIFunction>> GetToolsAsync(
        CancellationToken cancellationToken = default) =>
        ConnectAsync(forceRefresh: false, cancellationToken);

    public Task<IReadOnlyList<AIFunction>> RefreshToolsAsync(
        CancellationToken cancellationToken = default) =>
        ConnectAsync(forceRefresh: true, cancellationToken);

    public bool IsToolboxTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        return Volatile.Read(ref _state).ToolNames.Contains(toolName);
    }

    private async Task<IReadOnlyList<AIFunction>> ConnectAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation.Token);
        var operationToken = operationCancellation.Token;

        var state = Volatile.Read(ref _state);
        if (!forceRefresh &&
            state.Readiness == FoundryToolboxReadiness.Healthy)
        {
            return state.Tools;
        }

        try
        {
            await _connectionGate.WaitAsync(operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
            ThrowIfDisposed();
            throw;
        }

        try
        {
            ThrowIfDisposed();

            state = Volatile.Read(ref _state);
            if (!forceRefresh &&
                state.Readiness == FoundryToolboxReadiness.Healthy)
            {
                return state.Tools;
            }

            var priorConnection = state.Connection;
            if (priorConnection is null)
            {
                Volatile.Write(
                    ref _state,
                    state with
                    {
                        Readiness = FoundryToolboxReadiness.Initializing,
                    });
            }

            FoundryToolboxConnection newConnection;
            try
            {
                newConnection = await _connector
                    .OpenAsync(
                        cancellationToken,
                        _lifetimeCancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (_lifetimeCancellation.IsCancellationRequested)
            {
                ThrowIfDisposed();
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportDiscoveryFailure(priorConnection, ex);
                throw;
            }

            try
            {
                var tools = newConnection.Tools
                    .Select(tool => CreateSafeFunction(tool, newConnection))
                    .ToArray();
                EnsureUniqueToolNames(tools);
                var toolNames = tools
                    .Select(tool => tool.Name)
                    .ToHashSet(StringComparer.Ordinal);

                operationToken.ThrowIfCancellationRequested();
                if (_beforePublish is not null)
                {
                    await _beforePublish().ConfigureAwait(false);
                }

                if (!TryPublishConnection(
                    new ClientState(
                        newConnection,
                        tools,
                        toolNames,
                        FoundryToolboxReadiness.Healthy,
                        LastError: null)))
                {
                    ThrowIfDisposed();
                    throw new InvalidOperationException(
                        "The Foundry Toolbox connection could not be published.");
                }
            }
            catch (OperationCanceledException)
                when (_lifetimeCancellation.IsCancellationRequested)
            {
                await DisposeConnectionBestEffortAsync(
                    newConnection,
                    "new").ConfigureAwait(false);
                ThrowIfDisposed();
                throw;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await DisposeConnectionBestEffortAsync(
                    newConnection,
                    "new").ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await DisposeConnectionBestEffortAsync(
                    newConnection,
                    "new").ConfigureAwait(false);
                ReportDiscoveryFailure(priorConnection, ex);
                throw;
            }

            if (priorConnection is not null)
            {
                await DisposeConnectionBestEffortAsync(
                    priorConnection,
                    "replaced").ConfigureAwait(false);
            }

            var currentTools = Volatile.Read(ref _state).Tools;
            _logger.LogInformation(
                "Discovered {ToolCount} tools from Foundry Toolbox: {ToolNames}.",
                currentTools.Count,
                string.Join(", ", currentTools.Select(tool => tool.Name)));

            return currentTools;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private AIFunction CreateSafeFunction(
        McpClientTool tool,
        FoundryToolboxConnection connection)
    {
        var function = new FoundryToolboxFunction(
            tool,
            connection,
            (exception, readiness) =>
                ReportInvocationFailure(connection, exception, readiness));
        return FoundryToolboxToolMetadata.RequiresApproval(tool)
            ? new ApprovalRequiredAIFunction(function)
            : function;
    }

    private void ReportInvocationFailure(
        FoundryToolboxConnection connection,
        Exception exception,
        FoundryToolboxReadiness readiness)
    {
        while (Volatile.Read(ref _disposeState) == 0)
        {
            var state = Volatile.Read(ref _state);
            if (!ReferenceEquals(state.Connection, connection))
            {
                return;
            }

            var replacement = state with
            {
                LastError = exception,
                Readiness = readiness,
            };
            if (ReferenceEquals(
                Interlocked.CompareExchange(ref _state, replacement, state),
                state))
            {
                return;
            }
        }
    }

    private void ReportDiscoveryFailure(
        FoundryToolboxConnection? priorConnection,
        Exception exception)
    {
        while (Volatile.Read(ref _disposeState) == 0)
        {
            var state = Volatile.Read(ref _state);
            if (!ReferenceEquals(state.Connection, priorConnection))
            {
                return;
            }

            var readiness = FoundryToolboxConsent.TryParse(exception, out _)
                ? FoundryToolboxReadiness.ConsentRequired
                : priorConnection is null
                    ? FoundryToolboxReadiness.Unhealthy
                    : state.Readiness;
            var replacement = state with
            {
                LastError = exception,
                Readiness = readiness,
            };
            if (ReferenceEquals(
                Interlocked.CompareExchange(ref _state, replacement, state),
                state))
            {
                return;
            }
        }
    }

    private bool TryPublishConnection(ClientState state)
    {
        lock (_stateTransitionLock)
        {
            if (_disposeState != 0)
            {
                return false;
            }

            Interlocked.Exchange(ref _state, state);
            return true;
        }
    }

    private static void EnsureUniqueToolNames(IEnumerable<AIFunction> tools)
    {
        var duplicateNames = tools
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            throw new InvalidOperationException(
                $"Tool names must be unique. Duplicates: {string.Join(", ", duplicateNames)}.");
        }
    }

    private async Task DisposeConnectionBestEffortAsync(
        FoundryToolboxConnection connection,
        string connectionRole)
    {
        try
        {
            await FoundryToolboxDisposal.DisposeAsync(
                connection,
                _connectionDisposalTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "The {ConnectionRole} Foundry Toolbox connection did not dispose normally.",
                connectionRole);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);

    public ValueTask DisposeAsync() => new(_disposeTask.Value);

    private async Task DisposeCoreAsync()
    {
        lock (_stateTransitionLock)
        {
            Volatile.Write(ref _disposeState, 1);
        }

        _lifetimeCancellation.Cancel();
        await _connectionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var state = Volatile.Read(ref _state);
            Interlocked.Exchange(
                ref _state,
                state with
                {
                    Connection = null,
                    Tools = [],
                    ToolNames = new HashSet<string>(StringComparer.Ordinal),
                    Readiness = FoundryToolboxReadiness.Disposed,
                });

            var activeConnectionDisposal = state.Connection is null
                ? Task.CompletedTask
                : FoundryToolboxDisposal.DisposeAsync(
                    state.Connection,
                    _connectionDisposalTimeout);
            var lateAttemptCleanup = _connector.WaitForLateAttemptsAsync(
                _connectionDisposalTimeout);
            await Task.WhenAll(
                activeConnectionDisposal,
                lateAttemptCleanup).ConfigureAwait(false);
        }
        finally
        {
            _httpClient.Dispose();
            _lifetimeCancellation.Dispose();
            _connectionGate.Release();
        }
    }

    private sealed record ClientState(
        FoundryToolboxConnection? Connection,
        IReadOnlyList<AIFunction> Tools,
        IReadOnlySet<string> ToolNames,
        FoundryToolboxReadiness Readiness,
        Exception? LastError)
    {
        internal static ClientState Initial { get; } = new(
            Connection: null,
            Tools: [],
            ToolNames: new HashSet<string>(StringComparer.Ordinal),
            Readiness: FoundryToolboxReadiness.Initializing,
            LastError: null);

    }
}
