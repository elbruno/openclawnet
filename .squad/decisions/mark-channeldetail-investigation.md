# ChannelDetail.razor ↔ Gateway DTO Shape Mismatch Investigation

**Investigator:** Mark (Lead)  
**Date:** 2026-04-24  
**Context:** Helly flagged a shape mismatch during the channels-jobs sprint; Bruno approved investigation before any fix.

---

## Executive Summary

**SEVERITY: CRITICAL** — ChannelDetail.razor is fundamentally broken.

The Razor page (line 159) deserializes the `/api/channels/{jobId}` response into a `ChannelDetailDto` that contains **`RecentRuns: List<ChannelRunSummaryDto>`**, but then immediately tries to access **`channelDetail.Artifacts: List<ArtifactDto>`** (line 163). This property **does not exist** on the DTO. The page will throw a **runtime NullReferenceException** or silent null when `_artifacts` is assigned.

Secondary: Even if the correct endpoint were called, the Razor page's local `ArtifactDto` record (defined in the page code-behind, lines 220–230) has **6 property name mismatches** with the Gateway's `ArtifactDto` record (ChannelsApiEndpoints.cs, lines 273–280).

---

## Mismatch Inventory

### PRIMARY MISMATCH: Missing `Artifacts` on ChannelDetailDto

| Razor Binding | DTO Field | Gateway ChannelDetailDto Status | Impact |
|---|---|---|---|
| `channelDetail.Artifacts` | `Artifacts: List<ArtifactDto>` | **MISSING** — DTO has `RecentRuns` instead | **NullReferenceException** — page crashes |
| `channelDetail.JobName` | `JobName` | ✅ Present | ✅ Works |

### SECONDARY MISMATCHES: ArtifactDto Property Names

| Razor Access | Razor DTO Field | Gateway DTO Field | Status | Impact |
|---|---|---|---|---|
| `artifact.RunId` | `RunId: Guid` | **MISSING** (not in Gateway ArtifactDto) | ⚠️ Silent null on line 35 |
| `artifact.ArtifactType` | `ArtifactType: string` | `Type: string` | ❌ Name mismatch | Deserialization fails or silent null |
| `artifact.ContentInline` | `ContentInline: string?` | `ContentPreview: string?` | ❌ Name mismatch | Deserialization fails or silent null |
| `artifact.ContentSizeBytes` | `ContentSizeBytes: long` | `SizeBytes: long` | ❌ Name mismatch | Deserialization fails or silent null |
| `artifact.CreatedAtUtc` | `CreatedAtUtc: DateTime` | `CreatedAt: DateTime` | ❌ Name mismatch | Deserialization fails or silent null |
| `artifact.Title` | `Title: string?` | `Title: string?` | ✅ Matches | ✅ Works |
| `artifact.Id` | `Id: Guid` | `Id: Guid` | ✅ Matches | ✅ Works |
| `artifact.MimeType` | `MimeType: string?` | `MimeType: string?` | ✅ Matches | ✅ Works |
| `artifact.ContentPath` | `ContentPath: string?` | **MISSING** | ⚠️ Silent null on line 72 |

---

## Blast Radius

### Where Does This Break?

1. **Runtime Failure:** Line 159–163 in ChannelDetail.razor
   - `ReadFromJsonAsync<ChannelDetailDto>()` deserializes successfully from the Gateway DTO.
   - Line 162 (`_channelName = channelDetail.JobName`) works fine.
   - Line 163 (`_artifacts = channelDetail.Artifacts`) **throws NullReferenceException** because `Artifacts` does not exist.
   - User sees a blank page + error logged in browser console.

2. **Silent Failures (if primary issue is patched):**
   - If someone manually patches line 159 to use the correct endpoint that returns artifacts, the Razor local `ArtifactDto` (lines 220–230) will deserialize with null values for mismatched properties:
     - `artifact.ArtifactType` → null (expects "Type" in JSON)
     - `artifact.ContentInline` → null (expects "ContentPreview" in JSON)
     - `artifact.ContentSizeBytes` → 0 (expects "SizeBytes" in JSON)
     - `artifact.CreatedAtUtc` → DateTime.MinValue (expects "CreatedAt" in JSON)
   - Pages will render but show no content (markdown, JSON, error, file links will all be invisible).

