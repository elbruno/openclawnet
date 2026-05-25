using System.Text.Json;

namespace OpenClawNet.PlaywrightTests;

public sealed record AspireResolvedUrls(string WebBaseUrl, string GatewayBaseUrl, string SchedulerBaseUrl);

public static class AspireDescribeResolver
{
    public static bool TryResolveResources(string describeStdout, out AspireResolvedUrls? resolvedUrls)
    {
        resolvedUrls = null;

        if (string.IsNullOrWhiteSpace(describeStdout))
        {
            return false;
        }

        var trimmed = describeStdout.Trim();
        var jsonStart = trimmed.IndexOf('{');
        var jsonEnd = trimmed.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return false;
        }

        var json = trimmed.Substring(jsonStart, jsonEnd - jsonStart + 1);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            string? webUrl = null;
            string? gatewayUrl = null;
            string? schedulerUrl = null;

            foreach (var resource in resources.EnumerateArray())
            {
                var resourceName = resource.TryGetProperty("displayName", out var nameProp)
                    ? nameProp.GetString() ?? string.Empty
                    : string.Empty;

                if (!resource.TryGetProperty("urls", out var urlsProp) || urlsProp.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var selectedUrl = SelectBestUrl(urlsProp);
                if (selectedUrl is null)
                {
                    continue;
                }

                if (resourceName.Equals("web", StringComparison.OrdinalIgnoreCase))
                {
                    webUrl = selectedUrl;
                }
                else if (resourceName.Equals("gateway", StringComparison.OrdinalIgnoreCase))
                {
                    gatewayUrl = selectedUrl;
                }
                else if (resourceName.Equals("scheduler", StringComparison.OrdinalIgnoreCase))
                {
                    schedulerUrl = selectedUrl;
                }
            }

            if (!TryNormalizeHttpUrl(webUrl, out var normalizedWeb) ||
                !TryNormalizeHttpUrl(gatewayUrl, out var normalizedGateway))
            {
                return false;
            }

            _ = TryNormalizeHttpUrl(schedulerUrl, out var normalizedScheduler);
            resolvedUrls = new AspireResolvedUrls(
                normalizedWeb!,
                normalizedGateway!,
                normalizedScheduler ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? SelectBestUrl(JsonElement urls)
    {
        string? https = null;
        string? http = null;

        foreach (var node in urls.EnumerateArray())
        {
            if (!node.TryGetProperty("url", out var urlProp))
            {
                continue;
            }

            var candidate = urlProp.GetString();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                https ??= candidate;
                continue;
            }

            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                http ??= candidate;
            }
        }

        return https ?? http;
    }

    private static bool TryNormalizeHttpUrl(string? candidate, out string? normalizedUrl)
    {
        normalizedUrl = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedUrl = uri.ToString().TrimEnd('/');
        return true;
    }
}
