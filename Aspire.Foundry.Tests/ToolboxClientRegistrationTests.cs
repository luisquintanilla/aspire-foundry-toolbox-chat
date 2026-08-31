using Azure.AI.Projects;
using Microsoft.Extensions.Configuration;
using FoundryToolbox.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Aspire.Foundry.Tests;

public sealed class ToolboxClientRegistrationTests
{
    [Fact]
    public async Task StandaloneRegistration_DoesNotRegisterProjectClient()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.AddFoundryToolboxClient(
            "toolbox",
            settings =>
            {
                settings.Endpoint = ToolboxEndpoint("one", "toolbox");
                settings.Credential = new TestTokenCredential("toolbox");
                settings.EnableEagerInitialization = false;
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();

        Assert.NotNull(
            services.GetRequiredService<FoundryToolboxClient>());
        Assert.Null(services.GetService<AIProjectClient>());
    }

    [Fact]
    public async Task ChainedRegistration_InheritsProjectCredential()
    {
        var handler = new TestMcpHandler();
        var credential = new TestTokenCredential("shared-token");
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder
            .AddFoundryProjectClient(
                "project",
                settings =>
                {
                    settings.Endpoint = ProjectEndpoint("one");
                    settings.Credential = credential;
                })
            .AddToolboxClient(
                "toolbox",
                settings =>
                {
                    settings.Endpoint = ToolboxEndpoint("one", "toolbox");
                    settings.TransportHandlerFactory = () => handler;
                    settings.EnableEagerInitialization = false;
                });

        await using var services = hostBuilder.Services.BuildServiceProvider();
        var client = services.GetRequiredService<FoundryToolboxClient>();
        _ = await client.GetToolsAsync();

        Assert.NotEmpty(handler.BearerTokens);
        Assert.All(
            handler.BearerTokens,
            token => Assert.Equal("shared-token", token));
    }

    [Fact]
    public async Task ChainedRegistration_RejectsDifferentProject()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder
            .AddFoundryProjectClient(
                "project",
                settings =>
                {
                    settings.Endpoint = ProjectEndpoint("one");
                    settings.Credential = new TestTokenCredential("project");
                })
            .AddToolboxClient(
                "toolbox",
                settings =>
                {
                    settings.Endpoint = ToolboxEndpoint("two", "toolbox");
                    settings.EnableEagerInitialization = false;
                });

        await using var services = hostBuilder.Services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.GetRequiredService<FoundryToolboxClient>());
        Assert.Contains(
            "belongs to project endpoint",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyedRegistrations_AreIndependent()
    {
        var firstHandler = new TestMcpHandler();
        var secondHandler = new TestMcpHandler();
        var hostBuilder = TestHost.CreateApplicationBuilder();
        var project = hostBuilder.AddFoundryProjectClient(
            "project",
            settings =>
            {
                settings.Endpoint = ProjectEndpoint("one");
                settings.Credential = new TestTokenCredential("project");
            });
        project.AddKeyedToolboxClient(
            "first",
            settings =>
            {
                settings.Endpoint = ToolboxEndpoint("one", "first");
                settings.Credential = new TestTokenCredential("first");
                settings.TransportHandlerFactory = () => firstHandler;
                settings.EnableEagerInitialization = false;
            });
        project.AddKeyedToolboxClient(
            "second",
            settings =>
            {
                settings.Endpoint = ToolboxEndpoint("one", "second");
                settings.Credential = new TestTokenCredential("second");
                settings.TransportHandlerFactory = () => secondHandler;
                settings.EnableEagerInitialization = false;
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();
        var first = services.GetRequiredKeyedService<FoundryToolboxClient>("first");
        var second = services.GetRequiredKeyedService<FoundryToolboxClient>("second");

        Assert.NotSame(first, second);
        Assert.Null(services.GetService<FoundryToolboxClient>());
        await Task.WhenAll(first.GetToolsAsync(), second.GetToolsAsync());
        Assert.NotEmpty(firstHandler.BearerTokens);
        Assert.NotEmpty(secondHandler.BearerTokens);
        Assert.All(
            firstHandler.BearerTokens,
            token => Assert.Equal("first", token));
        Assert.All(
            secondHandler.BearerTokens,
            token => Assert.Equal("second", token));
    }

    [Fact]
    public async Task ChainedRegistration_ExplicitCredentialOverridesProjectCredential()
    {
        var handler = new TestMcpHandler();
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder
            .AddFoundryProjectClient(
                "project",
                settings =>
                {
                    settings.Endpoint = ProjectEndpoint("one");
                    settings.Credential = new TestTokenCredential("project");
                })
            .AddToolboxClient(
                "toolbox",
                settings =>
                {
                    settings.Endpoint = ToolboxEndpoint("one", "toolbox");
                    settings.Credential = new TestTokenCredential("toolbox");
                    settings.TransportHandlerFactory = () => handler;
                    settings.EnableEagerInitialization = false;
                });

        await using var services = hostBuilder.Services.BuildServiceProvider();
        _ = await services
            .GetRequiredService<FoundryToolboxClient>()
            .GetToolsAsync();

        Assert.NotEmpty(handler.BearerTokens);
        Assert.All(
            handler.BearerTokens,
            token => Assert.Equal("toolbox", token));
    }

    [Fact]
    public async Task EagerInitialization_DiscoversToolsDuringHostStartup()
    {
        var handler = new TestMcpHandler();
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.AddFoundryToolboxClient(
            "toolbox",
            settings =>
            {
                settings.Endpoint = ToolboxEndpoint("one", "toolbox");
                settings.Credential = new TestTokenCredential("toolbox");
                settings.TransportHandlerFactory = () => handler;
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();
        var startup = Assert.Single(
            services.GetServices<IHostedService>()
                .OfType<FoundryToolboxStartupService>());

        await startup.StartAsync(CancellationToken.None);

        Assert.NotEmpty(handler.BearerTokens);
        Assert.Equal(
            FoundryToolboxReadiness.Healthy,
            services.GetRequiredService<FoundryToolboxClient>().Readiness);
    }

    [Fact]
    public async Task ConfigureSettings_WinsOverConfigurationAndConnectionString()
    {
        var handler = new TestMcpHandler();
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Aspire:Foundry:Toolbox:Endpoint"] =
                    ToolboxEndpoint("configuration", "toolbox").AbsoluteUri,
                ["ConnectionStrings:toolbox"] =
                    $"Endpoint={ToolboxEndpoint("connection", "toolbox")}",
            });
        hostBuilder.AddFoundryToolboxClient(
            "toolbox",
            settings =>
            {
                settings.Endpoint = ToolboxEndpoint("delegate", "toolbox");
                settings.Credential = new TestTokenCredential("toolbox");
                settings.TransportHandlerFactory = () => handler;
                settings.EnableEagerInitialization = false;
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();
        _ = await services
            .GetRequiredService<FoundryToolboxClient>()
            .GetToolsAsync();

        Assert.NotEmpty(handler.RequestUris);
        Assert.All(
            handler.RequestUris,
            uri => Assert.Contains(
                "/api/projects/delegate/",
                uri?.AbsolutePath,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task HealthCheck_ReportsReadinessWithoutConnecting()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.AddFoundryToolboxClient(
            "toolbox",
            settings =>
            {
                settings.Endpoint = ToolboxEndpoint("one", "toolbox");
                settings.Credential = new TestTokenCredential("toolbox");
                settings.EnableEagerInitialization = false;
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();
        var report = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync();

        var entry = Assert.Single(report.Entries);
        Assert.Equal("foundry-toolbox", entry.Key);
        Assert.Equal(HealthStatus.Unhealthy, entry.Value.Status);
    }

    private static Uri ProjectEndpoint(string project) =>
        new($"https://example.services.ai.azure.com/api/projects/{project}");

    private static Uri ToolboxEndpoint(string project, string toolbox) =>
        new(
            $"{ProjectEndpoint(project).AbsoluteUri}/toolboxes/{toolbox}/mcp?api-version=v1");
}
