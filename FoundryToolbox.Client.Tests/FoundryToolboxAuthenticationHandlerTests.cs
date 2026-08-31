using System.Diagnostics;
using System.Net;
using Azure.Core;
using FoundryToolbox.Client;

namespace FoundryToolbox.Client.Tests;

public sealed class FoundryToolboxAuthenticationHandlerTests
{
    [Fact]
    public async Task SendAsync_AddsOnlyTokenFeaturesAndTraceContext()
    {
        var credential = new TestTokenCredential("test-token");
        var terminal = new RecordingHandler(HttpStatusCode.OK);
        using var handler = new FoundryToolboxAuthenticationHandler(
            credential,
            CreateOptions("AnotherFeature=Preview"))
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(handler);
        using var activity = new Activity("toolbox-test").Start();
        activity.AddBaggage("secret", "must-not-propagate");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://example.test/mcp");
        request.Headers.TryAddWithoutValidation(
            FoundryToolboxAuthenticationHandler.FeatureHeaderName,
            "ExistingFeature=V1,toolboxes=v1preview");
        request.Content = new StringContent("{}");

        using var response = await client.SendAsync(request);

        Assert.Equal(
            [FoundryToolboxAuthenticationHandler.Scope],
            credential.LastScopes);
        var sent = Assert.Single(terminal.Requests);
        Assert.Equal("Bearer", sent.AuthorizationScheme);
        Assert.Equal("test-token", sent.AuthorizationParameter);
        Assert.Equal(
            "Toolboxes=V1Preview,ExistingFeature=V1,AnotherFeature=Preview",
            sent.Features);
        Assert.Equal(activity.Id, sent.TraceParent);
        Assert.Null(sent.Baggage);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryFailedRequests()
    {
        var terminal = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var handler = new FoundryToolboxAuthenticationHandler(
            new TestTokenCredential("test-token"),
            CreateOptions())
        {
            InnerHandler = terminal,
        };
        using var client = new HttpClient(handler);

        using var response = await client.PostAsync(
            "https://example.test/mcp",
            new StringContent("""{"method":"tools/call"}"""));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(terminal.Requests);
    }

    [Fact]
    public void FeatureHeader_RejectsControlCharacters()
    {
        Assert.Throws<ArgumentException>(() =>
            FoundryToolboxAuthenticationHandler.BuildFeaturesHeaderValue(
                existingValues: null,
                "Other=Preview\r\nInjected=true"));
    }

    private static FoundryToolboxClientOptions CreateOptions(
        string? additionalFeatures = null) =>
        new()
        {
            Endpoint = new Uri(
                "https://example.test/project/toolboxes/toolbox/mcp?api-version=v1"),
            AdditionalFeatures = additionalFeatures,
        };

    private sealed class RecordingHandler(HttpStatusCode statusCode) :
        HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                string.Join(
                    ",",
                    request.Headers.GetValues(
                        FoundryToolboxAuthenticationHandler.FeatureHeaderName)),
                request.Headers.TryGetValues("traceparent", out var traceParent)
                    ? Assert.Single(traceParent)
                    : null,
                request.Headers.TryGetValues("baggage", out var baggage)
                    ? string.Join(",", baggage)
                    : null));

            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed record RecordedRequest(
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string Features,
        string? TraceParent,
        string? Baggage);
}
