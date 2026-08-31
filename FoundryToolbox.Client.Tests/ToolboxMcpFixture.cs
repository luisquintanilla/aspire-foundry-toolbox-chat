using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FoundryToolbox.Client.Tests;

internal sealed class ToolboxMcpFixture : HttpMessageHandler
{
    private int _remainingListFailures;

    public ToolboxMcpFixture(int listFailures = 0)
    {
        _remainingListFailures = listFailures;
    }

    public string ApprovalPolicy { get; init; } = "never";

    public bool ReturnConsentOnCall { get; set; }

    public bool ReturnConsentOnList { get; set; }

    public bool FailToolCalls { get; set; }

    public bool PauseToolCalls { get; set; }

    public TaskCompletionSource ToolCallEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseToolCall { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool FailListRequests { get; set; }

    public TaskCompletionSource ListEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource SubsequentListEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseList { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool PauseList { get; set; }

    public bool IgnoreListCancellation { get; init; }

    public bool PauseDelete { get; set; }

    public TaskCompletionSource DeleteEntered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseDelete { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<RecordedMcpRequest> Requests { get; } = [];

    public int Count(string method) =>
        Requests.Count(request =>
            string.Equals(request.Method, method, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Delete)
        {
            DeleteEntered.TrySetResult();
            if (PauseDelete)
            {
                await ReleaseDelete.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (request.Method == HttpMethod.Get)
        {
            return JsonResponse(
                HttpStatusCode.MethodNotAllowed,
                """{"error":"standalone GET is not enabled"}""");
        }

        var body = request.Content is null
            ? string.Empty
            : await request.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var method = root.GetProperty("method").GetString()!;
        var id = root.TryGetProperty("id", out var requestId)
            ? JsonNode.Parse(requestId.GetRawText())
            : null;
        var authorization = request.Headers.Authorization?.Parameter;
        Requests.Enqueue(new RecordedMcpRequest(method, body, authorization));

        if (method == "server/discover")
        {
            return RpcError(id, -32601, "Method not found.");
        }

        if (method == "initialize")
        {
            var response = RpcResult(
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
                        ["name"] = "toolbox-fixture",
                        ["version"] = "1.0.0",
                    },
                });
            response.Headers.TryAddWithoutValidation(
                "Mcp-Session-Id",
                $"fixture-{Count("initialize")}");
            return response;
        }

        if (method == "notifications/initialized")
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (method == "tools/list")
        {
            if (Count("tools/list") == 1)
            {
                ListEntered.TrySetResult();
            }
            else
            {
                SubsequentListEntered.TrySetResult();
            }

            if (PauseList)
            {
                if (IgnoreListCancellation)
                {
                    await ReleaseList.Task.ConfigureAwait(false);
                }
                else
                {
                    await ReleaseList.Task
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (FailListRequests ||
                Interlocked.Decrement(ref _remainingListFailures) >= 0)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (ReturnConsentOnList)
            {
                return RpcError(
                    id,
                    -32006,
                    """
                    {"error":{"code":"CONSENT_REQUIRED","message":"https://region.consent.azure-apihub.net/login?id=1"}}
                    """);
            }

            return RpcResult(
                id,
                new JsonObject
                {
                    ["tools"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "echo",
                            ["description"] = "Echoes a message.",
                            ["inputSchema"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["message"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                    },
                                },
                            },
                            ["_meta"] = new JsonObject
                            {
                                ["tool_configuration"] = new JsonObject
                                {
                                    ["require_approval"] = ApprovalPolicy,
                                },
                            },
                        },
                    },
                });
        }

        if (method == "tools/call")
        {
            ToolCallEntered.TrySetResult();
            if (PauseToolCalls)
            {
                await ReleaseToolCall.Task
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (FailToolCalls)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            if (ReturnConsentOnCall)
            {
                return RpcError(
                    id,
                    -32006,
                    """
                    {"error":{"code":"CONSENT_REQUIRED","message":"https://region.consent.azure-apihub.net/login?id=1"}}
                    """);
            }

            return RpcResult(
                id,
                new JsonObject
                {
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = "echoed",
                        },
                    },
                    ["isError"] = false,
                });
        }

        return RpcError(id, -32601, $"Unknown method '{method}'.");
    }

    private static HttpResponseMessage RpcResult(JsonNode? id, JsonNode result) =>
        JsonResponse(
            HttpStatusCode.OK,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result,
            }.ToJsonString());

    private static HttpResponseMessage RpcError(
        JsonNode? id,
        int code,
        string message) =>
        JsonResponse(
            HttpStatusCode.OK,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            }.ToJsonString());

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json) =>
        new(statusCode)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };
}

internal sealed record RecordedMcpRequest(
    string Method,
    string Body,
    string? Authorization);
