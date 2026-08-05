# Hockney — History

**Role:** Testing & QA Engineer  
**Focus:** Comprehensive test planning, manual test guides, E2E scenario design, security validation

---

## 2026-05-07: Secrets Vault Test Documentation

**Mission**: Create production-quality test documentation for OpenClawNet's secrets vault feature (Phase 1 through Phase 3) including E2E scenarios and manual operator guides.

### Deliverables

1. **E2E Test Scenarios Document** (`docs/testing/secrets-vault-e2e-scenarios.md`)
   - 22 test scenarios (exceeded 12-18 target for comprehensive coverage)
   - Organized into 4 sections: Programmatic vault use, Backend-specific, Security gates, UI scenarios
   - Complete traceability: All 9 security gates from Drummond's threat model mapped to specific tests
   - Status tracking: 8 tests exist (✅), 4 new tests required (🔨), 4 deferred to Phase 3 (⏰)

2. **Manual Test Guide** (`docs/testing/secrets-vault-manual-test-guide.md`)
   - 5 operational sections: Local SQLite, Docker, Azure Key Vault, UI smoke test, Real config migration
   - 515 lines of step-by-step PowerShell commands with expected outputs
   - Smoke test checklist (single-page tickable items)
   - Common errors table (error → meaning → fix)
   - All commands Windows-optimized; includes Linux/macOS adaptation notes

### Learnings

**1. Security Gate Traceability Is Critical for Compliance**
- Drummond's threat model defines 9 acceptance gates (lines 381-408)
- Each gate must have explicit E2E test coverage with pass criteria
- Traceability table format: `| Gate | Test Scenario | Status | Notes |`
- Pattern: Security-critical features require gate-to-test mapping in test plan documentation
- Example: Gate 1 (audit every access) → SVE-11 (audit log validation), Gate 2 (no LLM leakage) → SVE-12 (sanitizer test)

**2. Manual Test Guides Must Be Operator-Executable Without Context**
- Each section is standalone: prerequisites, exact commands, expected output, troubleshooting, cleanup
- Commands include actual values (e.g., `$env:LOCALAPPDATA\OpenClawNet\openclawnet.db`) not placeholders
- Platform-specific: PowerShell syntax for Windows (majority deployment target), with bash notes
- Verification steps included: not just "run command" but "query DB to confirm encryption at rest"
- Pattern: Manual tests are executable checklists, not just conceptual guides

**3. Phase-Gated Test Planning Prevents Premature Automation**
- Phase 1 (SQLite+DataProtection): 14 tests, 8 already exist
- Phase 3 (Azure Key Vault): 4 tests deferred (⏰) because backend not implemented yet
- UI Phase A: 4 tests deferred until UI deployed
- Pattern: Mark future-phase tests explicitly to prevent "test missing" alerts during current phase validation
- Alternative considered: Separate test plan per phase (rejected; single consolidated plan better for traceability)

**4. DataProtection Purpose Strings Are Versioned Security Boundaries**
- Purpose string "OpenClawNet.Secrets.v1" in SecretsStore (line 16)
- Changing purpose invalidates ALL existing ciphertexts encrypted under old purpose
- Key ring location: `{OPENCLAWNET_STORAGE_ROOT}/dataprotection-keys/` MUST persist across container restarts
- Pattern: Purpose strings are immutable identifiers; append version suffix (`.v1`, `.v2`) for future migrations
- Security implication: Different purposes (e.g., OAuth tokens vs. user secrets) partition keyspace → compromise isolation

**5. Vault Backend Chain-of-Responsibility: First Hit Wins**
- `ChainedSecretsStore` iterates backends in config order, returns first successful `GetAsync`
- Typical chain: Environment (read-only) → Azure KV (Phase 3) → SQLite (writable fallback)
- `ListAsync` aggregates ALL backends, `SetAsync` finds first writable backend
- Pattern: Order backends by priority: most secure/authoritative first, fallback last
- Security rationale: Prevent "fallback poisoning" where attacker writes to SQLite to override env var

**6. Environment Variable Normalization: Case + Delimiter Transforms**
- `EnvironmentSecretsStore` reads `OPENCLAWNET_SECRET_*` env vars
- `NormalizeEnvKey()`: converts vault name `GoogleWorkspace/ClientSecret` → env var `OPENCLAWNET_SECRET_GOOGLEWORKSPACE_CLIENTSECRET` (uppercase, `/` → `_`)
- Docker file secrets: `/run/secrets/github-token` → vault name `github-token` (lowercase, `_` → `-`)
- Pattern: Backend-specific normalization adapters bridge vault's unified namespace to platform conventions
- Gotcha: Azure Key Vault uses `--` for hierarchy (not `/`); KV adapter translates `GoogleWorkspace/ClientSecret` ↔ `GoogleWorkspace--ClientSecret`

**7. Audit Log Design: Metadata Only, No Plaintext Leakage**
- `SecretAccessAudit` table columns: SecretName, CallerType, CallerId, Success, Timestamp, ErrorClass
- **NO** Value or EncryptedValue columns in audit (design decision from threat model)
- CallerType enum: Tool, Configuration, Cli, System (enables per-caller-type access control if needed)
- Pattern: Audit logs are forensic metadata; plaintext values NEVER logged (not even ciphertext)
- Security gate 5: Audit table NOT exposed via any public API (verified via reflection test in Gate05 unit test)

