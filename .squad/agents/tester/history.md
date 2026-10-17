# Project Context

- **Owner:** Leif Bjarte Johansson
- **Project:** Tasta Skolekorps flaskeinnsamling — rodekart, rode assignments, reception duty list, published on GitHub Pages. Adding a .NET Aspire backend with Azure Table Storage persistence and continuous deploy to Azure Container Apps (norwayeast).
- **Stack:** Static HTML/JS (Leaflet), .NET 10, Aspire 13.6, Azure Table Storage, GitHub Actions
- **Created:** 2026-10-17T12:33:23+02:00

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->
- 2026-10-17T13:40:00+02:00: `tests/Flaskeinnsamling.Tests` (71 tests, xUnit, Aspire.Hosting.Testing 13.6.1) needs Docker; fixture uses a random admin key and drops the Azurite data volume. Still 71/71 after Lead's revision (SHA-256 seed hash, upsert-then-delete replace, size caps) — consider adding tests for changed-file reseed and >30,000-char / >10-korps 400s.
