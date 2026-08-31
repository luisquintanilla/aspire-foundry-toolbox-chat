# Security and privacy

This sample crosses several trust boundaries. Review them before replacing the
included example files with private or regulated data.

## Data-flow summary

| Data | Destination | Default behavior |
|---|---|---|
| User messages and conversation history | Configured Azure chat model | Sent for every model turn |
| Document files | Local MarkItDown container | Parsed locally |
| Document chunks and search queries | Configured Azure embedding deployment | Sent to create embeddings |
| Chunks and vectors | Local SQLite database | Stored on the developer machine |
| Selected document excerpts | Configured Azure chat model | Returned as local-tool results |
| Approved remote-tool arguments | Foundry Toolbox and its downstream source | Sent only after UI approval |
| Remote-tool results | Azure chat model | Returned for answer synthesis |
| AI content in telemetry | Configured OpenTelemetry destination | Disabled by default |

"Local document search" means parsing, storage, and vector search are local. It
does not mean all document content remains local.

## Credentials

The application uses `DefaultAzureCredential`. During development it normally
uses the Azure CLI token cache. The repository does not require or contain API
keys, client secrets, access tokens, SAS tokens, or passwords.

The configuration helper writes endpoints and resource names to .NET user
secrets. Those values are not authentication secrets, but they can still reveal
Azure inventory and should not be committed or pasted into public logs.

For deployed environments, use a managed identity or another approved
workload-identity mechanism. Do not transplant the development user-secret
workflow into production.

## Remote-tool approval

The runtime preserves approval requirements returned by Foundry. The sample
adds a stricter policy at the MEAI middleware boundary: every remote Toolbox
function requires approval.

Before a call, the UI shows:

- The `research-toolbox` destination.
- The remote function name.
- The complete function arguments.

Denying the request prevents that call. Review arguments carefully for local
document excerpts, credentials, personal data, or unrelated context.

This control depends on informed human review. It does not classify data,
guarantee that a person notices sensitive text, or enforce organizational DLP.

## Prompt injection

Documents, web pages, and MCP tool results are untrusted input. They can contain
instructions designed to influence the model or trigger another tool call.

Current mitigations:

- Local and remote functions remain distinguishable.
- Multiple tool calls are disabled for one model response.
- All remote Toolbox calls require approval.
- The approval UI exposes arguments instead of redacting them.
- The system prompt tells the model not to send local content to remote tools.
- Tool name collisions fail instead of silently choosing one implementation.

Residual risk:

- Prompt instructions are not a hard security boundary.
- A malicious document can still influence a later proposed call.
- An approved remote source can return misleading or malicious content.
- The model can make mistakes when attributing or summarizing evidence.

Use only synthetic or non-sensitive data unless you add deterministic
information-flow controls appropriate to your environment.

## Telemetry

Standard traces, metrics, and health data are enabled through Service Defaults.
Sensitive AI content is disabled by default with:

```json
{
  "Telemetry": {
    "EnableSensitiveData": false
  }
}
```

Enabling it can include prompts, tool arguments, tool results, document
excerpts, and embedding inputs in exported telemetry. Enable it only for a
controlled diagnostic session and only with a trusted exporter.

The host-neutral Toolbox runtime emits operation metadata but avoids
authorization values, consent URLs, arguments, results, and baggage by default.

## Local storage

The SQLite vector store contains document text and embeddings. It is excluded
from Git, but it is not encrypted by this application.

- On Linux and in the dev container, it is under the application data path.
- When a Windows process runs from a WSL UNC checkout, it is redirected to the
  user's Windows local application-data directory.

Delete local stores through normal filesystem controls when the data is no
longer needed. Protect the developer account and disk according to the
sensitivity of the documents.

## MarkItDown container

The sample parses files with the official `mcp/markitdown` container pinned to
an immutable multi-platform digest. Pinning prevents an unnoticed change behind
the `latest` tag; it does not replace vulnerability scanning.

The dev container runs Docker-in-Docker in privileged mode. Codespaces isolates
that environment, but other hosts should evaluate the privilege boundary.
Its base image is pinned to an immutable multi-platform digest. Dev-container
features still use reviewed major-version tags and should be updated through
Dependabot.

Treat all parsed files as untrusted. File parser vulnerabilities and resource
exhaustion remain possible. Production systems should add file-size, type,
malware-scanning, and tenant-isolation controls.

## Azure and provider behavior

Azure OpenAI, Microsoft Foundry, Web Search, and remote MCP sources can have
their own logging, retention, geographic, authorization, and abuse-monitoring
policies. Review the current service documentation and your tenant settings.

OAuth consent links are opened in a separate browser tab. Confirm the host and
tenant before granting consent.

Toolbox is currently accessed through a preview contract and preview feature
header. Preview APIs can change and may not carry production support guarantees.

## Managed mode

Existing mode is the default and performs no Azure deployment or Toolbox
reconciliation.

Managed mode can create or change Azure resources and incur cost. It requires a
separate acknowledgement setting. The reconciliation layer refuses to modify
an existing Toolbox that does not carry its Aspire ownership metadata, but that
guard is not a substitute for Azure policy, least privilege, budgets, or change
review.

## Public repository hygiene

- Keep `.env*`, local appsettings files, user secrets, certificates, databases,
  manifests, build output, and test results out of source control.
- Do not paste Azure inventory or diagnostic output into issues.
- Enable GitHub secret scanning and push protection.
- Publish this sample from a fresh sanitized root commit, not the development
  repository's existing history.
- Review PDF and image metadata before adding sample assets.

See [Publishing](publishing.md) for the complete checklist.

## Production gaps

Before treating this architecture as production-ready, add or confirm:

- Application authentication and authorization.
- Tenant and identity partitioning for every runtime client.
- Deterministic data-classification and egress policy.
- Input limits, malware scanning, and parser isolation.
- Persistent-store encryption and lifecycle management.
- Network isolation and private endpoints where required.
- Azure Policy, budgets, quotas, and deployment review.
- Service-specific retention and compliance requirements.
- Operational alerts, runbooks, and incident response.
- Dependency and container vulnerability scanning.
- Load, fault, and recovery testing with representative limits.

This repository proves integration seams and failure semantics. It does not
claim to provide those deployment controls.
