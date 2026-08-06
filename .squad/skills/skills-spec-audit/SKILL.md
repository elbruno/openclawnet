---
name: skills-spec-audit
description: Audit a project's skill/extension subsystem against an open spec (e.g. agentskills.io), the framework that supposedly implements it (e.g. MAF AgentSkillsProvider), and an external skill repo (e.g. awesome-copilot). Use when asked to evaluate alignment, divergence cost, progressive-disclosure correctness, external-import compatibility, and folder-organization choices.
category: analysis
tags:
  - skills
  - spec-alignment
  - progressive-disclosure
  - openclaw
  - maf
enabled: true
---

# Skills Spec Audit — Reusable Pattern

@extracted: 2026-04-26, petey, from OpenClawNet skills domain analysis  
@validated-by: petey (high), mark (high)

When asked to "review and propose an improved plan" for a skills/plugin/extension subsystem, run this five-pass audit. It produced `petey-skills-domain-analysis.md` for OpenClawNet on 2026-04-26 and is reusable for any framework that loads pluggable agent capabilities from disk.

## Pass 1 — Find every loader

Grep for all classes that read skill/plugin files. Don't trust the first one you find. OpenClawNet had **two parallel loaders** (`FileSkillLoader` for REST/UI + MAF's `AgentSkillsProvider` for the runtime) that didn't share state. The bundled skills on disk were only seen by the runtime; the UI loader's default roots pointed nowhere. **Always confirm which loader actually feeds the model — that's the source of truth; everything else is a UI mirage.**

## Pass 2 — Spec alignment table

Build a table with one row per spec field (`name`, `description`, `license`, `compatibility`, `metadata`, `allowed-tools`, body length…) and three columns: spec rule, current behavior, divergence cost. Flag silent failures (catch-all `try/catch` around the parser) — they're worse than loud ones because users see "installed" with broken behavior.

## Pass 3 — Progressive-disclosure stage map

agentskills.io defines four stages: **Advertise → Load → Read resource → Run script.** Map each one to the current code:

- Advertise: are only name+description injected, or the full body?
- Load: is there a `load_skill` tool, or is the body always present?
- Resource/Script: are `references/`, `assets/`, `scripts/` directories even read?

If the framework already implements all four (MAF does), the gap is usually "our parallel loader bypasses the framework." Recommend deletion, not duplication.

## Pass 4 — External-import matrix

For each plausible external source, decide: **ingest as-is, adapter needed, or out-of-scope.** Don't assume "compatible with X" means "all of X" — awesome-copilot has six primitive types (agents, instructions, skills, plugins, hooks, workflows) and only one is "skills." Marketplace endpoints that download a single file lose bundled assets. Subtree-aware install (zipball or sparse-checkout, pinned to commit SHA) is the right shape.

## Pass 5 — Organization recommendation

When asked "shared / per-agent / levels?", take a position. Defaults that work for an always-on personal assistant:

- **Tiered layout** under one storage root: `built-in/` (shipped, upgradable), `installed/` (marketplace), `user/` (hand-authored, never overwritten).
- **Per-agent overlay seam reserved from day 1**, even if not shipped — mirrors the storage layer's per-agent scoping seam.
- Explicit precedence (`user > installed > built-in`) and shadowing badges in the UI.

## Capability-gap checklist (always include)

Versioning, dependency declarations, parameter schemas / `allowed-tools`, side-effect declaration (idempotent? destructive?), bundled-asset preservation on install, signature/SHA verification, audit events on activation, conflict resolution between tiers, persistence of enable/disable state.

## Output shape

Single markdown file with these sections in order: TL;DR, Spec Alignment (a/b/c), Progressive Disclosure Check, External-Import Matrix, Folder-Organization Recommendation, 5–7 Capability Gaps, UX Improvements, Summary table of recommendations. No code changes — analysis only. Mark/lead arbitrates implementation order in a follow-up proposal cycle.
