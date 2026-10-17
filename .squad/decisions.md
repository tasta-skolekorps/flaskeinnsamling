# Squad Decisions

## Active Decisions

### 2026-10-17T12:33:23+02:00: Backend architecture
**By:** Leif Bjarte Johansson (via Copilot)
**What:**
- .NET Aspire (13.6, .NET 10) AppHost + minimal API, persisting to Azure Table Storage (Azurite locally).
- Backend serves season data (dates, contacts, vipps, housing), rode assignments (byRode) and reception slots; admin write API for assignments/reception.
- Writes protected by a shared admin API key stored as a secret (never committed).
- Static pages stay on GitHub Pages and fetch from the API (CORS), falling back to embedded data.
- Continuous deploy: GitHub Actions on push to `main` → `aspire deploy` to Azure Container Apps in `norwayeast`, authenticated with OIDC.
- Styreportalen remains master; public repo rules still apply (no addresses, birth dates, parent contact info).
**Why:** User request — captured for team memory

### 2026-10-17T13:20:00+02:00: Continuous deploy of the API to Azure Container Apps
**By:** Infra (requested by Leif Bjarte Johansson)
**What:**
- Workflow `.github/workflows/deploy.yml` ("Deploy API (Azure)"). Triggers (since 2026-10-17T14:20 `main` only changes via squash-merged PRs, so the push trigger fires on merge): push to `main` touching `src/**`, `data/**`, `*.sln`, `*.slnx`, `global.json`, `aspire.config.json` or `.github/workflows/deploy.yml`; plus `workflow_dispatch`. Concurrency group `deploy-production` (no cancel-in-progress). Job runs in GitHub environment `production`.
- Auth: `azure/login@v3` with OIDC (`id-token: write`, `contents: read`). Federated credential subject `repo:tasta-skolekorps/flaskeinnsamling:environment:production`.
- Aspire CLI pinned to **13.6.1** via `https://aspire.dev/install.sh --version 13.6.1` (keep in sync with `Aspire.AppHost.Sdk`/packages in `src/`). Deploy: `aspire deploy --apphost src/Flaskeinnsamling.AppHost/Flaskeinnsamling.AppHost.csproj --environment Production --non-interactive`.
- GitHub **variables** (repo or `production` env): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, optional `AZURE_RESOURCE_GROUP` (default `rg-flaskeinnsamling`).
- GitHub **secret** (environment `production`): `ADMIN_API_KEY` → `Parameters__admin_api_key` (AppHost parameter `admin-api-key`; dashes become underscores).
- Aspire settings passed as env: `Azure__SubscriptionId`, `Azure__TenantId`, `Azure__Location=norwayeast`, `Azure__ResourceGroup`, `Azure__AllowResourceGroupCreation=false`, `Azure__CredentialSource=AzureCli`. `cors-origins` uses its AppHost default.
- Resource group is pre-created by the user. Deploy identity gets **Contributor** + **Role Based Access Control Administrator** scoped to the resource group (Aspire's bicep creates AcrPull and Storage Blob/Table/Queue Data Contributor role assignments). Resource providers registered once by the user.
- Deployment state is **not** cached between runs (`~/.aspire/deployments` contains plaintext secrets; public repo). All inputs come from env vars each run.
- API URL is written to the job summary and the `production` environment URL (via `az resource show … Microsoft.App/containerApps/api`, fallback: grep of deploy log).
- One-time setup documented in `docs/deploy.md` (Bokmål). (superseded 2026-10-17: developer docs are now English)
**Why:** User request — continuous deploy with OIDC, no long-lived Azure secrets, lowest-cost scale-to-zero hosting in norwayeast.

### 2026-10-17T13:40:00+02:00: Backend API contract
**By:** Backend
**What:**

**Projects / paths**
- Solution: `Flaskeinnsamling.slnx` (repo root). `aspire.config.json` at repo root points to the AppHost.
- AppHost: `src/Flaskeinnsamling.AppHost/Flaskeinnsamling.AppHost.csproj` (Aspire.AppHost.Sdk 13.6.1)
- API: `src/Flaskeinnsamling.Api` · ServiceDefaults: `src/Flaskeinnsamling.ServiceDefaults`

**AppHost resource names**
- `env` – Azure Container Apps environment (managed Aspire dashboard disabled to save cost)
- `storage` – Azure Storage (Standard_LRS; Azurite with data volume locally), child `tables`
- `api` – the API project (external HTTP endpoints, scale 0–1 replicas, only role Storage Table Data Contributor)

**Parameters**
- `admin-api-key` (secret) → env `Admin__ApiKey` on `api`. Locally: AppHost user secret `Parameters:admin-api-key`. In CI: `Parameters__admin_api_key` env var (dashes → single underscores, per Aspire docs) (from a GitHub secret). If unset in the API, all writes return 401.
- `cors-origins` (non-secret, default `https://tasta-skolekorps.github.io`, comma-separated) → env `Cors__AllowedOrigins`. In Development the API also allows any loopback origin (localhost/127.0.0.1, any port).

**Endpoints** (JSON camelCase; dictionary keys are kept verbatim)
- `GET /api/seasons` → `["2026-27"]`
- `GET /api/seasons/{season}` → `{season, collections, handouts, notices, contacts, vipps, housing, korps, assignments, reception}` — same shape as `data/<season>.json` (minus `musicians`). 404 if missing.
- `PUT /api/seasons/{season}` body = season document → replaces all rows for the season. ADMIN. 204.
- `GET /api/seasons/{season}/assignments` → `{"2001": ["Name"]}`. 404 if season missing.
- `PUT /api/seasons/{season}/assignments/{rode}` body `["Name"]` → upsert, `[]` deletes. ADMIN. 204.
- `GET /api/seasons/{season}/reception` → `{"2026-10-21": {"17:00-18:30": ["Name"]}}`. 404 if season missing.
- `PUT /api/seasons/{season}/reception/{date}/{slot}` body `["Name"]` → upsert, `[]` deletes. ADMIN. 204. `:` in slot may be raw or `%3A`.
- `/health`, `/alive` (Development only, ServiceDefaults default).
- Errors: 400 = ValidationProblem (`season` `^\d{4}-\d{2}$`, `rode` `^\d{4}$`, date `yyyy-MM-dd` and a real date, slot `HH:mm-HH:mm` with from < to, names ≤100 chars, ≤20 per rode/slot); 401 = missing/wrong `X-Admin-Key`; 404 = season not found. Names are trimmed, blanks and duplicates dropped.
- Admin header: `X-Admin-Key` (constant-time compare to `Admin:ApiKey`). CORS allows GET, PUT and headers `X-Admin-Key`, `Content-Type`.

**Tables** (PartitionKey = season id, e.g. `2026-27`)
- `Seasons`: RowKey = season; `Collections`, `Handouts`, `Notices`, `Contacts`, `Korps` (JSON strings), `Vipps`
- `Housing`: RowKey = rode; `Addresses` (int), `Dwellings` (int)
- `Assignments`: RowKey = rode; `Names` (JSON array string)
- `Reception`: RowKey = `{date}_{slot}`; `Date`, `Slot`, `Names` (JSON array string)

**Seeding:** superseded by "Seed policy for data/<season>.json" (Lead) below — import when the `Seasons` row is missing or its `SeedHash` differs from the file's SHA-256.

**Why:** Fixed names so Infra (deploy workflow parameters), Frontend (fetch URLs/shape) and Tester can rely on them.

### 2026-10-17T13:40:00+02:00: Seed policy for data/<season>.json (supersedes contract seeding rule)
**By:** Lead (revision after review rejection of Backend)
**What:**
The repo season file wins when it changes; otherwise storage (admin edits) wins.

- On startup `SeedService` computes SHA-256 (lowercase hex) of the raw bytes of each `data/<YYYY-YY>.json`.
- It imports the season if the Seasons row is missing **or** its `SeedHash` differs from the file hash, then stores the new hash on the Seasons row (written last, so a half-finished import is retried).
- Admin `PUT /api/seasons/{season}` and the per-rode/per-slot PUTs **keep** the stored `SeedHash`. Setting it to null was rejected: null never equals the file hash, so every restart would overwrite admin edits. Keeping it means an unchanged file is a no-op and a changed file overwrites.
- Seasons created only via the API have `SeedHash = null`; if a matching file later appears it is imported.
**Why:** Styreportalen is master and the repo mirrors it, so a committed change to the season file is the authoritative update.
- Consequence: admin edits made via the API must also be committed to `data/<season>.json`, or they are lost the next time that file changes.

Also: `ReplaceSeasonAsync` now upserts new rows first, then deletes only stale RowKeys, then writes the Seasons row. Validation rejects serialized JSON columns (collections, handouts, notices, contacts, korps) over 30,000 chars and more than 10 korps groups with 400.

### 2026-10-17T13:40:00+02:00: Frontend API client and fallback
**By:** Frontend
**What:**
- New shared `api.js` (repo root) exposes `window.flaskeApi = { base, season, loadSeason(season), hasEntries, esc }`.
- **API base URL:** the `API_BASE` constant at the top of `api.js`. It is `''` (API disabled, no network calls) until the first Azure Container Apps deploy; then set it to the API origin, e.g. `https://<app>.<env>.norwayeast.azurecontainerapps.io` (no trailing slash needed). Default season constant `SEASON = '2026-27'` lives next to it; rodekart.html passes its own embedded `season.name`.
- **Test override:** `?api=<url>` — honoured only when the API is loopback (localhost/127.0.0.1/[::1]) or the page itself is served from localhost, so a crafted link can't make the public site show a foreign Vipps number/names.
- `loadSeason` calls `GET {base}/api/seasons/{season}`, 4 s timeout, never throws; returns `null` on any failure (network, CORS, non-2xx, bad JSON, season mismatch) and logs `console.warn` only.
- **Fallback:** every page keeps its embedded data and renders immediately; API data replaces it when it arrives. Empty `assignments`/`reception`/`housing`/`korps` objects from the API are treated as "not seeded" and the embedded data is kept. Rodes without a polygon in `polys` are ignored (warned).
- rodekart.html `?ark` waits for the API (or its timeout, max ~4 s) **before** building pages, so `window.arkReady` still means "everything rendered".
- All API strings are HTML-escaped before insertion. Contacts accept strings or `{name, phone}` objects; phone is never shown.
- The shared `<nav class="side">` (season + Vipps in the footer) is still static and not updated from the API.
**Why:** Keep the public pages working when the API is asleep/undeployed, and keep PDF generation deterministic.

### 2026-10-17T13:40:00+02:00: API integration tests
**By:** Tester
**What:**
- Project: `tests/Flaskeinnsamling.Tests` (xUnit 2.9, .NET 10, `Aspire.Hosting.Testing` 13.6.1), in `Flaskeinnsamling.slnx` under `/tests/`.
- Run: `dotnet test tests\Flaskeinnsamling.Tests` (or `dotnet test Flaskeinnsamling.slnx`). Needs Docker running (Azurite). First run ~1 min, later runs a few seconds plus container start.
- One AppHost per run (`ApiFixture`, xUnit collection `api`) via `DistributedApplicationTestingBuilder.CreateAsync<Projects.Flaskeinnsamling_AppHost>()`. The fixture sets `Parameters:admin-api-key` to a random `test-key-<guid>` — no real key or user secret is needed.
- The fixture removes the AppHost's Azurite named data volume, so each run starts from an empty emulator, seeds from `data/*.json` and never touches local dev data. Tests that write to `2026-27` only use a free rode / free slot (`21:15-22:00`) and restore it; full-season writes use `2099-00`/`2099-01` (no DELETE endpoint; left in the ephemeral emulator).
- Season-equality tests compare the API with `data/<season>.json` (copied into the test output). If someone edits the season file, the seed and the test stay in sync automatically.
- Coverage: seed equality (collections, contacts, vipps, housing, korps, assignments, reception), 400/401/404, assignment + reception round-trip and `[]` delete (incl. `%3A` slots), name normalization and limits, full-season import drops unknown fields (address, birth date, parent contacts, musicians), full-season replace removes old rows, CORS preflight and simple GET (GitHub Pages allowed, others not, DELETE not allowed).
**Why:** Writes are admin-only and data is public; these tests lock in the contract in `backend-api-contract.md` against a real Azurite, not mocks.

### 2026-10-17T13:55:27+02:00: User directive — language
**By:** Leif Bjarte Johansson (via Copilot)
**What:** Code, code comments, developer docs (README, docs/, workflow comments/log messages), API error messages and commit messages are written in English. All user-facing material (web pages, PDF, notices, contact/process pages for families) stays in Bokmål.
**Why:** User request — captured for team memory

### 2026-10-17T14:11:16+02:00: User directive — PR-based workflow
**By:** Leif Bjarte Johansson (via Copilot)
**What:** `main` is protected by a ruleset (PR required, 0 approvals, squash merge only, linear history). All changes go via a short-lived branch + pull request; the agent squash-merges its own PR once required checks pass, then deletes the branch. Never push directly to `main`. A PR CI workflow (build + tests) is a required status check.
**Why:** User request — captured for team memory

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
