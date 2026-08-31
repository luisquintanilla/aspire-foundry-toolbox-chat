using Aspire.Foundry;
using Azure.AI.Projects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class AspireFoundryProjectClientExtensions
{
    internal const string DefaultConfigSectionName =
        "Aspire:Foundry:ProjectClient";

    public static AspireFoundryProjectClientBuilder AddFoundryProjectClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<FoundryProjectClientSettings>? configureSettings = null,
        Action<AIProjectClientOptions>? configureClientOptions = null) =>
        AddProjectClient(
            builder,
            connectionName,
            serviceKey: null,
            configureSettings,
            configureClientOptions);

    public static AspireFoundryProjectClientBuilder AddKeyedFoundryProjectClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<FoundryProjectClientSettings>? configureSettings = null,
        Action<AIProjectClientOptions>? configureClientOptions = null) =>
        AddProjectClient(
            builder,
            name,
            serviceKey: name,
            configureSettings,
            configureClientOptions);

    private static AspireFoundryProjectClientBuilder AddProjectClient(
        IHostApplicationBuilder builder,
        string connectionName,
        string? serviceKey,
        Action<FoundryProjectClientSettings>? configureSettings,
        Action<AIProjectClientOptions>? configureClientOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var sectionName = serviceKey is null
            ? DefaultConfigSectionName
            : $"{DefaultConfigSectionName}:{serviceKey}";
        var settings = new FoundryProjectClientSettings();
        builder.Configuration.GetSection(sectionName).Bind(settings);
        settings.ParseConnectionString(
            builder.Configuration.GetConnectionString(connectionName));
        configureSettings?.Invoke(settings);

        var registration = new FoundryProjectClientRegistration(
            connectionName,
            serviceKey,
            sectionName,
            settings,
            builder.Configuration.GetSection($"{sectionName}:ClientOptions"),
            configureClientOptions);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(
                registration.CreateClient);
        }
        else
        {
            builder.Services.AddKeyedSingleton<AIProjectClient>(
                serviceKey,
                (serviceProvider, _) =>
                    registration.CreateClient(serviceProvider));
        }

        AddProjectTelemetry(builder, settings);
        return new AspireFoundryProjectClientBuilder(builder, registration);
    }

    private static void AddProjectTelemetry(
        IHostApplicationBuilder builder,
        FoundryProjectClientSettings settings)
    {
        var openTelemetry = builder.Services.AddOpenTelemetry();
        if (!settings.DisableTracing)
        {
            openTelemetry.WithTracing(tracing =>
                tracing.AddSource(
                    "Azure.AI.Projects.*",
                    "System.ClientModel"));
        }

        if (!settings.DisableMetrics)
        {
            openTelemetry.WithMetrics(metrics =>
                metrics.AddMeter("Azure.AI.Projects.*"));
        }
    }
}
