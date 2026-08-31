using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace FoundryToolbox.Client;

internal sealed class FoundryToolboxFunction(
    McpClientTool inner,
    FoundryToolboxConnection connection,
    Action<Exception, FoundryToolboxReadiness> reportFailure) : AIFunction
{
    public override string Name => inner.Name;

    public override string Description => inner.Description;

    public override JsonElement JsonSchema => inner.JsonSchema;

    public override JsonElement? ReturnJsonSchema => inner.ReturnJsonSchema;

    public override JsonSerializerOptions JsonSerializerOptions =>
        inner.JsonSerializerOptions;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        using var activity = FoundryToolboxTelemetry.ActivitySource.StartActivity(
            "foundry.toolbox.tools.call",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.tool.name", Name);

        try
        {
            using var lease = connection.AcquireInvocationLease();
            var result = await inner
                .InvokeAsync(arguments, cancellationToken)
                .ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex) when (
            FoundryToolboxConsent.TryParse(ex, out var consentUri) &&
            consentUri is not null)
        {
            var consentException =
                new FoundryToolboxConsentRequiredException(consentUri, ex);
            FoundryToolboxTelemetry.InvocationFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "error.type",
                    nameof(FoundryToolboxConsentRequiredException)));
            activity?.SetStatus(
                ActivityStatusCode.Error,
                nameof(FoundryToolboxConsentRequiredException));
            reportFailure(consentException, FoundryToolboxReadiness.ConsentRequired);
            throw consentException;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FoundryToolboxTelemetry.InvocationFailures.Add(
                1,
                new KeyValuePair<string, object?>(
                    "error.type",
                    ex.GetType().Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            reportFailure(ex, FoundryToolboxReadiness.Unhealthy);
            throw;
        }
    }
}
