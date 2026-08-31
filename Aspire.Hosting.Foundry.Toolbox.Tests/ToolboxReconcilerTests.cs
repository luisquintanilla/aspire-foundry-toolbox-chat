using Aspire.Hosting.Foundry.Toolbox;

namespace Aspire.Hosting.Foundry.Toolbox.Tests;

public sealed class ToolboxReconcilerTests
{
    [Fact]
    public async Task MissingToolbox_CreatesOwnedVersionWithoutPromotion()
    {
        var administration = new RecordingAdministration
        {
            CreatedVersion = new("research-toolbox", "1"),
        };
        var definition = CreateDefinition();

        var result = await new ToolboxReconciler(administration)
            .ReconcileAsync(definition, CancellationToken.None);

        Assert.Equal(ToolboxReconcileAction.Created, result.Action);
        Assert.Equal("1", result.Version);
        Assert.Equal(["get", "create"], administration.Calls);
        Assert.Equal(
            FoundryToolboxDefinition.ManagedByValue,
            administration.CreatedMetadata![FoundryToolboxDefinition.ManagedByKey]);
        Assert.Equal(
            result.ConfigurationHash,
            administration.CreatedMetadata[
                FoundryToolboxDefinition.ConfigurationHashKey]);
    }

    [Fact]
    public async Task MatchingOwnedConfiguration_ReusesDefaultVersionWithoutMutation()
    {
        var definition = CreateDefinition();
        var hash = definition.ComputeConfigurationHash();
        var administration = new RecordingAdministration
        {
            Existing = OwnedState("4", hash),
        };

        var result = await new ToolboxReconciler(administration)
            .ReconcileAsync(definition, CancellationToken.None);

        Assert.Equal(ToolboxReconcileAction.Reused, result.Action);
        Assert.Equal("4", result.Version);
        Assert.Equal(["get"], administration.Calls);
    }

    [Fact]
    public async Task ChangedOwnedConfiguration_CreatesThenPromotesNewVersion()
    {
        var administration = new RecordingAdministration
        {
            Existing = OwnedState("4", "old-hash"),
            CreatedVersion = new("research-toolbox", "5"),
        };

        var result = await new ToolboxReconciler(administration)
            .ReconcileAsync(CreateDefinition(), CancellationToken.None);

        Assert.Equal(ToolboxReconcileAction.CreatedAndPromoted, result.Action);
        Assert.Equal("5", result.Version);
        Assert.Equal(["get", "create", "promote:5"], administration.Calls);
    }

    [Fact]
    public async Task UnownedToolbox_FailsClosedBeforeAnyMutation()
    {
        var administration = new RecordingAdministration
        {
            Existing = new ToolboxState(
                "research-toolbox",
                "4",
                new Dictionary<string, string>()),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ToolboxReconciler(administration)
                .ReconcileAsync(CreateDefinition(), CancellationToken.None));

        Assert.Contains("not owned", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["get"], administration.Calls);
    }

    [Fact]
    public async Task PromotionFailure_IsPropagatedAndNeverReportedAsReconciled()
    {
        var administration = new RecordingAdministration
        {
            Existing = OwnedState("4", "old-hash"),
            CreatedVersion = new("research-toolbox", "5"),
            PromotionException = new InvalidOperationException("promotion failed"),
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ToolboxReconciler(administration)
                .ReconcileAsync(CreateDefinition(), CancellationToken.None));

        Assert.Equal("promotion failed", exception.Message);
        Assert.Equal(["get", "create", "promote:5"], administration.Calls);
    }

    [Fact]
    public void ConfigurationHash_IsDeterministicAcrossToolDeclarationOrder()
    {
        var first = new FoundryToolboxDefinition(
            "research-toolbox",
            "Research tools",
            [
                new FoundryWebSearchToolDefinition("web", "Search the web."),
                new FoundryMcpToolDefinition(
                    "learn",
                    "Search Microsoft Learn.",
                    "learn",
                    new Uri("https://learn.example/mcp"),
                    false),
            ]);
        var second = first with { Tools = [first.Tools[1], first.Tools[0]] };

        Assert.Equal(
            first.ComputeConfigurationHash(),
            second.ComputeConfigurationHash());
    }

    private static FoundryToolboxDefinition CreateDefinition() =>
        new(
            "research-toolbox",
            "Research tools",
            [new FoundryWebSearchToolDefinition("web", "Search the web.")]);

    private static ToolboxState OwnedState(string version, string hash) =>
        new(
            "research-toolbox",
            version,
            new Dictionary<string, string>
            {
                [FoundryToolboxDefinition.ManagedByKey] =
                    FoundryToolboxDefinition.ManagedByValue,
                [FoundryToolboxDefinition.ConfigurationHashKey] = hash,
            });

    private sealed class RecordingAdministration : IToolboxAdministration
    {
        public ToolboxState? Existing { get; init; }

        public ToolboxVersionState CreatedVersion { get; init; } =
            new("research-toolbox", "1");

        public Exception? PromotionException { get; init; }

        public List<string> Calls { get; } = [];

        public IReadOnlyDictionary<string, string>? CreatedMetadata { get; private set; }

        public Task<ToolboxState?> GetAsync(
            string name,
            CancellationToken cancellationToken)
        {
            Calls.Add("get");
            return Task.FromResult(Existing);
        }

        public Task<ToolboxVersionState> CreateVersionAsync(
            FoundryToolboxDefinition definition,
            string configurationHash,
            CancellationToken cancellationToken)
        {
            Calls.Add("create");
            CreatedMetadata = definition.CreateMetadata(configurationHash);
            return Task.FromResult(CreatedVersion);
        }

        public Task UpdateDefaultVersionAsync(
            string name,
            string version,
            CancellationToken cancellationToken)
        {
            Calls.Add($"promote:{version}");
            return PromotionException is null
                ? Task.CompletedTask
                : Task.FromException(PromotionException);
        }
    }
}
