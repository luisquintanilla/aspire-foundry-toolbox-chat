using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.Projects.Agents;
using OpenAI.Responses;

namespace Aspire.Hosting.Foundry.Toolbox;

public sealed record FoundryToolboxDefinition(
    string Name,
    string Description,
    IReadOnlyList<FoundryToolboxToolDefinition> Tools)
{
    public const string ManagedByKey = "managed-by";
    public const string ManagedByValue = "Aspire.Hosting.Foundry";
    public const string ConfigurationHashKey = "configuration-sha256";
    public const string SchemaVersionKey = "schema-version";

    public string ComputeConfigurationHash()
    {
        Validate();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("description", Description);
            writer.WritePropertyName("tools");
            writer.WriteStartArray();

            foreach (var tool in Tools.OrderBy(tool => tool.StableIdentity, StringComparer.Ordinal))
            {
                tool.WriteCanonicalJson(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public IReadOnlyDictionary<string, string> CreateMetadata(string configurationHash) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ManagedByKey] = ManagedByValue,
            [ConfigurationHashKey] = configurationHash,
            [SchemaVersionKey] = "1",
        };

    public IReadOnlyList<ToolboxTool> CreateSdkTools() =>
        GetValidatedTools()
            .Select(tool => tool.CreateSdkTool())
            .ToArray();

    private IReadOnlyList<FoundryToolboxToolDefinition> GetValidatedTools()
    {
        Validate();
        return Tools
            .OrderBy(tool => tool.StableIdentity, StringComparer.Ordinal)
            .ToArray();
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) ||
            !Regex.IsMatch(
                Name,
                @"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "The Toolbox name must be one safe URI path segment containing only letters, digits, '.', '_' or '-'.");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            throw new InvalidOperationException("The Toolbox description is required.");
        }

        var duplicateNames = Tools
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            throw new InvalidOperationException(
                $"Toolbox tool names must be unique. Duplicates: {string.Join(", ", duplicateNames)}.");
        }
    }
}

public abstract record FoundryToolboxToolDefinition(
    string Type,
    string Name,
    string Description)
{
    public string StableIdentity => $"{Type}:{Name}";

    public abstract ToolboxTool CreateSdkTool();

    public abstract void WriteCanonicalJson(Utf8JsonWriter writer);

    protected void WriteCommon(Utf8JsonWriter writer)
    {
        writer.WriteString("type", Type);
        writer.WriteString("name", Name);
        writer.WriteString("description", Description);
    }
}

public sealed record FoundryWebSearchToolDefinition(
    string Name,
    string Description)
    : FoundryToolboxToolDefinition("web_search", Name, Description)
{
    public override ToolboxTool CreateSdkTool() =>
        new WebSearchToolboxTool
        {
            Name = Name,
            Description = Description,
        };

    public override void WriteCanonicalJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        WriteCommon(writer);
        writer.WriteEndObject();
    }
}

public sealed record FoundryMcpToolDefinition(
    string Name,
    string Description,
    string ServerLabel,
    Uri ServerUri,
    bool AlwaysRequireApproval)
    : FoundryToolboxToolDefinition("mcp", Name, Description)
{
    public override ToolboxTool CreateSdkTool() =>
        new MCPToolboxTool(ServerLabel)
        {
            Name = Name,
            Description = Description,
            ServerUri = ServerUri,
            ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(
                AlwaysRequireApproval
                    ? GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval
                    : GlobalMcpToolCallApprovalPolicy.NeverRequireApproval),
        };

    public override void WriteCanonicalJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        WriteCommon(writer);
        writer.WriteString("serverLabel", ServerLabel);
        writer.WriteString("serverUri", ServerUri.AbsoluteUri);
        writer.WriteBoolean("alwaysRequireApproval", AlwaysRequireApproval);
        writer.WriteEndObject();
    }
}
