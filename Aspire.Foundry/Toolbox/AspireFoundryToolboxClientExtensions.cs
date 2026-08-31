using Aspire.Foundry;
using FoundryToolbox.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.Hosting;

public static class AspireFoundryToolboxClientExtensions
{
    internal const string DefaultConfigSectionName =
        "Aspire:Foundry:Toolbox";

    public static IHostApplicationBuilder AddFoundryToolboxClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<FoundryToolboxClientSettings>? configureSettings = null)
    {
        _ = AddToolboxClient(
            builder,
            connectionName,
            serviceKey: null,
            configureSettings,
            projectRegistration: null);
        return builder;
    }

    public static IHostApplicationBuilder AddKeyedFoundryToolboxClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<FoundryToolboxClientSettings>? configureSettings = null)
    {
        _ = AddToolboxClient(
            builder,
            name,
            serviceKey: name,
            configureSettings,
            projectRegistration: null);
        return builder;
    }

    public static AspireFoundryProjectClientBuilder AddToolboxClient(
        this AspireFoundryProjectClientBuilder projectBuilder,
        string connectionName,
        Action<FoundryToolboxClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(projectBuilder);
        _ = AddToolboxClient(
            projectBuilder.HostBuilder,
            connectionName,
            serviceKey: null,
            configureSettings,
            projectBuilder.Registration);
        return projectBuilder;
    }

    public static AspireFoundryProjectClientBuilder AddKeyedToolboxClient(
        this AspireFoundryProjectClientBuilder projectBuilder,
        string name,
        Action<FoundryToolboxClientSettings>? configureSettings = null)
    {
        ArgumentNullException.ThrowIfNull(projectBuilder);
        _ = AddToolboxClient(
            projectBuilder.HostBuilder,
            name,
            serviceKey: name,
            configureSettings,
            projectBuilder.Registration);
        return projectBuilder;
    }

    private static FoundryToolboxClientRegistration AddToolboxClient(
        IHostApplicationBuilder builder,
        string connectionName,
        string? serviceKey,
        Action<FoundryToolboxClientSettings>? configureSettings,
        FoundryProjectClientRegistration? projectRegistration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var sectionName = serviceKey is null
            ? DefaultConfigSectionName
            : $"{DefaultConfigSectionName}:{serviceKey}";
        var settings = new FoundryToolboxClientSettings();
        builder.Configuration.GetSection(sectionName).Bind(settings);
        settings.ParseConnectionString(
            builder.Configuration.GetConnectionString(connectionName));
        configureSettings?.Invoke(settings);

        var registration = new FoundryToolboxClientRegistration(
            connectionName,
            serviceKey,
            settings,
            projectRegistration);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(
                serviceProvider => registration.CreateClient(serviceProvider));
        }
        else
        {
            builder.Services.AddKeyedSingleton<FoundryToolboxClient>(
                serviceKey,
                (serviceProvider, _) =>
                    registration.CreateClient(serviceProvider));
        }

        if (settings.EnableEagerInitialization)
        {
            builder.Services.AddSingleton<IHostedService>(serviceProvider =>
                new FoundryToolboxStartupService(
                    ResolveClient(serviceProvider, serviceKey),
                    serviceProvider.GetRequiredService<
                        ILogger<FoundryToolboxStartupService>>()));
        }

        if (!settings.DisableHealthChecks)
        {
            builder.Services
                .AddHealthChecks()
                .Add(new HealthCheckRegistration(
                    serviceKey is null
                        ? "foundry-toolbox"
                        : $"foundry-toolbox-{serviceKey}",
                    serviceProvider => new FoundryToolboxHealthCheck(
                        ResolveClient(serviceProvider, serviceKey),
                        settings.Required),
                    failureStatus: null,
                    tags: null));
        }

        AddToolboxTelemetry(builder, settings);
        return registration;
    }

    private static FoundryToolboxClient ResolveClient(
        IServiceProvider serviceProvider,
        string? serviceKey) =>
        serviceKey is null
            ? serviceProvider.GetRequiredService<FoundryToolboxClient>()
            : serviceProvider.GetRequiredKeyedService<FoundryToolboxClient>(
                serviceKey);

    private static void AddToolboxTelemetry(
        IHostApplicationBuilder builder,
        FoundryToolboxClientSettings settings)
    {
        var openTelemetry = builder.Services.AddOpenTelemetry();
        if (!settings.DisableTracing)
        {
            openTelemetry.WithTracing(tracing =>
                tracing.AddSource(FoundryToolboxTelemetry.ActivitySourceName));
        }

        if (!settings.DisableMetrics)
        {
            openTelemetry.WithMetrics(metrics =>
                metrics.AddMeter(FoundryToolboxTelemetry.MeterName));
        }
    }
}
