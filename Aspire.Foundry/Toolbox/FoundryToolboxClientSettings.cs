using System.Data.Common;
using Azure.Core;
using FoundryToolbox.Client;

namespace Aspire.Foundry;

public sealed class FoundryToolboxClientSettings
{
    public Uri? Endpoint { get; set; }

    public Uri? ProjectEndpoint { get; set; }

    public string? ToolboxName { get; set; }

    public string? Version { get; set; }

    public TokenCredential? Credential { get; set; }

    public Func<HttpMessageHandler>? TransportHandlerFactory { get; set; }

    public string? AdditionalFeatures { get; set; }

    public int MaximumDiscoveryAttempts { get; set; } = 3;

    public TimeSpan InitialDiscoveryRetryDelay { get; set; } =
        TimeSpan.FromSeconds(1);

    public TimeSpan DiscoveryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ConnectionDisposalTimeout { get; set; } =
        TimeSpan.FromSeconds(5);

    public bool Required { get; set; } = true;

    public bool EnableEagerInitialization { get; set; } = true;

    public bool DisableHealthChecks { get; set; }

    public bool DisableTracing { get; set; }

    public bool DisableMetrics { get; set; }

    internal void ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var endpoint))
        {
            Endpoint = endpoint;
            return;
        }

        var values = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString,
        };

        if (values.TryGetValue("Endpoint", out var endpointValue) &&
            Uri.TryCreate(endpointValue?.ToString(), UriKind.Absolute, out endpoint))
        {
            Endpoint = endpoint;
        }

        if (values.TryGetValue("ProjectEndpoint", out var projectValue) &&
            Uri.TryCreate(projectValue?.ToString(), UriKind.Absolute, out var project))
        {
            ProjectEndpoint = project;
        }

        if (values.TryGetValue("ToolboxName", out var nameValue))
        {
            ToolboxName = nameValue?.ToString();
        }

        if (values.TryGetValue("Version", out var versionValue))
        {
            Version = versionValue?.ToString();
        }
    }

    internal FoundryToolboxClientOptions CreateOptions(string connectionName)
    {
        var endpoint = Endpoint;
        if (endpoint is null &&
            ProjectEndpoint is not null &&
            !string.IsNullOrWhiteSpace(ToolboxName))
        {
            endpoint = FoundryToolboxEndpoint.Build(
                ProjectEndpoint,
                ToolboxName,
                Version);
        }

        if (endpoint is null)
        {
            throw new InvalidOperationException(
                $"A Foundry Toolbox client could not be configured. Provide its endpoint in 'ConnectionStrings:{connectionName}' or configure ProjectEndpoint and ToolboxName.");
        }

        return new FoundryToolboxClientOptions
        {
            Endpoint = endpoint,
            AdditionalFeatures = AdditionalFeatures,
            MaximumDiscoveryAttempts = MaximumDiscoveryAttempts,
            InitialDiscoveryRetryDelay = InitialDiscoveryRetryDelay,
            DiscoveryTimeout = DiscoveryTimeout,
            ConnectionDisposalTimeout = ConnectionDisposalTimeout,
        };
    }
}
