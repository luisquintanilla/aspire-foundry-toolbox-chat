# Productionizing Aspire + Microsoft Foundry Toolbox

This sample proves two separate integration surfaces:

1. An Aspire AppHost declares, reconciles, and references a Foundry Toolbox.
2. An application connects to the Toolbox MCP endpoint and exposes safe
   `AIFunction` instances.

Those surfaces run in different processes and must not share implementation
dependencies.

## Target architecture

```text
AppHost process

Aspire.Hosting.Foundry
  ^
  |
Aspire.Hosting.Foundry.Toolbox
  AddToolbox, tool definitions, existing modes, reconciliation, references


Application process

Aspire.Foundry
  AddFoundryProjectClient, AddFoundryToolboxClient, health, telemetry, DI
  |
  +--> Azure.AI.Projects.AIProjectClient
  |
  +--> FoundryToolbox.Client
         endpoint, Entra auth, MCP lifetime, approval, consent
         |
         +--> ModelContextProtocol


Hosted agent process

Microsoft.Agents.AI.Foundry.Hosting
  hosted Responses and request-context behavior
  |
  +--> FoundryToolbox.Client
```

All proposed projects in this repository are non-packable. Their names are
working names for an executable contribution proposal, not claims on package
ownership.

## Package naming

The existing AppHost integration is `Aspire.Hosting.Foundry`. The proposed
application-side integration is `Aspire.Foundry`, not
`Aspire.Foundry.Client`.

Aspire normally uses `Aspire.Hosting.<Product>` for AppHost resource modeling
and omits a generic `.Client` suffix from client integrations. For example,
`Aspire.Hosting.OpenAI` is paired with `Aspire.OpenAI`.

`Aspire.Azure.AI.Projects` is a credible alternative because it mirrors the
Azure SDK package that contains `AIProjectClient`. This sample instead uses
`Aspire.Foundry` because the one client integration intentionally contains
both `AIProjectClient` and the MCP-based Toolbox client. Upstream API review
must make the final naming decision.

`Aspire.Foundry` remains deliberately narrow. It does not absorb model clients,
Foundry IQ, hosted-agent behavior, or generic MCP:

| Capability | Owner |
|---|---|
| Project administration and project OpenAI access | `Azure.AI.Projects` |
| Model inference | Existing OpenAI and Azure AI Inference integrations |
| Direct Foundry IQ retrieval | `Azure.Search.Documents` and its Aspire integration |
| Generic MCP transport and JSON-RPC | `ModelContextProtocol` |
| Hosted Responses and request context | Agent Framework Hosting |
| Toolbox-specific endpoint, auth, approval, and consent | `FoundryToolbox.Client` |

## Application API

The project-aware form is the primary sample API:

```csharp
builder.AddFoundryProjectClient("foundry-project")
       .AddToolboxClient("research-toolbox");
```

It registers the underlying `AIProjectClient`, reads the Toolbox leaf
connection independently, validates that both connections identify the same
project, and uses the project credential only as an explicit overridable
default.

Toolbox-only applications do not need `AIProjectClient`:

```csharp
builder.AddFoundryToolboxClient("research-toolbox");
```

Both APIs support keyed registration for multiple project or Toolbox
connections.

The runtime is concrete and the MEAI adaptation is optional:

```csharp
builder.Services
    .AddChatClient(CreateModelClient)
    .UseFoundryToolbox()
    .UseFunctionInvocation();
```

`UseFoundryToolbox` is outermost so it can discover and append the Toolbox
`AIFunction` instances before `FunctionInvokingChatClient` sees the request.
It clones `ChatOptions`, keeps application functions first, rejects duplicate
names, and supports both batch and streaming calls. A keyed overload resolves
the matching keyed `FoundryToolboxClient`.

Toolbox is not itself an `IChatClient`: it has no model and does not implement a
chat operation. The package therefore exposes the concrete
`FoundryToolboxClient` for Toolbox lifecycle and an optional
`FoundryToolboxChatClient` middleware for chat applications. It does not add a
provider-specific client interface or a generic MEAI "toolbox" abstraction;
`AITool` and `AIFunction` remain the portable boundary.

