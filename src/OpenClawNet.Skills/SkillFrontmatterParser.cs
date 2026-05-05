using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace OpenClawNet.Skills;

/// <summary>
/// K-1b — Minimal YAML-frontmatter parser for SKILL.md files (per
/// agentskills.io). Extracts <c>name</c>, <c>description</c>, optional
/// <c>tags</c>, and the markdown body. Tolerant: missing frontmatter is
/// treated as <c>name = (folder name)</c>, empty description, full file as
/// body. Invalid YAML throws.
/// </summary>
internal static class SkillFrontmatterParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"\A---\s*\r?\n(.*?)\r?\n---\s*\r?\n(.*)\z",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public sealed record ParsedSkill(
        string Name,
        string Description,
        IReadOnlyDictionary<string, string> Metadata,
        string Body);

    public static ParsedSkill Parse(string skillMdContent, string fallbackName)
    {
        var match = FrontmatterRegex.Match(skillMdContent);
        if (!match.Success)
        {
            // No frontmatter at all = malformed per agentskills.io.
            throw new FormatException("SKILL.md is missing required YAML frontmatter (--- ... ---).");
        }

        var yamlText = match.Groups[1].Value;
        var body = match.Groups[2].Value;

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var name = fallbackName;
        var description = string.Empty;

        var stream = new YamlStream();
        using (var reader = new StringReader(yamlText))
        {
            stream.Load(reader); // throws on syntactic garbage
        }

        if (stream.Documents.Count > 0
            && stream.Documents[0].RootNode is YamlMappingNode root)
        {
            foreach (var entry in root.Children)
            {
                if (entry.Key is not YamlScalarNode keyNode || keyNode.Value is null)
                    continue;

                var key = keyNode.Value;

                // Reject malformed scalar values (e.g. an unterminated flow
                // sequence "[" followed by EOF parses as a non-scalar node):
                // anything that isn't a plain scalar for name/description is
                // treated as malformed and rejected.
                if ((string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                    && entry.Value is not YamlScalarNode)
                {
                    throw new FormatException(
                        $"SKILL.md frontmatter '{key}' must be a string scalar.");
                }

                var value = FlattenYamlValue(entry.Value);

                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    name = value;
                }
                else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                {
                    description = value ?? string.Empty;
                }

                if (value is not null)
                {
                    metadata[key] = value;
                }
            }
        }

        // agentskills.io: description is required. Reject empty so the
        // registry skips the file rather than producing a degraded skill.
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new FormatException(
                $"SKILL.md frontmatter for '{name}' is missing a non-empty 'description'.");
        }

        return new ParsedSkill(name, description, metadata, body);
    }

    private static string? FlattenYamlValue(YamlNode node) => node switch
    {
        YamlScalarNode s => s.Value,
        YamlSequenceNode seq => string.Join(", ",
            seq.Children
                .OfType<YamlScalarNode>()
                .Select(c => c.Value)
                .Where(v => v is not null)),
        _ => null,
    };
}
