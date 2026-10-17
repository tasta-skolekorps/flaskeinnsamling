# Project Context

- **Owner:** Leif Bjarte Johansson
- **Project:** Tasta Skolekorps flaskeinnsamling — rodekart, rode assignments, reception duty list, published on GitHub Pages. Adding a .NET Aspire backend with Azure Table Storage persistence and continuous deploy to Azure Container Apps (norwayeast).
- **Stack:** Static HTML/JS (Leaflet), .NET 10, Aspire 13.6, Azure Table Storage, GitHub Actions
- **Created:** 2026-10-17T12:33:23+02:00

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-10-17T13:40:00+02:00: Reviewed Aspire backend and REJECTED (5 findings), then revised it myself. Seed policy: import `data/<season>.json` when the Seasons row is missing or its SHA-256 `SeedHash` differs; admin PUTs keep the stored hash (null would make every restart overwrite edits). `ReplaceSeasonAsync` = upsert new rows, delete stale RowKeys, write Seasons row last. Size caps: JSON columns ≤30,000 chars, ≤10 korps groups. `aspire-output/` is git-ignored.
- 2026-10-17T14:05:00+02:00: Language rule (user directive): code, comments, developer docs (README, docs/, workflow comments/log messages), API error messages and commit messages in English; all user-facing material (web pages, PDF, notices, family pages) in Bokmål.
