using FoundryToolbox.Client;

namespace FoundryToolbox.Client.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void RuntimeAssembly_HasNoHostOrAgentFrameworkDependencies()
    {
        var references = typeof(FoundryToolboxClient)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.DoesNotContain(
            references,
            name => name.StartsWith("Aspire.", StringComparison.Ordinal));
        Assert.DoesNotContain(
            references,
            name => name.StartsWith(
                "Microsoft.Extensions.Hosting",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            references,
            name => name.StartsWith(
                "Microsoft.Agents.",
                StringComparison.Ordinal));
    }
}