### Is the Page Reachable?

**Yes, the page route is defined and reachable** (`@page "/channels/{JobId:guid}"` line 1), but it **fails at runtime** when you navigate to it or when the polling timer fires (line 141).

### Did It Work Before Commit d010f33?

**No.** Looking at commit history:
- `f7bc624` (feat(channels): JobRunArtifact entity + auto-capture + REST endpoints) — introduced the endpoints.
- `d010f33` (fix(channels,jobs): repair DTO contracts) — **attempted** to repair ChannelSummaryDto but did NOT fix ChannelDetailDto.
  - Commit message notes: "Web/Razor pages bind to ChannelSummaryDto / JobDto / JobRunDto property names that had silently drifted from the Gateway DTO definitions."
  - Only ChannelSummaryDto was patched in d010f33 (LastActivity → LastActivityUtc, ArtifactCount → TotalArtifacts).
  - **ChannelDetailDto was left broken** — the property names in ChannelsApiEndpoints.cs were never aligned with the Razor expectations.

### Are Other Pages Affected?

- **ChannelsList.razor** (lines 36–38): Uses ChannelSummaryDto correctly ✅
  - Accesses: `context.JobName`, `context.LastActivityUtc`, `context.TotalArtifacts`
  - All properties exist in the DTO after d010f33's fix.
  - **Status: WORKING**

- **ChannelDetail.razor** — this page only. But it's the **details page** users click into, so it's high-visibility.

### What About Existing Tests?

- **ChannelsApiEndpointsTests.cs, lines 80–114:** Tests the GetChannelDetail endpoint. 
  - Asserts `detail.RecentRuns` exists (✅ correct).
  - Asserts `detail.Status` and `detail.Prompt` exist (✅ correct).
  - **Does NOT test deserialization into Razor's expected `ChannelDetailDto` record.**
  - Test is incomplete; it only validates the endpoint logic, not the Razor contract.

- **ChannelsHomeSmokeTests.cs:** Tests channel registry, not DTO contracts.

**Verdict:** No tests would catch this mismatch because no test deserializes the Gateway response into the Razor's local DTO record.

---

## Fix Options

### Option A: Rename Razor Bindings to Match Gateway DTO

**Approach:**
1. Modify ChannelDetail.razor lines 218–230 (local DTO definitions) to match Gateway field names exactly.
2. Change bindings throughout the page to use corrected names.

**Changes Required:**
- Update local `ArtifactDto` record in ChannelDetail.razor:
  ```csharp
  private record ArtifactDto(
      Guid Id,
      string Type,           // was ArtifactType
      string? Title,
      string? ContentPreview, // was ContentInline
      long SizeBytes,         // was ContentSizeBytes
      string? MimeType,
      DateTime CreatedAt     // was CreatedAtUtc
  );
  ```
- Update all Razor template bindings:
  - Line 37: `@artifact.ArtifactType` → `@artifact.Type`
  - Lines 42, 47, 54, 61: Similar changes
  - Line 37: `@artifact.ContentSizeBytes` → `@artifact.SizeBytes`
  - Line 37: `@artifact.CreatedAtUtc` → `@artifact.CreatedAt`
  - Lines 47, 54, 61, 67, 70: `@artifact.ContentInline` → `@artifact.ContentPreview`
- **BUT:** Still cannot access `artifact.RunId` or `artifact.ContentPath` — these don't exist in Gateway DTO.

**Pros:**
- Simplest change (rename-only, no backend work).
- Minimal risk if Gateway DTO is considered the authoritative contract.
- Easier code review (pure UI layer change).

**Cons:**
- **Incomplete:** Loses access to RunId and ContentPath (needed for the download link on line 76 and possibly logging).
- Gateway DTO must serve all UI needs (tight coupling).
- If UI requirements change, gateway must keep up.

