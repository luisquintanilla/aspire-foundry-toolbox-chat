using Aspire.Foundry;

namespace Aspire.Foundry.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ClientIntegration_DependsOnRuntimeButNotAppHost()
    {
        var references = typeof(AspireFoundryProjectClientBuilder)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.Contains("FoundryToolbox.Client", references);
        Assert.DoesNotContain(
            references,
            name => name.StartsWith(
                "Aspire.Hosting",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            references,
            name => name.StartsWith(
                "Microsoft.Agents.",
                StringComparison.Ordinal));
    }
}
