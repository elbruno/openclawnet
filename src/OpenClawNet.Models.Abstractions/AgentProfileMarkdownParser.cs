using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenClawNet.Models.Abstractions;

/// <summary>
/// Parses a Markdown string into an <see cref="AgentProfile"/>.
/// Supports optional YAML front-matter (between <c>---</c> delimiters)
/// for explicit field overrides; the remaining body becomes <see cref="AgentProfile.Instructions"/>.
/// </summary>
public static class AgentProfileMarkdownParser
{
    /// <summary>
    /// Parses a Markdown string into an AgentProfile.
    /// Supports optional YAML front-matter for explicit field overrides.
    /// </summary>
    public static AgentProfile Parse(string markdown, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var profile = new AgentProfile { Name = string.Empty };
        string body;

        if (HasFrontMatter(markdown))
        {
            var (frontMatter, rest) = ExtractFrontMatter(markdown);
            ApplyFrontMatter(profile, frontMatter);
            body = rest;
        }
        else
        {
            body = markdown;
        }

        profile.Instructions = body.Trim();

        // Derive name from heading if not set via front-matter
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            var heading = ExtractFirstHeading(body);
            if (!string.IsNullOrWhiteSpace(heading))
            {
                profile.Name = Slugify(heading);
            }
            else if (!string.IsNullOrWhiteSpace(fallbackName))
            {
                profile.Name = fallbackName;
            }
            else
            {
                profile.Name = $"imported-{DateTime.UtcNow.Ticks}";
            }
        }

        return profile;
    }

    private static bool HasFrontMatter(string markdown)
    {
        return markdown.StartsWith("---") &&
               markdown.IndexOf("---", 3, StringComparison.Ordinal) > 3;
    }

    private static (string FrontMatter, string Body) ExtractFrontMatter(string markdown)
    {
        // Skip the opening "---" line
        var startIndex = markdown.IndexOf('\n', 0);
        if (startIndex < 0)
            return (string.Empty, markdown);

        startIndex++; // move past the newline

        var endIndex = markdown.IndexOf("\n---", startIndex, StringComparison.Ordinal);
        if (endIndex < 0)
            return (string.Empty, markdown);

        var frontMatter = markdown[startIndex..endIndex];
        var bodyStart = markdown.IndexOf('\n', endIndex + 1);
        var body = bodyStart >= 0 ? markdown[(bodyStart + 1)..] : string.Empty;

        return (frontMatter, body);
    }

    private static void ApplyFrontMatter(AgentProfile profile, string frontMatter)
    {
        foreach (var rawLine in frontMatter.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var key = line[..colonIndex].Trim();
            var value = line[(colonIndex + 1)..].Trim();

            switch (key)
            {
                case "name":
                    profile.Name = value;
                    break;
                case "displayName":
                    profile.DisplayName = value;
                    break;
                case "provider":
                    profile.Provider = value;
                    break;
                case "model":
                    // PR-F: model now lives on the ModelProviderDefinition referenced by
                    // `provider`, not the agent profile. Silently ignore the legacy field
                    // so existing markdown imports don't fail.
                    break;
                case "tools":
                    profile.EnabledTools = ParseToolsList(value);
                    break;
                case "temperature":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var temp))
                        profile.Temperature = temp;
                    break;
                case "maxTokens":
                    if (int.TryParse(value, CultureInfo.InvariantCulture, out var tokens))
                        profile.MaxTokens = tokens;
                    break;
                case "kind":
                    if (Enum.TryParse<ProfileKind>(value, ignoreCase: true, out var kind))
                        profile.Kind = kind;
                    break;
                case "retrievalLevel":
                case "retrieval":
                    if (Enum.TryParse<RetrievalLevel>(value, ignoreCase: true, out var retrievalLevel))
                        profile.RetrievalLevel = retrievalLevel;
                    break;
            }
        }
    }

    private static string ParseToolsList(string value)
    {
        // Handle [tool1, tool2] or plain comma-separated
        var trimmed = value.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            trimmed = trimmed[1..^1];

        var tools = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(",", tools);
    }

    private static string? ExtractFirstHeading(string body)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimStart();
            if (line.StartsWith("# "))
                return line[2..].Trim();
        }

        return null;
    }

    internal static string Slugify(string text)
    {
        var slug = text.ToLowerInvariant().Trim();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", string.Empty);
        slug = Regex.Replace(slug, @"[\s]+", "-");
        slug = Regex.Replace(slug, @"-{2,}", "-");
        return slug.Trim('-');
    }
}
