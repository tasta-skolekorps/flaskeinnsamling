# Utrulling av API-et til Azure

API-et (Aspire-løsningen i `src/`) rulles ut automatisk til **Azure Container Apps** i regionen **Norway East** (`norwayeast`) av GitHub Actions-arbeidsflyten [`.github/workflows/deploy.yml`](../.github/workflows/deploy.yml). De statiske sidene ligger fortsatt på GitHub Pages og påvirkes ikke.

## Når kjører utrullingen?

- Ved hver push til `main` som endrer `src/**`, `data/**`, løsningsfilen (`*.sln`/`*.slnx`), `global.json`, `aspire.config.json` eller selve `deploy.yml`.
- Manuelt fra **Actions → Deploy API (Azure) → Run workflow**.
- Bare én utrulling kjører om gangen (`concurrency: deploy-production`). En ny utrulling venter til den forrige er ferdig.

Når `data/<sesong>.json` endres og pushes til `main`, importerer API-et den sesongen på nytt ved oppstart, og filen i repoet vinner. Endringer som admin gjør gjennom API-et, gjelder til filen endres neste gang.

Arbeidsflyten logger inn i Azure med **OIDC (føderert legitimasjon)**. Den bruker ingen langlivede Azure-passord eller -nøkler. Deretter kjører den `aspire deploy --non-interactive`, som bygger API-et, lager Azure-ressursene og publiserer en ny versjon.

## Det Aspire lager i Azure

Alt havner i én ressursgruppe (standard `rg-flaskeinnsamling`):

- Container Apps-miljø med Log Analytics-arbeidsområde (uten Aspire-dashboard i Azure, for å holde kostnadene nede; bruk Log Analytics til logger)
- Container App `api` med offentlig HTTPS-adresse (0–1 replika, skalerer til null)
- Azure Container Registry (Basic) for containerbildene
- To brukertildelte administrerte identiteter (managed identity): én for å hente bilder fra registeret og én for API-ets tilgang til lagringskontoen
- Lagringskonto (Standard_LRS) med Table Storage (`tables`)
- **Rolletildelinger:** AcrPull på registeret, og Storage Table Data Contributor på lagringskontoen for API-ets identitet

Fordi Aspire oppretter rolletildelinger, er rollen *Contributor* ikke nok for identiteten som ruller ut. Den trenger i tillegg en rolle som kan tildele roller (se steg 4).

## Engangsoppsett

Du kjører kommandoene selv i PowerShell med `az` (Azure CLI) og `gh` (GitHub CLI). Bytt ut alt i `<…>` med dine egne verdier. **Ikke** lim inn ID-er, nøkler eller passord i repoet, i issues eller i chat.

Du trenger:

- *Owner* (eller *Contributor* + *User Access Administrator*) på abonnementet, så du kan opprette ressursgruppen og rolletildelinger.
- Rett til å opprette app-registreringer i Microsoft Entra ID (standard for vanlige brukere i de fleste tenanter). Har du ikke det, bruk alternativet med administrert identitet i steg 3b.
- Admin-tilgang til GitHub-repoet `tasta-skolekorps/flaskeinnsamling`.

### 1. Logg inn og velg abonnement

```powershell
az login
$SUBSCRIPTION_ID = "<abonnement-id>"
az account set --subscription $SUBSCRIPTION_ID
$TENANT_ID = az account show --query tenantId -o tsv
$RG = "rg-flaskeinnsamling"
$LOCATION = "norwayeast"
$REPO = "tasta-skolekorps/flaskeinnsamling"
```

### 2. Registrer ressursleverandører og opprett ressursgruppen

Identiteten får bare rettigheter på ressursgruppen, så leverandørene må registreres én gang på forhånd:

```powershell
foreach ($ns in "Microsoft.App", "Microsoft.ContainerRegistry", "Microsoft.OperationalInsights", "Microsoft.ManagedIdentity", "Microsoft.Storage") {
  az provider register --namespace $ns
}
az group create --name $RG --location $LOCATION
```

Arbeidsflyten setter `Azure__AllowResourceGroupCreation=false`, så ressursgruppen **må** finnes før første utrulling.

