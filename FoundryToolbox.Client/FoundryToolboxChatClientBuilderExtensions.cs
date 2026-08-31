using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FoundryToolbox.Client;

/// <summary>
/// Adds Foundry Toolbox function discovery to MEAI chat pipelines.
/// </summary>
public static class FoundryToolboxChatClientBuilderExtensions
{
    /// <summary>
    /// Adds functions from an explicit or unkeyed Toolbox client to each request.
    /// </summary>
    public static ChatClientBuilder UseFoundryToolbox(
        this ChatClientBuilder builder,
        FoundryToolboxClient? toolboxClient = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerClient, services) =>
            new FoundryToolboxChatClient(
                innerClient,
                toolboxClient ??
                    services.GetRequiredService<FoundryToolboxClient>()));
    }

    /// <summary>
    /// Adds functions from an unkeyed Toolbox client and applies request policy.
    /// </summary>
    public static ChatClientBuilder UseFoundryToolbox(
        this ChatClientBuilder builder,
        Action<FoundryToolboxChatClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FoundryToolboxChatClientOptions();
        configure(options);

        return builder.Use((innerClient, services) =>
            new FoundryToolboxChatClient(
                innerClient,
                services.GetRequiredService<FoundryToolboxClient>(),
                options));
    }

    /// <summary>
    /// Adds functions from a keyed Toolbox client to each request.
    /// </summary>
    public static ChatClientBuilder UseFoundryToolbox(
        this ChatClientBuilder builder,
        object serviceKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceKey);

        return builder.Use((innerClient, services) =>
            new FoundryToolboxChatClient(
                innerClient,
                services.GetRequiredKeyedService<FoundryToolboxClient>(
                    serviceKey)));
    }

    /// <summary>
    /// Adds functions from a keyed Toolbox client and applies request policy.
    /// </summary>
    public static ChatClientBuilder UseFoundryToolbox(
        this ChatClientBuilder builder,
        object serviceKey,
        Action<FoundryToolboxChatClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FoundryToolboxChatClientOptions();
        configure(options);

        return builder.Use((innerClient, services) =>
            new FoundryToolboxChatClient(
                innerClient,
                services.GetRequiredKeyedService<FoundryToolboxClient>(
                    serviceKey),
                options));
    }
}
