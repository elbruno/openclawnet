using System.Net.Http.Json;
using OpenClawNet.Web.Models.Secrets;

namespace OpenClawNet.Web.Services;

public sealed class SecretsVaultClient
{
    private readonly HttpClient _http;

    public SecretsVaultClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task<IReadOnlyList<SecretSummaryDto>> ListAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/secrets", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<SecretSummaryDto>>(ct).ConfigureAwait(false) ?? [];
    }

    public async Task SetAsync(string name, SecretWriteRequest request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"api/secrets/{Uri.EscapeDataString(name)}", request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<int>> ListVersionsAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/secrets/{Uri.EscapeDataString(name)}/versions", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<List<int>>(ct).ConfigureAwait(false) ?? [];
    }

    public async Task RotateAsync(string name, SecretRotateRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"api/secrets/{Uri.EscapeDataString(name)}/rotate", request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"api/secrets/{Uri.EscapeDataString(name)}", ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task RecoverAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"api/secrets/{Uri.EscapeDataString(name)}/recover", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task PurgeAsync(string name, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/secrets/{Uri.EscapeDataString(name)}/purge");
        request.Headers.Add("X-Confirm-Purge", name);
        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task<bool> VerifyAuditChainAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("api/secrets/audit/verify", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        var payload = await response.Content.ReadFromJsonAsync<AuditVerifyResponse>(ct).ConfigureAwait(false);
        return payload?.Valid ?? false;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException(
            $"Secrets API call failed: HTTP {(int)response.StatusCode} {response.StatusCode}. {body}");
    }
}
