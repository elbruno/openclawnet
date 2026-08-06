# Dylan — Bugs Found in Irving's Second-Pass Endpoints

**Created:** 2026-04-28  
**Reporter:** Dylan (QA)  
**Scope:** Integration testing for Irving's 14 second-pass REST endpoints

---

## Critical: InMemory Provider Incompatibility

**Severity:** High (blocks testing)  
**Status:** ⚠️ Open

### 1. `POST /api/channels/{jobId}/clear` — ExecuteDeleteAsync Not Supported

**Location:** `src\OpenClawNet.Gateway\Endpoints\ChannelsExtraEndpoints.cs:85`

**Issue:**  
The endpoint uses `ExecuteDeleteAsync()` for bulk deletion, which is not supported by the InMemory EF Core provider used in integration tests.

```csharp
var artifactCount = await db.JobRunArtifacts
    .Where(a => a.JobId == jobId)
    .ExecuteDeleteAsync();  // ❌ InMemory provider doesn't support this
```

**Impact:**  
- Integration tests fail with `InvalidOperationException`
- Irving's test `ChannelsExtraEndpointsTests.ClearChannel_DeletesRunsEventsArtifacts` fails
- My test `SecondPassEndpointsEdgeCasesTests.ClearChannel_DeletesAllRunsAndArtifacts` had to be skipped

**Recommended Fix:**  
Add a fallback for InMemory provider:

```csharp
// Check if provider is InMemory
if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
{
    var artifacts = await db.JobRunArtifacts.Where(a => a.JobId == jobId).ToListAsync();
    db.JobRunArtifacts.RemoveRange(artifacts);
    artifactCount = artifacts.Count;
}
else
{
    artifactCount = await db.JobRunArtifacts
        .Where(a => a.JobId == jobId)
        .ExecuteDeleteAsync();
}
```

---

### 2. `GET /api/diagnostics/db` — GetConnectionString Not Supported

**Location:** `src\OpenClawNet.Gateway\Endpoints\DiagnosticsEndpoints.cs:20`

**Issue:**  
The endpoint calls `db.Database.GetConnectionString()`, which throws `InvalidOperationException` with InMemory provider ("Relational-specific methods can only be used when the context is using a relational database provider").

```csharp
var connectionString = db.Database.GetConnectionString();  // ❌ InMemory provider doesn't support this
```

**Impact:**  
- Integration tests fail with exception
- Irving's tests `DiagnosticsEndpointsTests.GetDatabaseDiagnostics_*` fail
- My test `SecondPassEndpointsEdgeCasesTests.Diagnostics_DbInfo_ContainsTableCounts` had to be skipped

**Recommended Fix:**  
Catch the exception or check provider type:

```csharp
try
{
    if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
    {
        connectionString = db.Database.GetConnectionString();
        // ... parse SQLite path
    }
    else
    {
        error = "In-memory database (test environment)";
    }
}
catch (InvalidOperationException ex)
{
    error = $"Failed to read database info: {ex.Message}";
}
```

---

## Medium: Test Bugs in Irving's Test Files

**Severity:** Medium (test build failure)  
**Status:** ✅ Fixed by Dylan

### 3. Wrong Enum Names in Multiple Test Files

**Files Affected:**
- `tests\OpenClawNet.IntegrationTests\ChannelsExtraEndpointsTests.cs` (5 instances)
- `tests\OpenClawNet.IntegrationTests\McpServerToolsEndpointsTests.cs` (2 instances)

**Issues:**

| Wrong Name | Correct Name | Entity |
|------------|-------------|--------|
| `JobRunArtifactType` | `JobRunArtifactKind` | Artifact enum |
| `StoragePath` | `ContentPath` | JobRunArtifact property |
| `McpServer` | `McpServerDefinitionEntity` | MCP server entity |
| `db.McpServers` | `db.McpServerDefinitions` | DbSet name |

**Fix Applied:**  
Dylan fixed these in commit 25ff163 via PowerShell replace operations.

---

## Low: Test Logic Issues

### 4. ChannelAdapterEndpointsTests JSON Parsing Error

**Location:** `tests\OpenClawNet.IntegrationTests\ChannelAdapterEndpointsTests.cs:41,81`

**Issue:**  
Tests expect single adapter object but endpoint returns adapter list (array).

```csharp
// Test expects: { "name": "...", ... }
// Endpoint returns: [ { "name": "...", ... } ]
```

**Impact:**  
Tests fail with "The requested operation requires an element of type 'Object', but the target element has type 'Array'."

**Status:** ⚠️ Open (Irving's test, not Dylan's responsibility)

---

### 5. JobScheduleEndpointsTests NextRun Assertion

**Location:** `tests\OpenClawNet.IntegrationTests\JobScheduleEndpointsTests.cs:142`

**Issue:**  
Test expects `null` error but endpoint returns non-null string.

**Status:** ⚠️ Open (Irving's test)

---

## Security: Good News!

### Runtime Settings Secret-Leak Check ✅

**Test:** `SecondPassEndpointsEdgeCasesTests.RuntimeSettings_DoesNotLeakSecrets`

**Result:** PASSED

The `GET /api/runtime-settings` endpoint correctly:
- Exposes `hasApiKey: true/false` (boolean)
- Never leaks the actual `apiKey` field
- Redacts secrets properly

No security issues found in runtime settings endpoint.

---

## Summary

**Critical Issues:** 2 (InMemory provider incompatibility — blocks testing)  
**Medium Issues:** 1 (fixed by Dylan)  
**Low Issues:** 2 (Irving's test logic bugs)  
**Security Issues:** 0 ✅

**Recommendation:** Irving should:
1. Add InMemory provider fallbacks to ClearChannel and DiagnosticsDb endpoints
2. Fix ChannelAdapter and JobSchedule test logic issues
3. Verify all tests pass in production (SQLite) environment

**Dylan's Contribution:**  
- Fixed 3 build-blocking test bugs in Irving's files
- Delivered 16 edge-case tests (14 passing, 2 skipped due to provider issues)
- Verified no security leaks in runtime settings endpoint
