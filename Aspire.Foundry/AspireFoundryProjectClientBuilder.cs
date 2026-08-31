using Microsoft.Extensions.Hosting;

namespace Aspire.Foundry;

public sealed class AspireFoundryProjectClientBuilder
{
    internal AspireFoundryProjectClientBuilder(
        IHostApplicationBuilder hostBuilder,
        FoundryProjectClientRegistration registration)
    {
        HostBuilder = hostBuilder;
        Registration = registration;
    }

    internal IHostApplicationBuilder HostBuilder { get; }

    internal FoundryProjectClientRegistration Registration { get; }

    public string ConnectionName => Registration.ConnectionName;

    public string? ServiceKey => Registration.ServiceKey;

    public string ConfigurationSectionName => Registration.ConfigurationSectionName;
}
