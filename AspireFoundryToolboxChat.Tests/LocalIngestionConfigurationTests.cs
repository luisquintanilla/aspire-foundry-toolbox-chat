using AspireFoundryToolboxChat.Web.Services.Ingestion;

namespace AspireFoundryToolboxChat.Tests;

public sealed class LocalIngestionConfigurationTests
{
    [Fact]
    public void VectorStorePath_UsesLocalApplicationDataForWindowsUncBuilds()
    {
        var path = LocalDataPath.GetVectorStorePath(
            @"\\wsl.localhost\Ubuntu-24.04\home\user\app\bin",
            @"C:\Users\user\AppData\Local",
            isWindows: true);

        Assert.Equal(
            Path.Combine(
                @"C:\Users\user\AppData\Local",
                "AspireFoundryToolboxChat",
                "vector-store.db"),
            path);
    }

    [Fact]
    public void VectorStorePath_RemainsBesideApplicationForLocalBuilds()
    {
        var baseDirectory = Path.Combine("home", "user", "app", "bin");

        var path = LocalDataPath.GetVectorStorePath(
            baseDirectory,
            Path.Combine("home", "user", ".local", "share"),
            isWindows: false);

        Assert.Equal(Path.Combine(baseDirectory, "vector-store.db"), path);
    }

    [Theory]
    [InlineData("http://localhost:3001", "http://localhost:3001/mcp/")]
    [InlineData("http://localhost:3001/", "http://localhost:3001/mcp/")]
    public void MarkItDownUrl_UsesCanonicalMcpPath(string baseUrl, string expected)
    {
        Assert.Equal(
            new Uri(expected),
            DocumentReader.GetMarkItDownMcpServerUrl(baseUrl));
    }

    [Fact]
    public void MarkItDownClient_PinsServerSupportedProtocol()
    {
        var options = DocumentReader.CreateMarkItDownMcpClientOptions();

        Assert.Equal(DocumentReader.MarkItDownProtocolVersion, options.ProtocolVersion);
    }
}
