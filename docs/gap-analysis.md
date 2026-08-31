# Extensions.AI and Agent Framework gap analysis

This document records a historical prototype comparison against equivalent Foundry resources and application behavior. The Agent Framework spike is intentionally not included in the public snapshot.

| Capability | Direct Extensions.AI application | Historical Agent Framework spike |
|---|---|---|
| `IChatClient` abstraction | Native in Web | Native under `AIAgent`; Web uses the hosted Responses endpoint |
| Toolbox endpoint construction | Host-neutral `FoundryToolbox.Client` | `AddFoundryToolboxes` |
| Entra token and feature header | Host-neutral client using an explicit credential partition | `AddFoundryToolboxes` using `AzureCliCredential` |
| MCP initialize/list/call | MCP SDK directly | Packaged hosting service |
| Local RAG functions | Native `AIFunction` in Web | Same source linked into Agent and passed to `AsAIAgent` |
| Approval state machine | Native `ApprovalRequiredAIFunction`; structured `_meta.tool_configuration.require_approval` detection | **Gap:** current `AddFoundryToolboxes` does not decorate `require_approval=always`; live test invoked the tool immediately |
| OAuth consent translation | Exact MCP `-32006` translation into typed application behavior | Packaged `oauth_consent_request` Responses output; not exercised because the sample MCP source is public |
| Readiness/degraded states | Runtime readiness observed by `Aspire.Foundry` health and startup integration | Packaged `/readiness` and Toolbox startup states |
| Retry and trace propagation | Bounded host-neutral discovery retry, cancelable shutdown, no call replay, W3C trace propagation | Packaged |
| Hosted request call context | Not available from a normal Web process | Available when hosted |
| Foundry Responses server | Not required | Required process boundary |
| `AsHostedAgent` deployment | Not applicable | Supported; local manifest generated without deployment |
| Dependency/complexity cost | Smaller package surface, more Foundry-specific code | Four added central packages and a separate Agent process |

## Current extraction result

The original direct MEAI prototype proved the service but mixed reusable
runtime behavior, Aspire lifecycle, and manual application tool composition in
one sample adapter. The current `main` branch separates those concerns:

| Layer | Current project |
|---|---|
| Foundry-over-MCP runtime | `FoundryToolbox.Client` |
| Optional MEAI request attachment | `FoundryToolbox.Client` through `UseFoundryToolbox` |
| Aspire application integration | `Aspire.Foundry` |
| Aspire AppHost staging integration | `Aspire.Hosting.Foundry.Toolbox` |
| Local tool creation, selection, and UX | `AspireFoundryToolboxChat.Web` |
| Deterministic proof | A dedicated test project beside each candidate layer |

The Agent Framework comparison still uses fewer sample files because its
Toolbox adapter is packaged inside Hosting. That convenience also hides the
reusable behavior behind an otherwise unnecessary hosted-agent dependency.

## Runtime evidence

- Direct Extensions.AI Toolbox discovery returned four tools and read structured approval metadata from `McpClientTool.ProtocolTool.Meta`.
- The extracted host-neutral client rediscovered those four live tools and
  directly completed approved Microsoft Learn and Web Search calls; both
  returned source links.
- Agent Framework readiness returned HTTP 200 and discovered the same four tools.
- Agent Framework `/responses` completed a model turn with `agent-ready`.
- The remote Blazor client completed a round trip with `agent-web-ready`.
- Toolbox version `1` remained the only version; no lifecycle churn occurred.
- Extensions.AI full Aspire orchestration started successfully with Web and MarkItDown healthy before the package extraction.
- A second model-heavy Toolbox scenario hit the existing deployment rate limit.

## Existing-resource attachment finding

Aspire 13.4.6 `RunAsExisting` and `PublishAsExisting` still submit ARM deployments to resolve existing resource outputs. In addition, Foundry `AddProject` emits an Application Insights project connection and role assignments. Against this existing project, that connection deployment failed because the project already has its single allowed `AppInsights` category connection.

The `main` AppHost therefore defaults to native Aspire connection resources
for the existing project and Toolbox. The default model has no Foundry Azure
provisioning resource and performs no Toolbox reconciliation. A separate,
explicit managed mode exercises the typed
`AddFoundry(...).AddProject(...).AddToolbox(...)` hierarchy and provisions its
own GPT-4.1-mini deployment instead of assuming the existing project model is
present.

The default manifest proves the boundary: `foundry-project` and
`research-toolbox` are `value.v0` connection resources, and Web receives them
through `ConnectionStrings__foundry-project` and
`ConnectionStrings__research-toolbox`. No Azure or Toolbox deployment resource
is present.

The custom `ExistingFoundryProjectResource` workaround has been removed.
Adopting a typed existing hierarchy remains blocked until Aspire suppresses
the implicit children for the selected existing execution mode.

The earlier typed-existing experiment demonstrated that the path can create account-scoped role assignments and implicit project connections. That side effect is why the public sample defaults to external connection resources instead of presenting the typed path as non-mutating.

## Local ingestion compatibility

Windows SQLite cannot reliably lock a database stored under a WSL UNC build output. The Web app therefore places `vector-store.db` under the Windows local application-data directory when its executable is running from a UNC path.

The Web app resolves `ModelContextProtocol` 2.1.0 for Foundry Toolbox, whose default negotiation starts with the MCP 2026-07-28 `server/discover` probe. The current `mcp/markitdown` server advertises MCP 2024-11-05 and terminates its session on that unknown probe instead of allowing the client's fallback. The MarkItDown reader explicitly pins MCP 2024-11-05 and uses the canonical `/mcp/` path.

## Package compatibility finding

The current Foundry packages declare `Microsoft.Extensions.AI` `10.9.0`, but their Foundry/OpenAI bridge is built against `Microsoft.Extensions.AI.OpenAI` `10.6.0` and `OpenAI` `2.10.0`. Forcing the newer `10.9.0`/`2.12.0` adapter produced a runtime `MissingMethodException`. The branch therefore follows the package-declared compatible adapter versions.
