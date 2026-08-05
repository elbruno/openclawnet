---
name: external-bundle-threat-model
description: "Reusable threat-model checklist for any feature that imports third-party content bundles (skills, plugins, prompt packs, MCP servers, model weights) into an LLM agent runtime."
category: hardening
tags:
  - hardening
  - threat-model
  - supply-chain
  - prompt-injection
  - drummond
enabled: true
---

# External Bundle Import — Threat Model Skill

@extracted: 2026-05-22, drummond, from awesome-copilot skill-import review  
@validated-by: drummond (high), petey (high), mark (medium)

Use this when reviewing **any** feature that pulls third-party content from a remote source (Git repo, registry, URL) and makes it available to an LLM agent. Originally distilled from the OpenClawNet `awesome-copilot` skill-import review (2026-05-22).

## When this applies

- Importing skills, plugins, prompt packs, agent personas, tools, MCP servers, model weights, embeddings, RAG corpora, or any artifact whose contents will be **read by, executed by, or injected into the prompt of** an LLM agent.
- Whether or not the source org is "trusted" by name. (Org reputation does not blunt prompt injection.)

## The trust statement to anchor on

> Any byte that did not originate in our own source tree is **untrusted**. The LLM cannot distinguish "imported content" from "system rules." Therefore imported content == arbitrary instruction execution at the prompt layer.

If the team has not internalized that sentence, stop the review and have the conversation first.

## The eight questions every import feature must answer

1. **Provenance.** Source URL pinned to an immutable identifier (commit SHA, content hash) — never `main`, never `latest`, never a moving tag?
2. **Integrity.** Bundle SHA-256 stored at install time and re-verified on every load? Drift triggers quarantine, not silent reload?
3. **Bundle file-type allowlist.** Explicit allowlist of permitted file extensions and content types, with explicit deny for executables (`.py .ps1 .sh .js .ts .cs .dll .exe .so .pyc .onnx .pickle .gguf .safetensors`), things with the executable bit, things with `MZ` / `#!` magic? Bundle-level fail-closed when an unknown type appears?
4. **Containment.** All writes through the project's single safe-path resolver, rooted in storage root, with name allowlist (`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`), reserved-name reject, symlink/junction reject?
5. **Approval gate.** Two-step preview-then-confirm with a UI that shows: source, pinned ref, full file list with sizes and per-file hashes, rendered content, diff vs prior version, and a prominent "this becomes trusted instructions to the agent" warning? Never single-call install.
6. **Identifier namespace.** Imported items get a namespaced identifier (`<source>/<name>`) so they cannot shadow built-ins. Hard-fail on collision; never silent overwrite.
7. **Update policy.** No background auto-update. Updates re-run the full approval gate with diff. Reload from local disk only — load path must not touch the network.
8. **Revocation latency.** Disable / kill-switch takes effect within one agent turn. Authoritative state in DB, not in-memory. Global kill-switch returns empty for everything regardless of per-item state.

## Audit minimums

- **Lifecycle:** install / update / uninstall / quarantine / enable / disable — actor, source, pinned ref, bundle hash, outcome.
- **Load:** every process boot and every reload — id, manifest hash, on-disk hash, match boolean (mismatch ⇒ quarantine + WARN).
- **Compose / inject:** per turn — run id, agent id, ordered list of injected items with their version hashes and token contributions, kill-switch state.
- **Invoke (if executable):** parameters (redacted), file ops, network egress, exit code, resource use.
- **Never log:** the imported content body itself (it's attacker-controlled — log hash + length); user chat; secrets / tokens / key material.

## Network fetch hardening (default)

- HTTPS only. Reject `http://`, `file://`, `git+ssh://`, etc.
- Host allowlist. v1: name the exact hosts.
- 30s timeout. ≤ 4 MB total response. ≤ 3 redirects.
- No credentials sent on cross-host redirect.

## Resource budgets

- Per-file size cap. Per-bundle file count cap. Per-bundle total size cap.
- For prompt-injected content: per-item token cap **and** per-agent total injected-token budget. Drop oldest with a WARN audit when over budget.

## Shared vs per-X storage decision

Default to **shared storage with per-consumer enablement** (cheaper, single source of truth, single approval per update) **iff** all of: containment, namespaced identifiers, default-disabled-on-import, per-consumer enablement in DB, one-turn revocation are in place. Otherwise fall back to per-consumer storage.

Per-consumer (per-agent / per-tenant) storage is appropriate when: blast-radius isolation matters more than disk cost, content can carry consumer-specific secrets, or different consumers must pin different versions of the same item.

## v1 scope discipline (the brake)

For any new third-party import feature, the most-restrictive viable v1 is roughly:

- Static, declarative content only (no scripts, no binaries, no executables).
- One allowlisted source.
- Commit-SHA pinned at install. No auto-update.
- Two-step preview/confirm install. Default-disabled per consumer.
- Audit on lifecycle / load / compose. Global kill-switch present and prominent.
- "Add a new source" is an `appsettings` edit, not a UI gesture, in v1.

Loosening any of these is a follow-up proposal with its own review — not a v1 addition.

## Verdict template

**APPROVE-with-changes** when direction is right and S-style invariants are accepted as acceptance criteria.
**REJECT** when the implementation as written creates a remote-prompt-injection or RCE primitive that would survive merge — even if the *intent* is sound. Direction can be approved separately from the mechanism.

## Provenance

Distilled from `docs/proposals/storage-location-hardening-review.md` (storage H-1..H-8) and `.squad/decisions/inbox/drummond-skills-hardening-review.md` (skill-import S-1..S-12), OpenClawNet, 2026-05.
