using FoundryToolbox.Client;
using Microsoft.Extensions.AI;

namespace FoundryToolbox.Client.Tests;

public sealed class FoundryToolboxClientTests
{
    [Fact]
    public async Task GetToolsAsync_DiscoversApprovalSafeFunctions()
    {
        using var fixture = new ToolboxMcpFixture
        {
            ApprovalPolicy = "always",
        };
        await using var client = CreateClient(fixture);

        var tools = await client.GetToolsAsync();

        var tool = Assert.Single(tools);
        Assert.IsType<ApprovalRequiredAIFunction>(tool);
        Assert.Equal("echo", tool.Name);
        Assert.True(client.IsToolboxTool("echo"));
        Assert.Equal(FoundryToolboxReadiness.Healthy, client.Readiness);
    }

    [Fact]
    public async Task GetToolsAsync_CoalescesConcurrentInitialization()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
        };
        await using var client = CreateClient(fixture);

        var first = client.GetToolsAsync();
        await fixture.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = client.GetToolsAsync();
        fixture.ReleaseList.TrySetResult();

        await Task.WhenAll(first, second);

        Assert.Equal(1, fixture.Count("initialize"));
        Assert.Equal(1, fixture.Count("tools/list"));
    }

    [Fact]
    public async Task WaitingInitialization_CanBeCanceledWithoutCancelingOwner()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
        };
        await using var client = CreateClient(fixture);
        var owner = client.GetToolsAsync();
        await fixture.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiter = client.GetToolsAsync(cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await waiter);
        fixture.ReleaseList.TrySetResult();

        Assert.Single(await owner);
        Assert.Equal(FoundryToolboxReadiness.Healthy, client.Readiness);
    }

    [Fact]
    public async Task Discovery_RetriesOnlyFreshConnectionAttempts()
    {
        using var fixture = new ToolboxMcpFixture(listFailures: 1);
        await using var client = CreateClient(fixture);

        var tools = await client.GetToolsAsync();

        Assert.Single(tools);
        Assert.Equal(2, fixture.Count("initialize"));
        Assert.Equal(2, fixture.Count("tools/list"));
    }

    [Fact]
    public async Task Discovery_StopsAtConfiguredRetryBudget()
    {
        using var fixture = new ToolboxMcpFixture(listFailures: 3);
        await using var client = CreateClient(fixture);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.GetToolsAsync());

        Assert.Contains("retry budget", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, fixture.Count("initialize"));
        Assert.Equal(3, fixture.Count("tools/list"));
    }

    [Fact]
    public async Task DiscoveryTimeout_RetriesAndExhaustsWithinBoundedTime()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
        };
        await using var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
                MaximumDiscoveryAttempts = 2,
                InitialDiscoveryRetryDelay = TimeSpan.Zero,
                DiscoveryTimeout = TimeSpan.FromMilliseconds(50),
            },
            new TestTokenCredential("test-token"),
            fixture);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.GetToolsAsync().WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Equal(2, fixture.Count("tools/list"));
    }

    [Fact]
    public async Task DiscoveryTimeout_DoesNotRequireTransportCancellationCooperation()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
            IgnoreListCancellation = true,
        };
        var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
                MaximumDiscoveryAttempts = 1,
                DiscoveryTimeout = TimeSpan.FromMilliseconds(50),
                ConnectionDisposalTimeout = TimeSpan.FromMilliseconds(50),
            },
            new TestTokenCredential("test-token"),
            fixture);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client
                    .GetToolsAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2)));

            Assert.IsType<TimeoutException>(exception.InnerException);
            fixture.ReleaseList.TrySetResult();
            await fixture.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(FoundryToolboxReadiness.Unhealthy, client.Readiness);
            await client
                .DisposeAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(FoundryToolboxReadiness.Disposed, client.Readiness);
        }
        finally
        {
            fixture.ReleaseList.TrySetResult();
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task DiscoveryTimeout_BoundsUncooperativeLateAttempts()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
            IgnoreListCancellation = true,
        };
        var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
                MaximumDiscoveryAttempts = 2,
                InitialDiscoveryRetryDelay = TimeSpan.Zero,
                DiscoveryTimeout = TimeSpan.FromMilliseconds(50),
                ConnectionDisposalTimeout = TimeSpan.FromSeconds(1),
            },
            new TestTokenCredential("test-token"),
            fixture);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client
                    .GetToolsAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2)));

            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client
                    .GetToolsAsync()
                    .WaitAsync(TimeSpan.FromSeconds(2)));

            Assert.Contains("still cleaning up", blocked.Message, StringComparison.Ordinal);
            Assert.Equal(2, fixture.Count("tools/list"));
        }
        finally
        {
            fixture.ReleaseList.TrySetResult();
            await client.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_AllowsLateAttemptCleanupBeforeHttpShutdown()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
            IgnoreListCancellation = true,
        };
        var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
                MaximumDiscoveryAttempts = 1,
                DiscoveryTimeout = TimeSpan.FromMilliseconds(50),
                ConnectionDisposalTimeout = TimeSpan.FromSeconds(1),
            },
            new TestTokenCredential("test-token"),
            fixture);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client
                .GetToolsAsync()
                .WaitAsync(TimeSpan.FromSeconds(2)));

        var disposal = client.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        fixture.ReleaseList.TrySetResult();

        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(FoundryToolboxReadiness.Disposed, client.Readiness);
    }

    [Fact]
    public async Task InvocationFailure_IsNeverReplayed()
    {
        using var fixture = new ToolboxMcpFixture
        {
            FailToolCalls = true,
        };
        await using var client = CreateClient(fixture);
        var tool = Assert.Single(await client.GetToolsAsync());

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tool.InvokeAsync(new AIFunctionArguments
            {
                ["message"] = "hello",
            }));

        Assert.Equal(1, fixture.Count("tools/call"));
        Assert.Equal(FoundryToolboxReadiness.Unhealthy, client.Readiness);
    }

    [Fact]
    public async Task Invocation_TranslatesExactConsentContract()
    {
        using var fixture = new ToolboxMcpFixture
        {
            ReturnConsentOnCall = true,
        };
        await using var client = CreateClient(fixture);
        var tool = Assert.Single(await client.GetToolsAsync());

        var exception = await Assert.ThrowsAsync<
            FoundryToolboxConsentRequiredException>(async () =>
                await tool.InvokeAsync(new AIFunctionArguments
                {
                    ["message"] = "hello",
                }));

        Assert.Equal(
            "https://region.consent.azure-apihub.net/login?id=1",
            exception.ConsentUri.AbsoluteUri);
        Assert.False(exception.OccurredDuringDiscovery);
        Assert.Equal(
            FoundryToolboxReadiness.ConsentRequired,
            client.Readiness);
    }

    [Fact]
    public async Task SeparateClients_PreserveCredentialPartitions()
    {
        using var firstFixture = new ToolboxMcpFixture();
        using var secondFixture = new ToolboxMcpFixture();
        await using var first = CreateClient(firstFixture, "first-token");
        await using var second = CreateClient(secondFixture, "second-token");

        await Task.WhenAll(
            first.GetToolsAsync(),
            second.GetToolsAsync());

        Assert.All(
            firstFixture.Requests,
            request => Assert.Equal("first-token", request.Authorization));
        Assert.All(
            secondFixture.Requests,
            request => Assert.Equal("second-token", request.Authorization));
    }

    [Fact]
    public async Task StaleToolFailure_DoesNotPoisonReplacementConnection()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var client = CreateClient(fixture);
        var staleTool = Assert.Single(await client.GetToolsAsync());
        _ = await client.RefreshToolsAsync();
        fixture.FailToolCalls = true;

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await staleTool.InvokeAsync(new AIFunctionArguments
            {
                ["message"] = "hello",
            }));

        Assert.Equal(FoundryToolboxReadiness.Healthy, client.Readiness);
    }

    [Fact]
    public async Task RefreshToolsAsync_WaitsForInFlightInvocationBeforeRetiringConnection()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseToolCalls = true,
        };
        await using var client = CreateClient(fixture);
        var originalTool = Assert.Single(await client.GetToolsAsync());
        var invocation = originalTool
            .InvokeAsync(new AIFunctionArguments
            {
                ["message"] = "hello",
            })
            .AsTask();
        await fixture.ToolCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var refresh = client.RefreshToolsAsync();
        await fixture.SubsequentListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        AIFunction? replacementTool = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var currentTool = Assert.Single(await client.GetToolsAsync());
            if (!ReferenceEquals(originalTool, currentTool))
            {
                replacementTool = currentTool;
                break;
            }

            await Task.Delay(10);
        }

        Assert.NotNull(replacementTool);
        Assert.False(refresh.IsCompleted);

        fixture.ReleaseToolCall.TrySetResult();
        _ = await invocation.WaitAsync(TimeSpan.FromSeconds(5));
        var refreshedTools = await refresh.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(replacementTool, Assert.Single(refreshedTools));
        Assert.Equal(1, fixture.Count("tools/call"));
        Assert.Equal(2, fixture.Count("initialize"));
    }

    [Fact]
    public async Task InvalidApprovalMetadata_FailsDiscoveryClosed()
    {
        using var fixture = new ToolboxMcpFixture
        {
            ApprovalPolicy = "sometimes",
        };
        await using var client = CreateClient(fixture);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.GetToolsAsync());

        Assert.Equal(FoundryToolboxReadiness.Unhealthy, client.Readiness);
        Assert.IsType<InvalidOperationException>(client.LastError);
    }

    [Fact]
    public async Task FailedRecovery_PreservesUnhealthyReadiness()
    {
        using var fixture = new ToolboxMcpFixture
        {
            FailToolCalls = true,
        };
        await using var client = CreateClient(fixture);
        var tool = Assert.Single(await client.GetToolsAsync());
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tool.InvokeAsync(new AIFunctionArguments
            {
                ["message"] = "hello",
            }));
        fixture.FailListRequests = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.RefreshToolsAsync());

        Assert.Equal(FoundryToolboxReadiness.Unhealthy, client.Readiness);
    }

    [Fact]
    public async Task FailedRecovery_PreservesConcurrentInvocationFailure()
    {
        using var fixture = new ToolboxMcpFixture();
        await using var client = CreateClient(fixture);
        var tool = Assert.Single(await client.GetToolsAsync());
        fixture.PauseList = true;
        fixture.FailListRequests = true;
        var refresh = client.RefreshToolsAsync();
        await fixture.SubsequentListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.FailToolCalls = true;

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await tool.InvokeAsync(new AIFunctionArguments
            {
                ["message"] = "hello",
            }));
        fixture.ReleaseList.TrySetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await refresh);

        Assert.Equal(FoundryToolboxReadiness.Unhealthy, client.Readiness);
    }

    [Fact]
    public async Task LoggingFailure_DoesNotCorruptPublishedReplacement()
    {
        using var fixture = new ToolboxMcpFixture();
        using var loggerFactory = new ThrowingInformationLoggerFactory();
        await using var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
            },
            new TestTokenCredential("test-token"),
            fixture,
            loggerFactory);
        _ = await client.GetToolsAsync();
        loggerFactory.ThrowOnInformation = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await client.RefreshToolsAsync());

        Assert.Equal(FoundryToolboxReadiness.Healthy, client.Readiness);
        var tool = Assert.Single(await client.GetToolsAsync());
        _ = await tool.InvokeAsync(new AIFunctionArguments
        {
            ["message"] = "hello",
        });
    }

    [Fact]
    public async Task PostOpenCallerCancellation_DoesNotReportDiscoveryFailure()
    {
        using var fixture = new ToolboxMcpFixture();
        using var cancellation = new CancellationTokenSource();
        await using var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
            },
            new TestTokenCredential("test-token"),
            fixture,
            loggerFactory: null,
            beforePublish: () =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.GetToolsAsync(cancellation.Token));

        Assert.Equal(
            FoundryToolboxReadiness.Initializing,
            client.Readiness);
        Assert.Null(client.LastError);
        Assert.True(fixture.DeleteEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotentAndRejectsFurtherUse()
    {
        using var fixture = new ToolboxMcpFixture();
        var client = CreateClient(fixture);
        await client.GetToolsAsync();

        await client.DisposeAsync();
        await client.DisposeAsync();

        Assert.Equal(FoundryToolboxReadiness.Disposed, client.Readiness);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await client.GetToolsAsync());
    }

    [Fact]
    public async Task ConcurrentDisposeAsync_CallersAwaitTheSameCleanup()
    {
        using var fixture = new ToolboxMcpFixture();
        var client = CreateClient(fixture);
        await client.GetToolsAsync();
        fixture.PauseDelete = true;

        var firstDisposal = client.DisposeAsync().AsTask();
        await fixture.DeleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondDisposal = client.DisposeAsync().AsTask();

        Assert.Same(firstDisposal, secondDisposal);
        Assert.False(secondDisposal.IsCompleted);
        fixture.ReleaseDelete.TrySetResult();
        await Task.WhenAll(firstDisposal, secondDisposal);
        Assert.Equal(FoundryToolboxReadiness.Disposed, client.Readiness);
    }

    [Fact]
    public async Task DisposeAsync_WinsRaceBeforeConnectionPublication()
    {
        using var fixture = new ToolboxMcpFixture();
        var publicationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublication = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FoundryToolboxClient(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
            },
            new TestTokenCredential("test-token"),
            fixture,
            loggerFactory: null,
            beforePublish: async () =>
            {
                publicationEntered.TrySetResult();
                await releasePublication.Task.ConfigureAwait(false);
            });
        var discovery = client.GetToolsAsync();
        await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = client.DisposeAsync().AsTask();
        releasePublication.TrySetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await discovery);
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FoundryToolboxReadiness.Disposed, client.Readiness);
        Assert.False(client.IsToolboxTool("echo"));
        Assert.True(fixture.DeleteEntered.Task.IsCompleted);
    }

    [Fact]
    public async Task DisposeAsync_CancelsPublicationOfInFlightInitialization()
    {
        using var fixture = new ToolboxMcpFixture
        {
            PauseList = true,
        };
        var client = CreateClient(fixture);
        var initialization = client.GetToolsAsync();
        await fixture.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = client.DisposeAsync().AsTask();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await initialization);
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(FoundryToolboxReadiness.Disposed, client.Readiness);
    }

    private static FoundryToolboxClient CreateClient(
        HttpMessageHandler handler,
        string token = "test-token") =>
        new(
            new FoundryToolboxClientOptions
            {
                Endpoint = new Uri(
                    "https://example.test/projects/p/toolboxes/t/mcp?api-version=v1"),
                MaximumDiscoveryAttempts = 3,
                InitialDiscoveryRetryDelay = TimeSpan.Zero,
            },
            new TestTokenCredential(token),
            handler);
}
