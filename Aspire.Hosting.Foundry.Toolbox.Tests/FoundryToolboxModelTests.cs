using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;
using Aspire.Hosting.Foundry.Toolbox;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aspire.Hosting.Foundry.Toolbox.Tests;

public sealed class FoundryToolboxModelTests
{
    public static TheoryData<string, DistributedApplicationOperation, bool>
        ExistingModeCases => new()
        {
            { "managed", DistributedApplicationOperation.Run, true },
            { "managed", DistributedApplicationOperation.Publish, true },
            { "run-existing", DistributedApplicationOperation.Run, false },
            { "run-existing", DistributedApplicationOperation.Publish, true },
            { "publish-existing", DistributedApplicationOperation.Run, true },
            { "publish-existing", DistributedApplicationOperation.Publish, false },
            { "existing", DistributedApplicationOperation.Run, false },
            { "existing", DistributedApplicationOperation.Publish, false },
        };

    [Fact]
    public void AddToolbox_UsesOfficialFoundryProjectParent()
    {
        var project = CreateProject();
        var toolbox = new FoundryToolboxResource("research-toolbox", project)
        {
            Version = "7",
        };

        Assert.Same(project, toolbox.Parent);
        Assert.IsType<AzureCognitiveServicesProjectResource>(
            toolbox.Parent);
        var addToolbox = Assert.Single(
            typeof(FoundryToolboxBuilderExtensions).GetMethods(),
            method => method.Name == nameof(FoundryToolboxBuilderExtensions.AddToolbox));
        Assert.Equal(
            typeof(IResourceBuilder<AzureCognitiveServicesProjectResource>),
            addToolbox.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void ConnectionProperties_HandOffDirectToolboxEndpointAndIdentityData()
    {
        var toolbox = new FoundryToolboxResource(
            "research-toolbox",
            CreateProject())
        {
            ApiVersion = "v1",
            Version = "7",
        };

        var properties = ((IResourceWithConnectionString)toolbox)
            .GetConnectionProperties()
            .ToDictionary(
                property => property.Key,
                property => property.Value.ValueExpression,
                StringComparer.Ordinal);

        Assert.Equal(
            "{project.outputs.endpoint}/toolboxes/research-toolbox/versions/7/mcp?api-version=v1",
            properties["Uri"]);
        Assert.Equal("{project.outputs.endpoint}", properties["ProjectEndpoint"]);
        Assert.Equal("research-toolbox", properties["Name"]);
        Assert.Equal("v1", properties["ApiVersion"]);
        Assert.Equal("7", properties["Version"]);
        Assert.Equal(
            "https://ai.azure.com/.default",
            properties["AuthorizationScope"]);
        Assert.Equal("Toolboxes=V1Preview", properties["FoundryFeatures"]);
        Assert.Equal(properties["Uri"], toolbox.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void ConnectionProperties_OmitVersionForDefaultEndpoint()
    {
        var toolbox = new FoundryToolboxResource(
            "research-toolbox",
            CreateProject());

        var properties = ((IResourceWithConnectionString)toolbox)
            .GetConnectionProperties()
            .ToDictionary(property => property.Key, property => property.Value);

        Assert.DoesNotContain("Version", properties.Keys);
        Assert.Equal(
            "{project.outputs.endpoint}/toolboxes/research-toolbox/mcp?api-version=v1",
            properties["Uri"].ValueExpression);
    }

    [Theory]
    [MemberData(nameof(ExistingModeCases))]
    public async Task ExistingModeMatrix_SuppressesOnlySelectedExecutionMode(
        string existingMode,
        DistributedApplicationOperation operation,
        bool expectsReconciliation)
    {
        var resource = new FoundryToolboxResource(
            "research-toolbox",
            CreateProject());
        IResourceBuilder<FoundryToolboxResource> toolbox =
            new StubResourceBuilder<FoundryToolboxResource>(resource);

        _ = existingMode switch
        {
            "managed" => toolbox,
            "run-existing" => toolbox.RunAsExisting(),
            "publish-existing" => toolbox.PublishAsExisting(),
            "existing" => toolbox.AsExisting(),
            _ => throw new ArgumentOutOfRangeException(nameof(existingMode)),
        };

        var steps = await CreateStepsAsync(toolbox.Resource, operation);

        Assert.Equal(expectsReconciliation ? 1 : 0, steps.Count);
        if (!expectsReconciliation)
        {
            return;
        }

        var step = Assert.Single(steps);
        Assert.Same(toolbox.Resource, step.Resource);
        if (operation == DistributedApplicationOperation.Run)
        {
            Assert.Equal("deploy-research-toolbox-before-start", step.Name);
            Assert.Contains("before-start", step.RequiredBySteps);
            Assert.Contains("run-mode-azure-provision", step.DependsOnSteps);
        }
        else
        {
            Assert.Equal("deploy-research-toolbox", step.Name);
            Assert.Contains(WellKnownPipelineTags.DeployCompute, step.Tags);
            Assert.Contains(WellKnownPipelineSteps.Deploy, step.RequiredBySteps);
            Assert.Contains(WellKnownPipelineSteps.DeployPrereq, step.DependsOnSteps);
        }
    }

    [Fact]
    public void ControlPlaneAssembly_DoesNotReferenceLocalRuntimeClientAssemblies()
    {
        var references = typeof(FoundryToolboxResource)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain("FoundryToolbox.Client", references);
        Assert.DoesNotContain("Aspire.Foundry", references);
        Assert.DoesNotContain("Microsoft.Extensions.AI.Foundry.Toolbox", references);
    }

    private static AzureCognitiveServicesProjectResource CreateProject()
    {
        var foundry = new FoundryResource("foundry", _ => { });
        return new AzureCognitiveServicesProjectResource(
            "project",
            _ => { },
            foundry);
    }

    private static async Task<IReadOnlyList<PipelineStep>> CreateStepsAsync(
        FoundryToolboxResource toolbox,
        DistributedApplicationOperation operation)
    {
        var annotation = Assert.Single(
            toolbox.Annotations.OfType<PipelineStepAnnotation>());
        var model = new DistributedApplicationModel([toolbox.Parent, toolbox]);
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new PipelineStepFactoryContext
        {
            PipelineContext = new PipelineContext(
                model,
                new DistributedApplicationExecutionContext(operation),
                services,
                NullLogger.Instance,
                CancellationToken.None),
            Resource = toolbox,
        };

        return (await annotation.CreateStepsAsync(context)).ToArray();
    }

    private sealed class StubResourceBuilder<T>(T resource) : IResourceBuilder<T>
        where T : IResource
    {
        public IDistributedApplicationBuilder ApplicationBuilder =>
            throw new NotSupportedException();

        public T Resource { get; } = resource;

        public IResourceBuilder<T> WithAnnotation<TAnnotation>(
            ResourceAnnotationMutationBehavior behavior = ResourceAnnotationMutationBehavior.Append)
            where TAnnotation : IResourceAnnotation, new() =>
            WithAnnotation(new TAnnotation(), behavior);

        public IResourceBuilder<T> WithAnnotation<TAnnotation>(
            TAnnotation annotation,
            ResourceAnnotationMutationBehavior behavior = ResourceAnnotationMutationBehavior.Append)
            where TAnnotation : IResourceAnnotation
        {
            Resource.Annotations.Add(annotation);
            return this;
        }
    }
}
