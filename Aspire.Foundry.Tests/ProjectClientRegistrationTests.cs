using Aspire.Foundry;
using Azure.AI.Projects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aspire.Foundry.Tests;

public sealed class ProjectClientRegistrationTests
{
    [Fact]
    public async Task AddFoundryProjectClient_RegistersUnderlyingClient()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.AddFoundryProjectClient(
            "project",
            settings =>
            {
                settings.Endpoint = ProjectEndpoint("one");
                settings.Credential = new TestTokenCredential("project");
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();

        Assert.NotNull(services.GetRequiredService<AIProjectClient>());
    }

    [Fact]
    public async Task AddKeyedFoundryProjectClient_RegistersIndependentClients()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.AddKeyedFoundryProjectClient(
            "one",
            settings =>
            {
                settings.Endpoint = ProjectEndpoint("one");
                settings.Credential = new TestTokenCredential("one");
            });
        hostBuilder.AddKeyedFoundryProjectClient(
            "two",
            settings =>
            {
                settings.Endpoint = ProjectEndpoint("two");
                settings.Credential = new TestTokenCredential("two");
            });

        await using var services = hostBuilder.Services.BuildServiceProvider();

        Assert.NotSame(
            services.GetRequiredKeyedService<AIProjectClient>("one"),
            services.GetRequiredKeyedService<AIProjectClient>("two"));
        Assert.Null(services.GetService<AIProjectClient>());
    }

    [Fact]
    public void ConfigureSettings_WinsOverConfigurationAndConnectionString()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Aspire:Foundry:ProjectClient:Endpoint"] =
                    ProjectEndpoint("configuration").AbsoluteUri,
                ["ConnectionStrings:project"] =
                    $"Endpoint={ProjectEndpoint("connection")}",
            });

        var clientBuilder = hostBuilder.AddFoundryProjectClient(
            "project",
            settings => settings.Endpoint = ProjectEndpoint("delegate"));

        Assert.Equal(
            ProjectEndpoint("delegate"),
            clientBuilder.Registration.Settings.Endpoint);
        Assert.Equal(
            "Aspire:Foundry:ProjectClient",
            clientBuilder.ConfigurationSectionName);
    }

    [Fact]
    public void ConfigureClientOptions_WinsOverConfiguration()
    {
        var hostBuilder = TestHost.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Aspire:Foundry:ProjectClient:ClientOptions:NetworkTimeout"] =
                    "00:00:02",
            });

        var clientBuilder = hostBuilder.AddFoundryProjectClient(
            "project",
            settings =>
            {
                settings.Endpoint = ProjectEndpoint("one");
                settings.Credential = new TestTokenCredential("project");
            },
            options => options.NetworkTimeout = TimeSpan.FromSeconds(8));

        var options = clientBuilder.Registration.CreateClientOptions();

        Assert.Equal(TimeSpan.FromSeconds(8), options.NetworkTimeout);
    }

    private static Uri ProjectEndpoint(string project) =>
        new($"https://example.services.ai.azure.com/api/projects/{project}");
}
