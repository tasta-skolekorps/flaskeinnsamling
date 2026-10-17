# flaskeinnsamling
## Backend

API-et ligger i `src/` (Aspire 13.6, .NET 10) og lagrer sesongdata i Azure Table Storage (Azurite lokalt).

- `src/Flaskeinnsamling.AppHost` – Aspire AppHost (ressursen heter `api`)
- `src/Flaskeinnsamling.Api` – minimal API
- `src/Flaskeinnsamling.ServiceDefaults` – felles helsesjekk/telemetri

### Kjøre lokalt

Krever .NET 10 SDK, Aspire CLI og Docker (for Azurite).

```powershell
# Engangsoppsett: lokal admin-nøkkel (velg en egen verdi, aldri commit den)
dotnet user-secrets set "Parameters:admin-api-key" "<lokal-nøkkel>" --project src/Flaskeinnsamling.AppHost
aspire start
aspire wait api
aspire stop
```

Ved oppstart lages tabellene, og hver `data/<sesong>.json` importeres hvis sesongen mangler eller filens SHA-256 avviker fra `SeedHash` lagret på sesongen – en endret fil i repoet vinner altså, slik repoet speiler Styreportalen. Admin-endringer via API-et beholder lagret `SeedHash`, så de overlever omstart helt til fila i repoet endres.

### Endepunkter

| Metode | Sti | Beskrivelse |
| --- | --- | --- |
| GET | `/api/seasons` | Liste over sesonger, f.eks. `["2026-27"]` |
| GET | `/api/seasons/{season}` | Hele sesongen i samme format som `data/<sesong>.json` |
| PUT | `/api/seasons/{season}` | Erstatter hele sesongen (import). Admin |
| GET | `/api/seasons/{season}/assignments` | `{"2001": ["Navn"]}` |
| PUT | `/api/seasons/{season}/assignments/{rode}` | Body `["Navn"]`; tom liste sletter. Admin |
| GET | `/api/seasons/{season}/reception` | `{"2026-10-21": {"17:00-18:30": ["Navn"]}}` |
| PUT | `/api/seasons/{season}/reception/{date}/{slot}` | Body `["Navn"]`; tom liste sletter. Admin |

Admin-kall krever headeren `X-Admin-Key`. Uten gyldig nøkkel svarer API-et `401`. Bare navn lagres; adresser, fødselsdatoer og foreldreinfo forkastes.
