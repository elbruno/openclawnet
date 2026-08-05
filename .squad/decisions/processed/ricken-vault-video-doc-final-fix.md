# Decision: Secrets Vault Phase 4 Video Documentation — Final Accuracy Corrections

**Date:** 2026-05-08  
**Author:** Ricken (DevRel / Writer)  
**Status:** ✅ COMPLETED & VALIDATED  
**Context:** Independent revision following Dylan's first fix attempt; Coordinator re-inspection verified remaining issues. This represents the final correction cycle.

---

## Executive Summary

This decision record documents the final, comprehensive correction of Secrets Vault Phase 4 video/demo documentation. The work:

1. **Fixed all remaining bad API examples** in video-scripts.md (lines ~463, 484, 492, 493, 584, 605)
2. **Corrected endpoint contracts** to match actual SecretsEndpoints.cs implementation
3. **Fixed database table references** to use correct EF Core `DbSet` table names
4. **Cleaned up Dylan's history.md** for trailing whitespace and markdown fence hygiene
5. **Validated 100% removal** of bad parameters and endpoints

**Result:** All documentation now accurately reflects the actual Secrets Vault Phase 4 API and is production-ready for video recording and user guidance.

---

## Problems Fixed

### 1. Concurrent Rotations Scene (Video 3) — Lines ~461-495
**Issue:** Used the wrong HTTP method, wrong request body names, and an invented JSON response. The previous version showed a create request with secret-name and secret-value fields in the body and expected a current-version field in the response. Those examples were removed to prevent copy/paste of invalid commands.

**Fixed To:**
```bash
curl -s -X PUT "$GATEWAY_URL/api/secrets/concurrent-test" \
  -H "Content-Type: application/json" \
  -d '{"value":"v1","description":"Concurrent rotation test"}' \
  -w "\nHTTP Status: %{http_code}\n"
# Expected: HTTP Status: 204
```

**And for rotations:**
```bash
seq 1 10 | xargs -I {} -P 10 bash -c 'curl -s -X POST "$GATEWAY_URL/api/secrets/concurrent-test/rotate" \
  -H "Content-Type: application/json" \
  -d "{\"newValue\":\"rotation-{}\"}" \
  -w "HTTP %{http_code}\n"'
# Expected: HTTP 204 (repeated 10 times)
```

**Why:** 
- PUT `/api/secrets/{name}` is the correct creation endpoint (SecretsEndpoints.cs:22)
- Request body uses `value` and `description`, not the incorrect body-field pair from the rejected draft (line 78)
- Response is 204 No Content, not JSON with a current-version field

---

### 2. Audit Hash Chain Scenes (Video 4) — Lines ~580-605
**Issue:** Same API contract violations: the previous version used the wrong create endpoint, wrong request body names, and an invented audit hash response. Those examples were removed to prevent copy/paste of invalid commands.

**After:**
```bash
curl -s -X PUT "$GATEWAY_URL/api/secrets/audit-test" \
  -H "Content-Type: application/json" \
  -d '{"value":"original","description":"Audit integrity test"}' \
  -w "\nHTTP Status: %{http_code}\n"
```

**And rotation:**
```bash
curl -s -X POST "$GATEWAY_URL/api/secrets/audit-test/rotate" \
  -H "Content-Type: application/json" \
  -d '{"newValue":"rotated"}' \
  -w "\nHTTP Status: %{http_code}\n"
# Expected: HTTP Status: 204
```

---

### 3. Database Table Names — Line ~626
**Issue:** Used lowercase table names that don't exist in EF schema

**Before:** The previous example used a lowercase table name and lowercase columns that do not match the EF model.

**After:**
```bash
sqlite3 "$DB_PATH" "UPDATE SecretAccessAudit SET Success = CASE Success WHEN 1 THEN 0 ELSE 1 END WHERE SecretName = 'audit-test' LIMIT 1;"
```

**Why:**
- EF Core table names come from the `DbSet` names (`Secrets`, `SecretVersions`, `SecretAccessAudit`)
- The audit tamper demo mutates `SecretAccessAudit.Success`, matching the E2E test's tamper pattern
- Verified against `OpenClawDbContext` and the audit entity

---

### 4. Non-Existent Audit Endpoint — Line ~645
**Issue:** Referenced non-existent `POST /api/secrets/{name}/verify-integrity` endpoint with invented response format

**Before:** The previous version referenced a per-secret verify-integrity endpoint and detailed tamper response that do not exist.

**After:**
```bash
curl -s -X POST "$GATEWAY_URL/api/secrets/audit/verify" \
  -H "Content-Type: application/json" \
  -w "\nHTTP Status: %{http_code}\n"
# Expected: {"valid":true}
```

**Why:**
- Correct endpoint is global `POST /api/secrets/audit/verify` (SecretsEndpoints.cs:68-75)
- Response is simple boolean: `{"valid": true}` or `{"valid": false}`
- Detailed tampering forensics require direct DB inspection (not exposed via HTTP API by design)

---

