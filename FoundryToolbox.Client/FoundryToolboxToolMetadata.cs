using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FoundryToolbox.Client;

public static class FoundryToolboxToolMetadata
{
    public static bool RequiresApproval(McpClientTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return RequiresApproval(tool.ProtocolTool);
    }

    public static bool RequiresApproval(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var approvalNode = tool.Meta?["tool_configuration"]?["require_approval"];
        if (approvalNode is null)
        {
            return true;
        }

        if (approvalNode is not JsonValue approvalValue ||
            !approvalValue.TryGetValue<string>(out var approval))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' returned an invalid Foundry approval policy.");
        }

        return approval.ToLowerInvariant() switch
        {
            "always" => true,
            "never" => false,
            _ => throw new InvalidOperationException(
                $"Tool '{tool.Name}' returned unsupported Foundry approval policy '{approval}'."),
        };
    }
}
