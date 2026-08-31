using System.Text.Json;
using System.Text.Json.Nodes;
using FoundryToolbox.Client;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace FoundryToolbox.Client.Tests;

public sealed class FoundryToolboxPolicyTests
{
    [Theory]
    [InlineData("always", true)]
    [InlineData("never", false)]
    public void RequiresApproval_UsesStructuredMetadata(
        string policy,
        bool expected)
    {
        Assert.Equal(
            expected,
            FoundryToolboxToolMetadata.RequiresApproval(CreateTool(policy)));
    }

    [Fact]
    public void RequiresApproval_FailsClosedWhenMetadataIsAbsent()
    {
        var tool = new Tool
        {
            Name = "tool",
            InputSchema = EmptySchema,
        };

        Assert.True(FoundryToolboxToolMetadata.RequiresApproval(tool));
    }

    [Theory]
    [InlineData("sometimes")]
    [InlineData("")]
    public void RequiresApproval_RejectsUnknownPolicy(string policy)
    {
        Assert.Throws<InvalidOperationException>(() =>
            FoundryToolboxToolMetadata.RequiresApproval(CreateTool(policy)));
    }

    [Fact]
    public void Consent_AcceptsOnlyNestedFoundryContract()
    {
        var exception = new McpProtocolException(
            """
            Request failed: {"error":{"code":"CONSENT_REQUIRED","message":"https://region.consent.azure-apihub.net/login?id=1"}}
            """,
            (McpErrorCode)(-32006));

        Assert.True(FoundryToolboxConsent.TryParse(exception, out var uri));
        Assert.Equal(
            "https://region.consent.azure-apihub.net/login?id=1",
            uri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("Consent required: https://example.test/consent")]
    [InlineData("""{"code":"CONSENT_REQUIRED","message":"https://example.test/consent"}""")]
    [InlineData("""{"error":{"code":"OTHER","message":"https://example.test/consent"}}""")]
    [InlineData("""{"error":{"code":"CONSENT_REQUIRED","message":"http://example.test/consent"}}""")]
    [InlineData("""{"error":{"code":"CONSENT_REQUIRED","message":"https://user:password@example.test/consent"}}""")]
    public void Consent_RejectsNonContractPayloads(string message)
    {
        var exception = new McpProtocolException(
            message,
            (McpErrorCode)(-32006));

        Assert.False(FoundryToolboxConsent.TryParse(exception, out _));
    }

    [Fact]
    public void Consent_RejectsWrongOuterErrorCode()
    {
        var exception = new McpProtocolException(
            """{"error":{"code":"CONSENT_REQUIRED","message":"https://example.test/consent"}}""",
            McpErrorCode.InternalError);

        Assert.False(FoundryToolboxConsent.TryParse(exception, out _));
    }

    private static JsonElement EmptySchema =>
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object"}""");

    private static Tool CreateTool(string policy) =>
        new()
        {
            Name = "tool",
            InputSchema = EmptySchema,
            Meta = new JsonObject
            {
                ["tool_configuration"] = new JsonObject
                {
                    ["require_approval"] = policy,
                },
            },
        };
}
