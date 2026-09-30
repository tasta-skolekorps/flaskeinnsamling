---
description: "Use when working on Tasta Skolekorps flaskeinnsamling: rodekart, roder, rode assignment (rodefordeling), musikanter, season data, Styreportalen member extraction, Kommunekart rode polygons, printable PDF, GitHub Pages."
applyTo: "**"
---
# Flaskeinnsamling – Tasta Skolekorps

Bottle and can collection (flaskeinnsamling) is a fundraiser. Musicians collect door to door within assigned **roder** (Stavanger kommune districts).

## Calendar
- A **season** is the school year (Aug–Jun) and has 3 collections: autumn, winter and spring. The dates are set in advance.
- Collection day: **Wednesday 17:00–20:30**. Reception is at **Byfjord skole**. Bottles and cans are sorted into Infinitum bags, and a truck picks them up at the end. Amounts are not recorded.
- Packages with notices (lapper) are handed out to musicians on the **Monday at least 2 weeks before** collection, unless overridden in the season file's `handouts` (`{"<collection>": "<date>"}`, e.g. autumn break). Notices go into mailboxes that same week: **preferably Wednesday, Sunday at the latest**, unless the season file's `notices` (`{"<collection>": "<date>"}`) fixes a single date (e.g. after Christmas). The text is written outside this repo.

## Who does what
- Korps groups: aspirantkorps, juniorkorps, mellomkorps, seniorkorps. Only mellom and senior take part.
- **Reception (Byfjord skole):** needs **6 seniors per slot** (18 total) in slots **17:00–18:30, 18:30–20:00 and 20:00–20:30**. Put each one in a slot that doesn't overlap their activities in **Spond** that Wednesday. A slot may have one extra if the user approves.
- **Rode collectors:** all of mellomkorps + seniors not needed in reception. The youngest seniors collect first, but the **user picks who stays in reception**. Ask.
- Tell the user which collectors have Spond lessons on collection day. They can still collect, but should be warned.
- Edge cases (e.g. too few or too many seniors): ask the user and decide case by case.
- Siblings may share one rode (counts as one slot) whenever the family wants. When siblings are split or displaced, each gets their own rode unless told otherwise.
- If a musician can't collect, the family finds a replacement. Don't reassign.

## Assignment rules
- Assignments are fixed once the season's first collection has happened. Before that, reassign when the user asks.
- Returning collectors (including seniors coming back from reception) get their **previous rode** back. The holder who loses it gets a new rode.
- New or displaced collectors (usually the youngest mellomkorps kids) get free roder by **vicinity and mailbox count**:
  1. Pick the candidate free roder inside `shownFree`. If there are more roder than kids, drop the ones with the fewest mailboxes first.
  2. Geocode each home with Geonorge. Assign kids to roder so that the **total home→rode-centre distance is minimal** (optimal assignment, not greedy).
  3. Show the result as a table (kid, rode, mailboxes, distance) and flag unusually large roder (e.g. > 150 mailboxes).
- Mid-season changes: freed roder stay empty until the next season.
- Unassigned roder are not collected.

## Data sources
The user always logs in to every site themselves in the Playwright browser. Never ask for, type, print or store credentials or tokens. Extract only the fields you need. **Never extract or store parents' names, e-mails or phone numbers.**
- **Members:** Styreportalen at https://drift.styreportalen.no/medlemmer. The list is a virtualized MUI `role=grid` ("Antall rader: N"), so scroll `.MuiDataGrid-virtualScroller` and collect rows by `Medlemsnummer` (`data-field=person_id`). Fields: Fornavn, Etternavn, Fødselsdato, Adresse, Postnummer, Avdeling (`department`: Mellomkorps/Senior) and Flaskeinnsamling (`custom_fields.sone_for_flaskeinnsamling`). Each member's page is `/person/<row data-id>`.
- **Styreportalen is the master** for who collects which rode and who has which reception slot. The Flaskeinnsamling field holds `Rode <nr>` (several roder: `Rode 2013, 2014`) or `Vakt <slot>` (e.g. `Vakt 18:30-20:00`). Leave it empty for members who don't take part. Always write changes to Styreportalen, and the repo files mirror it. If the repo and Styreportalen disagree, show the differences and ask before changing either.
- **Previous season:** a Google Sheet member export (e.g. `Medlemmer-<date> flaskeinnsamling.xlsx`) with columns Oppgave (Innsamling/Mottak), Rode, Øving and Vakt. It is private: after the user logs in, fetch `…/export?format=csv&gid=<gid>` via `page.context().request`. In-page `fetch` fails on CORS.
- **Spond activities:** use only the group **"Tasta Skolekorps - Medlemmer"** and never read the user's other groups. In the page context, call `api.spond.com/core/v1/sponds?groupId=<id>&includeHidden=true&minStartTimestamp=…&maxEndTimestamp=…` with the bearer token from `localStorage.token`, and never return the token. The activities are individual lessons ("Spilletime…", "Messingundervisning…"). Ignore the "Flaskeinnsamling" and "Tilsynsvakt" events. Times are UTC, so convert to Europe/Oslo.
- **Rode polygons:** Kommunekart https://kommunekart.com/klient/stavanger/roder (WMS layer `1103_WMS_Roder:RODER`, feature type `Lokalutvalgområde`, attribute `Nummer`). From the Kommunekart page origin, `POST /api/WebPublisher/GfiProxy` (form: `service=WMS&srs=EPSG:4326&tolerance=5&queryLayers=1103_WMS_Roder:RODER;&x=<lon>&y=<lat>&appId=-StavangerApp-Roder-`) returns `Nummer` + `Geometry.Positions` (`X`=lon, `Y`=lat). Use `GfiProxyNoGeom` for point→rode lookup only.
- **Geocoding / dwelling counts:** Geonorge `ws.geonorge.no/adresser/v1` (`sok?kommunenummer=1103&utkoordsys=4258&sok=<address postnr>`; `punktsok` + point-in-polygon for counts). Dwellings = Σ max(1, `bruksenhetsnummer.length`).
- Rode series: **20xx = Sør**, **21xx = Nord**. Neighbouring numbers are usually adjacent.

