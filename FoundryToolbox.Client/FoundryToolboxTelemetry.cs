using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FoundryToolbox.Client;

public static class FoundryToolboxTelemetry
{
    public const string ActivitySourceName = "FoundryToolbox.Client";
    public const string MeterName = "FoundryToolbox.Client";

    internal static readonly ActivitySource ActivitySource =
        new(ActivitySourceName);

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> DiscoveryAttempts =
        Meter.CreateCounter<long>("foundry.toolbox.discovery.attempts");

    internal static readonly Counter<long> InvocationFailures =
        Meter.CreateCounter<long>("foundry.toolbox.invocation.failures");
}
