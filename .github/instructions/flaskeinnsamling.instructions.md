---
description: "Use when working on Tasta Skolekorps flaskeinnsamling: rodekart, roder, rode assignment (rodefordeling), musikanter, season data, Styreportalen member extraction, Kommunekart rode polygons, printable PDF, GitHub Pages."
applyTo: "**"
---
# Flaskeinnsamling – Tasta Skolekorps

Bottle and can collection (flaskeinnsamling) is a fundraiser. Musicians collect door to door within assigned **roder** (Stavanger kommune districts).

## Calendar
- A **season** is the school year (Aug–Jun) and has 3 collections: autumn, winter and spring. The dates are set in advance.
- Collection day: **Wednesday 17:00–21:00**. Reception is at **Byfjord skole**. Bottles and cans are sorted into Infinitum bags, and a truck picks them up at the end. Amounts are not recorded.
- Notices (lapper) go into mailboxes on the **Monday at least 2 weeks before** collection. The text is written outside this repo.

## Who does what
- Korps groups: aspirantkorps, juniorkorps, mellomkorps, seniorkorps. Only mellom and senior take part.
- **Rode collectors:** all of mellomkorps + seniorkorps musicians born in the **latest birth year** in seniorkorps.
- **Reception (Byfjord skole):** all other seniorkorps musicians. They work in time slots **17:00–18:30, 18:30–20:00 and 20:00–21:00**. Put each one in a slot that doesn't overlap their rehearsal in **Spond** that Wednesday. Extract rehearsal times from Spond with Playwright MCP, and have the user log in themselves.
- Edge cases (e.g. seniorkorps has only one birth year, or too few or too many seniors in the latest year): ask the user and decide case by case.
- Siblings may share one rode (counts as one slot) whenever the family wants.
- If a musician can't collect, the family finds a replacement. Don't reassign.

## Assignment rules
- Assignments are fixed for the whole season. Do new assignments only at a new season.
- Returning collectors keep last season's rode.
- Roder freed by musicians who quit go to new musicians: pick the **nearest free rode to their home**.
- If more roder are needed, add free roder with **dense housing first** (address count per rode).
- Changes mid-season: freed roder stay empty until the next season.
- Unassigned roder are not collected.

## Data sources
- **Members:** Styreportalen at https://drift.styreportalen.no/medlemmer. Extract with Playwright MCP. The user logs in manually. Never ask for, type or store credentials. Needed fields: name, korps group, birth year, address.
- **Rode polygons:** Kommunekart https://kommunekart.com/klient/stavanger/roder (WMS layer `1103_WMS_Roder:RODER`, feature type `Lokalutvalgområde`, attribute `Nummer`). From the Kommunekart page origin, `POST /api/WebPublisher/GfiProxy` (form: `service=WMS&srs=EPSG:4326&tolerance=5&queryLayers=1103_WMS_Roder:RODER;&x=<lon>&y=<lat>&appId=-StavangerApp-Roder-`) returns `Nummer` + `Geometry.Positions` (`X`=lon, `Y`=lat). Use `GfiProxyNoGeom` for point→rode lookup only.
- **Home → rode / dwelling counts:** geocode and count addresses per polygon with the Geonorge address API (`ws.geonorge.no/adresser/v1`).
- Rode series: **20xx = Sør**, **21xx = Nord**. Neighbouring numbers are usually adjacent.

## Repo conventions
- No generator scripts. Artifacts are produced by the agent in chat.
- Store each season in `data/<YYYY-YY>.json` (e.g. `data/2026-27.json`): collection dates, `contacts` (name, phone), `vipps`, `housing` (`{"2001": [addresses, dwellings]}` from Geonorge; dwellings = notices), musicians (name, group, birthYear, address, homeRode), `assignments` (`{"2001": ["Name", ...]}`) and `reception` (`{"<date>": {"17:00-18:30": ["Name", ...], ...}}`). Read the previous season file to preserve continuity.
- 2026-27 collection dates: 2026-10-21, 2027-01-06, 2027-05-19. Contact: Leif Bjarte Johansson, 92423946. Vipps: #87153. For later seasons, read these values from the season file.
- Full names of musicians are OK in the repo and on the published map.
- Bokmål for all user-facing text. English for code and identifiers.
- Published via GitHub Pages from this repo.

## Deliverables per season
- [rodekart.html](../../rodekart.html): a self-contained Leaflet map with embedded `polys` and `byRode`, Kartverket `topograatone` tiles, assigned roder coloured, free roder grey and dashed. Only show free roder between the northern line 2114–2117 and the southern line 2002–2005 (`shownFree`). Every label shows its mailbox count. Show contact persons and Vipps info.
- Printable PDF: one overview page with all roder and names, then **one A4 page per rode** with that rode's map, the responsible name(s) and the number of mailboxes/dwellings (= notices needed).
- Each musician gets a package: the rode page, notices and a **badge**. Don't generate badges. They wear the badge while collecting and return it at the reception at Byfjord skole.
- Reception duty list: reception seniors grouped by time slot for each collection date.
- Process one-pager for families (HTML on GitHub Pages, Bokmål). It explains how collection day works: notices, time, rode, badge, drop-off at Byfjord skole, contact and Vipps.
- Flag any collection date that is not a Wednesday.
