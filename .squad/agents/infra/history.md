# Project Context

- **Owner:** Leif Bjarte Johansson
- **Project:** Tasta Skolekorps flaskeinnsamling — rodekart, rode assignments, reception duty list, published on GitHub Pages. Adding a .NET Aspire backend with Azure Table Storage persistence and continuous deploy to Azure Container Apps (norwayeast).
- **Stack:** Static HTML/JS (Leaflet), .NET 10, Aspire 13.6, Azure Table Storage, GitHub Actions
- **Created:** 2026-10-17T12:33:23+02:00

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-10-17T13:40:00+02:00: Deploy workflow passes the admin key as `Parameters__admin_api_key` (AppHost parameter `admin-api-key`; dashes → single underscores, confirmed in Aspire docs). Aspire CLI pinned to 13.6.1 — keep in sync with `Aspire.AppHost.Sdk`. Season file changes now redeploy and re-seed (SHA-256 `SeedHash` policy), so `data/**` must stay in the trigger paths. Don't cache `~/.aspire/deployments` (plaintext secrets).
- 2026-10-17T14:05:00+02:00: Language rule (user directive): code, comments, developer docs (README, docs/, workflow comments/log messages), API error messages and commit messages in English; all user-facing material (web pages, PDF, notices, family pages) in Bokmål.