## AppHost API and existing resources

The desired typed hierarchy follows native Aspire composition:

```csharp
var foundry = builder.AddFoundry("foundry")
    .AsExisting(foundryAccountName, resourceGroupName)
    .ClearDefaultRoleAssignments();

var project = foundry.AddProject("foundry-project")
    .AsExisting(foundryProjectName, resourceGroupName);

var toolbox = project.AddToolbox("research-toolbox")
    .AsExisting();
```

Aspire's existing-resource family has distinct execution-mode semantics:

| API | Run | Publish/deploy |
|---|---|---|
| `RunAsExisting` | Existing | Managed |
| `PublishAsExisting` | Managed | Existing |
| `AsExisting` | Existing | Existing |

Toolbox should expose the same three parameterless methods. `AddToolbox`
already supplies the remote name; these methods only control whether the
Toolbox reconciliation pipeline participates in each execution mode.

The sample requires no mutation in either mode, so its typed end state uses
`AsExisting()`.

### Safe bridge used today

The typed hierarchy is not safe against the current live project. Aspire
13.4.6 and current `main` model an existing project in Bicep, but
`AddProject(...).AsExisting(...)` still emits an Application Insights child,
a project connection, and role assignments. This previously collided with
the project's one existing App Insights connection.

Until that upstream implicit-child behavior is corrected, the default AppHost
uses only native external connection resources:

```csharp
var projectEndpoint = builder.AddParameter("foundry-project-endpoint");
var project = builder.AddConnectionString(
    "foundry-project",
    ReferenceExpression.Create($"{projectEndpoint}"));
var toolbox = builder.AddConnectionString(
    "research-toolbox",
    ReferenceExpression.Create(
        $"ProjectEndpoint={projectEndpoint};ToolboxName={toolboxName}"));

builder.AddProject<Projects.Web>("web")
    .WithReference(project)
    .WithReference(toolbox);
```

This is less richly typed but honest: it emits no Azure provisioning resources
and performs no Toolbox reconciliation. The connection names match the future
typed resources, so migration changes only AppHost declarations.

Managed Toolbox reconciliation is opt-in configuration and is never selected
implicitly. The explicit managed mode creates a Foundry account and project,
deploys its own GPT-4.1-mini chat model, and reconciles the project-parented
Toolbox. It does not reuse the existing-mode deployment-name parameter.

## Responsibility matrix

| Concern | Host-neutral client | Aspire client | AppHost integration | Application |
|---|---:|---:|---:|---:|
| Validate/build Toolbox URI | Yes | No | Emits properties | No |
| Entra bearer and preview header | Yes | Supplies credential | No | No |
| MCP initialize/list/call | Delegates to MCP SDK | No | No | No |
| Preserve `require_approval` | Yes | No | Declares policy | Enforces UX |
| Translate exact consent contract | Yes | No | No | Pauses/resumes UX |
| Retry connection/discovery | Yes | Configures policy | No | No |
| Retry `tools/call` | Never | Never | No | Decides after failure |
| Keyed DI, startup, health, telemetry | No | Yes | No | Configures |
| Declare tools and versions | No | No | Yes | Selects desired tools |
| Reconcile ownership and promotion | No | No | Yes | No |
| Attach Toolbox functions to `ChatOptions` | Optional middleware | No | No | Supplies application tools |
| Hosted Responses/request markers | No | No | No | Agent Framework adapter |

## Runtime contract

`FoundryToolbox.Client` builds on the MCP C# SDK instead of reimplementing it.
One client instance represents one explicit credential and security partition.

The client owns:

- Consumer endpoint:
  `{project}/toolboxes/{name}/mcp?api-version=v1`.
- Pinned endpoint:
  `{project}/toolboxes/{name}/versions/{version}/mcp?api-version=v1`.
- Token scope `https://ai.azure.com/.default`.
- Header `Foundry-Features: Toolboxes=V1Preview`.
- MCP connection, discovery, reconnect, and disposal.
- A bounded timeout, cancellation, and a cap on late discovery cleanup.
- Invocation leases that drain before connection replacement.
- Strict structured `require_approval` parsing.
- Approval-preserving `AIFunction` exposure.
- Exact Foundry MCP consent-error translation.

