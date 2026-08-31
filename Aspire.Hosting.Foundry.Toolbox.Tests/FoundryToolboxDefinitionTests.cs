using Aspire.Hosting.Foundry.Toolbox;

namespace Aspire.Hosting.Foundry.Toolbox.Tests;

public sealed class FoundryToolboxDefinitionTests
{
    [Fact]
    public void ComputeConfigurationHash_ChangesForBehaviorChanges()
    {
        var baseline = CreateDefinition(
            new FoundryMcpToolDefinition(
                "learn",
                "Search Microsoft Learn.",
                "learn",
                new Uri("https://learn.microsoft.com/api/mcp"),
                true));
        var changedApproval = CreateDefinition(
            new FoundryMcpToolDefinition(
                "learn",
                "Search Microsoft Learn.",
                "learn",
                new Uri("https://learn.microsoft.com/api/mcp"),
                false));
        var changedUri = CreateDefinition(
            new FoundryMcpToolDefinition(
                "learn",
                "Search Microsoft Learn.",
                "learn",
                new Uri("https://example.test/mcp"),
                true));

        Assert.NotEqual(
            baseline.ComputeConfigurationHash(),
            changedApproval.ComputeConfigurationHash());
        Assert.NotEqual(
            baseline.ComputeConfigurationHash(),
            changedUri.ComputeConfigurationHash());
    }

    [Fact]
    public void ComputeConfigurationHash_RejectsDuplicateToolNames()
    {
        var definition = CreateDefinition(
            new FoundryWebSearchToolDefinition("duplicate", "Search."),
            new FoundryMcpToolDefinition(
                "duplicate",
                "Docs.",
                "docs",
                new Uri("https://example.test/mcp"),
                true));

        var exception = Assert.Throws<InvalidOperationException>(
            definition.ComputeConfigurationHash);

        Assert.Contains("duplicate", exception.Message, StringComparison.Ordinal);
    }

    private static FoundryToolboxDefinition CreateDefinition(
        params FoundryToolboxToolDefinition[] tools) =>
        new("sample-toolbox", "Sample Toolbox.", tools);
}
