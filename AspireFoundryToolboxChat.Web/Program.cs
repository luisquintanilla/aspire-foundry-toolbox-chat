using AspireFoundryToolboxChat.Web.Components;
using AspireFoundryToolboxChat.Web.Services;
using AspireFoundryToolboxChat.Web.Services.Ingestion;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using FoundryToolbox.Client;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
var captureSensitiveAiTelemetry =
    builder.Configuration.GetValue<bool>("Telemetry:EnableSensitiveData");

TokenCredential credential = new DefaultAzureCredential(
    new DefaultAzureCredentialOptions
    {
        ExcludeManagedIdentityCredential = builder.Environment.IsDevelopment(),
    });
builder.Services.AddSingleton(credential);
builder
    .AddFoundryProjectClient(
        "foundry-project",
        settings => settings.Credential = credential)
    .AddToolboxClient("research-toolbox");

builder.Services
    .AddChatClient(services =>
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var deployment = GetRequired(configuration, "CHAT_MODEL_DEPLOYMENT");

        return services
            .GetRequiredService<AIProjectClient>()
            .ProjectOpenAIClient
            .GetResponsesClient()
            .AsIChatClient(deployment);
    })
    .UseFoundryToolbox(options =>
        options.RequireApprovalForAllTools = true)
    .UseFunctionInvocation(configure: invocation =>
    {
        invocation.AllowConcurrentInvocation = false;
        invocation.MaximumConsecutiveErrorsPerRequest = 0;
    })
    .UseOpenTelemetry(configure: telemetry =>
        telemetry.EnableSensitiveData = captureSensitiveAiTelemetry);

builder.Services
    .AddEmbeddingGenerator<string, Embedding<float>>(services =>
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var credential = services.GetRequiredService<TokenCredential>();
        var endpoint = GetRequiredHttpsUri(
            configuration,
            "AZURE_OPENAI_EMBEDDING_ENDPOINT");
        var deployment = GetRequired(
            configuration,
            "AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME");

        return new AzureOpenAIClient(endpoint, credential)
            .GetEmbeddingClient(deployment)
            .AsIEmbeddingGenerator(IngestedChunk.VectorDimensions);
    })
    .UseOpenTelemetry(configure: telemetry =>
        telemetry.EnableSensitiveData = captureSensitiveAiTelemetry);

var vectorStorePath = LocalDataPath.GetVectorStorePath(
    AppContext.BaseDirectory,
    Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolderOption.Create),
    OperatingSystem.IsWindows());
Directory.CreateDirectory(Path.GetDirectoryName(vectorStorePath)!);
var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
builder.Services.AddSqliteVectorStore(
    _ => vectorStoreConnectionString,
    services => new()
    {
        EmbeddingGenerator = services.GetRequiredService<
            IEmbeddingGenerator<string, Embedding<float>>>(),
    });
builder.Services.AddSqliteCollection<string, IngestedChunk>(
    IngestedChunk.CollectionName,
    _ => vectorStoreConnectionString,
    services => new()
    {
        EmbeddingGenerator = services.GetRequiredService<
            IEmbeddingGenerator<string, Embedding<float>>>(),
    });
builder.Services.AddSingleton<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddSingleton<LocalKnowledgeTools>();
builder.Services.AddKeyedSingleton(
    "ingestion_directory",
    new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string GetRequired(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException(
            $"Missing required configuration value '{key}'.")
        : value;
}

static Uri GetRequiredHttpsUri(IConfiguration configuration, string key)
{
    var value = GetRequired(configuration, key);
    return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new InvalidOperationException(
                $"Configuration value '{key}' must be an absolute HTTPS URI.");
}
