using Azure.Core;
using Azure.Identity;
using FoundryToolbox.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Foundry;

internal sealed class FoundryToolboxClientRegistration(
    string connectionName,
    string? serviceKey,
    FoundryToolboxClientSettings settings,
    FoundryProjectClientRegistration? projectRegistration)
{
    private readonly object _credentialLock = new();
    private TokenCredential? _resolvedCredential;

    internal string ConnectionName { get; } = connectionName;

    internal string? ServiceKey { get; } = serviceKey;

    internal FoundryToolboxClientSettings Settings { get; } = settings;

    internal FoundryToolboxClient CreateClient(IServiceProvider serviceProvider)
    {
        var options = Settings.CreateOptions(ConnectionName);
        ValidateProjectRelationship(options);
        return new FoundryToolboxClient(
            options,
            GetCredential(),
            Settings.TransportHandlerFactory?.Invoke(),
            loggerFactory: serviceProvider.GetService<ILoggerFactory>());
    }

    private TokenCredential GetCredential()
    {
        if (_resolvedCredential is not null)
        {
            return _resolvedCredential;
        }

        lock (_credentialLock)
        {
            return _resolvedCredential ??=
                Settings.Credential ??
                projectRegistration?.GetCredential() ??
                new DefaultAzureCredential();
        }
    }

    private void ValidateProjectRelationship(
        FoundryToolboxClientOptions toolboxOptions)
    {
        if (projectRegistration is null)
        {
            return;
        }

        var projectEndpoint =
            projectRegistration.Settings.GetRequiredEndpoint(
                projectRegistration.ConnectionName);
        var toolboxProject =
            FoundryToolboxEndpoint.Parse(toolboxOptions.Endpoint).ProjectEndpoint;

        if (!UriEquals(projectEndpoint, toolboxProject))
        {
            throw new InvalidOperationException(
                $"Toolbox connection '{ConnectionName}' belongs to project endpoint '{toolboxProject}', not the project endpoint registered as '{projectRegistration.ConnectionName}'.");
        }
    }

    private static bool UriEquals(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port &&
        string.Equals(
            left.AbsolutePath.TrimEnd('/'),
            right.AbsolutePath.TrimEnd('/'),
            StringComparison.Ordinal);
}
