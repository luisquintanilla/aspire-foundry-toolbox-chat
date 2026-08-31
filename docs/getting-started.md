# Getting started

This guide runs the sample against Azure resources that already exist. That is
the default and safest mode.

## Choose an environment

### Dev container or GitHub Codespaces

The repository includes `.devcontainer/devcontainer.json`, based on the
official Aspire dev-container shape:

- .NET 10 on Ubuntu Noble.
- Aspire CLI and VS Code extension.
- Docker-in-Docker for the MarkItDown MCP container.
- Azure CLI.
- C# Dev Kit and container tooling.

The requested host profile is 8 CPUs, 32 GB of memory, and 64 GB of storage.
A smaller machine may work but will restore and build more slowly.

Open the repository in a dev container, or create a Codespace from the
repository. `dotnet restore` runs when the container is created. HTTPS
development certificates are prepared each time it starts.

The Docker-in-Docker feature runs the dev container as privileged. This is
normal in Codespaces, but review that choice before using the configuration on
another host.

### Local machine

Install:

1. The .NET SDK selected by `global.json`.
2. Docker Desktop, Podman with Docker compatibility, or another Docker daemon
   Aspire can use.
3. Azure CLI.
4. An editor such as Visual Studio Code with C# Dev Kit and the Aspire
   extension.

On Windows, a WSL checkout is supported. When Windows launches the project from
a WSL UNC path, the sample enables polling file watching and stores the SQLite
vector database in Windows local application data to avoid UNC locking issues.

## Prepare Azure resources

Existing mode requires:

| Resource | Purpose |
|---|---|
| Microsoft Foundry account and project | Provides the project endpoint and chat model |
| Chat model deployment | Handles the MEAI conversation and tool selection |
| Foundry Toolbox | Exposes remote functions through its MCP endpoint |
| Azure OpenAI embedding deployment | Embeds document chunks and search queries |

To reproduce the full demo, configure the existing Toolbox with:

- Web Search.
- An MCP source named for Microsoft Learn that points to
  `https://learn.microsoft.com/api/mcp`.

The signed-in identity must be authorized to invoke the model, embedding
deployment, and Toolbox in your tenant. Use your organization's normal
least-privilege role-assignment process. The sample does not create role
assignments in existing mode.

## Authenticate

Use Azure CLI authentication:

```bash
az login --use-device-code
az account show
```

If your identity can access multiple subscriptions, select the intended one:

```bash
az account set --subscription "<subscription-id>"
```

Do not add credentials to `appsettings.json`, `.env` files, the dev-container
configuration, Codespaces secrets, or source control. The application uses
`DefaultAzureCredential`, which can consume the Azure CLI token cache during
development.

## Configure existing mode

Run the helper from Bash:

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

If the embedding account is in the same resource group as Foundry, omit
`--embedding-resource-group`.

Every option also has an environment-variable form. See:

```bash
./scripts/configure-existing-azure.sh --help
```

The script performs read-only Azure CLI lookups and writes these non-secret
values to AppHost .NET user secrets:

- Foundry project endpoint.
- Toolbox name.
- Chat deployment name.
- Embedding endpoint.
- Embedding deployment name.

It does not store a key, token, password, or connection secret. User secrets
are a development configuration store, not a production secret manager.

### Manual configuration

If Bash is unavailable, set the same AppHost parameters manually:

```bash
dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Parameters:foundry-project-endpoint" \
  "https://<account>.services.ai.azure.com/api/projects/<project>"

dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Parameters:toolbox-name" "<toolbox-name>"

dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Parameters:chat-deployment-name" "<chat-deployment>"

dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Parameters:embedding-endpoint" \
  "https://<embedding-account>.openai.azure.com/"

dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Parameters:embedding-deployment-name" "<embedding-deployment>"
```

PowerShell users can put each command on one line instead of using Bash line
continuations.

## Run

```bash
dotnet run --project AspireFoundryToolboxChat.AppHost
```

Open the Aspire dashboard URL printed in the terminal. Wait for:

- `markitdown` to be running.
- `web` to become healthy.
- `research-toolbox` health to report ready after discovery.

Then open the Web endpoint from the dashboard.

Aspire selects dynamic ports. Codespaces recognizes and forwards the localhost
URLs printed by Aspire, so `forwardPorts` is intentionally absent from the dev
container. Keep all forwarded ports private because this sample has no
application-level user authentication.

Try:

- "What safety advice is in the sample document?"
- "Find the current Microsoft Learn guidance for Aspire service defaults."
- "Compare the local guidance with current public sources."

The first local-document question can trigger ingestion. The first remote MCP
use can require provider OAuth consent. Every remote Toolbox call then pauses
for a UI approval that shows the function and argument values.

## Managed mode

Managed mode is for exercising the AppHost control-plane prototype, not for
normal sample use. It can create or change an Azure Foundry account, project,
model deployment, Toolbox, and Toolbox versions. Cloud costs and tenant policy
apply.

Two settings are required so the mode cannot be enabled accidentally:

```bash
dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Foundry:Toolbox:Mode" "managed"

dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Foundry:Toolbox:AllowResourceChanges" "true"
```

The existing embedding endpoint is still used. Aspire will request any
additional deployment inputs it needs. Read
[Resource modes](architecture.md#resource-modes) and the ownership rules in
[Productionization](toolbox-productionization.md) before running this mode.

Return to the safe mode with:

```bash
dotnet user-secrets --project AspireFoundryToolboxChat.AppHost set \
  "Foundry:Toolbox:Mode" "existing"
```

## Sensitive telemetry

Prompt text, tool arguments, function results, and embedding inputs are omitted
from AI telemetry by default. For a controlled local diagnostic session only,
set:

```bash
dotnet user-secrets --project AspireFoundryToolboxChat.Web set \
  "Telemetry:EnableSensitiveData" "true"
```

Do not enable this with private documents or with an exporter you do not fully
trust.

## Troubleshooting

### Configuration is missing

Run the configuration script again and check that every Azure CLI lookup
succeeds. AppHost parameters are stored under the AppHost project's user-secret
ID, not the Web project's ID.

### `DefaultAzureCredential` cannot authenticate

Run `az account show`, confirm the active tenant and subscription, and sign in
again if needed. In Codespaces, use device-code login. Do not work around the
problem by adding a client secret to the repository.

### Toolbox discovery is unhealthy

Confirm the Toolbox name and project endpoint, the identity's data-plane
permissions, and whether the provider requires OAuth consent. The endpoint is
constructed as:

```text
{project-endpoint}/toolboxes/{toolbox-name}/mcp?api-version=v1
```

The runtime adds the required `Foundry-Features: Toolboxes=V1Preview` header.

### MarkItDown does not start

Confirm the Docker daemon is running and can pull the pinned `mcp/markitdown`
image. In a dev container, check the Docker-in-Docker service rather than the
host Docker socket.

### A URL is not reachable in Codespaces

Open the URL emitted by Aspire or the link in the Aspire dashboard. Do not
assume a fixed port. In the Codespaces **Ports** panel, confirm the forwarded
port is private.

### SQLite reports locking errors on Windows

Stop duplicate Web processes. The sample redirects its vector database away
from a WSL UNC build output, but two running instances can still contend for
the same local database.

### The model returns quota or rate-limit errors

Wait for the deployment limit to reset or choose a deployment with sufficient
capacity. Automated tests do not invoke live models.

## Stop and clean up

Use `Ctrl+C` in the AppHost terminal. Aspire stops the Web process and local
MarkItDown container.

Existing mode has no Azure resources to clean up. Managed resources are not
automatically deleted; review them in Azure and follow your organization's
normal cleanup process.