## Repo conventions
- No generator scripts. Artifacts are produced by the agent in chat.
- Store each season in `data/<YYYY-YY>.json` (e.g. `data/2026-27.json`): collection dates, `contacts` (name, phone), `vipps`, `housing` (`{"2001": [addresses, dwellings]}` from Geonorge; dwellings = notices), `assignments` (`{"2001": ["Name", ...]}`) and `reception` (`{"<date>": {"17:00-18:30": ["Name", ...], ...}}`). Read the previous season file to preserve continuity.
- The repo is **public**. Full names are OK, but **don't commit home addresses, birth dates or parent contact info**. Use them only in memory for matching.
- 2026-27 collection dates: 2026-10-21, 2027-01-06, 2027-05-19. Contact: Leif Bjarte Johansson, 92423946. Vipps: #87153. For later seasons, read these values from the season file.
- Full names of musicians are OK in the repo and on the published map.
- Bokmål for all user-facing text and commit messages. English for code and identifiers.
- Published via GitHub Pages from this repo. The user expects **commit + push to `main`** after each completed change.
- Web pages share [theme.css](../../theme.css) (bottle-green theme and side menu). Every page has the same `<nav class="side">` markup, with `aria-current="page"` on its own link. Keep the menu's season, PDF link and contact up to date.

## After every assignment or reception change
1. Update the Flaskeinnsamling field in Styreportalen for every affected member, then re-read the grid to verify.
2. Update `byRode` (and `dwellings` for newly used roder) plus the panel counts ("N musikanter · N roder") in `rodekart.html`.
3. Update `assignments` / `reception` in the season file, and [vaktliste.html](../../vaktliste.html) for reception.
4. Serve the repo locally (`python -m http.server`) and add `?v=<n>` to bypass the cache. Check that `byRode` equals the season file's `assignments`.
5. Regenerate the PDF: open `rodekart.html?ark`, wait for `window.arkReady`, then `page.pdf` (A4, `preferCSSPageSize`). Reset `emulateMedia` to screen afterwards. Check that there are no broken tiles and that the overview page doesn't overflow.
6. Commit and push.

## Deliverables per season
- [rodekart.html](../../rodekart.html): a self-contained Leaflet map with embedded `polys` and `byRode`, Kartverket `topograatone` tiles, assigned roder coloured, free roder grey and dashed. Only show free roder between the northern line 2114–2117 and the southern line 2002–2005 (`shownFree`). Every label shows its mailbox count. Show contact persons and Vipps info.
- Printable PDF `rodeark-<season>.pdf`, generated from `rodekart.html?ark`: one overview page with all roder, names and mailboxes, then **one A4 page per rode** with that rode's map, the responsible name(s) and the number of mailboxes/dwellings (= notices needed). The ark maps load one at a time with tile retries because Kartverket's tile server resets connections under load.
- Each musician gets a package: the rode page, notices and a **badge**. Don't generate badges. They wear the badge while collecting and return it at the reception at Byfjord skole.
- Reception duty list [vaktliste.html](../../vaktliste.html): reception seniors grouped by time slot for each collection date.
- Process one-pager for families [index.html](../../index.html) (Bokmål). It explains how collection day works: notices, time, rode, badge, drop-off at Byfjord skole, contact and Vipps.
- Flag any collection date that is not a Wednesday.
