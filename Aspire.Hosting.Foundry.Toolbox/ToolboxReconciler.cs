namespace Aspire.Hosting.Foundry.Toolbox;

public sealed class ToolboxReconciler(IToolboxAdministration administration)
{
    public async Task<ToolboxReconcileResult> ReconcileAsync(
        FoundryToolboxDefinition definition,
        CancellationToken cancellationToken)
    {
        var configurationHash = definition.ComputeConfigurationHash();
        var existing = await administration.GetAsync(definition.Name, cancellationToken);

        if (existing is not null)
        {
            if (!existing.DefaultVersionMetadata.TryGetValue(
                    FoundryToolboxDefinition.ManagedByKey,
                    out var managedBy) ||
                !string.Equals(
                    managedBy,
                    FoundryToolboxDefinition.ManagedByValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Toolbox '{definition.Name}' exists but is not owned by Aspire.Hosting.Foundry. No changes were made.");
            }

            if (existing.DefaultVersionMetadata.TryGetValue(
                    FoundryToolboxDefinition.ConfigurationHashKey,
                    out var existingHash) &&
                string.Equals(existingHash, configurationHash, StringComparison.Ordinal))
            {
                return new ToolboxReconcileResult(
                    existing.DefaultVersion,
                    configurationHash,
                    ToolboxReconcileAction.Reused);
            }
        }

        var created = await administration.CreateVersionAsync(
            definition,
            configurationHash,
            cancellationToken);

        if (existing is not null &&
            !string.Equals(
                existing.DefaultVersion,
                created.Version,
                StringComparison.Ordinal))
        {
            await administration.UpdateDefaultVersionAsync(
                definition.Name,
                created.Version,
                cancellationToken);
        }

        return new ToolboxReconcileResult(
            created.Version,
            configurationHash,
            existing is null
                ? ToolboxReconcileAction.Created
                : ToolboxReconcileAction.CreatedAndPromoted);
    }
}

public sealed record ToolboxReconcileResult(
    string Version,
    string ConfigurationHash,
    ToolboxReconcileAction Action);

public enum ToolboxReconcileAction
{
    Reused,
    Created,
    CreatedAndPromoted,
}
