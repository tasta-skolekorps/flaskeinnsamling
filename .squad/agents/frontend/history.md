# Project Context

- **Owner:** Leif Bjarte Johansson
- **Project:** Tasta Skolekorps flaskeinnsamling — rodekart, rode assignments, reception duty list, published on GitHub Pages. Adding a .NET Aspire backend with Azure Table Storage persistence and continuous deploy to Azure Container Apps (norwayeast).
- **Stack:** Static HTML/JS (Leaflet), .NET 10, Aspire 13.6, Azure Table Storage, GitHub Actions
- **Created:** 2026-10-17T12:33:23+02:00

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-10-17T13:40:00+02:00: `api.js` exposes `window.flaskeApi`; the API origin is the `API_BASE` constant at the top of `api.js` (`''` = API disabled until the first ACA deploy). `?api=` override only for loopback. Pages render embedded data first; empty API objects mean "not seeded". `?ark` waits for API or ~4 s timeout before `window.arkReady`.
- 2026-10-17T14:05:00+02:00: Language rule (user directive): code, comments, developer docs (README, docs/, workflow comments/log messages), API error messages and commit messages in English; all user-facing material (web pages, PDF, notices, family pages) in Bokmål.
- 2026-10-17T14:20:00+02:00: PR workflow (user directive): never push to `main` (protected ruleset). Work on a short-lived branch → open PR → squash auto-merge once the required `build-and-test` check passes; branch is deleted on merge. Deploy runs on merge.