It does not own hosted services, health-check registration, Aspire, Agent
Framework, UI, application function creation, or function invocation. Its
optional MEAI middleware only attaches discovered functions to a cloned
`ChatOptions`; the existing `FunctionInvokingChatClient` remains responsible
for execution.

Authentication adds only the bearer token, required feature header, and W3C
trace context. Baggage is not forwarded by default.

Fresh connection and discovery attempts may retry before tools are exposed.
`tools/call` is never automatically replayed. A transport failure after a call
is dispatched has an indeterminate outcome. Discovery timeout and caller
cancellation cancel the attempt but retain an observer so a late MCP session can
still be disposed. Uncooperative late attempts are capped at the configured
discovery-attempt budget, and shutdown cancels every remaining attempt before
the shared HTTP transport closes.

Each projected function leases its source connection for one invocation. A
refresh publishes the replacement, retires the old connection to new calls, and
lets already-dispatched calls drain before disposing that MCP session.

Unknown or malformed approval metadata fails closed. The raw MCP tools are not
available through the public facade, so a required approval cannot be erased by
an accidental cast or alternate public API.

## Aspire PR #17742 reconciliation

Draft PR [microsoft/aspire#17742](https://github.com/microsoft/aspire/pull/17742)
provides the correct project-parented control-plane direction but not runtime
consumption.

| Area | PR #17742 | This proving repository |
|---|---|---|
| `project.AddToolbox(...)` | Yes | Converge on it |
| Web Search, MCP, Search definitions | Yes | Preserve compatible definitions |
| Run/publish deployment steps | Yes | Add native existing-mode suppression |
| Project data-plane readiness retry | Yes | Preserve the same narrow 404 predicate and bounded retry |
| Connection properties | Yes | Preserve direct URI and identity properties |
| Idempotent matching-version reuse | No | Configuration hash |
| Ownership protection | No | Explicit managed-by metadata |
| Later-version default promotion | No | Guarded promotion |
| Runtime MCP client | No | Separate `FoundryToolbox.Client` |
| Approval and consent runtime | No | Separate `FoundryToolbox.Client` |

Every automatic mutation requires explicit Aspire ownership. Existing or
unowned Toolboxes fail closed.

## Source-to-upstream lift and shift

| Local project | Proposed destination | Move together |
|---|---|---|
| `FoundryToolbox.Client` | CommunityToolkit incubation candidate, Azure SDK, or official MEAI package after review | Source and dedicated tests |
| `Aspire.Foundry` | `microsoft/aspire`, subject to package/API naming review | Source and dedicated tests |
| `Aspire.Hosting.Foundry.Toolbox` | Merge into existing `Aspire.Hosting.Foundry` and reconcile with #17742 | Source and dedicated tests |

After an owner accepts a layer:

1. Move its source and tests to the owning repository.
2. Publish it from that repository.
3. Replace this sample's `ProjectReference` with the package reference.
4. Remove the local proving project.

Application connection names and registration flow stay stable. The
host-neutral layer may require one mechanical namespace/type rename after
ownership is selected.

## Host-neutral feature proposal draft

**Candidate title:** Add a Foundry Toolbox client for direct
Microsoft.Extensions.AI applications

**Problem:** Foundry Toolbox exposes a project-scoped MCP endpoint, but a
direct .NET application currently has to reproduce its endpoint, Entra,
preview-header, approval, consent, retry, and lifetime rules or depend on
Agent Framework Hosting. These rules are service behavior above generic MCP
and are useful without Aspire or a hosted agent.

**Proposed component:** Incubate the source and tests currently named
`FoundryToolbox.Client`. `CommunityToolkit.AI.FoundryToolbox` is only a
candidate package ID for discussion. The compiled prototype intentionally
does not use a CommunityToolkit namespace.

**Initial public contract:**

- `FoundryToolboxClientOptions` for a consumer or pinned-version endpoint,
  discovery-only retry settings, bounded discovery, and bounded cleanup.
- Concrete `FoundryToolboxClient` for safe discovery, explicit refresh,
  readiness, and disposal.
- `FoundryToolboxChatClient` and `UseFoundryToolbox` for optional MEAI request
  augmentation without reimplementing function invocation, including an
  application policy that can require approval for every remote function.
- `FoundryToolboxEndpoint` for canonical endpoint construction and parsing.
- `FoundryToolboxConsentRequiredException` for the exact Foundry `-32006`
  consent contract.
- Approval-preserving `AIFunction` results, with no public raw-MCP bypass.

**Dependencies:** `Azure.Core`, `Microsoft.Extensions.AI`,
`ModelContextProtocol`, and logging abstractions. The full MEAI package is used
only for the standard `ChatClientBuilder` extension; the runtime remains free
of Aspire, hosting, health-check, UI, and Agent Framework dependencies.

**Evidence:** Deterministic MCP-over-HTTP tests cover endpoint validation,
headers, trace propagation, approval, consent, safe discovery retry,
discovery timeout, at-most-once invocation, credential partitions,
concurrency, atomic replacement/readiness, late-session cleanup, and shared
disposal completion. Live validation is read-only and opt-in.

**Acceptance questions for maintainers:**

1. Is AI Community Toolkit willing to incubate its first provider outside the
   current VectorData family?
2. Is a Foundry-specific MCP client in scope, or should its long-term owner be
   the Azure SDK or a Microsoft.Extensions.AI repository?
3. Is depending directly on `ModelContextProtocol` and `Azure.Core`
   acceptable?
4. Should the public surface return approval-decorated `AIFunction` instances,
   or expose a lower-level policy record as well?
5. Which preview and breaking-change policy should apply while the service
   requires `Foundry-Features: Toolboxes=V1Preview`?
6. What graduation signal would move the package to an official Microsoft or
   Azure owner?

If accepted for incubation, publish only prerelease versions while the
Toolbox service contract is preview. A future official graduation should keep
the behavioral tests and Aspire registration stable, even if the namespace
and package ID change.

## Aspire contribution map

The client contribution is one proposed package with an intentional internal
seam:

| Local folder | Upstream responsibility |
|---|---|
| `Aspire.Foundry/ProjectClient` | Configure and register `AIProjectClient`, keyed DI, credentials, options, and Azure SDK telemetry |
| `Aspire.Foundry/Toolbox` | Configure the leaf connection, standalone and chained registration, eager startup, health, and runtime telemetry |
| `Aspire.Foundry.Tests` | Configuration, DI, identity, relationship, startup, health, telemetry, and dependency conformance |

The proposed package name is `Aspire.Foundry`. The SDK-aligned alternative is
`Aspire.Azure.AI.Projects`. If Aspire reviewers do not want every project
client user to receive MCP dependencies, move the `Toolbox` folder and its
tests to a companion package. The application-facing
`AddFoundryProjectClient(...).AddToolboxClient(...)` shape can remain the same.

The AppHost contribution targets the existing `Aspire.Hosting.Foundry`
package. `Aspire.Hosting.Foundry.Toolbox` is only a local staging project for
reconciling the source and tests with PR #17742.

## Native existing-resource correction

Toolbox needs parameterless `RunAsExisting`, `PublishAsExisting`, and
`AsExisting` methods that suppress its data-plane reconciliation steps in the
selected execution modes. This is independent of the Azure resource APIs
because a Toolbox version is a project data-plane object, not an ARM
resource.

The typed parent hierarchy also needs one upstream correction before this
sample can use it safely:

1. Determine the project's existing state for the current execution mode
   before adding implicit children.
2. Do not emit an Application Insights connection or its backing resource for
   an existing project unless the user explicitly requests one.
3. Do not emit project role assignments in an existing mode unless explicitly
   requested.
4. Represent a referenced existing project connection without creating a new
   Foundry connection.
5. Add a manifest test proving `AddProject(...).AsExisting(...)` contributes no
   mutable child resources.

`RunAsExisting` must apply those suppressions only in run mode,
`PublishAsExisting` only in publish mode, and `AsExisting` in both.

## Agent Framework extraction contract

Agent Framework Hosting should consume the same host-neutral behavior rather
than keep a second Foundry-over-MCP implementation.

| Move to host-neutral client | Keep in Agent Framework Hosting |
|---|---|
| Toolbox endpoint construction and validation | Hosted Responses endpoint and protocol |
| Foundry token scope and preview header | Hosted request and call-context resolution |
| MCP open, initialize, list, call, and disposal | Per-request credential selection and partition key |
| Safe discovery retry and no call replay | Responses-native approval and consent events |
| Structured approval metadata | Agent lifecycle and readiness aggregation |
| Exact consent parsing and typed exception | Mapping client events into hosted response items |

Conformance requires both adapters to discover the same tools, preserve every
`require_approval` decision, accept only the same consent contract, avoid
automatic `tools/call` replay, isolate delegated identities, and emit the same
safe telemetry fields. This repository does not add an Agent Framework
reference or claim that extraction has already happened.

## Graduation options

The runtime ownership decision remains open:

| Destination | Best fit | Constraint |
|---|---|---|
| AI Community Toolkit | Fast incubation with MEAI provider conventions | First non-VectorData component; maintainer scope approval needed |
| Azure SDK | Official Foundry service client ownership | Public service contract and Azure SDK design review needed |
| Microsoft.Extensions.AI | Direct AI function/tool integration | Product-specific auth and endpoint policy may be too service-specific |
| Agent Framework Hosting only | Hosted-agent scenarios | Rejects direct MEAI and Aspire reuse |

The code keeps this decision reversible: one host-neutral source project, one
dedicated test project, and no application dependency on a candidate package
name.

## Rejected alternatives

- **Put live MCP sessions in `Aspire.Hosting.Foundry`:** AppHost must not join
  user requests or own application credentials and connection lifetime.
- **Keep the connector in Agent Framework Hosting:** direct MEAI and Aspire
  consumers should not take hosted-agent dependencies.
- **Put Foundry behavior in the MCP SDK:** endpoint, auth, approval, and consent
  are service semantics, not generic MCP.
- **Create one monolithic Foundry runtime:** Foundry IQ, models, projects,
  Toolboxes, and hosted agents do not share one protocol or lifetime.
- **Use `AddExisting*` or `ReuseExisting*`:** native Aspire vocabulary is
  `RunAsExisting`, `PublishAsExisting`, and `AsExisting`.
- **Use `Aspire.Foundry.Client`:** the generic `.Client` suffix is not native
  Aspire package vocabulary.

## Validation

The proposal is proven at four seams:

1. Deterministic MCP fixture tests cover initialize, discovery, invocation,
   metadata, consent, delays, disconnects, bounded discovery, concurrency,
   readiness recovery, and disposal.
2. Aspire client tests cover configuration precedence, keyed services,
   standalone and chained Toolbox registration, identity isolation, health,
   startup, and telemetry.
3. AppHost model tests cover managed reconciliation, ownership rejection,
   promotion, connection handoff, and all existing-mode combinations.
4. A live read-only smoke test uses the existing promoted Toolbox for discovery,
   approved Microsoft Learn use, and cited Web Search.

The current suite has 103 passing tests: 61 host-neutral runtime tests, 13
Aspire client tests, 24 AppHost control-plane tests, and 5 application tests.
The generated default AppHost manifest contains only parameters, two
`value.v0` connection resources, MarkItDown, and Web. It contains no Azure
provisioning resource or Toolbox reconciliation step.

Automated validation never provisions infrastructure, creates a Toolbox
version, changes a default version, or leaves a sample process running.

## Contribution sequence

1. Propose the host-neutral runtime contract and ownership questions to
   CommunityToolkit/AI without claiming its namespace.
2. Reconcile the AppHost work with #17742, including existing modes,
   ownership, idempotency, and promotion.
3. Propose the `Aspire.Foundry` client integration, with
   `Aspire.Azure.AI.Projects` retained as the SDK-aligned naming alternative.
4. Adapt Agent Framework Hosting to the accepted host-neutral runtime in its
   own repository.
5. Graduate the runtime to an Azure SDK or official MEAI package if Microsoft
   establishes that owner.

No issue, pull request, or package publication happens without explicit user
approval.
