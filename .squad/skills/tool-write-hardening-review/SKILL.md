# Skill — Hardening Review of Tool-Write Surfaces (.NET agent platforms)

@extracted: 2026-05-21, drummond, from storage-location proposal day-1 review  
@validated-by: drummond (high), petey (high), mark (high)

**Owner:** Drummond
**Origin:** Day-1 review of OpenClawNet's storage-location proposal (2026-05-21).
**When to use:** Any proposal or PR that introduces, broadens, or relocates a path that an LLM-driven tool will write to (FileSystemTool-equivalents, model-download caches, upload sinks, export sinks, MCP filesystem wrappers).

---

## The 8-point hardening checklist

Run every tool-write surface through these. If any one fails, the surface is not ready.

### 1. Containment is fail-closed by default
- Are LLM-supplied **absolute** paths accepted unchanged? → ❌ reject.
- Is the only defense a substring blocklist of "known bad" filenames? → ❌ reject; substring blocklists are trivially bypassed.
- Is there an explicit user-configured allowlist of additional writable roots? → ✅.

### 2. Single resolver, single sanitizer
- Does the tool call `Path.GetFullPath` / `Path.Combine` directly on input? → ❌. All resolution must funnel through one tested `ISafePathResolver`.
- Is the same resolver reused by every tool that takes a path? → ✅.

### 3. Reparse points / symlinks / junctions
- Does the resolver call `FileInfo.ResolveLinkTarget(returnFinalTarget: true)` on the final path **and every parent segment**, then re-check containment? → ✅.
- Can the tool itself create symlinks? → ❌ forbid.

### 4. Boundary-safe prefix check
- Pattern: `Path.TrimEndingDirectorySeparator(root)` + `path == root || path.StartsWith(root + Path.DirectorySeparatorChar, OrdinalIgnoreCase)`.
- Anti-pattern: `path.StartsWith(root, OrdinalIgnoreCase)` — vulnerable to `C:\foo` vs `C:\foo-evil` prefix collisions.
- Regression test: name-collision case must be in the test corpus.

### 5. Name allowlist for user-/agent-supplied path segments
- Allowlist regex (default): `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`.
- Reject: Windows reserved device names (CON/PRN/AUX/NUL/COM1-9/LPT1-9, case-insensitive), trailing dot, trailing space, leading dot, control characters.
- Anti-pattern: substring denylist of `..`, `/`, `\` — misses reserved names, Unicode lookalikes, control chars, trailing dot/space.

### 6. Per-tenant / per-agent scope seam
- Even if you don't ship per-agent isolation today, the resolver API must accept a `scopeRoot` parameter so a future runtime can narrow the boundary without an API break.

### 7. ACL hardening at directory creation
- Auto-create is OK only if you also **verify ACLs on every startup** (an attacker or prior install may have pre-created the dir with weak permissions, making your `CreateDirectory` a silent no-op).
- Credential-bearing subdirs (`dataprotection-keys/`, `vault/`, `tokens/`): explicit DACL = current user + SYSTEM Full Control, no inheritance (Windows); `chmod 0700` (POSIX). Refuse to start credential services if the check fails.
- Top-level dirs at volume root (e.g. `C:\foo`) inherit `Users:(OI)(CI)M` on most Windows boxes — multi-user-readable by default. Prefer `%LOCALAPPDATA%` for default placement.

### 8. Audit every write
- Successful write must emit: agent id, resolved absolute path, byte length, SHA-256 of content, source (LLM-suggested vs user-explicit), correlation/run id.
- Failed write (blocked / traversal / ACL-denied) audited at WARN with the unresolved input string for forensics.
- Without this, "what did the agent write to disk last week?" is unanswerable.

---

## Bonus checks for proposals (not just code)

- **Process-wide env vars** (`OLLAMA_MODELS`, `HF_HOME`, `TRANSFORMERS_CACHE`, etc.): set them in the orchestrator (Aspire AppHost) at resource-definition time, not at runtime in a service. Runtime mutation leaks into sibling processes.
- **Config-write endpoints** (`PUT /api/.../location`): authn/authz gate + server-side re-validation at write time, not only at PUT-time. The validator denylist must run twice.
- **Multiple env var names for the same setting** (`OPENCLAWNET_STORAGE_ROOT` vs `OPENCLAW_STORAGE_DIR`): pick one, document, explicitly ignore others. Two names = silent redirect attack surface.
- **Two-phase migration over destructive auto-move**: when changing default paths, log a migration notice and pin the old value in config; never silently move user data.
- **Fuzz corpus for any new resolver**: `..` segments, mixed `/` and `\`, UNC (`\\?\C:\...`, `\\.\PhysicalDrive0`), reparse points, reserved names, prefix-collision strings, NUL/control chars, very long paths (>260 / >32K), `file://` URIs.

---

## Verdict heuristics

- **APPROVE-with-changes** when the structural direction is right and the gaps are concrete, listable invariants. Pair with named invariants the implementer must satisfy. Default choice — preserves momentum and keeps the original author in the loop (Reviewer Rejection Lockout).
- **REJECT** only when the proposal cements an architectural anti-pattern that invariants cannot patch around (e.g., distributing path-resolution logic across N tools by design, no single resolver possible).
- **DEFER** when more threat-model context is needed before the call can be made (e.g., the deployment topology / tenancy model isn't decided yet and changes the answer).