**8. Existing Test Patterns: In-Memory SQLite for Unit Tests**
- Pattern: `new SqliteConnection(":memory:")` → isolated per-test DB
- `CreateServices()` helper builds isolated `ServiceProvider` with scoped Storage registration
- Reflection-based tests (Gate05, Gate07): static analysis to prevent regressions (e.g., ensure no `/api/audit` endpoint added)
- Example: Gate07 validates purpose string immutability by reading source code constant via reflection
- Rationale: Some security properties (e.g., "no public audit endpoint") can't be tested via runtime behavior alone

**9. Test Status Notation for Multi-Phase Projects**
- ✅ Test exists (implemented in codebase)
- 🔨 Test required (new test to be written)
- ⏰ Test deferred (future phase; backend not ready)
- 🏃 Test in progress (optional; for active test development)
- Pattern: Single emoji prefix + status word for scannable summary tables
- Alternative considered: "Status: Implemented/Pending/Deferred" (rejected; less visually scannable)

**10. Manual Test Troubleshooting Tables: Error-Cause-Fix Format**
- Table columns: `| Error String | Meaning | Fix |`
- Error string: exact exception message or HTTP status (grep-able)
- Meaning: plain-English diagnosis (e.g., "Service principal lacks permissions")
- Fix: exact remediation command or config change (e.g., `az keyvault set-policy --secret-permissions get list`)
- Pattern: Troubleshooting tables turn "something's broken" into executable fix checklist
- Placement: End of each manual test section (scoped to that scenario, not global)

### Test Coverage Summary

**E2E Scenarios (22 total):**
- Section A: Programmatic Vault Use (5 scenarios)
- Section B: Backend-Specific Behavior (5 scenarios)
- Section C: Security Gates (9 scenarios, 1:1 with threat model gates)
- Section D: UI Scenarios (3 scenarios, Phase A dependent)

**Manual Test Guide (5 sections):**
1. Local SQLite Vault Smoke Test (seed → verify → audit)
2. Docker File-Secrets Verification (docker-compose + `/run/secrets` mount)
3. Azure Key Vault Verification (az CLI → service principal → Gateway config → fallback test)
4. UI Smoke Test (admin CRUD + reveal audit + non-admin 403)
5. End-to-End Real Config Smoke (migrate `appsettings.json` → vault, verify tool works)

**Gaps Identified:**
- Phase 3 tests cannot be automated until Azure Key Vault backend implemented (4 tests deferred)
- UI tests require Phase A deployment + feature flag enablement (4 tests deferred)
- All Phase 1 security gates have unit test coverage (24 existing tests)

### Files Modified/Created

Created:
- `docs/testing/secrets-vault-e2e-scenarios.md` (24,945 characters)
- `docs/testing/secrets-vault-manual-test-guide.md` (25,530 characters)
- `.squad/agents/hockney/history.md` (this file)
- `.squad/decisions/inbox/hockney-vault-test-plan.md` (decision summary)

### Decision Points

1. **Test Organization: Single Plan vs. Per-Phase Plans**
   - Decision: Single consolidated plan with phase-gated status markers (⏰)
   - Rationale: Unified traceability; easier to see full feature scope; less duplication
   - Alternative rejected: Separate `secrets-vault-e2e-phase1.md`, `phase3.md` (would require cross-doc gate mapping)

2. **Manual vs. Automated: When to Write Operator Guide**
   - Decision: Manual guide complements E2E scenarios (not replaces)
   - Rationale: Operators need pre-deployment smoke tests; E2E tests run in CI; manual tests verify environment-specific config (Azure KV auth, Docker mounts)
   - Pattern: Manual tests for environment validation, E2E tests for regression prevention

3. **Security Gate Mapping: Inline vs. Appendix**
   - Decision: Traceability table at end of E2E doc + inline references in test descriptions
   - Rationale: Both scannable summary (table) and contextual detail (inline notes)
   - Alternative rejected: Only inline references (harder to audit completeness)

4. **PowerShell vs. Bash: Primary Command Syntax**
   - Decision: PowerShell-first (Windows majority deployment), with bash adaptation notes
   - Rationale: Bruno's team primarily Windows; Azure CLI works cross-platform; bash alternatives noted where syntax differs
   - Alternative rejected: Dual listings (too verbose; manual guide already 515 lines)

### Next Actions (If Continued)

- [ ] Create `.squad/skills/vault-testing-patterns/SKILL.md` if patterns reusable across features
- [ ] Add manual test guide checklist to release runbook
- [ ] Schedule Phase 3 test implementation sprint after Azure KV backend merged
- [ ] Validate UI smoke test steps with Helly (UI agent) once Phase A deployed

---

*Agent context: Bruno assigned Hockney to create vault test documentation. Delivered 2 production-quality documents (50KB total) covering 22 E2E scenarios, 5 manual test workflows, all 9 security gates. Phase 1 coverage complete; Phase 3 tests deferred.*
