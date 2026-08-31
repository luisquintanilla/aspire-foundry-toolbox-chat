using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Extensions.Logging;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Aspire.Hosting.Foundry.Toolbox;

public interface IToolboxAdministration
{
    Task<ToolboxState?> GetAsync(string name, CancellationToken cancellationToken);

    Task<ToolboxVersionState> CreateVersionAsync(
        FoundryToolboxDefinition definition,
        string configurationHash,
        CancellationToken cancellationToken);

    Task UpdateDefaultVersionAsync(
        string name,
        string version,
        CancellationToken cancellationToken);
}

public sealed record ToolboxState(
    string Name,
    string DefaultVersion,
    IReadOnlyDictionary<string, string> DefaultVersionMetadata);

public sealed record ToolboxVersionState(string Name, string Version);

public sealed class AzureToolboxAdministration : IToolboxAdministration
{
    private const int ProjectEndpointReadinessMaxRetryAttempts = 11;
    private static readonly TimeSpan s_projectEndpointReadinessDelay =
        TimeSpan.FromSeconds(5);
    private readonly AgentToolboxes _toolboxes;
    private readonly ILogger _logger;

    public AzureToolboxAdministration(
        AgentToolboxes toolboxes,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(toolboxes);
        ArgumentNullException.ThrowIfNull(logger);
        _toolboxes = toolboxes;
        _logger = logger;
    }

    public async Task<ToolboxState?> GetAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            ToolboxRecord toolbox = await ExecuteWithProjectReadinessRetryAsync(
                token => _toolboxes.GetAsync(name, token),
                "read Toolbox",
                cancellationToken);
            ToolboxVersion defaultVersion =
                await ExecuteWithProjectReadinessRetryAsync(
                    token => _toolboxes.GetVersionAsync(
                        name,
                        toolbox.DefaultVersion,
                        token),
                    "read Toolbox version",
                    cancellationToken);

            return new ToolboxState(
                toolbox.Name,
                toolbox.DefaultVersion,
                new Dictionary<string, string>(defaultVersion.Metadata, StringComparer.Ordinal));
        }
        catch (ClientResultException ex)
            when (ex.Status == 404 &&
                  !IsProjectEndpointNotReady(ex.Status, ex.Message))
        {
            return null;
        }
    }

    public async Task<ToolboxVersionState> CreateVersionAsync(
        FoundryToolboxDefinition definition,
        string configurationHash,
        CancellationToken cancellationToken)
    {
        ToolboxVersion version = await ExecuteWithProjectReadinessRetryAsync(
            token => _toolboxes.CreateVersionAsync(
                definition.Name,
                definition.CreateSdkTools(),
                definition.Description,
                new Dictionary<string, string>(
                    definition.CreateMetadata(configurationHash),
                    StringComparer.Ordinal),
                skills: null,
                policies: null,
                token),
            "create Toolbox version",
            cancellationToken);

        return new ToolboxVersionState(version.Name, version.Version);
    }

    public async Task UpdateDefaultVersionAsync(
        string name,
        string version,
        CancellationToken cancellationToken)
    {
        _ = await ExecuteWithProjectReadinessRetryAsync(
            async token =>
            {
                await _toolboxes.UpdateDefaultVersionAsync(
                    name,
                    version,
                    new RequestOptions { CancellationToken = token });
                return true;
            },
            "promote Toolbox version",
            cancellationToken);
    }

    internal static bool IsProjectEndpointNotReady(int status, string message) =>
        status == 404 &&
        (message.Contains(
             "Subdomain does not map to a resource",
             StringComparison.OrdinalIgnoreCase) ||
         message.Contains(
             "The project does not exist",
             StringComparison.OrdinalIgnoreCase));

    private async Task<T> ExecuteWithProjectReadinessRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        for (var retry = 0; ; retry++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (ClientResultException ex)
                when (IsProjectEndpointNotReady(ex.Status, ex.Message) &&
                      retry < ProjectEndpointReadinessMaxRetryAttempts)
            {
                _logger.LogWarning(
                    "Foundry project endpoint is not ready to {OperationName}. Retrying in {RetryDelaySeconds} seconds ({RetryAttempt}/{MaximumRetryAttempts}).",
                    operationName,
                    s_projectEndpointReadinessDelay.TotalSeconds,
                    retry + 1,
                    ProjectEndpointReadinessMaxRetryAttempts);
                await Task.Delay(
                    s_projectEndpointReadinessDelay,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

internal sealed class FoundryFeaturesPolicy : PipelinePolicy
{
    private const string HeaderName = "Foundry-Features";
    private const string HeaderValue = "Toolboxes=V1Preview";

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        message.Request.Headers.Add(HeaderName, HeaderValue);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        message.Request.Headers.Add(HeaderName, HeaderValue);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }
}
