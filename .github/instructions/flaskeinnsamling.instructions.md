---
description: "Use when working on Tasta Skolekorps flaskeinnsamling: rodekart, roder, rode assignment (rodefordeling), musikanter, season data in the API (admin.html), Kommunekart rode polygons, printable PDF, GitHub Pages."
applyTo: "**"
---
# Flaskeinnsamling – Tasta Skolekorps

Bottle and can collection (flaskeinnsamling) is a fundraiser. Musicians collect door to door within assigned **roder** (Stavanger kommune districts).

## Calendar
- A **season** is the school year (Aug–Jun) and has 3 collections: autumn, winter and spring. The dates are set in advance.
- Collection day: **Wednesday 17:00–20:30**. Reception is at **Byfjord skole**. Bottles and cans are sorted into Infinitum bags, and a truck picks them up at the end. Amounts are not recorded.
- Packages with notices (lapper) are handed out to musicians on the **Monday at least 2 weeks before** collection, unless overridden in the season's `handouts` in the API (`{"<collection>": "<date>"}`, e.g. autumn break). Notices go into mailboxes that same week: **preferably Wednesday, Sunday at the latest**, unless the season's `notices` (`{"<collection>": "<date>"}`) fixes a single date (e.g. after Christmas). The text is written outside this repo.

