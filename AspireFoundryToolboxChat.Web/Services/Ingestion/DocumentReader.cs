using Microsoft.Extensions.DataIngestion;
using ModelContextProtocol.Client;

namespace AspireFoundryToolboxChat.Web.Services.Ingestion;

internal sealed class DocumentReader(DirectoryInfo rootDirectory) : IngestionDocumentReader
{
    internal const string MarkItDownProtocolVersion = "2024-11-05";

    private readonly MarkdownReader _markdownReader = new();
    private readonly MarkItDownMcpReader _pdfReader = new(
        GetMarkItDownMcpServerUrl(Environment.GetEnvironmentVariable("MARKITDOWN_MCP_URL")),
        CreateMarkItDownMcpClientOptions());

    public override Task<IngestionDocument> ReadAsync(FileInfo source, string identifier, string? mediaType = null, CancellationToken cancellationToken = default)
    {
        if (Path.IsPathFullyQualified(identifier))
        {
            // Normalize the identifier to its relative path
            identifier = Path.GetRelativePath(rootDirectory.FullName, identifier);
        }

        mediaType = GetCustomMediaType(source) ?? mediaType;
        return base.ReadAsync(source, identifier, mediaType, cancellationToken);
    }

    public override Task<IngestionDocument> ReadAsync(Stream source, string identifier, string mediaType, CancellationToken cancellationToken = default)
        => mediaType switch
        {
            "application/pdf" => _pdfReader.ReadAsync(source, identifier, mediaType, cancellationToken),
            "text/markdown" => _markdownReader.ReadAsync(source, identifier, mediaType, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported media type '{mediaType}'"),
        };

    private static string? GetCustomMediaType(FileInfo source)
        => source.Extension switch
        {
            ".md" => "text/markdown",
            _ => null
        };

    internal static Uri GetMarkItDownMcpServerUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "MARKITDOWN_MCP_URL must be an absolute HTTP or HTTPS URI.");
        }

        return new Uri($"{uri.AbsoluteUri.TrimEnd('/')}/mcp/");
    }

    internal static McpClientOptions CreateMarkItDownMcpClientOptions() =>
        new() { ProtocolVersion = MarkItDownProtocolVersion };
}