### 3a. Opprett app-registrering med føderert legitimasjon (anbefalt)

```powershell
$APP_ID = az ad app create --display-name "github-flaskeinnsamling-deploy" --query appId -o tsv
$SP_ID = az ad sp create --id $APP_ID --query id -o tsv

@{
  name      = "github-flaskeinnsamling-production"
  issuer    = "https://token.actions.githubusercontent.com"
  subject   = "repo:tasta-skolekorps/flaskeinnsamling:environment:production"
  audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json | Set-Content -Encoding utf8 fic.json
az ad app federated-credential create --id $APP_ID --parameters fic.json
Remove-Item fic.json
```

`subject` må samsvare nøyaktig med repoet og GitHub-miljøet `production`. Den føderte legitimasjonen gjelder bare jobber som kjører i det miljøet.

### 3b. Alternativ: brukertildelt administrert identitet

Bruk dette hvis du ikke kan opprette app-registreringer. Legg identiteten i en **egen** ressursgruppe, så den ikke slettes hvis du river ned `rg-flaskeinnsamling` med `aspire destroy`.

```powershell
az group create --name "rg-flaskeinnsamling-ci" --location $LOCATION
$APP_ID = az identity create --name "id-github-flaskeinnsamling" --resource-group "rg-flaskeinnsamling-ci" --query clientId -o tsv
$SP_ID = az identity show --name "id-github-flaskeinnsamling" --resource-group "rg-flaskeinnsamling-ci" --query principalId -o tsv
az identity federated-credential create `
  --name "github-flaskeinnsamling-production" `
  --identity-name "id-github-flaskeinnsamling" `
  --resource-group "rg-flaskeinnsamling-ci" `
  --issuer "https://token.actions.githubusercontent.com" `
  --subject "repo:tasta-skolekorps/flaskeinnsamling:environment:production" `
  --audiences "api://AzureADTokenExchange"
```

### 4. Gi identiteten roller på ressursgruppen

```powershell
$SCOPE = "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG"
az role assignment create --assignee-object-id $SP_ID --assignee-principal-type ServicePrincipal --role "Contributor" --scope $SCOPE
az role assignment create --assignee-object-id $SP_ID --assignee-principal-type ServicePrincipal --role "Role Based Access Control Administrator" --scope $SCOPE
```

- **Contributor** lar Aspire opprette og oppdatere ressursene.
- **Role Based Access Control Administrator** lar Aspire opprette rolletildelingene for de administrerte identitetene (AcrPull og Storage Table Data Contributor). *User Access Administrator* fungerer også, men gir mer enn nødvendig.
- Rollene gjelder **bare** ressursgruppen, ikke hele abonnementet.

Rolletildelinger kan bruke noen minutter på å slå gjennom. Feiler første utrulling med `AuthorizationFailed`, venter du litt og kjører den på nytt.

### 5. Opprett GitHub-miljøet `production`

```powershell
gh api --method PUT "repos/$REPO/environments/production"
```

Anbefalt: tillat bare utrulling fra `main`.

```powershell
gh api --method PUT "repos/$REPO/environments/production" -F "deployment_branch_policy[protected_branches]=false" -F "deployment_branch_policy[custom_branch_policies]=true"
gh api --method POST "repos/$REPO/environments/production/deployment-branch-policies" -f name=main
```

Vil du godkjenne hver utrulling manuelt, legger du til deg selv som *Required reviewer* under **Settings → Environments → production**.

### 6. Legg inn variabler og hemmelighet i GitHub

Variabler (ikke hemmelige, men hold dem utenfor koden):

```powershell
gh variable set AZURE_CLIENT_ID --repo $REPO --body $APP_ID
gh variable set AZURE_TENANT_ID --repo $REPO --body $TENANT_ID
gh variable set AZURE_SUBSCRIPTION_ID --repo $REPO --body $SUBSCRIPTION_ID
gh variable set AZURE_RESOURCE_GROUP --repo $REPO --body $RG   # valgfri, standard er rg-flaskeinnsamling
```

