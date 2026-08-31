using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FoundryToolbox.Client.Tests;

public sealed class FoundryToolboxChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_AppendsToolsWithoutMutatingCallerOptions()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);
        var localTool = AIFunctionFactory.Create(
            () => "local",
            "local",
            "A local function.");
        var options = new ChatOptions
        {
            AllowMultipleToolCalls = false,
            Tools = [localTool],
        };
        using var cancellation = new CancellationTokenSource();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            options,
            cancellation.Token);

        Assert.Equal("batch", response.Text);
        Assert.Equal(1, innerClient.BatchCallCount);
        Assert.NotSame(options, innerClient.LastOptions);
        Assert.False(innerClient.LastOptions!.AllowMultipleToolCalls);
        Assert.Equal(
            ["local", "echo"],
            innerClient.LastOptions.Tools!.Select(tool => tool.Name));
        Assert.Same(localTool, innerClient.LastOptions.Tools![0]);
        Assert.Single(options.Tools);
        Assert.Equal("local", options.Tools[0].Name);
        Assert.Equal(cancellation.Token, innerClient.LastCancellationToken);
        Assert.Equal(1, fixture.Count("tools/list"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AttachesToolsAndStreamsInnerUpdates()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);
        using var cancellation = new CancellationTokenSource();
        var updates = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: cancellation.Token))
        {
            updates.Add(update);
        }

        var streamedUpdate = Assert.Single(updates);
        Assert.Equal("stream", streamedUpdate.Text);
        Assert.Equal(1, innerClient.StreamingCallCount);
        Assert.Equal(
            ["echo"],
            innerClient.LastOptions!.Tools!.Select(tool => tool.Name));
        Assert.Equal(
            cancellation.Token,
            innerClient.LastCancellationToken);
    }

    [Fact]
    public async Task GetResponseAsync_PreservesApprovalRequiredFunctions()
    {
        using var fixture = new ToolboxMcpFixture
        {
            ApprovalPolicy = "always",
        };
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.IsType<ApprovalRequiredAIFunction>(
            Assert.Single(innerClient.LastOptions!.Tools!));
    }

    [Fact]
    public async Task GetResponseAsync_RequireApprovalForAllToolsWrapsOptionalFunctions()
    {
        using var fixture = new ToolboxMcpFixture
        {
            ApprovalPolicy = "never",
        };
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient,
            new FoundryToolboxChatClientOptions
            {
                RequireApprovalForAllTools = true,
            });

        _ = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.IsType<ApprovalRequiredAIFunction>(
            Assert.Single(innerClient.LastOptions!.Tools!));
    }

    [Fact]
    public async Task GetResponseAsync_DuplicateNamesFailBeforeCallingInnerClient()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);
        var duplicate = AIFunctionFactory.Create(
            () => "local",
            "echo",
            "Conflicts with the Toolbox function.");
        var options = new ChatOptions
        {
            Tools = [duplicate],
        };

        var exception = await Assert.ThrowsAsync<
            FoundryToolboxToolConflictException>(() =>
                client.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "hello")],
                    options));

        Assert.Equal(["echo"], exception.DuplicateToolNames);
        Assert.Contains("echo", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, innerClient.BatchCallCount);
        Assert.Same(duplicate, Assert.Single(options.Tools));
    }

    [Fact]
    public async Task GetResponseAsync_DiscoveryFailureIsTypedAndDoesNotCallInnerClient()
    {
        using var fixture = new ToolboxMcpFixture
        {
            FailListRequests = true,
        };
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);

        var exception = await Assert.ThrowsAsync<
            FoundryToolboxDiscoveryException>(() =>
                client.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "hello")]));

        Assert.NotNull(exception.InnerException);
        Assert.Equal(FoundryToolboxReadiness.Unhealthy, toolboxClient.Readiness);
        Assert.Equal(0, innerClient.BatchCallCount);
        Assert.Equal(1, fixture.Count("tools/list"));
    }

    [Fact]
    public async Task GetResponseAsync_DiscoveryConsentPreservesConsentDetails()
    {
        using var fixture = new ToolboxMcpFixture
        {
            ReturnConsentOnList = true,
        };
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);

        var exception = await Assert.ThrowsAsync<
            FoundryToolboxConsentRequiredException>(() =>
                client.GetResponseAsync(
                    [new ChatMessage(ChatRole.User, "hello")]));

        Assert.True(exception.OccurredDuringDiscovery);
        Assert.Equal(
            "https://region.consent.azure-apihub.net/login?id=1",
            exception.ConsentUri.AbsoluteUri);
        Assert.Equal(
            FoundryToolboxReadiness.ConsentRequired,
            toolboxClient.Readiness);
        Assert.Equal(0, innerClient.BatchCallCount);
    }

    [Fact]
    public async Task GetResponseAsync_CallerCancellationIsNotWrappedOrDelegated()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
        };
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);
        using var cancellation = new CancellationTokenSource();
        var response = client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: cancellation.Token);
        await fixture.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await response);
        Assert.Equal(0, innerClient.BatchCallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CallerCancellationIsNotWrappedOrDelegated()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
        };
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        using var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hello")],
                cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();
        await fixture.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await moveNext);
        Assert.Equal(0, innerClient.StreamingCallCount);
    }

    [Fact]
    public async Task Dispose_DoesNotDisposeSeparatelyOwnedToolboxClient()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var toolboxClient = CreateToolboxClient(fixture);
        var innerClient = new RecordingChatClient();
        var client = new FoundryToolboxChatClient(
            innerClient,
            toolboxClient);

        client.Dispose();
        var tools = await toolboxClient.GetToolsAsync();

        Assert.True(innerClient.IsDisposed);
        Assert.Equal("echo", Assert.Single(tools).Name);
        Assert.Equal(FoundryToolboxReadiness.Healthy, toolboxClient.Readiness);
    }

    [Fact]
    public async Task UseFoundryToolbox_ResolvesUnkeyedAndKeyedClients()
    {
        using var defaultFixture = new ToolboxMcpFixture();
        using var keyedFixture = new ToolboxMcpFixture();
        await using var defaultClient = CreateToolboxClient(defaultFixture);
        await using var keyedClient = CreateToolboxClient(keyedFixture);
        var serviceProvider = new ToolboxServiceProvider(
            defaultClient,
            "research",
            keyedClient);

        using var defaultPipeline = new ChatClientBuilder(
                new RecordingChatClient())
            .UseFoundryToolbox()
            .Build(serviceProvider);
        using var keyedPipeline = new ChatClientBuilder(
                new RecordingChatClient())
            .UseFoundryToolbox("research")
            .Build(serviceProvider);

        Assert.Same(
            defaultClient,
            Assert.IsType<FoundryToolboxChatClient>(
                defaultPipeline).ToolboxClient);
        Assert.Same(
            keyedClient,
            Assert.IsType<FoundryToolboxChatClient>(
                keyedPipeline).ToolboxClient);
    }

    [Fact]
    public async Task UseFoundryToolbox_BeforeFunctionInvocationBuildsItOutermost()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var toolboxClient = CreateToolboxClient(fixture);

        using var pipeline = new ChatClientBuilder(
                new RecordingChatClient())
            .UseFoundryToolbox(toolboxClient)
            .UseFunctionInvocation()
            .Build();

        var toolboxMiddleware =
            Assert.IsType<FoundryToolboxChatClient>(pipeline);
        Assert.NotNull(
            toolboxMiddleware.GetService<FunctionInvokingChatClient>());
    }

    private static FoundryToolboxClient CreateToolboxClient(
        HttpMessageHandler handler) =>
        new(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
                MaximumDiscoveryAttempts = 1,
                InitialDiscoveryRetryDelay = TimeSpan.Zero,
            },
            new TestTokenCredential("test-token"),
            handler);

    private sealed class RecordingChatClient : IChatClient
    {
        public int BatchCallCount { get; private set; }

        public int StreamingCallCount { get; private set; }

        public ChatOptions? LastOptions { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            BatchCallCount++;
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(
                new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, "batch")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate>
            GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation]
                CancellationToken cancellationToken = default)
        {
            _ = messages;
            StreamingCallCount++;
            LastOptions = options;
            LastCancellationToken = cancellationToken;
            await Task.Yield();
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                "stream");
        }

        public object? GetService(
            Type serviceType,
            object? serviceKey = null)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            return serviceKey is null &&
                serviceType.IsInstanceOfType(this)
                    ? this
                    : null;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ToolboxServiceProvider(
        FoundryToolboxClient defaultClient,
        object serviceKey,
        FoundryToolboxClient keyedClient) : IKeyedServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(FoundryToolboxClient)
                ? defaultClient
                : null;

        public object? GetKeyedService(
            Type serviceType,
            object? requestedServiceKey) =>
            serviceType == typeof(FoundryToolboxClient) &&
            Equals(serviceKey, requestedServiceKey)
                ? keyedClient
                : null;

        public object GetRequiredKeyedService(
            Type serviceType,
            object? requestedServiceKey) =>
            GetKeyedService(serviceType, requestedServiceKey) ??
            throw new InvalidOperationException(
                $"No keyed service was registered for '{requestedServiceKey}'.");
    }
}
