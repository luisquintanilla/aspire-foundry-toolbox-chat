namespace FoundryToolbox.Client;

/// <summary>
/// Represents a failure to discover functions for a chat request.
/// </summary>
public sealed class FoundryToolboxDiscoveryException
    : InvalidOperationException
{
    public FoundryToolboxDiscoveryException(Exception innerException)
        : base(
            "Foundry Toolbox functions could not be discovered.",
            innerException ??
                throw new ArgumentNullException(nameof(innerException)))
    {
    }
}

/// <summary>
/// Represents conflicting function names in a chat request.
/// </summary>
public sealed class FoundryToolboxToolConflictException
    : InvalidOperationException
{
    public FoundryToolboxToolConflictException(
        IReadOnlyList<string> duplicateToolNames)
        : this(Normalize(duplicateToolNames))
    {
    }

    private FoundryToolboxToolConflictException(
        string[] duplicateToolNames)
        : base(CreateMessage(duplicateToolNames))
    {
        DuplicateToolNames = Array.AsReadOnly(
            duplicateToolNames);
    }

    /// <summary>
    /// Gets the duplicate names in ordinal sort order.
    /// </summary>
    public IReadOnlyList<string> DuplicateToolNames { get; }

    private static string[] Normalize(
        IReadOnlyList<string> duplicateToolNames)
    {
        ArgumentNullException.ThrowIfNull(duplicateToolNames);
        if (duplicateToolNames.Count == 0)
        {
            throw new ArgumentException(
                "At least one duplicate tool name is required.",
                nameof(duplicateToolNames));
        }

        return duplicateToolNames
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateMessage(
        IReadOnlyList<string> duplicateToolNames) =>
        $"Duplicate chat tool name(s): {string.Join(", ", duplicateToolNames)}. " +
            "Tool names must be unique across application and Foundry Toolbox tools.";
}
