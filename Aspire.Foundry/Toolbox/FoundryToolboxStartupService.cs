using FoundryToolbox.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aspire.Foundry;

internal sealed class FoundryToolboxStartupService(
    FoundryToolboxClient client,
    ILogger<FoundryToolboxStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await client
                .GetToolsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            if (FoundryToolboxConsent.TryParse(ex, out _))
            {
                logger.LogWarning(
                    "Foundry Toolbox discovery requires user consent.");
            }
            else
            {
                logger.LogWarning(
                    ex,
                    "Foundry Toolbox discovery did not complete during host startup.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.CompletedTask;
    }
}
