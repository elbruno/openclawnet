# Repository Split Decision — 2025-07-14

> **TL;DR:** All application source code consolidates into `elbruno/openclawnet`. The plan repo retains only planning artifacts, squad configuration, and documentation.

## Context

OpenClawNet development spans two GitHub repositories:

| Repository | Intended purpose |
|-----------|-----------------|
| `elbruno/openclawnet` (code repo) | Production source, CI, GitHub Pages deployment |
| `elbruno/openclawnet-plan` (plan repo) | Planning, `.squad/` agent orchestration, docs, issue tracking |

Over time, production source code accumulated in the plan repo because squad agents execute there. This was never an intentional architectural decision.

## Current State Inventory (as of 2025-07-14)

### `src/` projects

| Location | Projects |
|----------|----------|
| **Both repos** (38 projects) | Agent, AppHost, Channels, Gateway, Memory, Models.*, ServiceDefaults, Services.*, Skills, Storage, Tools.*, Adapters.Teams, Web, Mcp.Abstractions, Mcp.Core, Mcp.Web, Mcp.Shell, Mcp.Browser, Mcp.FileSystem |
| **Plan-only** (1 project) | `OpenClawNet.Mcp.Server` |
| **Code-only** (1 project) | `OpenClawNet.Tools.Memory` |

### `tests/` projects

| Location | Projects |
|----------|----------|
| **Both repos** (3 projects) | IntegrationTests, PlaywrightTests, UnitTests |
| **Plan-only** (2 projects) | `E2ETests` (1589 files), `Tests.Fixtures` |

### Critical divergence: `OpenClawNet.Skills`

The Skills project has **massively diverged**:
- **Plan repo** — full implementation: registry, import service, semantic ranker, approval auditor, logging, system skills seeder (~60 files including build output)
- **Code repo** — minimal stub: `FileSkillLoader`, `SkillParser`, `SkillDefinition`, `SkillContent`, `ISkillLoader` (~5 source files)

### Solution files

Both repos have `OpenClawNet.slnx`. The plan repo includes `E2ETests`; the code repo includes `Tools.Memory`. Otherwise identical project references.

### CI / workflows

- **Code repo:** `deploy-pages.yml` (GitHub Pages from `_site/`)
- **Plan repo:** `squad-ci.yml` (disabled), `tool-e2e-nightly.yml`, `publish-session.yml`, plus 10+ disabled squad workflows

## Options Considered

### (a) Consolidate to code repo ✅ CHOSEN

Move all `src/` and `tests/` from plan repo into code repo. Plan repo becomes documentation + squad config only.

**Pros:** Single source of truth, no double-PRs, unified CI, simpler onboarding.  
**Cons:** Migration effort, temporary PR disruption, plan repo loses runnable state.

### (b) Codify the split

Formalize rules: "incubating projects live in plan repo; promoted projects in code repo."

**Pros:** No migration needed.  
**Cons:** Legitimizes an accident, requires ongoing promotion ceremony, doesn't solve the Skills divergence, adds process.

### (c) Status quo

Document the quirk and move on.

**Pros:** Zero effort.  
**Cons:** Double-PRs continue, divergence grows, new contributors confused.

## Decision

**Option (a): Consolidate to code repo.**

## Migration Plan

> ⚠️ Migration is NOT executed in this PR. A separate follow-up issue tracks the work.

### Phase 1 — Move plan-only projects to code repo
1. Copy `src/OpenClawNet.Mcp.Server` → code repo `src/`
2. Copy `tests/OpenClawNet.E2ETests` and `tests/OpenClawNet.Tests.Fixtures` → code repo `tests/`
3. Update code repo `OpenClawNet.slnx` to include new projects
4. Verify code repo builds and tests pass

### Phase 2 — Reconcile diverged projects
1. **Skills:** Replace code repo's stub with plan repo's full implementation. Verify all downstream references.
2. **Tools.Memory** (code-only): Confirm it's not a duplicate of plan's Memory tooling; keep in code repo.

### Phase 3 — Remove source from plan repo
1. Delete `src/` and `tests/` from plan repo
2. Remove `OpenClawNet.slnx` and `Directory.Build.props` from plan repo (or keep as read-only pointers)
3. Update plan repo CI workflows (remove build steps, keep doc/session workflows)
4. Add `README.md` in plan repo root explaining "source code lives in elbruno/openclawnet"

### Phase 4 — Cleanup
1. Update squad agent charters to reference code repo for builds/tests
2. Archive or redirect plan repo's disabled CI workflows
3. Update contributing guide

## Promotion Criteria (N/A)

Since we chose consolidation, there is no "promotion" process. All code lives in one repo from day one.

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Open PRs targeting plan repo `src/` | Coordinate timing; announce migration window |
| Git blame lost on moved files | Use `git log --follow`; add migration commit message documenting provenance |
| Squad agents expect to build in plan repo | Update agent tooling to clone/reference code repo |
| E2E test infrastructure tied to plan repo secrets | Migrate secrets to code repo; update workflow references |

## References

- Issue: elbruno/openclawnet-plan#116
- Double-PR incidents: #110 + #112
- Dylan's workaround: #115
- Irving's flags: issue comments on #104, #106

## Migration Log

| Project/File | Moved to code repo | Code PR | Date |
|---|---|---|---|
| `Mcp.Abstractions/IMcpProcessIsolationPolicy.cs` | ✅ | [openclawnet#21](https://github.com/elbruno/openclawnet/pull/21) | 2026-05-03 |
| `OpenClawNet.Storage*` | ✅ | [openclawnet#23](https://github.com/elbruno/openclawnet/pull/23) | 2026-05-04 |


| `OpenClawNet.Channels*`, `OpenClawNet.Services.Channels*` | OK | [openclawnet#22](https://github.com/elbruno/openclawnet/pull/22) | 2026-05-04 |

| 2026-05-04 | #118 (rounds 4-7) | Big-bang | Skills, Gateway, Agent, Web, all Models.*, all Mcp.* (except Abstractions), all Tools.* (except Memory + Abstractions), all Services.*, Adapters.Teams, AppHost, ServiceDefaults, UnitTests, IntegrationTests, E2ETests | After staged migration repeatedly broke builds due to Skills/Gateway/Agent/Web entanglement, combined into single PR. Closes #124, #125, #126, #127. | openclawnet#25 (merged), openclawnet-plan#NN (this PR) |