**Pre-existing Tests Affected:**
- ChannelsApiEndpointsTests.cs: No changes (tests Gateway, not Razor).
- Would need new test to assert Razor deserialization works end-to-end.

---

### Option B: Extend Gateway DTO to Provide All Razor Fields

**Approach:**
1. Add missing fields to the Gateway ChannelDetailDto and ArtifactDto.
2. Update the endpoint handler to populate them.

**Changes Required:**
- ChannelsApiEndpoints.cs, lines 251–280:
  ```csharp
  public record ChannelDetailDto(
      Guid JobId,
      string JobName,
      string Status,
      string Prompt,
      List<ChannelRunSummaryDto> RecentRuns,
      List<ArtifactDto> Artifacts  // ADD THIS
  );

  public record ArtifactDto(
      Guid Id,
      Guid RunId,                  // ADD THIS
      string Type,
      string? Title,
      string? ContentInline,       // RENAME FROM ContentPreview; fetch full content
      string? ContentPath,         // ADD THIS
      long ContentSizeBytes,       // RENAME FROM SizeBytes
      string? MimeType,
      DateTime CreatedAtUtc        // RENAME FROM CreatedAt
  );
  ```
- Update GetChannelDetail endpoint handler (lines 84–96) to fetch artifacts instead of/in addition to RecentRuns.
- Populate ContentInline (currently truncated to 500 chars in GetRunArtifacts endpoint; would need fetching logic).
- Maintain backward compatibility? RunArtifactsDto currently used by another endpoint; coordinate changes.

**Pros:**
- Razor gets full, clean API without renaming bindings.
- DTO becomes the single source of truth, with all UI needs baked in.
- Easier for UI developers (no surprises from missing fields).