## Who does what
- Korps groups: aspirantkorps, juniorkorps, mellomkorps, seniorkorps. Only mellom and senior take part.
- **Reception (Byfjord skole):** needs **6 seniors per slot** (18 total) in slots **17:00–18:30, 18:30–20:00 and 20:00–21:15**. Put each one in a slot that doesn't overlap their activities in **Spond** that Wednesday. A slot may have one extra if the user approves.
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
- **The API is the master** for all season data: collection dates, `handouts`, `notices`, `contacts`, `vipps`, `housing`, `korps`, `assignments` (who collects which rode) and `reception` (who has which slot). Base URL: `API_BASE` in [api.js](../../api.js). Read with `GET /api/seasons/<season>`. `housing` (mailbox counts) is **admin-only**: the API omits it unless the admin key is sent, so only admin.html shows it. There are no season files in the repo, and Styreportalen is no longer used.
- **Writing to the API** needs the admin key, which the agent never asks for, types, reads or stores. Changes go through [admin.html](../../admin.html): the user pastes the key into the page in the Playwright browser themselves (it stays in that tab's sessionStorage), and the agent may then fill in names and press "Lagre", or import a whole season as JSON. Re-read the season with `GET` afterwards to verify.
- **Members** (names, korps, home addresses for geocoding new collectors): ask the user for the current member list or export. Use addresses and birth dates only in memory; never write them to the repo or the API.
- **Previous season:** a Google Sheet member export (e.g. `Medlemmer-<date> flaskeinnsamling.xlsx`) with columns Oppgave (Innsamling/Mottak), Rode, Øving and Vakt. It is private: after the user logs in, fetch `…/export?format=csv&gid=<gid>` via `page.context().request`. In-page `fetch` fails on CORS.
- **Spond activities:** use only the group **"Tasta Skolekorps - Medlemmer"** and never read the user's other groups. In the page context, call `api.spond.com/core/v1/sponds?groupId=<id>&includeHidden=true&minStartTimestamp=…&maxEndTimestamp=…` with the bearer token from `localStorage.token`, and never return the token. The activities are individual lessons ("Spilletime…", "Messingundervisning…"). Ignore the "Flaskeinnsamling" and "Tilsynsvakt" events. Times are UTC, so convert to Europe/Oslo.
- **Rode polygons:** Kommunekart https://kommunekart.com/klient/stavanger/roder (WMS layer `1103_WMS_Roder:RODER`, feature type `Lokalutvalgområde`, attribute `Nummer`). From the Kommunekart page origin, `POST /api/WebPublisher/GfiProxy` (form: `service=WMS&srs=EPSG:4326&tolerance=5&queryLayers=1103_WMS_Roder:RODER;&x=<lon>&y=<lat>&appId=-StavangerApp-Roder-`) returns `Nummer` + `Geometry.Positions` (`X`=lon, `Y`=lat). Use `GfiProxyNoGeom` for point→rode lookup only.
- **Geocoding / dwelling counts:** Geonorge `ws.geonorge.no/adresser/v1` (`sok?kommunenummer=1103&utkoordsys=4258&sok=<address postnr>`; `punktsok` + point-in-polygon for counts). Dwellings = Σ max(1, `bruksenhetsnummer.length`).
- Rode series: **20xx = Sør**, **21xx = Nord**. Neighbouring numbers are usually adjacent.

## Repo conventions
- No generator scripts. Artifacts are produced by the agent in chat.
- Each season lives in the API (Table Storage): collection dates, `contacts` (names), `vipps`, `housing` (`{"2001": [addresses, dwellings]}` from Geonorge; dwellings = notices), `korps`, `assignments` (`{"2001": ["Name", ...]}`) and `reception` (`{"<date>": {"17:00-18:30": ["Name", ...], ...}}`). Never add season JSON files to the repo. Create a new season by importing JSON in admin.html, starting from the previous season (download it there) to preserve continuity. The download button is also the backup.
- The repo is **public**. Full names are OK, but **don't commit home addresses, birth dates or parent contact info**. Use them only in memory for matching.
- 2026-27 collection dates: 2026-10-21, 2027-01-06, 2027-05-19. Contacts: the flaskekomité in the season's `contacts`. Vipps: #87153. For later seasons, read these values from the API.
- Full names of musicians are OK in the repo and on the published map.
- English for code, identifiers, code comments, developer docs (README, `docs/`, workflow messages), API error messages and commit messages.
- Bokmål for all user-facing material: the web pages (index, rodekart, vaktliste, kontakt), the PDF, notices and anything families or musicians see.
- Published via GitHub Pages from this repo. `main` is protected: **never push directly to `main`**. After each completed change, create a short-lived branch (e.g. `<topic>`), commit (English message), push the branch, open a PR with `gh pr create` (English title/body), enable squash auto-merge with `gh pr merge --squash --auto --delete-branch` so it merges once the required check `build-and-test` passes, then switch back to `main` and pull. The published site, PDF and API only update after the merge (merging triggers GitHub Pages and the deploy workflow).
- Web pages share [theme.css](../../theme.css) (bottle-green theme and side menu). Every page has the same `<nav class="side">` markup, with `aria-current="page"` on its own link. Keep the menu's season and PDF link up to date.

## After every assignment or reception change
1. Write the change to the API through [admin.html](../../admin.html) (see Data sources), then `GET` the season to verify.
2. Refresh the embedded offline fallback from the API: `byRode` plus the panel counts ("N musikanter · N roder") in `rodekart.html`, the reception list in [vaktliste.html](../../vaktliste.html), and the names in [kontakt.html](../../kontakt.html) if `contacts` changed.
3. Serve the repo locally (`python -m http.server`) and add `?v=<n>` to bypass the cache. Check that the embedded `byRode` equals the API's `assignments`.
4. Regenerate the PDF (it renders from the API): open `rodekart.html?ark`, wait for `window.arkReady`, then `page.pdf` (A4, `preferCSSPageSize`). Reset `emulateMedia` to screen afterwards. Check that there are no broken tiles and that the overview page doesn't overflow.
5. Commit the refreshed fallback and PDF on a branch, open a PR and squash-merge it (see Repo conventions). The published map and PDF update only after the merge.

## Deliverables per season
- [rodekart.html](../../rodekart.html): a self-contained Leaflet map with embedded `polys` and `byRode`, Kartverket `topograatone` tiles, assigned roder coloured, free roder grey and dashed. Only show free roder between the northern line 2114–2117 and the southern line 2002–2005 (`shownFree`). Mailbox counts are admin-only and never shown on the public map. Show a link to the contact page and Vipps info.
- Printable PDF `rodeark-<season>.pdf`, generated from `rodekart.html?ark`: one overview page with all roder and names, then **one A4 page per rode** with that rode's map and the responsible name(s). No mailbox counts in the PDF (admin-only). The ark maps load one at a time with tile retries because Kartverket's tile server resets connections under load.
- Each musician gets a package: the rode page, notices and a **badge**. Don't generate badges. They wear the badge while collecting and return it at the reception at Byfjord skole.
- Reception duty list [vaktliste.html](../../vaktliste.html): reception seniors grouped by time slot for each collection date.
- Contact page [kontakt.html](../../kontakt.html): the whole Flaskekomiteen (names only, from the season's `contacts` in the API), a pointer to Spond for phone numbers, plus Vipps. Don't publish phone numbers. Every other mention of contact (menu footer, pages, rodekart panel, PDF) points to this page instead of listing people.
- Admin page [admin.html](../../admin.html) (Bokmål, not in the menu, `noindex`): edit `assignments` and `reception`, download a season as JSON (backup) and import a whole season.
- Process one-pager for families [index.html](../../index.html) (Bokmål). It explains how collection day works: notices, time, rode, badge, drop-off at Byfjord skole, contact and Vipps.
- Flag any collection date that is not a Wednesday.
