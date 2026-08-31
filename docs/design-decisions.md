# Design decisions

These decisions keep the sample native to Aspire and MEAI while leaving package
ownership reversible.

## Decision summary

| Decision | Chosen shape |
|---|---|
| What is Toolbox? | A remote source of `AIFunction` instances |
| Is Toolbox an `IChatClient`? | No; the Azure model remains the chat client |
| How does Toolbox join chat? | Optional `UseFoundryToolbox` request middleware |
| Who runs functions? | Standard MEAI `FunctionInvokingChatClient` |
| Who owns MCP? | The official MCP C# SDK |
| Where does Foundry-specific runtime policy live? | Host-neutral `FoundryToolbox.Client` |
| Where does DI, health, and startup live? | `Aspire.Foundry` |
| Where does provisioning live? | AppHost integration only |
| Is Agent Framework required? | No |
| Is there a generic MEAI Toolbox abstraction? | No, not until a stable cross-provider contract exists |

## Toolbox is not a chat client

An `IChatClient` accepts messages and produces model responses. Foundry Toolbox
does not run a model conversation; it exposes callable tools over MCP.

Modeling Toolbox as another `IChatClient` would invent routing and response
semantics it does not have. Projecting discovered tools as MEAI `AIFunction`
instances uses the existing abstraction that already represents callable
capabilities.

## Middleware adds tools, not another agent loop

`UseFoundryToolbox` is a `DelegatingChatClient` that:

1. Discovers or retrieves cached Toolbox functions.
2. Clones caller-owned `ChatOptions`.
3. Preserves local tool order.
4. Adds remote functions.
5. Rejects duplicate names deterministically.
6. Delegates to the existing pipeline.

It does not execute model-selected functions. `UseFunctionInvocation` already
owns that responsibility, including approval request and response content.

This keeps the pipeline composable:

```text
Tool attachment -> function invocation -> telemetry -> model
```

## Foundry policy sits above generic MCP

The MCP SDK owns JSON-RPC, protocol negotiation, transport, and tool calls.
`FoundryToolbox.Client` owns only service-specific behavior:

- Endpoint shape.
- Entra scope.
- Preview feature header.
- Foundry approval metadata.
- Foundry OAuth-consent error contract.
- Safe retry and lifecycle policy.

Moving that policy into the generic MCP SDK would couple a protocol library to
one provider. Reimplementing MCP would duplicate mature protocol machinery.

## The runtime remains host-neutral

Direct MEAI applications and hosted agents can both need Foundry Toolbox without
Aspire. Therefore the runtime does not live only inside an `Aspire.*` assembly.

`Aspire.Foundry` consumes the runtime and adds host concerns. Aspire users still
get one coherent registration API and can receive the runtime transitively if
the layers become packages.

## One Aspire client package, with an extraction seam

The proving project exposes both:

```csharp
builder.AddFoundryToolboxClient("research-toolbox");
```

and:

```csharp
builder.AddFoundryProjectClient("foundry-project")
       .AddToolboxClient("research-toolbox");
```

The chained form is convenient for project-aware applications. The standalone
form proves that Toolbox does not require `AIProjectClient`.

Project-client and Toolbox registration remain in separate source folders. If
upstream dependency policy later requires a package split, the move is
mechanical rather than an application redesign.

## AppHost owns the control plane only

The hosting integration declares resources and, in managed mode, reconciles
Toolbox definitions. It never opens request-time MCP sessions.

The Web process owns:

- Credentials for live user traffic.
- MCP connection lifetime.
- Function selection and invocation.
- Consent and approval UX.
- Application-specific local tools.

This avoids turning AppHost into a service proxy or a hidden request-path
dependency.

## Existing resources use native Aspire vocabulary

Aspire's established family is:

| API | Run mode | Publish mode |
|---|---|---|
| `RunAsExisting()` | Reference existing | Managed |
| `PublishAsExisting()` | Managed | Reference existing |
| `AsExisting()` | Reference existing | Reference existing |

There is no need for a sample-specific `AddExistingFoundryProject` or
`ReuseExisting` vocabulary.

The sample currently uses external connection resources for its default
existing path. This is an intentional safety bridge because the typed Foundry
project path still contributes mutable implicit children. Once upstream can
represent a truly non-mutating existing project, only the AppHost declaration
needs to change.

## Remote calls require application approval

The Toolbox service can mark individual functions as requiring approval, and
the runtime preserves that metadata. This sample applies a stricter application
policy: all remote Toolbox functions are wrapped with
`ApprovalRequiredAIFunction`.

The UI displays the destination, function name, and argument values. This makes
the prompt's "do not send local content to remote tools" instruction observable
by a person. It is still human-in-the-loop control, not deterministic DLP.

The stricter behavior is a middleware option rather than a universal runtime
default so other applications can make an explicit policy choice.

## No Agent Framework dependency

Agent Framework is useful when an application needs hosted-agent lifecycle,
Foundry Responses hosting, call context, or `AsHostedAgent` deployment.

This sample needs a direct MEAI application with one model and a set of tools.
Adding Agent Framework would introduce another process and package surface
without owning a required responsibility. A future Agent Framework adapter can
reuse the same host-neutral runtime.

Semantic Kernel is intentionally outside the architecture.

## No generic MEAI Toolbox abstraction yet

"Toolbox" is not a stable industry-wide runtime contract. Providers use the
term for different catalogs, registries, MCP aggregators, and agent services.
Promoting a generic abstraction now would encode Foundry semantics into MEAI.

`AITool` and `AIFunction` are the portable narrow waist. A broader abstraction
should be proposed only after multiple providers demonstrate shared discovery,
identity, approval, version, and lifecycle semantics.

## Failure and retry policy

- Discovery can retry by opening a fresh connection because no tool side effect
  has occurred.
- `tools/call` is never automatically replayed.
- A post-dispatch transport failure is indeterminate.
- Caller cancellation is preserved.
- Malformed or unknown approval policy fails closed.
- One runtime instance represents one credential and security partition.
- Timed-out discovery attempts are canceled and capped while late cleanup runs.
- Connection replacement waits for dispatched calls to drain before retiring
  their MCP session.

These choices prioritize correctness over an apparently successful but
duplicated side effect.

## Rejected shapes

| Alternative | Why it was rejected |
|---|---|
| Toolbox implements `IChatClient` | It has no model or chat operation |
| Manual tool merging in the Blazor page | Duplicates policy and mutates application code per request |
| Runtime inside AppHost | Mixes control plane with user traffic |
| Runtime only inside Agent Framework Hosting | Forces hosted-agent dependencies on direct MEAI consumers |
| Custom MCP transport | Reinvents the official SDK |
| Generic MEAI Toolbox API now | No converged cross-provider contract |
| Implicit managed mode | Can mutate Azure resources or incur cost unexpectedly |

## Open upstream decisions

- Final owner and package ID for the host-neutral runtime.
- Whether Aspire prefers `Aspire.Foundry` or the SDK-aligned
  `Aspire.Azure.AI.Projects`.
- Whether MCP dependencies require a separate Aspire client package.
- The upstream correction for truly non-mutating existing Foundry projects.
- Preview and breaking-change policy while Toolbox remains a preview service.

No package name in this repository is claimed as approved.
