using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Foundry;

internal sealed class FoundryProjectClientRegistration(
    string connectionName,
    string? serviceKey,
    string configurationSectionName,
    FoundryProjectClientSettings settings,
    IConfiguration clientOptionsConfiguration,
    Action<AIProjectClientOptions>? configureClientOptions)
{
    private readonly object _credentialLock = new();
    private TokenCredential? _resolvedCredential;

    internal string ConnectionName { get; } = connectionName;

    internal string? ServiceKey { get; } = serviceKey;

    internal string ConfigurationSectionName { get; } = configurationSectionName;

    internal FoundryProjectClientSettings Settings { get; } = settings;

    internal TokenCredential GetCredential()
    {
        if (_resolvedCredential is not null)
        {
            return _resolvedCredential;
        }

        lock (_credentialLock)
        {
            return _resolvedCredential ??=
                Settings.Credential ?? new DefaultAzureCredential();
        }
    }

    internal AIProjectClient CreateClient(IServiceProvider serviceProvider)
    {
        _ = serviceProvider;
        var options = CreateClientOptions();
        return new AIProjectClient(
            Settings.GetRequiredEndpoint(ConnectionName),
            GetCredential(),
            options);
    }

    internal AIProjectClientOptions CreateClientOptions()
    {
        var options = new AIProjectClientOptions();
        clientOptionsConfiguration.Bind(options);
        configureClientOptions?.Invoke(options);
        return options;
    }
}
