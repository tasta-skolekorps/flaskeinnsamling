# Project Context

- **Owner:** Leif Bjarte Johansson
- **Project:** Tasta Skolekorps flaskeinnsamling — rodekart, rode assignments, reception duty list, published on GitHub Pages. Adding a .NET Aspire backend with Azure Table Storage persistence and continuous deploy to Azure Container Apps (norwayeast).
- **Stack:** Static HTML/JS (Leaflet), .NET 10, Aspire 13.6, Azure Table Storage, GitHub Actions
- **Created:** 2026-10-17T12:33:23+02:00

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-10-17T13:40:00+02:00: Work rejected by Lead (seed skipped changed season files; non-atomic replace) and revised by Lead. Now: seed when Seasons row missing or SHA-256 `SeedHash` of the raw file differs; admin edits keep the hash; replace = upsert-then-delete-stale, Seasons row last. AppHost parameter `admin-api-key` maps to env `Parameters__admin_api_key` (dashes → single underscores, `:` → `__`).
- 2026-10-17T14:05:00+02:00: Language rule (user directive): code, comments, developer docs (README, docs/, workflow comments/log messages), API error messages and commit messages in English; all user-facing material (web pages, PDF, notices, family pages) in Bokmål.
