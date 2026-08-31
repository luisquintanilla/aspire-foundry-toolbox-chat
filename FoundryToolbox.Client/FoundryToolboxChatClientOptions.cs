namespace FoundryToolbox.Client;

/// <summary>
/// Configures how Foundry Toolbox functions are attached to chat requests.
/// </summary>
public sealed class FoundryToolboxChatClientOptions
{
    /// <summary>
    /// Gets or sets whether every Toolbox function requires MEAI approval,
    /// including functions that the Toolbox service marks as optional.
    /// </summary>
    public bool RequireApprovalForAllTools { get; set; }
}
