using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;
using Aspire.Hosting.Foundry.Toolbox;

if (OperatingSystem.IsWindows() &&
    AppContext.BaseDirectory.StartsWith(@"\\", StringComparison.Ordinal))
{
    // Windows FileSystemWatcher can continuously signal changes for a WSL UNC checkout.
    Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "1");
}

var builder = DistributedApplication.CreateBuilder(args);

var embeddingEndpoint = builder.AddParameter("embedding-endpoint");
var embeddingDeploymentName = builder.AddParameter("embedding-deployment-name");

var markitdown = builder.AddContainer(
        "markitdown",
        "mcp/markitdown@sha256:9cb5f26d30b609a1d5560a090371cb9dba84fc32e7df10e5496aa22aa7b52658")
    .WithArgs("--http", "--host", "0.0.0.0", "--port", "3001")
    .WithHttpEndpoint(targetPort: 3001, name: "http");

var web = builder
    .AddProject<Projects.AspireFoundryToolboxChat_Web>("web")
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_ENDPOINT", embeddingEndpoint)
    .WithEnvironment("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME", embeddingDeploymentName)
    .WithEnvironment("MARKITDOWN_MCP_URL", markitdown.GetEndpoint("http"))
    .WaitFor(markitdown)
    .WithExternalHttpEndpoints();

const string toolboxDescription =
    "Hybrid Toolbox for current web information and Microsoft Learn documentation.";
var toolboxMode = builder.Configuration["Foundry:Toolbox:Mode"] ?? "existing";

switch (toolboxMode.ToLowerInvariant())
{
    case "existing":
    {
        var chatDeploymentName = builder.AddParameter("chat-deployment-name");
        var foundryProjectEndpoint = builder.AddParameter("foundry-project-endpoint");
        var toolboxName = builder.AddParameter("toolbox-name");
        var foundryProject = builder.AddConnectionString(
            "foundry-project",
            ReferenceExpression.Create($"{foundryProjectEndpoint}"));
        var toolbox = builder.AddConnectionString(
            "research-toolbox",
            ReferenceExpression.Create(
                $"ProjectEndpoint={foundryProjectEndpoint};ToolboxName={toolboxName}"));

        web.WithReference(foundryProject)
            .WithReference(toolbox)
            .WithEnvironment("CHAT_MODEL_DEPLOYMENT", chatDeploymentName);
        break;
    }
    case "managed":
    {
        if (!bool.TryParse(
                builder.Configuration[
                    "Foundry:Toolbox:AllowResourceChanges"],
                out var allowResourceChanges) ||
            !allowResourceChanges)
        {
            throw new InvalidOperationException(
                "Managed mode can create or change Azure resources. Set " +
                "'Foundry:Toolbox:AllowResourceChanges' to true to acknowledge that behavior.");
        }

        var foundryProject = builder.AddFoundry("foundry")
            .AddProject("foundry-project");
        var chatDeployment = foundryProject.AddModelDeployment(
            "chat",
            FoundryModel.OpenAI.Gpt41Mini);
        var toolbox = foundryProject
            .AddToolbox("research-toolbox")
            .WithDescription(toolboxDescription)
            .WithWebSearchTool(
                "web-search",
                "Searches the public web for current information and returns cited results.")
            .WithMcpTool(
                "microsoft-learn",
                new Uri("https://learn.microsoft.com/api/mcp"),
                "Searches official Microsoft Learn documentation through its public MCP server.",
                approval: ToolboxApprovalMode.Always);

        web.WithReference(foundryProject)
            .WithReference(toolbox)
            .WithEnvironment(
                "CHAT_MODEL_DEPLOYMENT",
                chatDeployment.Resource.DeploymentName)
            .WaitFor(chatDeployment);
        break;
    }
    default:
        throw new InvalidOperationException(
            $"Unsupported Foundry Toolbox mode '{toolboxMode}'. Use 'existing' or 'managed'.");
}

builder.Build().Run();
