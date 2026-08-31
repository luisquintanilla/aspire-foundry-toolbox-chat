using Aspire.Hosting.Foundry.Toolbox;

namespace Aspire.Hosting.Foundry.Toolbox.Tests;

public sealed class ProjectEndpointReadinessTests
{
    [Theory]
    [InlineData(404, "Subdomain does not map to a resource", true)]
    [InlineData(404, "The project does not exist", true)]
    [InlineData(404, "Toolbox does not exist", false)]
    [InlineData(403, "The project does not exist", false)]
    public void RetryPredicate_MatchesOnlyKnownProjectReadinessFailures(
        int status,
        string message,
        bool expected)
    {
        Assert.Equal(
            expected,
            AzureToolboxAdministration.IsProjectEndpointNotReady(
                status,
                message));
    }
}
