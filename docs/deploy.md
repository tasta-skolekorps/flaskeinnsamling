# Deploying the API to Azure

The API (the Aspire solution in `src/`) is deployed automatically to **Azure Container Apps** in the **Norway East** region (`norwayeast`) by the GitHub Actions workflow [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml). The static pages remain on GitHub Pages and are not affected.

## When does the deployment run?

- On every push to `main` that changes `src/**`, `data/**`, the solution file (`*.sln`/`*.slnx`), `global.json`, `aspire.config.json` or `deploy.yml` itself.
- Manually from **Actions → Deploy API (Azure) → Run workflow**.
- Only one deployment runs at a time (`concurrency: deploy-production`). A new deployment waits until the previous one has finished.

`main` is protected by a ruleset: changes reach it only through a pull request (squash merge). Merging a PR is the push to `main`, so **deploy runs when a PR is merged to `main`**. Every PR to `main` runs the workflow [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) ("CI"), whose job `build-and-test` (actionlint, `dotnet build` and `dotnet test` of `Flaskeinnsamling.slnx`, including the Aspire integration tests against Azurite in Docker) is a **required status check**. A PR cannot be merged until it passes.

When `data/<season>.json` is changed and merged to `main`, the API re-imports that season on startup, and the file in the repo wins. Changes an admin makes through the API last until the file is changed the next time.

The workflow signs in to Azure with **OIDC (federated credentials)**. It uses no long-lived Azure passwords or keys. It then runs `aspire deploy --non-interactive`, which builds the API, creates the Azure resources and publishes a new revision.

## What Aspire creates in Azure

Everything goes into one resource group (default `rg-flaskeinnsamling`):

- Container Apps environment with a Log Analytics workspace (no Aspire dashboard in Azure, to keep costs down; use Log Analytics for logs)
- Container App `api` with a public HTTPS address (0–1 replicas, scales to zero)
- Azure Container Registry (Basic) for the container images
- Two user-assigned managed identities: one for pulling images from the registry and one for the API's access to the storage account
- Storage account (Standard_LRS) with Table Storage (`tables`)
- **Role assignments:** AcrPull on the registry, and Storage Table Data Contributor on the storage account for the API's identity

Because Aspire creates role assignments, the *Contributor* role is not enough for the deploying identity. It also needs a role that can assign roles (see step 4).

## One-time setup

You run the commands yourself in PowerShell with `az` (Azure CLI) and `gh` (GitHub CLI). Replace everything in `<…>` with your own values. Do **not** paste IDs, keys or passwords into the repo, issues or chat.

You need:

- *Owner* (or *Contributor* + *User Access Administrator*) on the subscription, so you can create the resource group and role assignments.
- Permission to create app registrations in Microsoft Entra ID (the default for regular users in most tenants). If you don't have it, use the managed identity alternative in step 3b.
- Admin access to the GitHub repo `tasta-skolekorps/flaskeinnsamling`.

### 1. Sign in and select the subscription

```powershell
az login
$SUBSCRIPTION_ID = "<subscription-id>"
az account set --subscription $SUBSCRIPTION_ID
$TENANT_ID = az account show --query tenantId -o tsv
$RG = "rg-flaskeinnsamling"
$LOCATION = "norwayeast"
$REPO = "tasta-skolekorps/flaskeinnsamling"
```

### 2. Register resource providers and create the resource group

The identity only gets permissions on the resource group, so the providers must be registered once in advance:

```powershell
foreach ($ns in "Microsoft.App", "Microsoft.ContainerRegistry", "Microsoft.OperationalInsights", "Microsoft.ManagedIdentity", "Microsoft.Storage") {
  az provider register --namespace $ns
}
az group create --name $RG --location $LOCATION
```

The workflow sets `Azure__AllowResourceGroupCreation=false`, so the resource group **must** exist before the first deployment.

### 3a. Create an app registration with a federated credential (recommended)

```powershell
$APP_ID = az ad app create --display-name "github-flaskeinnsamling-deploy" --query appId -o tsv
$SP_ID = az ad sp create --id $APP_ID --query id -o tsv

@{
  name      = "github-flaskeinnsamling-production"
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = "repo:tasta-skolekorps@334118641/flaskeinnsamling@1388875818:environment:production"
  audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json | Set-Content -Encoding utf8 fic.json
az ad app federated-credential create --id $APP_ID --parameters fic.json
Remove-Item fic.json
```

`subject` must exactly match the repo and the GitHub environment `production`. The federated credential only applies to jobs running in that environment. The repo uses GitHub's immutable subject format (`owner@<owner-id>/repo@<repo-id>`), so the plain `repo:tasta-skolekorps/flaskeinnsamling:...` form is rejected with `AADSTS700213`. Check the current prefix with `gh api repos/$REPO/actions/oidc/customization/sub` (`sub_claim_prefix`).

### 3b. Alternative: user-assigned managed identity

Use this if you cannot create app registrations (the current setup uses this option). Put the identity in the **separate** resource group `github-action-identities`, which holds identities for GitHub Actions, so it is not deleted if you tear down `rg-flaskeinnsamling` with `aspire destroy`.

```powershell
az group create --name "github-action-identities" --location $LOCATION
$APP_ID = az identity create --name "id-github-flaskeinnsamling" --resource-group "github-action-identities" --query clientId -o tsv
$SP_ID = az identity show --name "id-github-flaskeinnsamling" --resource-group "github-action-identities" --query principalId -o tsv
az identity federated-credential create `
  --name "github-flaskeinnsamling-production" `
  --identity-name "id-github-flaskeinnsamling" `
  --resource-group "github-action-identities" `
  --issuer "https://token.actions.githubusercontent.com" `
  --subject "repo:tasta-skolekorps@334118641/flaskeinnsamling@1388875818:environment:production" `
  --audiences "api://AzureADTokenExchange"
```