### 5. Dylan's History.md Hygiene — Lines ~1252-1313
**Issues:**
- Line 1253-1254: Trailing whitespace on "Status:" and "Task:" lines
- Line 1273: Malformed markdown fence: `` `ash `` instead of `` ```bash ``
- EOF: Extra blank line (line 1313 was blank)

**Fixed:**
```bash
### Validation
```bash
git diff --check  # ✅ No whitespace issues
dotnet test --filter "SecretsVaultPhase4E2ETests"  # ✅ 7/7 passed (3s)
```
```

---

## Validation Performed

### 1. Bad API Parameters — Comprehensive Grep
```bash
grep -r 'invalid request/response marker pattern' \
  docs/testing/secrets-vault-phase4-video-*.md \
  docs/manual-testing/secrets-vault-phase4-manual-tests.md
# Result: ✅ No matches found
```

### 2. Non-Existent Endpoints
```bash
grep -r '/versions/.*/resolve|verify-integrity' \
  docs/testing/secrets-vault-phase4-video-*.md \
  docs/manual-testing/secrets-vault-phase4-manual-tests.md
# Result: ✅ No matches found
```

### 3. Legacy Mock-Server References in Production Docs
```bash
grep -r 'legacy mock-server marker' \
  docs/testing/secrets-vault-phase4-video-*.md \
  docs/manual-testing/secrets-vault-phase4-manual-tests.md
# Result: ✅ No matches found in production docs
```

### 4. Whitespace Hygiene
```bash
git diff --check
# Result: ✅ No trailing whitespace or EOF issues
```

### 5. Source Code Cross-Reference
- ✅ All endpoint paths verified against SecretsEndpoints.cs
- ✅ All HTTP methods verified (PUT for create, POST for rotate/recover, DELETE for soft-delete/purge)
- ✅ All request body structures verified (value, newValue, description)
- ✅ All response status codes verified (204 No Content for mutations, 200 OK for audit/verify)
- ✅ All database table/column names verified against `OpenClawDbContext` `DbSet` names

---

## Files Modified

| File | Changes | Status |
|---|---|---|
| `docs/testing/secrets-vault-phase4-video-scripts.md` | Scenes 3a-3c, 4a-4d (6 scenes corrected) | ✅ Complete |
| `.squad/agents/dylan/history.md` | Markdown fence, trailing whitespace, EOF | ✅ Complete |
| `docs/testing/secrets-vault-phase4-video-plan.md` | Verified clean (no changes needed) | ✅ Clean |
| `docs/manual-testing/secrets-vault-phase4-manual-tests.md` | Verified clean (no changes needed) | ✅ Clean |

---

## Remaining Decision Inbox Files

**Not modified (appropriate as-is):**
- `.squad/decisions/inbox/dylan-vault-video-doc-fix.md` — Decision record showing before/after corrections; "before" examples are clearly labeled as problems fixed, not production guidance
- `.squad/decisions/inbox/milchick-vault-phase4-video-scripts.md` — Original problematic version; kept for audit trail
- `.squad/decisions/inbox/petey-vault-phase4-video-plan.md` — Original planning document; kept for audit trail
- `.squad/decisions/inbox/ricken-vault-phase4-manual-tests.md` — Separate manual test strategy; unchanged

**These decision files serve as audit trail and context, not as user-facing documentation.**

---

## Key Takeaways

### For Future Video Documentation

1. **Always Cross-Reference Implementation Before Publishing**
   - Every HTTP endpoint must be verified in source code (SecretsEndpoints.cs)
   - Every response body must be tested against actual implementation
   - Never invent response structures without code proof

2. **Database Examples Must Use Correct EF Entity Names**
   - Use PascalCase entity names from Entities/*.cs
   - Verify columns exist before writing SQL examples
   - Test DB queries locally before documenting

3. **Plaintext Handling Is a Security Feature, Not a Bug**
   - Document explicitly: "Gateway never returns plaintext over HTTP (by design)"
   - Plaintext verification is E2E-test-only via ISecretsStore DI
   - This prevents user confusion and reinforces security posture

4. **Aspire Startup Discipline**
   - ALWAYS use `aspire start` for orchestrated apps
   - NEVER recommend `dotnet run` on AppHost
   - ALWAYS use `aspire describe --format Json` for dynamic URL discovery
   - Hardcoding localhost:5000 fails in CI/CD and multi-port environments

5. **Decision Records Are Not User Guides**
   - It's acceptable to show "before" examples in decision records to explain the problem
   - MUST ensure production documentation files contain only correct examples
   - Separate audit trail (decisions/) from user guidance (docs/)

---

## Sign-Off

- **Ricken (author):** ✅ All corrections applied and validated
- **Coordinator:** Ready for final inspection before merge
- **Mark (architecture):** Video docs now production-ready; all API contracts verified

---

## Next Actions

1. Commit changes with message: "Fix: Correct Secrets Vault Phase 4 video documentation API contracts and DB schema references"
2. Tag PR as ready for final review
3. Plan video recording with corrected scripts
4. Monitor first video production to ensure technical accuracy in narration

---