**Cons:**
- **Major backend change:** Endpoint logic must change significantly.
- Increases DTO surface area (fewer fields = simpler API for other consumers).
- Fetching full artifact content in GetChannelDetail could be slow (N+1 queries if not optimized).
- Schema drift risk: If UI and Gateway evolve separately, this option doesn't prevent future mismatches.
- **Requires schema/query design review** (Irving's domain).

**Pre-existing Tests Affected:**
- ChannelsApiEndpointsTests.cs: Lines 80–114 must be rewritten to assert artifacts are returned.
- New tests for RunId, ContentPath, full ContentInline.
- Tests for query performance (artifacts fetched per job).

---

### Option C: Introduce a ChannelDetailViewDto (Hybrid)

**Approach:**
1. Keep the existing Gateway ChannelDetailDto and ArtifactDto unchanged (for API consumers).
2. Create a separate ChannelDetailViewDto record in ChannelsApiEndpoints.cs with all Razor-expected fields.
3. Add a new internal endpoint (or a separate handler) that returns ChannelDetailViewDto.
4. Update ChannelDetail.razor to use the new endpoint / DTO.

**Changes Required:**
- ChannelsApiEndpoints.cs:
  ```csharp
  // New DTO for Razor consumption only (internal)
  public record ChannelDetailViewDto(
      Guid JobId,
      string JobName,
      List<ArtifactForViewDto> Artifacts
  );

  public record ArtifactForViewDto(
      Guid Id,
      Guid RunId,
      string ArtifactType,
      string? Title,
      string? ContentInline,
      string? ContentPath,
      long ContentSizeBytes,
      string? MimeType,
      DateTime CreatedAtUtc
  );
  ```
- Add a new endpoint (e.g., GET `/api/channels/{jobId}/view`) that returns ChannelDetailViewDto.
- Update ChannelDetail.razor line 155 to call `/api/channels/{jobId}/view` instead of `/api/channels/{jobId}`.
- Update the local ChannelDetailDto and ArtifactDto in Razor to match the new ViewDto.

**Pros:**
- Gateway API stays clean (ChannelDetailDto unchanged).
- Razor gets exactly what it needs without compromises.
- API contracts are explicit (ViewDto for UI, ChannelDetailDto for API).
- Clear separation of concerns: UI layer DTOs vs. public API DTOs.
- Easier for future API evolution (don't break Razor when adding fields for other consumers).

**Cons:**
- Introduces DTO duplication (not DRY).
- Slightly more complex endpoint mapping (two endpoints for similar data).
- **Requires Irving to add new endpoint** — backend work, but localized.
- Still doesn't solve the root cause (multiple DTOs, potential for future drift).

**Pre-existing Tests Affected:**
- ChannelsApiEndpointsTests.cs: New test for GetChannelDetailView endpoint.
- Existing GetChannelDetail tests unchanged.
- ChannelsHomeSmokeTests.cs: No changes.

---

## Recommendation

**Option C (Hybrid / ViewDto)** is my recommendation.

**Rationale:**

1. **Root cause:** The Gateway is an internal loopback API serving multiple consumers (Razor pages, potential future external APIs, tools). A single DTO is too rigid.

2. **Gateway DTO is not the UI contract:** d010f33 shows that Razor expectations drift from the "canonical" DTO. Option A (rename Razor) makes Gateway the source of truth, which is backwards. The Gateway should serve the Razor, not vice versa.

3. **Option B is heavy-handed:** Fetching all artifacts into ChannelDetailDto bloats the primary endpoint response and couples concerns. The current `/api/channels/{jobId}/runs/{runId}` endpoint already provides artifacts — we should reuse that, not duplicate.

4. **Option C is explicit:** A dedicated ViewDto makes it clear that this is Razor-specific shape. Future maintainers see immediately that ChannelDetailViewDto is a UI contract, not a general API contract. It's a small tax for clarity.

5. **Low risk:** Irving adds one endpoint mapping (< 15 lines). No existing API consumers break. Razor moves to the new endpoint and gets the exact shape it needs.

6. **Maintainability:** When the UI needs a new field (e.g., `TotalRunCount`, `IsArchived`), we add it to ViewDto, not the canonical DTO. The public API stays lean.

**Implementation Path (for Irving):**
1. Add ChannelDetailViewDto and ArtifactForViewDto records to ChannelsApiEndpoints.cs.
2. Map new GET `/api/channels/{jobId}/view` route that returns ChannelDetailViewDto (reuse existing endpoint logic for RecentRuns, add artifact fetch).
3. Update Helly's ChannelDetail.razor to call the new endpoint.

---

## Estimated Effort

| Option | Effort | Risk | Owner |
|---|---|---|---|
| A: Rename Razor Bindings | **S** (1–2 hrs) | **L** — Loses RunId, ContentPath; incomplete fix | Helly (Frontend) |
| B: Extend Gateway DTO | **M–L** (4–8 hrs) | **H** — Schema impact, query design, potential perf issues | Irving (Backend) + Schema review |
| C: ViewDto (Hybrid) | **S–M** (2–4 hrs) | **M** — New endpoint, but isolated; no schema risk | Irving (Backend) + Helly (Frontend) |

**Effort Breakdown for Option C:**
- Irving: 2 hrs (add ViewDto records, new endpoint handler, integrate into existing middleware).
- Helly: 1 hr (update ChannelDetail.razor DTOs + endpoint call).
- Testing: 1 hr (new smoke test for ViewDto endpoint).

---

## Appendix: Current Code References

### ChannelDetail.razor
- **Lines 159–163:** Deserialization and binding (broken).
- **Lines 218–230:** Local DTO definitions (names mismatch Gateway).
- **Lines 35–77:** All Razor bindings that fail on missing/misnamed fields.

### ChannelsApiEndpoints.cs
- **Lines 251–256:** ChannelDetailDto definition (missing `Artifacts`).
- **Lines 273–280:** ArtifactDto definition (5 property name mismatches).
- **Lines 55–100:** GetChannelDetail endpoint handler (returns RecentRuns, not Artifacts).

### Tests
- **ChannelsApiEndpointsTests.cs, lines 80–114:** Tests GetChannelDetail but doesn't validate Razor deserialization.

---

**Next Steps (Bruno's decision):**
- [ ] Approve Option A, B, or C.
- [ ] If Option C: Brief Irving on new endpoint scope; assign to Helly for Razor update.
- [ ] Add comprehensive DTO contract tests to prevent future drift.
