# Test Results Index

## Latest Test Run

| Test Category | Date | Result | Duration | Notes |
|---|---|---|---|---|
| MarkItDownToolTests | 2026-05-23 | ✓ PASSED | 1.5s | All 3 tests passed (ExecuteAsync_WithValidUrl_ReturnsMarkdownContent, SaveToFileRequiresAgentName, ToolMetadata_IncludesSaveToFileParameter, StorageProvider_Interface_Exists) |

## Test Summary

- **Total Tests**: 3
- **Passed**: 3
- **Failed**: 0
- **Skipped**: 0
- **Total Duration**: 1.5s

## Test Details

### MarkItDownToolTests

**File**: `tests/OpenClawNet.UnitTests/Tools/MarkItDownToolTests.cs`

1. **ExecuteAsync_WithValidUrl_ReturnsMarkdownContent** ✓ PASSED
   - Validates MarkItDownTool output with mocked HTTP responses
   - Confirms markdown content is non-empty and contains markdown markers
   - Verifies integration between IMarkdownService and HttpClient
   - Simulates HTML to Markdown conversion with sample HTML page

2. **SaveToFileRequiresAgentName** ✓ PASSED
   - Tests parameter validation for save_to_file functionality
   - Verifies agent_name requirement

3. **ToolMetadata_IncludesSaveToFileParameter** ✓ PASSED
   - Validates tool metadata includes required parameters

4. **StorageProvider_Interface_Exists** ✓ PASSED
   - Tests IStorageDirectoryProvider interface implementation

## Command Used

```bash
dotnet test tests/OpenClawNet.UnitTests/OpenClawNet.UnitTests.csproj --filter "MarkItDownToolTests" -v minimal
```

## Run Environment

- .NET Version: 10.0
- Test Framework: xUnit
- Test Platform: Windows_NT
