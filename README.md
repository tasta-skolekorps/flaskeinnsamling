# flaskeinnsamling

The published site (GitHub Pages) is in Norwegian; code and docs are in English. See [docs/deploy.md](docs/deploy.md) for deployment.

## Contributing

`main` is protected, so direct pushes are rejected. Work on a short-lived branch and open a pull request to `main`. PRs are squash-merged only (linear history), and the CI check `build-and-test` must pass before merging. Merging to `main` deploys the API and updates GitHub Pages.

```powershell
git switch -c <topic>
git commit -am "Describe the change"
git push -u origin <topic>
gh pr create --fill
gh pr merge --squash --auto --delete-branch
git switch main; git pull
```

## Backend

The API lives in `src/` (Aspire 13.6, .NET 10) and stores season data in Azure Table Storage (Azurite locally).

- `src/Flaskeinnsamling.AppHost` – Aspire AppHost (the resource is named `api`)
- `src/Flaskeinnsamling.Api` – minimal API
- `src/Flaskeinnsamling.ServiceDefaults` – shared health checks/telemetry

### Run locally

Requires the .NET 10 SDK, the Aspire CLI and Docker (for Azurite).

```powershell
# One-time setup: local admin key (pick your own value, never commit it)
dotnet user-secrets set "Parameters:admin-api-key" "<local-key>" --project src/Flaskeinnsamling.AppHost
aspire start
aspire wait api
aspire stop
```

The API (Azure Table Storage) is the **master** for all season data. There are no season files in the repo and nothing is imported on startup: startup only creates missing tables and never touches existing rows. Seasons are created and edited through the admin page [`admin.html`](admin.html) (or the admin endpoints below). Use its "Last ned sesongen" button to take a JSON backup.

The public pages ([`rodekart.html`](rodekart.html), [`vaktliste.html`](vaktliste.html), [`kontakt.html`](kontakt.html)) keep an embedded snapshot as an offline fallback, render it first, and re-render with API data when it arrives (timeout 15 s, to cover cold starts).

### Endpoints

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/seasons` | List of seasons, e.g. `["2026-27"]` |
| GET | `/api/seasons/{season}` | The whole season (`collections`, `handouts`, `notices`, `contacts`, `vipps`, `housing`, `korps`, `assignments`, `reception`) |
| PUT | `/api/seasons/{season}` | Replaces the whole season (import). Admin |
| GET | `/api/seasons/{season}/assignments` | `{"2001": ["Name"]}` |
| PUT | `/api/seasons/{season}/assignments/{rode}` | Body `["Name"]`; an empty list deletes. Admin |
| GET | `/api/seasons/{season}/reception` | `{"2026-10-21": {"17:00-18:30": ["Name"]}}` |
| PUT | `/api/seasons/{season}/reception/{date}/{slot}` | Body `["Name"]`; an empty list deletes. Admin |

Admin calls require the `X-Admin-Key` header. Without a valid key the API returns `401`. Only names are stored; addresses, birth dates and parent info are discarded.