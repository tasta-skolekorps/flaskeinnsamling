# flaskeinnsamling

The published site (GitHub Pages) is in Norwegian; code and docs are in English. See [docs/deploy.md](docs/deploy.md) for deployment.

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

On startup the tables are created, and each `data/<season>.json` is imported if the season is missing or the file's SHA-256 differs from the `SeedHash` stored on the season. A changed file in the repo therefore wins, so the repo mirrors Styreportalen. Admin changes made through the API keep the stored `SeedHash`, so they survive restarts until the repo file changes.

### Endpoints

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/seasons` | List of seasons, e.g. `["2026-27"]` |
| GET | `/api/seasons/{season}` | The whole season in the same format as `data/<season>.json` |
| PUT | `/api/seasons/{season}` | Replaces the whole season (import). Admin |
| GET | `/api/seasons/{season}/assignments` | `{"2001": ["Name"]}` |
| PUT | `/api/seasons/{season}/assignments/{rode}` | Body `["Name"]`; an empty list deletes. Admin |
| GET | `/api/seasons/{season}/reception` | `{"2026-10-21": {"17:00-18:30": ["Name"]}}` |
| PUT | `/api/seasons/{season}/reception/{date}/{slot}` | Body `["Name"]`; an empty list deletes. Admin |

Admin calls require the `X-Admin-Key` header. Without a valid key the API returns `401`. Only names are stored; addresses, birth dates and parent info are discarded.