# Decision: Retire AppHostFixture / AppHostCollection / PlaywrightTestBase

**Author:** Irving (Backend Dev)  
**Date:** 2026-05-25  
**Status:** Executed  

---

## Context

The `feat/aspirehostfixture-phase1` migration (Waves 3a–3c) moved all Playwright tests from
`[Collection("AppHost")]` → `[Collection("AspireHost")]` and from `PlaywrightTestBase` →
`AspireHostPlaywrightTestBase`. After Wave 3c, the three AppHost-only files had zero live consumers.

## Decision

**Retire** the following files:

| File | Reason |
|---|---|
| `tests/OpenClawNet.PlaywrightTests/AppHostFixture.cs` | No `[Collection("AppHost")]` test remained; `AspireHostFixture` is the canonical replacement |
| `tests/OpenClawNet.PlaywrightTests/AppHostCollection.cs` | `[CollectionDefinition("AppHost")]` had no consumers |
| `tests/OpenClawNet.PlaywrightTests/PlaywrightTestBase.cs` | No test class extended it; all use `AspireHostPlaywrightTestBase` |

**Update** doc-comment references in `Demos/AttachedAspireTestBase.cs` (4 instances) from
`AppHostFixture` → `AspireHostFixture`.

**Update** manual doc references in `docs/manuals/35-website-watcher-e2e.md` and
`docs/manuals/images/02-hello-world/README.md`.

## Rationale

Removing dead infrastructure prevents future contributors from inheriting or accidentally using
the deprecated fixtures. `AppHostFixture` used `DistributedApplicationTestingBuilder` (which
conflicts with the `aspire start` / `aspire describe` pattern); its retention after all test
migration was complete created confusion.

## Verification

- Zero `error CS` compiler errors after deletion
- `scripts\test-and-publish.ps1 -SkipTests` ✅ pipeline complete
- `grep -r "AppHostFixture|Collection(\"AppHost\")|PlaywrightTestBase"` — only historical/non-functional
  references remain (agent history files, analysis docs)
