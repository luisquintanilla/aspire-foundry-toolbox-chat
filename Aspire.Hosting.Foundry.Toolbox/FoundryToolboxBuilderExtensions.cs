using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;
using Aspire.Hosting.Foundry.Toolbox;

namespace Aspire.Hosting;

public static class FoundryToolboxBuilderExtensions
{
    public static IResourceBuilder<FoundryToolboxResource> AddToolbox(
        this IResourceBuilder<AzureCognitiveServicesProjectResource> project,
        [ResourceName] string name,
        Action<FoundryToolboxResource>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new FoundryToolboxResource(name, project.Resource);
        configure?.Invoke(resource);

        return project.ApplicationBuilder
            .AddResource(resource)
            .WithIconName("Toolbox")
            .WithUrl("https://ai.azure.com", "Foundry portal");
    }

    public static IResourceBuilder<FoundryToolboxResource> RunAsExisting(
        this IResourceBuilder<FoundryToolboxResource> toolbox)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        toolbox.Resource.ExistingInRunMode = true;
        return toolbox;
    }

    public static IResourceBuilder<FoundryToolboxResource> PublishAsExisting(
        this IResourceBuilder<FoundryToolboxResource> toolbox)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        toolbox.Resource.ExistingInPublishMode = true;
        return toolbox;
    }

    public static IResourceBuilder<FoundryToolboxResource> AsExisting(
        this IResourceBuilder<FoundryToolboxResource> toolbox)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        toolbox.Resource.ExistingInRunMode = true;
        toolbox.Resource.ExistingInPublishMode = true;
        return toolbox;
    }

    public static IResourceBuilder<FoundryToolboxResource> WithDescription(
        this IResourceBuilder<FoundryToolboxResource> toolbox,
        string description)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        toolbox.Resource.Description = description;
        return toolbox;
    }

    public static IResourceBuilder<FoundryToolboxResource> WithWebSearchTool(
        this IResourceBuilder<FoundryToolboxResource> toolbox,
        string name = "web-search",
        string description = "Search the public web.")
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        toolbox.Resource.AddTool(
            new FoundryWebSearchToolDefinition(name, description));
        return toolbox;
    }

    public static IResourceBuilder<FoundryToolboxResource> WithMcpTool(
        this IResourceBuilder<FoundryToolboxResource> toolbox,
        string name,
        Uri serverUri,
        string description,
        string? serverLabel = null,
        ToolboxApprovalMode approval = ToolboxApprovalMode.Never)
    {
        ArgumentNullException.ThrowIfNull(toolbox);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(serverUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!serverUri.IsAbsoluteUri || serverUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "The MCP server URI must be an absolute HTTPS URI.",
                nameof(serverUri));
        }

        toolbox.Resource.AddTool(new FoundryMcpToolDefinition(
            name,
            description,
            serverLabel ?? name,
            serverUri,
            approval == ToolboxApprovalMode.Always));
        return toolbox;
    }
}
