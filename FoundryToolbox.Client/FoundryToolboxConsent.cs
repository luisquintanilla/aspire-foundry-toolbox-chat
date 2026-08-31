using System.Text.Json;
using ModelContextProtocol;

namespace FoundryToolbox.Client;

public static class FoundryToolboxConsent
{
    private const int ConsentRequiredErrorCode = -32006;
    private const string ConsentRequiredCode = "CONSENT_REQUIRED";

    public static bool TryParse(Exception exception, out Uri? consentUri)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is McpProtocolException protocolException &&
                (int)protocolException.ErrorCode == ConsentRequiredErrorCode &&
                TryParsePayload(protocolException.Message, out consentUri))
            {
                return true;
            }
        }

        consentUri = null;
        return false;
    }

    private static bool TryParsePayload(string message, out Uri? consentUri)
    {
        var objectStart = message.IndexOf('{', StringComparison.Ordinal);
        var objectEnd = message.LastIndexOf('}');
        if (objectStart < 0 || objectEnd <= objectStart)
        {
            consentUri = null;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(
                message.Substring(objectStart, objectEnd - objectStart + 1));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object ||
                !error.TryGetProperty("code", out var code) ||
                code.ValueKind != JsonValueKind.String ||
                !string.Equals(
                    code.GetString(),
                    ConsentRequiredCode,
                    StringComparison.Ordinal) ||
                !error.TryGetProperty("message", out var consent) ||
                consent.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(consent.GetString(), UriKind.Absolute, out var candidate) ||
                candidate.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(candidate.UserInfo))
            {
                consentUri = null;
                return false;
            }

            consentUri = candidate;
            return true;
        }
        catch (JsonException)
        {
            consentUri = null;
            return false;
        }
    }
}

public sealed class FoundryToolboxConsentRequiredException
    : InvalidOperationException
{
    public FoundryToolboxConsentRequiredException(
        Uri consentUri,
        Exception innerException)
        : this(
            consentUri,
            innerException,
            occurredDuringDiscovery: false)
    {
    }

    internal FoundryToolboxConsentRequiredException(
        Uri consentUri,
        Exception innerException,
        bool occurredDuringDiscovery)
        : base(
            "OAuth consent is required for a Foundry Toolbox source.",
            innerException ??
                throw new ArgumentNullException(nameof(innerException)))
    {
        ConsentUri = consentUri ??
            throw new ArgumentNullException(nameof(consentUri));
        OccurredDuringDiscovery = occurredDuringDiscovery;
    }

    /// <summary>
    /// Gets the HTTPS URI where the user can grant consent.
    /// </summary>
    public Uri ConsentUri { get; }

    /// <summary>
    /// Gets whether consent interrupted function discovery rather than invocation.
    /// </summary>
    public bool OccurredDuringDiscovery { get; }
}
