using System.Diagnostics;
using System.Net.Http.Headers;
using Azure.Core;

namespace FoundryToolbox.Client;

internal sealed class FoundryToolboxAuthenticationHandler(
    TokenCredential credential,
    FoundryToolboxClientOptions options) : DelegatingHandler
{
    internal const string Scope = "https://ai.azure.com/.default";
    internal const string FeatureHeaderName = "Foundry-Features";
    internal const string FeatureHeaderValue = "Toolboxes=V1Preview";

    private static readonly TokenRequestContext TokenContext = new([Scope]);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await credential
            .GetTokenAsync(TokenContext, cancellationToken)
            .ConfigureAwait(false);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);
        MergeFeatureHeader(request, options.AdditionalFeatures);
        PropagateTraceContext(request);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildFeaturesHeaderValue(
        IEnumerable<string>? existingValues,
        string? additionalFeatures)
    {
        var features = new List<string>();
        AddFeatures(features, existingValues);
        AddFeatures(features, [additionalFeatures ?? string.Empty]);
        features.RemoveAll(feature =>
            string.Equals(feature, FeatureHeaderValue, StringComparison.OrdinalIgnoreCase));
        features.Insert(0, FeatureHeaderValue);
        return string.Join(",", features);
    }

    private static void MergeFeatureHeader(
        HttpRequestMessage request,
        string? additionalFeatures)
    {
        request.Headers.TryGetValues(FeatureHeaderName, out var existingValues);
        var headerValue = BuildFeaturesHeaderValue(existingValues, additionalFeatures);
        request.Headers.Remove(FeatureHeaderName);
        request.Headers.TryAddWithoutValidation(FeatureHeaderName, headerValue);
    }

    private static void AddFeatures(List<string> destination, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            foreach (var feature in value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (feature.Any(char.IsControl))
                {
                    throw new ArgumentException(
                        "Foundry feature values cannot contain control characters.",
                        nameof(values));
                }

                if (!destination.Contains(feature, StringComparer.OrdinalIgnoreCase))
                {
                    destination.Add(feature);
                }
            }
        }
    }

    private static void PropagateTraceContext(HttpRequestMessage request)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        AddHeaderIfMissing(request, "traceparent", activity.Id);
        AddHeaderIfMissing(request, "tracestate", activity.TraceStateString);
    }

    private static void AddHeaderIfMissing(
        HttpRequestMessage request,
        string name,
        string? value)
    {
        if (!string.IsNullOrEmpty(value) && !request.Headers.Contains(name))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
