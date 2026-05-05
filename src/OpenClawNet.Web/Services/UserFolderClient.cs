using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using OpenClawNet.Web.Models.UserFolders;

namespace OpenClawNet.Web.Services;

/// <summary>
/// Typed HTTP client for the W-4 user-folder gateway endpoints.
/// Routes through the Aspire <c>https+http://gateway</c> base address registered
/// in <c>Program.cs</c> via the named "gateway" HttpClient. All methods return
/// either a strongly-typed result or a <see cref="UserFolderClientException"/>
/// carrying the structured <see cref="UserFolderProblem"/> from the server.
/// </summary>
public sealed class UserFolderClient
{
    /// <summary>HTTP header carrying the typed-back folder name on DELETE (Drummond W-4 P0 #3).</summary>
    public const string ConfirmHeader = "X-Confirm-FolderName";

    private readonly HttpClient _http;

    public UserFolderClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<IReadOnlyList<UserFolderDto>> ListAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/user-folders", ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        var list = await response.Content.ReadFromJsonAsync<List<UserFolderDto>>(ct).ConfigureAwait(false);
        return list ?? [];
    }

    public async Task<UserFolderDto> CreateAsync(string folderName, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "api/user-folders",
            new CreateUserFolderRequest(folderName),
            ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<UserFolderDto>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Create returned null body.");
    }

    public async Task DeleteAsync(string folderName, CancellationToken ct = default)
    {
        // Confirmation is mandatory per Drummond W-4 P0 #3 — the typed-back
        // folder name is rendered into the X-Confirm-FolderName header so
        // GET-only / accidental-curl deletions are impossible.
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"api/user-folders/{Uri.EscapeDataString(folderName)}");
        request.Headers.Add(ConfirmHeader, folderName);

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<UserFolderUploadResult> UploadAsync(
        string folderName,
        IBrowserFile file,
        IProgress<long>? progress = null,
        long maxAllowedSize = 1024L * 1024L * 1024L, // 1 GB per file at the wire (server enforces real quota)
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        using var content = new MultipartFormDataContent();
        var stream = file.OpenReadStream(maxAllowedSize, ct);
        var streamContent = new ProgressStreamContent(stream, progress);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        content.Add(streamContent, "file", file.Name);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/user-folders/{Uri.EscapeDataString(folderName)}/files")
        {
            Content = content
        };

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<UserFolderUploadResult>(ct).ConfigureAwait(false)
            ?? new UserFolderUploadResult(folderName, 0, DateTime.UtcNow);
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        UserFolderProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<UserFolderProblem>(ct).ConfigureAwait(false);
        }
        catch
        {
            // Body wasn't JSON — fall through with null problem.
        }

        throw new UserFolderClientException(response.StatusCode, problem);
    }

    /// <summary>
    /// Streams an upload while reporting bytes read. The progress reporter is
    /// invoked from <see cref="SerializeToStreamAsync"/> on background thread —
    /// callers that update Razor state must marshal back to the renderer.
    /// </summary>
    private sealed class ProgressStreamContent : StreamContent
    {
        private const int CopyBuffer = 81_920;
        private readonly Stream _source;
        private readonly IProgress<long>? _progress;

        public ProgressStreamContent(Stream source, IProgress<long>? progress) : base(source)
        {
            _source = source;
            _progress = progress;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var buffer = new byte[CopyBuffer];
            long total = 0;
            int read;
            while ((read = await _source.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                total += read;
                _progress?.Report(total);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            try
            {
                length = _source.Length;
                return true;
            }
            catch
            {
                length = -1;
                return false;
            }
        }
    }
}

/// <summary>
/// Carries the structured 4xx / 5xx response from a user-folder API call.
/// UI consumers render <see cref="Problem"/>.<see cref="UserFolderProblem.Reason"/>
/// in alerts; <see cref="StatusCode"/> distinguishes 413 (quota) from 400 (validation).
/// </summary>
public sealed class UserFolderClientException : Exception
{
    public UserFolderClientException(HttpStatusCode statusCode, UserFolderProblem? problem)
        : base(BuildMessage(statusCode, problem))
    {
        StatusCode = statusCode;
        Problem = problem;
    }

    public HttpStatusCode StatusCode { get; }
    public UserFolderProblem? Problem { get; }

    public string Reason => Problem?.Reason ?? StatusCode.ToString();

    private static string BuildMessage(HttpStatusCode statusCode, UserFolderProblem? problem)
        => problem is null
            ? $"User-folder request failed: HTTP {(int)statusCode} {statusCode}."
            : $"User-folder request failed: {problem.Reason} (HTTP {(int)statusCode}).";
}
