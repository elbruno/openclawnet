// W-4 — Dylan
// Integration tests for /api/user-folders/* endpoints.
// Spec sources:
//   - This prompt (squad spawn) — Irving's W-4 contract #4
//   - .squad/decisions/inbox/drummond-w3-gate-verdict.md (W-4 P0 #1/#3, P1 #5)
//
// Contract under test:
//   - POST   /api/user-folders                       body { folderName }      → 200 metadata | 400 Reason
//   - GET    /api/user-folders                                                → list[ { name, sizeBytes, lastWriteTime } ]
//   - DELETE /api/user-folders/{folderName}          REQUIRES X-Confirm-FolderName header
//   - POST   /api/user-folders/{folderName}/files    multipart upload         → 200 | 413 quota
//   - INFO logs use redacted folder name on validation failure (>32 chars → first 32 + "...")
//   - Audit append to audit/user-folders/{date}.jsonl on CREATE/DELETE
//
// ────────────────────────────────────────────────────────────────────────────
// ⚠ DORMANT until Irving's W-4 implementation lands. Activate by defining
// the W4_LANDED constant in the OpenClawNet.IntegrationTests .csproj.
// ────────────────────────────────────────────────────────────────────────────
// Activated — Irving's W-4 #4 endpoint file present (working tree, untracked).
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.IntegrationTests.Gateway;

[Trait("Area", "Storage")]
[Trait("Wave", "W-4")]
[Trait("Layer", "Gateway")]
public sealed class UserFolderEndpointTests : IClassFixture<UserFolderEndpointTests.Fixture>, IDisposable
{
    private readonly Fixture _fx;

    public UserFolderEndpointTests(Fixture fx) => _fx = fx;

    public void Dispose()
    {
        // Clean any folders created during a test, EXCLUDING scope folders the
        // host may have populated at boot (data-protection keys especially).
        if (!Directory.Exists(_fx.Root)) return;
        foreach (var d in Directory.GetDirectories(_fx.Root))
        {
            var name = Path.GetFileName(d);
            if (name is "agents" or "models" or "skills" or "binary" or "dataprotection-keys")
                continue;
            try { Directory.Delete(d, recursive: true); } catch { }
        }
    }

    public sealed class Fixture : GatewayWebAppFactory
    {
        public string Root { get; }

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"oc-w4-ep-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, Root);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    // ====================================================================
    // A. POST /api/user-folders — create
    // ====================================================================

    [Fact]
    public async Task PostValidName_Returns201_AndCreatesFolderOnDisk()
    {
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/user-folders", new { folderName = "mysamplefiles" });

        // Irving returns 201 Created (Results.Created with Location header).
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        Directory.Exists(Path.Combine(_fx.Root, "mysamplefiles")).Should().BeTrue();
    }

    [Fact]
    public async Task PostInvalidName_Returns400_WithReason()
    {
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/user-folders", new { folderName = "BadName" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("InvalidUserFolderName",
            "spec: 400 body must carry Reason='InvalidUserFolderName'");
    }

    [Fact]
    public async Task PostVeryLongName_Returns400_WithRedactedName()
    {
        var client = _fx.CreateClient();
        var longName = new string('A', 65); // exceeds 64-char limit AND >32 char redaction threshold

        var resp = await client.PostAsJsonAsync("/api/user-folders", new { folderName = longName });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotContain(longName,
            "spec: folder name >32 chars MUST be redacted to first 32 + '...' before being echoed back");
    }

    // ====================================================================
    // B. GET /api/user-folders
    // ====================================================================

