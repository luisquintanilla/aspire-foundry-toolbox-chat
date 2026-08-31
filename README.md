# Aspire Foundry Toolbox Chat

An executable architecture sample for using Microsoft Foundry Toolbox from a
.NET Aspire application through Microsoft.Extensions.AI (MEAI) and the official
MCP C# SDK.

> [!IMPORTANT]
> This is an unofficial incubation sample, not a supported Microsoft package or
> production service. Several dependencies and the Foundry Toolbox service
> contract are previews.

The sample combines:

- An Azure-hosted chat model.
- Local PDF and Markdown ingestion through a pinned MarkItDown container.
- Local semantic search backed by SQLite.
- Remote Foundry Toolbox functions such as Web Search and Microsoft Learn.
- MEAI function invocation, per-call approval, citations, and OpenTelemetry.
- Aspire orchestration with a safe existing-resource mode and an explicit
  managed-resource mode.

![System architecture showing Aspire control-plane orchestration and the application data plane](docs/images/system-architecture.svg)

## Why this repository exists

Foundry Toolbox exposes MCP tools, but direct MEAI applications currently need
to assemble several pieces themselves: the Toolbox endpoint, Entra
authentication, the preview feature header, MCP lifecycle, approval metadata,
OAuth consent, retry boundaries, Aspire registration, and health reporting.

This repository separates those responsibilities into project-shaped proving
layers that can later move to an accepted upstream owner:

```csharp
builder.AddFoundryProjectClient("foundry-project")
       .AddToolboxClient("research-toolbox");

builder.Services
    .AddChatClient(/* model IChatClient */)
    .UseFoundryToolbox(options =>
        options.RequireApprovalForAllTools = true)
    .UseFunctionInvocation();
```

Toolbox is treated as a source of `AIFunction` instances, not as a model or a
second `IChatClient`. The middleware adds those functions to each request; the
standard MEAI function-invocation middleware owns the model/tool loop.

## What works

- Toolbox discovery and invocation over the official MCP C# SDK.
- Both batch and streaming MEAI calls.
- Strict approval-metadata and OAuth-consent handling.
- No automatic replay of `tools/call`.
- Keyed and unkeyed Aspire client registration.
- Existing and managed AppHost resource modes.
- Local ingestion, semantic search, citations, and approval UI.
- Deterministic tests for the runtime, Aspire client, control plane, and app.

See [Architecture](docs/architecture.md) and
[Design decisions](docs/design-decisions.md) for the boundaries and rationale.

## Safety defaults

The default `existing` mode references resources you already own. It does not
provision Azure resources or reconcile Toolbox versions.

Additional public-sample safeguards are enabled:

- Every remote Toolbox function requires an explicit approval in the UI.
- The approval card shows the destination, function name, and arguments.
- Sensitive AI telemetry is disabled unless explicitly enabled.
- The MarkItDown image is pinned to an immutable multi-platform digest.
- Credentials are supplied by `DefaultAzureCredential`; configuration scripts
  store only non-secret endpoints and resource names in .NET user secrets.
- Managed mode refuses to start without a separate resource-change
  acknowledgement.

These controls are not a data-loss-prevention system. Local document chunks are
sent to the configured embedding service, and excerpts selected by local search
become context for the remote chat model. Remote content can contain prompt
injection. Read [Security and privacy](docs/security-and-privacy.md) before
using non-sample data.

## Quick start

The easiest environment is the included
[dev container](.devcontainer/devcontainer.json), which also works in GitHub
Codespaces. You still need access to:

- An existing Microsoft Foundry project.
- A chat model deployment in that Foundry account.
- An embedding deployment, which may be in a different Azure OpenAI account.
- An existing Foundry Toolbox. To match the demo, include Web Search and the
  public Microsoft Learn MCP endpoint at
  `https://learn.microsoft.com/api/mcp`.

Authenticate without putting credentials in the repository:

```bash
az login --use-device-code
```

Configure the existing resources:

```bash
./scripts/configure-existing-azure.sh \
  --subscription-id "<subscription-id>" \
  --foundry-resource-group "<foundry-resource-group>" \
  --foundry-account "<foundry-account>" \
  --foundry-project "<foundry-project>" \
  --toolbox "<toolbox-name>" \
  --chat-deployment "<chat-deployment>" \
  --embedding-resource-group "<embedding-resource-group>" \
  --embedding-account "<embedding-account>" \
  --embedding-deployment "<embedding-deployment>"
```

Then run the AppHost:

```bash
dotnet run --project AspireFoundryToolboxChat.AppHost
```

Open the Aspire dashboard URL printed in the terminal, wait for `markitdown` and
`web` to become healthy, then open the Web endpoint. Codespaces forwards the
dynamic Aspire URLs automatically; keep forwarded ports private.

For prerequisites, manual configuration, managed mode, and troubleshooting, see
[Getting started](docs/getting-started.md).

## Repository map

| Project | Responsibility |
|---|---|
| `AspireFoundryToolboxChat.AppHost` | Safe existing-resource composition, explicit managed mode, MarkItDown container, and Web references |
| `AspireFoundryToolboxChat.Web` | Blazor UI, MEAI pipeline, local tools, approvals, consent, citations, and local vector storage |
| `AspireFoundryToolboxChat.ServiceDefaults` | Service discovery, health endpoints, resilience, and OpenTelemetry defaults |
| `FoundryToolbox.Client` | Host-neutral Foundry-over-MCP runtime and optional `UseFoundryToolbox` middleware |
| `Aspire.Foundry` | `AIProjectClient` and Toolbox registration, keyed DI, startup, health, and telemetry |
| `Aspire.Hosting.Foundry.Toolbox` | AppHost resource model and guarded Toolbox reconciliation |
| `*.Tests` | Dedicated tests beside each candidate layer and the application |

The local `FoundryToolbox.Client`, `Aspire.Foundry`, and
`Aspire.Hosting.Foundry.Toolbox` projects are non-packable proving projects.
Their names are proposals, not reserved or approved package IDs.

## Validate

```bash
dotnet build AspireFoundryToolboxChat.sln --no-incremental
dotnet test AspireFoundryToolboxChat.sln --no-build
```

Live Azure checks are intentionally not part of automated validation because
tests must not create resources, mutate Toolbox versions, or spend model quota.

## Documentation

- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Design decisions](docs/design-decisions.md)
- [Security and privacy](docs/security-and-privacy.md)
- [Productionization and upstream contribution map](docs/toolbox-productionization.md)
- [Agent Framework comparison](docs/gap-analysis.md)
- [Publishing a sanitized standalone repository](docs/publishing.md)

## Publishing

Do not push the development repository's existing Git history or all refs to a
public remote. Publish a sanitized working-tree snapshot as a new repository
with a fresh root commit. The required procedure and release checklist are in
[Publishing](docs/publishing.md).

## License

Licensed under the [MIT License](LICENSE).
Redistributed sample assets and browser libraries retain their own terms; see
[Third-party notices](THIRD-PARTY-NOTICES.md).
