# Skills Inventory

**Last Updated:** 2026-04-27  
**Maintained by:** Petey (Agent Platform Specialist)

This inventory tracks all extracted skills in `.squad/skills/`, their validation status, and searchable keywords for rapid discovery.

---

## Quick Reference

| Skill Name | Extracted | Extracted By | Confidence | Keywords |
|------------|-----------|--------------|------------|----------|
| blazor-table-mudblazor-migration | 2026-04-22 | helly | **HIGH** | blazor, mudblazor, datagrid, bootstrap, table-migration, frontend, v9, dotnet-10 |
| tool-write-hardening-review | 2026-05-21 | drummond | **HIGH** | hardening, security, path-traversal, containment, tool-write, llm-safety, filesystem |
| aspire-blazor-scaffold | 2026-04-23 | mark | **HIGH** | aspire, blazor-server, scaffold, mudblazor, service-discovery, dotnet-10 |
| ndjson-tail | 2026-04-27 | petey | **HIGH** | ndjson, streaming, blazor, db-tail, polling, live-updates, http-streaming |
| ndjson-request-correlation | 2026-04-27 | petey | **HIGH** | ndjson, correlation, async, tool-approval, mid-stream, guid, taskcompletionsource |
| skills-spec-audit | 2026-04-26 | petey | **HIGH** | skills, spec-alignment, agentskills-io, maf, progressive-disclosure, audit |
| mudblazor-blazor-server-setup | 2026-04-27 | helly | **HIGH** | mudblazor, blazor-server, bootstrap, theming, setup, typography, dotnet-10 |
| external-bundle-threat-model | 2026-05-22 | drummond | **HIGH** | hardening, threat-model, supply-chain, prompt-injection, external-content, security |
| live-test-coverage | 2026-04-30 | petey | **HIGH** | testing, llm-testing, live-tests, provider-testing, ollama, azure-openai, coverage-analysis |
| blazor-screenshot-capture | 2026-04-27 | petey | **MEDIUM** | playwright, aspire, screenshot, blazor-server, documentation, chromium |
| blazor-flex-height-constraint | 2026-04-27 | helly | **MEDIUM** | blazor, css, flexbox, layout, height-constraint, overflow |

---

## By Category

### Frontend (3 skills)
- **blazor-table-mudblazor-migration** — Migrate Bootstrap tables to MudBlazor MudDataGrid
- **mudblazor-blazor-server-setup** — Wire MudBlazor into Blazor Server without losing Bootstrap
- **blazor-flex-height-constraint** — Fix flexbox height constraints in Blazor layouts

### Hardening (2 skills)
- **tool-write-hardening-review** — 8-point checklist for LLM tool-write surfaces
- **external-bundle-threat-model** — Threat model for third-party content imports

### Testing (1 skill)
- **live-test-coverage** — Coverage strategy for LLM-driven platforms

### Streaming / NDJSON (2 skills)
- **ndjson-tail** — DB-tail streaming pattern for live updates
- **ndjson-request-correlation** — Mid-stream correlation for async requests

### Analysis (1 skill)
- **skills-spec-audit** — Audit skills subsystem against open specs

### Infrastructure (2 skills)
- **aspire-blazor-scaffold** — Scaffold Aspire-registered Blazor Server projects
- **blazor-screenshot-capture** — Automated screenshot capture under Aspire

---

## Confidence Levels

### HIGH (9 skills)
Skills validated by multiple agents or proven in production across multiple contexts:
- blazor-table-mudblazor-migration (9 pages validated)
- tool-write-hardening-review (Mark, Drummond, Petey approved)
- aspire-blazor-scaffold (Mark, Helly, Irving validated)
- ndjson-tail (Irving, Petey validated)
- ndjson-request-correlation (Irving, Petey validated)
- skills-spec-audit (Mark, Petey validated)
- mudblazor-blazor-server-setup (Helly, Petey validated)
- external-bundle-threat-model (Drummond, Mark, Petey validated)
- live-test-coverage (Petey, Dylan validated)

### MEDIUM (2 skills)
Skills validated independently but not yet proven across multiple contexts:
- blazor-screenshot-capture (Petey, Helly validated)
- blazor-flex-height-constraint (Helly, Petey validated)

### LOW (0 skills)
Skills recently observed but not yet independently validated.

---

## Search Index

Use `grep -i "keyword" .squad/SKILLS_INVENTORY.md` to quickly find relevant skills:

**Blazor:** blazor-table-mudblazor-migration, mudblazor-blazor-server-setup, blazor-screenshot-capture, blazor-flex-height-constraint, aspire-blazor-scaffold  
**MudBlazor:** blazor-table-mudblazor-migration, mudblazor-blazor-server-setup, aspire-blazor-scaffold  
**Hardening:** tool-write-hardening-review, external-bundle-threat-model  
**Streaming:** ndjson-tail, ndjson-request-correlation  
**Testing:** live-test-coverage  
**Aspire:** aspire-blazor-scaffold, blazor-screenshot-capture  
**Playwright:** blazor-screenshot-capture  
**Security:** tool-write-hardening-review, external-bundle-threat-model  
**LLM:** live-test-coverage, external-bundle-threat-model, tool-write-hardening-review  

---

## Maintenance Notes

**Adding a new skill:**
1. Extract skill to `.squad/skills/<skill-name>/SKILL.md`
2. Add `@extracted` marker with date, agent, context
3. Add `@validated-by` marker with initial validator (yourself) at `low` confidence
4. Add row to this inventory with keywords
5. Independent validation bumps confidence to `medium`
6. Team-wide validation or production proof bumps to `high`

**Updating confidence:**
When a skill gains independent validation (different agent/session confirms it works):
1. Update the `@validated-by` line in the SKILL.md
2. Update the confidence column in this inventory
3. Move the skill between confidence sections

**Archiving stale skills:**
When a skill becomes obsolete (tech stack change, pattern superseded):
1. Add `@deprecated: <date>, <reason>` marker to SKILL.md
2. Move to `.squad/skills/archived/` directory
3. Remove from this inventory (keep in git history)