### 4. Grant the identity roles on the resource group

```powershell
$SCOPE = "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG"
az role assignment create --assignee-object-id $SP_ID --assignee-principal-type ServicePrincipal --role "Contributor" --scope $SCOPE
az role assignment create --assignee-object-id $SP_ID --assignee-principal-type ServicePrincipal --role "Role Based Access Control Administrator" --scope $SCOPE
```

- **Contributor** lets Aspire create and update the resources.
- **Role Based Access Control Administrator** lets Aspire create the role assignments for the managed identities (AcrPull and Storage Table Data Contributor). *User Access Administrator* also works, but grants more than needed.
- The roles apply **only** to the resource group, not the whole subscription.

Role assignments can take a few minutes to propagate. If the first deployment fails with `AuthorizationFailed`, wait a bit and run it again.

### 5. Create the GitHub environment `production`

```powershell
gh api --method PUT "repos/$REPO/environments/production"
```

Recommended: allow deployments from `main` only.

```powershell
gh api --method PUT "repos/$REPO/environments/production" -F "deployment_branch_policy[protected_branches]=false" -F "deployment_branch_policy[custom_branch_policies]=true"
gh api --method POST "repos/$REPO/environments/production/deployment-branch-policies" -f name=main
```

To approve each deployment manually, add yourself as a *Required reviewer* under **Settings → Environments → production**.

### 6. Add variables and the secret in GitHub

Variables (not secret, but keep them out of the code):

```powershell
gh variable set AZURE_CLIENT_ID --repo $REPO --body $APP_ID
gh variable set AZURE_TENANT_ID --repo $REPO --body $TENANT_ID
gh variable set AZURE_SUBSCRIPTION_ID --repo $REPO --body $SUBSCRIPTION_ID
gh variable set AZURE_RESOURCE_GROUP --repo $REPO --body $RG   # optional, defaults to rg-flaskeinnsamling
```

Secret for the admin API (`admin-api-key` in the AppHost). Generate a strong key, store it in the flaskekomité's password manager and register it as an environment secret. `gh` prompts for the value, so it does not end up in your command history:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) | Set-Clipboard
# Paste the key into the password manager first, then:
gh secret set ADMIN_API_KEY --repo $REPO --env production
# Paste the key when prompted, then clear the clipboard:
Set-Clipboard -Value " "
```

| Name | Type | Contents |
|---|---|---|
| `AZURE_CLIENT_ID` | variable | Client ID of the app registration/identity |
| `AZURE_TENANT_ID` | variable | Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | variable | Subscription ID |
| `AZURE_RESOURCE_GROUP` | variable (optional) | Resource group, default `rg-flaskeinnsamling` |
| `ADMIN_API_KEY` | secret (environment `production`) | Admin key for the write API |

The variables can also be set on the `production` environment (`--env production`) instead of on the repo. The workflow reads `vars.*` from both places.

## First deployment

1. Go to **Actions → Deploy API (Azure) → Run workflow** on `main`, or merge a pull request that changes `src/`.
2. The first run typically takes 5–15 minutes (the registry, environment and storage account are created).
3. The workflow stops early with a clear error message if a variable or `ADMIN_API_KEY` is missing.

## Find the API address

After a successful run, you can find the address here:

- The run's **job summary** (Summary) shows "API URL".
- **Deployments / Environments → production** in the repo shows the same URL.
- With Azure CLI:

  ```powershell
  az resource show --resource-group rg-flaskeinnsamling --resource-type Microsoft.App/containerApps --name api --query properties.configuration.ingress.fqdn -o tsv
  ```

The address has the form `https://api.<something>.norwayeast.azurecontainerapps.io`. It does not change on later deployments as long as the resource group and environment are kept.

**Then point the frontend at the API:** set `API_BASE` in [`api.js`](../api.js) to the address (without a trailing `/`), then open a pull request; deploy runs when it is merged to `main`. Until that is done, the pages use the embedded data. CORS is already configured: the AppHost parameter `cors-origins` defaults to `https://tasta-skolekorps.github.io`.

## Costs

- **Azure Container Apps (Consumption):** the API scales to zero when idle, and the monthly free grant (vCPU-seconds, GiB-seconds and 2 million requests) normally covers a small band. Expect a cold start of a few seconds after a period without traffic.
- **Storage account (Table Storage):** a few kroner per month for such small amounts of data.
- **Azure Container Registry (Basic):** the largest fixed cost, about USD 5 per month.
- **Log Analytics:** pay per GB of ingested logs. Low volume means very low cost.

Consider setting up a budget alert on the resource group under **Cost Management → Budgets** in the Azure portal.

## Operations and maintenance

- **Rotate the admin key:** run `gh secret set ADMIN_API_KEY --repo tasta-skolekorps/flaskeinnsamling --env production` again, then start the workflow manually.
- **Troubleshooting:** the log from the "Deploy with Aspire" step shows each Aspire step. In Azure: `az containerapp logs show --resource-group rg-flaskeinnsamling --name api --tail 100` (requires `az extension add --name containerapp`).
- **Tear everything down:** run `aspire destroy` locally after `az login`. The command deletes the **entire** resource group. The app registration, or the identity in `github-action-identities`, is left in place.
- **Upgrade Aspire:** update `ASPIRE_VERSION` in `deploy.yml` together with the Aspire packages in `src/`.
