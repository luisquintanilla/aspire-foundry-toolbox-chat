using FoundryToolbox.Client;

namespace FoundryToolbox.Client.Tests;

public sealed class FoundryToolboxEndpointTests
{
    private static readonly Uri ProjectEndpoint =
        new("https://example.services.ai.azure.com/api/projects/project-one");

    [Fact]
    public void Build_CreatesConsumerEndpoint()
    {
        var endpoint = FoundryToolboxEndpoint.Build(
            ProjectEndpoint,
            "sample_toolbox-1");

        Assert.Equal(
            "https://example.services.ai.azure.com/api/projects/project-one/toolboxes/sample_toolbox-1/mcp?api-version=v1",
            endpoint.AbsoluteUri);
    }

    [Fact]
    public void Build_CreatesPinnedVersionEndpoint()
    {
        var endpoint = FoundryToolboxEndpoint.Build(
            ProjectEndpoint,
            "sample-toolbox",
            "17");

        Assert.Equal(
            "https://example.services.ai.azure.com/api/projects/project-one/toolboxes/sample-toolbox/versions/17/mcp?api-version=v1",
            endpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData(
        "https://example.services.ai.azure.com/api/projects/p/toolboxes/t/mcp?api-version=v1",
        "t",
        null)]
    [InlineData(
        "https://example.services.ai.azure.com/api/projects/p/toolboxes/t/versions/8/mcp?api-version=v1",
        "t",
        "8")]
    public void Parse_RecoversProjectRelationship(
        string endpoint,
        string expectedToolbox,
        string? expectedVersion)
    {
        var parsed = FoundryToolboxEndpoint.Parse(new Uri(endpoint));

        Assert.Equal(
            "https://example.services.ai.azure.com/api/projects/p",
            parsed.ProjectEndpoint.AbsoluteUri.TrimEnd('/'));
        Assert.Equal(expectedToolbox, parsed.ToolboxName);
        Assert.Equal(expectedVersion, parsed.Version);
        Assert.Equal("v1", parsed.ApiVersion);
    }

    [Theory]
    [InlineData("https://example.test/project/toolboxes/t/mcp")]
    [InlineData("https://example.test/project/toolboxes/t/mcp?api-version=v2")]
    [InlineData("https://example.test/project/toolboxes/t/mcp?api-version=v1&other=1")]
    [InlineData("https://example.test/project/toolboxes/t/other/mcp?api-version=v1")]
    [InlineData("https://example.test/project/toolboxes/one%2Ftwo/mcp?api-version=v1")]
    [InlineData("http://example.test/project/toolboxes/t/mcp?api-version=v1")]
    public void Parse_RejectsNonCanonicalEndpoints(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            FoundryToolboxEndpoint.Parse(new Uri(endpoint)));
    }
}
