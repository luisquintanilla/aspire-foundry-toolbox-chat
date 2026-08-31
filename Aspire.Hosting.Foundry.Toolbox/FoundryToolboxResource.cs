using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ClientModel.Primitives;

namespace Aspire.Hosting.Foundry.Toolbox;

public sealed class FoundryToolboxResource
    : Resource,
      IResourceWithParent<AzureCognitiveServicesProjectResource>,
      IResourceWithConnectionString,
      IResourceWithWaitSupport
{
    internal const string DefaultApiVersion = "v1";
    internal const string PreviewFeatureHeaderValue = "Toolboxes=V1Preview";
    internal const string AuthorizationScopeValue = "https://ai.azure.com/.default";

    private const string BeforeStartStepName = "before-start";
    private const string RunModeAzureProvisionStepName = "run-mode-azure-provision";
    private readonly List<FoundryToolboxToolDefinition> _tools = [];

    public FoundryToolboxResource(
        [ResourceName] string name,
        AzureCognitiveServicesProjectResource parent)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Parent = parent;

        Annotations.Add(new PipelineStepAnnotation(context =>
            Task.FromResult(CreatePipelineSteps(context))));
    }

    public AzureCognitiveServicesProjectResource Parent { get; }

    public string Description { get; set; } =
        "Microsoft Foundry Toolbox managed by Aspire.";

    public IReadOnlyList<FoundryToolboxToolDefinition> Tools => _tools;

    public string ApiVersion { get; set; } = DefaultApiVersion;

    public string? Version { get; set; }

    public string? DeployedVersion { get; private set; }

    public string? ConfigurationHash { get; private set; }

    public ReferenceExpression UriExpression => Version is { Length: > 0 } version
        ? ReferenceExpression.Create(
            $"{Parent.Endpoint}/toolboxes/{Name}/versions/{version}/mcp?api-version={ApiVersion}")
        : ReferenceExpression.Create(
            $"{Parent.Endpoint}/toolboxes/{Name}/mcp?api-version={ApiVersion}");

    public ReferenceExpression ConnectionStringExpression => UriExpression;

    internal bool ExistingInRunMode { get; set; }

    internal bool ExistingInPublishMode { get; set; }

    internal void AddTool(FoundryToolboxToolDefinition tool) => _tools.Add(tool);

    internal FoundryToolboxDefinition CreateDefinition() =>
        new(Name, Description, [.. _tools]);

    IEnumerable<KeyValuePair<string, ReferenceExpression>>
        IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Uri", UriExpression);
        yield return new("ProjectEndpoint", ReferenceExpression.Create($"{Parent.Endpoint}"));
        yield return new("Name", ReferenceExpression.Create($"{Name}"));
        yield return new("ApiVersion", ReferenceExpression.Create($"{ApiVersion}"));
        yield return new(
            "AuthorizationScope",
            ReferenceExpression.Create($"{AuthorizationScopeValue}"));
        yield return new(
            "FoundryFeatures",
            ReferenceExpression.Create($"{PreviewFeatureHeaderValue}"));

        if (Version is { Length: > 0 } version)
        {
            yield return new("Version", ReferenceExpression.Create($"{version}"));
        }
    }

    private IEnumerable<PipelineStep> CreatePipelineSteps(
        PipelineStepFactoryContext context)
    {
        if (context.PipelineContext.ExecutionContext.IsRunMode)
        {
            if (!ExistingInRunMode)
            {
                yield return new PipelineStep
                {
                    Name = $"deploy-{Name}-before-start",
                    Description = $"Reconciles Toolbox {Name} before the application starts.",
                    Action = ReconcileAsync,
                    RequiredBySteps = [BeforeStartStepName],
                    DependsOnSteps = [RunModeAzureProvisionStepName],
                    Resource = this,
                };
            }

            yield break;
        }

        if (!ExistingInPublishMode)
        {
            yield return new PipelineStep
            {
                Name = $"deploy-{Name}",
                Description = $"Reconciles Toolbox {Name}.",
                Action = ReconcileAsync,
                Tags = [WellKnownPipelineTags.DeployCompute],
                RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                DependsOnSteps =
                [
                    WellKnownPipelineSteps.DeployPrereq,
                    AzureEnvironmentResource.ProvisionInfrastructureStepName,
                ],
                Resource = this,
            };
        }
    }

    private async Task ReconcileAsync(PipelineStepContext context)
    {
        var notifications = context.Services.GetService<ResourceNotificationService>();
        if (notifications is not null)
        {
            await notifications.PublishUpdateAsync(this, snapshot => snapshot with
            {
                State = new ResourceStateSnapshot(
                    "Reconciling",
                    KnownResourceStateStyles.Info),
            });
        }

        try
        {
            var endpointValue = await Parent.Endpoint.GetValueAsync(context.CancellationToken);
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"Foundry project '{Parent.Name}' did not resolve to an absolute HTTPS endpoint.");
            }

            endpoint = new Uri(endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/'));

            var administration = context.Services.GetService<IToolboxAdministration>();
            if (administration is null)
            {
                var credential = context.Services
                    .GetRequiredService<ITokenCredentialProvider>()
                    .TokenCredential;
                var options = new AgentAdministrationClientOptions();
                options.AddPolicy(new FoundryFeaturesPolicy(), PipelinePosition.PerCall);
                var client = new AgentAdministrationClient(endpoint, credential, options);
                administration = new AzureToolboxAdministration(
                    client.GetAgentToolboxes(),
                    context.Logger);
            }

            var result = await new ToolboxReconciler(administration)
                .ReconcileAsync(CreateDefinition(), context.CancellationToken);

            DeployedVersion = result.Version;
            ConfigurationHash = result.ConfigurationHash;

            context.Logger.LogInformation(
                "Toolbox {ToolboxName} version {Version}: {Action}.",
                Name,
                result.Version,
                result.Action);

            if (notifications is not null)
            {
                await notifications.PublishUpdateAsync(this, snapshot => snapshot with
                {
                    State = KnownResourceStates.Running,
                    Properties =
                    [
                        new(CustomResourceKnownProperties.Source, "Aspire.Hosting.Foundry"),
                        new("Toolbox name", Name),
                        new("Foundry project", Parent.Name),
                        new("Default version", result.Version),
                        new("Configuration SHA-256", result.ConfigurationHash),
                        new("Action", result.Action.ToString()),
                    ],
                });
            }
        }
        catch
        {
            if (notifications is not null)
            {
                await notifications.PublishUpdateAsync(this, snapshot => snapshot with
                {
                    State = new ResourceStateSnapshot(
                        KnownResourceStates.FailedToStart,
                        KnownResourceStateStyles.Error),
                });
            }

            throw;
        }
    }
}
