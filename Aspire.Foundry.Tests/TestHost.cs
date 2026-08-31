using Microsoft.Extensions.Hosting;

namespace Aspire.Foundry.Tests;

internal static class TestHost
{
    internal static HostApplicationBuilder CreateApplicationBuilder() =>
        new(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
        });
}
