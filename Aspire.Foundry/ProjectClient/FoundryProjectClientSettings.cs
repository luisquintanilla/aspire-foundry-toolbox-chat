using System.Data.Common;
using Azure.Core;

namespace Aspire.Foundry;

public sealed class FoundryProjectClientSettings
{
    public Uri? Endpoint { get; set; }

    public TokenCredential? Credential { get; set; }

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
    }

    internal Uri GetRequiredEndpoint(string connectionName)
    {
        if (Endpoint is null ||
            !Endpoint.IsAbsoluteUri ||
            Endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) ||
            !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
        {
            throw new InvalidOperationException(
                $"A Foundry project client could not be configured. Provide an absolute HTTPS endpoint in 'ConnectionStrings:{connectionName}' or the Aspire Foundry project-client configuration section.");
        }

        return Endpoint;
    }
}
