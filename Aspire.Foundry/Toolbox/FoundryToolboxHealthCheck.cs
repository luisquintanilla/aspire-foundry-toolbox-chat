using FoundryToolbox.Client;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Foundry;

internal sealed class FoundryToolboxHealthCheck(
    FoundryToolboxClient client,
    bool required) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = cancellationToken;

        var result = client.Readiness switch
        {
            FoundryToolboxReadiness.Healthy =>
                HealthCheckResult.Healthy("Toolbox tools are ready."),
            FoundryToolboxReadiness.Initializing when !required =>
                HealthCheckResult.Degraded("Optional Toolbox discovery has not completed."),
            FoundryToolboxReadiness.ConsentRequired =>
                HealthCheckResult.Degraded("A Toolbox source requires user consent."),
            FoundryToolboxReadiness.Unhealthy when !required =>
                HealthCheckResult.Degraded(
                    "Optional Toolbox discovery failed.",
                    client.LastError),
            FoundryToolboxReadiness.Disposed =>
                HealthCheckResult.Unhealthy("The Toolbox client is disposed."),
            _ =>
                HealthCheckResult.Unhealthy(
                    $"Toolbox readiness is {client.Readiness}.",
                    client.LastError),
        };

        return Task.FromResult(result);
    }
}
