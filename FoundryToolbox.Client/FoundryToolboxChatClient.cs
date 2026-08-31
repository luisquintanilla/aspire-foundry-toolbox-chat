using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace FoundryToolbox.Client;

/// <summary>
/// Adds functions discovered from a Foundry Toolbox to each chat request.
/// </summary>
public sealed class FoundryToolboxChatClient : DelegatingChatClient
{
    private readonly bool _requireApprovalForAllTools;

    public FoundryToolboxChatClient(
        IChatClient innerClient,
        FoundryToolboxClient toolboxClient)
        : this(innerClient, toolboxClient, new FoundryToolboxChatClientOptions())
    {
    }

    public FoundryToolboxChatClient(
        IChatClient innerClient,
        FoundryToolboxClient toolboxClient,
        FoundryToolboxChatClientOptions options)
        : base(innerClient)
    {
        ToolboxClient = toolboxClient ??
            throw new ArgumentNullException(nameof(toolboxClient));
        ArgumentNullException.ThrowIfNull(options);
        _requireApprovalForAllTools = options.RequireApprovalForAllTools;
    }

    /// <summary>
    /// Gets the Toolbox runtime used to discover request functions.
    /// </summary>
    public FoundryToolboxClient ToolboxClient { get; }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var augmentedOptions = await AugmentOptionsAsync(
            options,
            cancellationToken).ConfigureAwait(false);

        return await base.GetResponseAsync(
            messages,
            augmentedOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate>
        GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var augmentedOptions = await AugmentOptionsAsync(
            options,
            cancellationToken).ConfigureAwait(false);

        await foreach (var update in base.GetStreamingResponseAsync(
            messages,
            augmentedOptions,
            cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private async Task<ChatOptions?> AugmentOptionsAsync(
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AIFunction> toolboxTools;
        try
        {
            toolboxTools = await ToolboxClient.GetToolsAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FoundryToolboxConsentRequiredException ex)
        {
            throw new FoundryToolboxConsentRequiredException(
                ex.ConsentUri,
                ex,
                occurredDuringDiscovery: true);
        }
        catch (Exception ex) when (
            FoundryToolboxConsent.TryParse(ex, out var consentUri) &&
            consentUri is not null)
        {
            throw new FoundryToolboxConsentRequiredException(
                consentUri,
                ex,
                occurredDuringDiscovery: true);
        }
        catch (Exception ex)
        {
            throw new FoundryToolboxDiscoveryException(ex);
        }

        IReadOnlyList<AIFunction> requestToolboxTools =
            _requireApprovalForAllTools
                ? toolboxTools
                    .Select(RequireApproval)
                    .ToArray()
                : toolboxTools;
        var combinedTools = new List<AITool>(
            (options?.Tools?.Count ?? 0) + requestToolboxTools.Count);

        if (options?.Tools is not null)
        {
            combinedTools.AddRange(options.Tools);
        }

        combinedTools.AddRange(requestToolboxTools);
        ThrowIfDuplicateNames(combinedTools);

        if (toolboxTools.Count == 0)
        {
            return options;
        }

        var augmentedOptions = options?.Clone() ?? new ChatOptions();
        augmentedOptions.Tools = combinedTools;
        return augmentedOptions;
    }

    private static AIFunction RequireApproval(AIFunction function) =>
        function is ApprovalRequiredAIFunction
            ? function
            : new ApprovalRequiredAIFunction(function);

    private static void ThrowIfDuplicateNames(IReadOnlyList<AITool> tools)
    {
        var duplicateNames = tools
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (duplicateNames.Length > 0)
        {
            throw new FoundryToolboxToolConflictException(duplicateNames);
        }
    }
}