    [Fact]
    public async Task GetEmpty_ReturnsEmptyList()
    {
        var client = _fx.CreateClient();

        var resp = await client.GetAsync("/api/user-folders");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetWithTwoFolders_ListsBothWithSizeAndLastWriteTime()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "alpha" });
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "beta" });
        File.WriteAllText(Path.Combine(_fx.Root, "alpha", "a.txt"), "hello");

        var resp = await client.GetAsync("/api/user-folders");
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToArray();
        names.Should().BeEquivalentTo(["alpha", "beta"]);

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            entry.TryGetProperty("sizeBytes", out _).Should().BeTrue();
            entry.TryGetProperty("lastWriteTimeUtc", out _).Should().BeTrue(
                "Irving's UserFolderDto field is LastWriteTimeUtc → camelCase 'lastWriteTimeUtc'");
        }
    }

    // ====================================================================
    // C. DELETE /api/user-folders/{name} — confirmation header
    // ====================================================================

    [Fact]
    public async Task DeleteWithoutConfirmHeader_Returns400_ConfirmationRequired()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "todelete" });

        var resp = await client.DeleteAsync("/api/user-folders/todelete");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("ConfirmationRequired");
        Directory.Exists(Path.Combine(_fx.Root, "todelete")).Should().BeTrue(
            "folder must NOT be deleted without confirmation");
    }

    [Fact]
    public async Task DeleteWithMismatchedConfirmHeader_Returns400_ConfirmationRequired()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "todelete2" });

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/user-folders/todelete2");
        req.Headers.Add("X-Confirm-FolderName", "wrong-name");
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("ConfirmationRequired");
        Directory.Exists(Path.Combine(_fx.Root, "todelete2")).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteWithMatchingConfirmHeader_Returns204_AndFolderGone()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "todelete3" });

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/user-folders/todelete3");
        req.Headers.Add("X-Confirm-FolderName", "todelete3");
        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NoContent, HttpStatusCode.OK);
        Directory.Exists(Path.Combine(_fx.Root, "todelete3")).Should().BeFalse();
    }

    // ====================================================================
    // D. POST /api/user-folders/{name}/files — multipart upload
    // ====================================================================

    [Fact]
    public async Task UploadFile_HappyPath_Returns200_AndFileOnDisk()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "uploads" });

        var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("hello, world");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "hello.txt");

        var resp = await client.PostAsync("/api/user-folders/uploads/files", content);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        File.Exists(Path.Combine(_fx.Root, "uploads", "hello.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task UploadFile_OverQuota_Returns413()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "fillme" });
        var existing = Path.Combine(_fx.Root, "fillme", "existing.bin");
        using (var fs = new FileStream(existing, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength(5L * 1024 * 1024 * 1024); // 5 GB sparse — at the per-folder default
        }

        var content = new MultipartFormDataContent();
        var bytes = new byte[1024];
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "tip.bin");

        var resp = await client.PostAsync("/api/user-folders/fillme/files", content);

        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge,
            "upload exceeding per-folder quota must return 413");
    }

    // ====================================================================
    // E. Audit emission — CREATE + DELETE write JSONL line
    // ====================================================================

    [Fact]
    public async Task Create_EmitsAuditJsonl()
    {
        var client = _fx.CreateClient();

        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "audited-create" });

        var auditDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var auditFile = Path.Combine(_fx.Root, "audit", "user-folders", $"{auditDate}.jsonl");
        File.Exists(auditFile).Should().BeTrue("CREATE must emit a JSONL line");

        var content = File.ReadAllText(auditFile);
        content.Should().Contain("audited-create");
        content.ToLowerInvariant().Should().Contain("create");
    }

    [Fact]
    public async Task Delete_EmitsAuditJsonl()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "audited-delete" });

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/user-folders/audited-delete");
        req.Headers.Add("X-Confirm-FolderName", "audited-delete");
        await client.SendAsync(req);

        var auditDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var auditFile = Path.Combine(_fx.Root, "audit", "user-folders", $"{auditDate}.jsonl");
        File.Exists(auditFile).Should().BeTrue();
        var content = File.ReadAllText(auditFile);
        content.Should().Contain("audited-delete");
        content.ToLowerInvariant().Should().Contain("delete");
    }

    // ====================================================================
    // F. Concurrent uploads to same folder — quota cache integrity
    // ====================================================================

    [Fact]
    public async Task ConcurrentUploads_SameFolder_DontCorruptQuotaCache()
    {
        var client = _fx.CreateClient();
        await client.PostAsJsonAsync("/api/user-folders", new { folderName = "concur" });

        async Task<HttpResponseMessage> Upload(int i)
        {
            var content = new MultipartFormDataContent();
            var bytes = Encoding.UTF8.GetBytes($"payload-{i}-" + new string('x', 1024));
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", $"f{i}.bin");
            return await client.PostAsync("/api/user-folders/concur/files", content);
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(Upload));

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge));
        Directory.GetFiles(Path.Combine(_fx.Root, "concur"))
            .Should().HaveCount(8);
    }
}
