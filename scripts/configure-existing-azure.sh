#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
apphost_project="$root_dir/AspireFoundryToolboxChat.AppHost/AspireFoundryToolboxChat.AppHost.csproj"

usage() {
  cat <<'EOF'
Configure the sample to reference existing Azure resources.

Usage:
  ./scripts/configure-existing-azure.sh [options]

Required options (or matching environment variables):
  --subscription-id ID          AZURE_SUBSCRIPTION_ID
  --foundry-resource-group NAME FOUNDRY_RESOURCE_GROUP
  --foundry-account NAME        FOUNDRY_ACCOUNT_NAME
  --foundry-project NAME        FOUNDRY_PROJECT_NAME
  --toolbox NAME                FOUNDRY_TOOLBOX_NAME
  --chat-deployment NAME        CHAT_DEPLOYMENT_NAME
  --embedding-account NAME      EMBEDDING_ACCOUNT_NAME
  --embedding-deployment NAME   EMBEDDING_DEPLOYMENT_NAME

Optional:
  --embedding-resource-group NAME
                                EMBEDDING_RESOURCE_GROUP
                                Defaults to --foundry-resource-group.
  -h, --help                    Show this help.

The script stores non-secret endpoints and resource names in .NET user
secrets. Authentication remains with DefaultAzureCredential and Azure CLI.
EOF
}

subscription_id="${AZURE_SUBSCRIPTION_ID:-}"
foundry_resource_group="${FOUNDRY_RESOURCE_GROUP:-}"
foundry_account_name="${FOUNDRY_ACCOUNT_NAME:-}"
foundry_project_name="${FOUNDRY_PROJECT_NAME:-}"
toolbox_name="${FOUNDRY_TOOLBOX_NAME:-}"
chat_deployment_name="${CHAT_DEPLOYMENT_NAME:-}"
embedding_resource_group="${EMBEDDING_RESOURCE_GROUP:-}"
embedding_account_name="${EMBEDDING_ACCOUNT_NAME:-}"
embedding_deployment_name="${EMBEDDING_DEPLOYMENT_NAME:-}"

while (($# > 0)); do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --subscription-id|--foundry-resource-group|--foundry-account|--foundry-project|--toolbox|--chat-deployment|--embedding-resource-group|--embedding-account|--embedding-deployment)
      if (($# < 2)); then
        echo "Option $1 requires a value." >&2
        exit 2
      fi
      option="$1"
      value="$2"
      shift 2
      case "$option" in
        --subscription-id) subscription_id="$value" ;;
        --foundry-resource-group) foundry_resource_group="$value" ;;
        --foundry-account) foundry_account_name="$value" ;;
        --foundry-project) foundry_project_name="$value" ;;
        --toolbox) toolbox_name="$value" ;;
        --chat-deployment) chat_deployment_name="$value" ;;
        --embedding-resource-group) embedding_resource_group="$value" ;;
        --embedding-account) embedding_account_name="$value" ;;
        --embedding-deployment) embedding_deployment_name="$value" ;;
      esac
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

missing=()
[[ -n "$subscription_id" ]] || missing+=("--subscription-id")
[[ -n "$foundry_resource_group" ]] || missing+=("--foundry-resource-group")
[[ -n "$foundry_account_name" ]] || missing+=("--foundry-account")
[[ -n "$foundry_project_name" ]] || missing+=("--foundry-project")
[[ -n "$toolbox_name" ]] || missing+=("--toolbox")
[[ -n "$chat_deployment_name" ]] || missing+=("--chat-deployment")
[[ -n "$embedding_account_name" ]] || missing+=("--embedding-account")
[[ -n "$embedding_deployment_name" ]] || missing+=("--embedding-deployment")

if ((${#missing[@]} > 0)); then
  echo "Missing required options: ${missing[*]}" >&2
  usage >&2
  exit 2
fi

embedding_resource_group="${embedding_resource_group:-$foundry_resource_group}"

command -v az >/dev/null 2>&1 ||
  { echo "Azure CLI is required." >&2; exit 1; }

az account show --subscription "$subscription_id" --query id -o tsv >/dev/null

foundry_project_endpoint="$(
  az resource show \
    --ids "/subscriptions/$subscription_id/resourceGroups/$foundry_resource_group/providers/Microsoft.CognitiveServices/accounts/$foundry_account_name/projects/$foundry_project_name" \
    --api-version 2025-06-01 \
    --query 'properties.endpoints."AI Foundry API"' \
    -o tsv
)"

embedding_endpoint="$(
  az cognitiveservices account show \
    --subscription "$subscription_id" \
    --resource-group "$embedding_resource_group" \
    --name "$embedding_account_name" \
    --query properties.endpoint \
    -o tsv
)"

chat_deployment="$(
  az cognitiveservices account deployment show \
    --subscription "$subscription_id" \
    --resource-group "$foundry_resource_group" \
    --name "$foundry_account_name" \
    --deployment-name "$chat_deployment_name" \
    --query name \
    -o tsv
)"

embedding_deployment="$(
  az cognitiveservices account deployment show \
    --subscription "$subscription_id" \
    --resource-group "$embedding_resource_group" \
    --name "$embedding_account_name" \
    --deployment-name "$embedding_deployment_name" \
    --query name \
    -o tsv
)"

if [[ -z "$foundry_project_endpoint" || -z "$embedding_endpoint" || -z "$chat_deployment" ||
      -z "$embedding_deployment" ]]; then
  echo "One or more existing Azure resources could not be resolved." >&2
  exit 1
fi

if command -v dotnet >/dev/null 2>&1; then
  dotnet_command=(dotnet)
  secrets_project="$apphost_project"
elif command -v dotnet.exe >/dev/null 2>&1; then
  dotnet_command=(dotnet.exe)
  secrets_project="$(wslpath -w "$apphost_project")"
else
  echo "The .NET SDK was not found in WSL or Windows." >&2
  exit 1
fi

set_secret() {
  "${dotnet_command[@]}" user-secrets \
    --project "$secrets_project" \
    set "Parameters:$1" "$2" >/dev/null
}

set_secret "foundry-project-endpoint" "$foundry_project_endpoint"
set_secret "toolbox-name" "$toolbox_name"
set_secret "chat-deployment-name" "$chat_deployment_name"
set_secret "embedding-endpoint" "$embedding_endpoint"
set_secret "embedding-deployment-name" "$embedding_deployment_name"

echo "Configured AppHost user secrets from existing Azure resources."
echo "No credentials were stored and no Azure resources were changed."
