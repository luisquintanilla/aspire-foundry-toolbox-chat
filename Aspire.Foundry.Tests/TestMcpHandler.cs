using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aspire.Foundry.Tests;

internal sealed class TestMcpHandler : HttpMessageHandler
{
    public ConcurrentQueue<string?> BearerTokens { get; } = [];

    public ConcurrentQueue<Uri?> RequestUris { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Delete)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (request.Method == HttpMethod.Get)
        {
            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        }

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var method = root.GetProperty("method").GetString();
        var id = root.TryGetProperty("id", out var requestId)
            ? JsonNode.Parse(requestId.GetRawText())
            : null;
        BearerTokens.Enqueue(request.Headers.Authorization?.Parameter);
        RequestUris.Enqueue(request.RequestUri);

        return method switch
        {
            "server/discover" => Error(id, -32601, "Method not found."),
            "initialize" => Result(
                id,
                new JsonObject
                {
                    ["protocolVersion"] = "2025-11-25",
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject(),
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "test",
                        ["version"] = "1",
                    },
                }),
            "notifications/initialized" =>
                new HttpResponseMessage(HttpStatusCode.Accepted),
            "tools/list" => Result(
                id,
                new JsonObject
                {
                    ["tools"] = new JsonArray(),
                }),
            _ => Error(id, -32601, "Method not found."),
        };
    }

    private static HttpResponseMessage Result(JsonNode? id, JsonNode result) =>
        Json(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result,
            });

    private static HttpResponseMessage Error(
        JsonNode? id,
        int code,
        string message) =>
        Json(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            });

    private static HttpResponseMessage Json(JsonNode body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                body.ToJsonString(),
                Encoding.UTF8,
                "application/json"),
        };
}