Hemmelighet for admin-API-et (`admin-api-key` i AppHost). Lag en sterk nøkkel, legg den i passordbehandleren til flaskekomiteen og registrer den som miljøhemmelighet. `gh` spør etter verdien, så den havner ikke i kommandohistorikken:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) | Set-Clipboard
# Lim nøkkelen inn i passordbehandleren først, deretter:
gh secret set ADMIN_API_KEY --repo $REPO --env production
# Lim inn nøkkelen når du blir spurt, og tøm utklippstavlen etterpå:
Set-Clipboard -Value " "
```

| Navn | Type | Innhold |
|---|---|---|
| `AZURE_CLIENT_ID` | variabel | Client-ID til app-registreringen/identiteten |
| `AZURE_TENANT_ID` | variabel | Tenant-ID |
| `AZURE_SUBSCRIPTION_ID` | variabel | Abonnements-ID |
| `AZURE_RESOURCE_GROUP` | variabel (valgfri) | Ressursgruppe, standard `rg-flaskeinnsamling` |
| `ADMIN_API_KEY` | hemmelighet (miljø `production`) | Admin-nøkkel for skrive-API-et |

Variablene kan også ligge på miljøet `production` (`--env production`) i stedet for på repoet. Arbeidsflyten leser `vars.*` begge steder.

## Første utrulling

1. Gå til **Actions → Deploy API (Azure) → Run workflow** på `main`, eller push en endring under `src/`.
2. Første kjøring tar typisk 5–15 minutter (registeret, miljøet og lagringskontoen opprettes).
3. Arbeidsflyten stopper tidlig med en tydelig feilmelding hvis en variabel eller `ADMIN_API_KEY` mangler.

## Finn API-adressen

Etter en vellykket kjøring finner du adressen her:

- **Jobbsammendraget** (Summary) til kjøringen viser «API-URL».
- **Deployments / Environments → production** i repoet viser samme URL.
- Med Azure CLI:

  ```powershell
  az resource show --resource-group rg-flaskeinnsamling --resource-type Microsoft.App/containerApps --name api --query properties.configuration.ingress.fqdn -o tsv
  ```

Adressen har formen `https://api.<noe>.norwayeast.azurecontainerapps.io`. Den endrer seg ikke ved senere utrullinger så lenge ressursgruppen og miljøet beholdes.

**Deretter må frontenden peke på API-et:** sett `API_BASE` i [`api.js`](../api.js) til adressen (uten avsluttende `/`), og commit og push til `main`. Til det blir gjort, bruker sidene de innebygde dataene. CORS er allerede satt opp: AppHost-parameteren `cors-origins` har `https://tasta-skolekorps.github.io` som standardverdi.

## Kostnader

- **Azure Container Apps (Consumption):** API-et skalerer til null når det ikke brukes, og den månedlige gratiskvoten (vCPU-sekunder, GiB-sekunder og 2 millioner forespørsler) dekker normalt et lite korps. Forvent kald start på noen sekunder etter en periode uten trafikk.
- **Lagringskonto (Table Storage):** noen få kroner i måneden for så små datamengder.
- **Azure Container Registry (Basic):** den største faste kostnaden, ca. 5 USD per måned.
- **Log Analytics:** betaling per GB innsamlet logg. Lite volum gir svært lav kostnad.

Sett gjerne opp et budsjettvarsel på ressursgruppen under **Cost Management → Budgets** i Azure-portalen.

## Drift og vedlikehold

- **Bytte admin-nøkkel:** kjør `gh secret set ADMIN_API_KEY --repo tasta-skolekorps/flaskeinnsamling --env production` på nytt, og start arbeidsflyten manuelt.
- **Feilsøking:** loggen fra steget «Deploy with Aspire» viser hvert Aspire-steg. I Azure: `az containerapp logs show --resource-group rg-flaskeinnsamling --name api --tail 100` (krever `az extension add --name containerapp`).
- **Rive ned alt:** kjør `aspire destroy` lokalt etter `az login`. Kommandoen sletter **hele** ressursgruppen. App-registreringen, eller identiteten i `rg-flaskeinnsamling-ci`, blir liggende.
- **Oppgradere Aspire:** oppdater `ASPIRE_VERSION` i `deploy.yml` samtidig med Aspire-pakkene i `src/`.
