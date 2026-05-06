using System.Text;
using Microsoft.Extensions.Options;
using OpenClawNet.Storage;

namespace OpenClawNet.Agent.ToolApproval;

/// <summary>
/// Default <see cref="IToolResultSanitizer"/> implementation:
/// <list type="bullet">
///   <item>normalizes Unicode to NFC form to prevent homoglyph attacks,</item>
///   <item>strips ASCII control characters except CR/LF/TAB,</item>
///   <item>enforces maximum line length to prevent pathological line attacks,</item>
///   <item>HTML-escapes <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> to neutralize injection markers,</item>
///   <item>detects and wraps prompt-injection markers with clear delimiters,</item>
///   <item>removes obvious "ignore previous instructions"-style sentinels by wrapping
///         the content in a fenced block with a clear header,</item>
///   <item>truncates at configurable MaxLength with a visible truncation marker.</item>
/// </list>
/// Concept-review §4a (Security) — adopted as the default first line of defense.
/// Feature 2 Story 2 — enhanced with Unicode normalization, injection-marker detection, and line-length limits.
/// </summary>
public sealed class DefaultToolResultSanitizer : IToolResultSanitizer
{
    /// <summary>Maximum characters retained from the raw content. Excess is truncated.</summary>
    public const int MaxLength = 64 * 1024;

    private const string TruncationMarker = "\n…[tool output truncated by sanitizer]…";

    // Prompt-injection markers to detect and wrap
    private static readonly string[] InjectionMarkers = new[]
    {
        "ignore previous",
        "ignore all previous",
        "disregard previous",
        "system:",
        "assistant:",
        "user:",
        "[system]",
        "[assistant]",
        "[user]",
        "<|im_start|>",
        "<|im_end|>",
    };

    private readonly ToolResultSanitizerOptions _options;
    private readonly IVaultSecretRedactor? _vaultRedactor;

    public DefaultToolResultSanitizer(
        IOptions<ToolResultSanitizerOptions> options,
        IVaultSecretRedactor? vaultRedactor = null)
    {
        _options = options.Value;
        _vaultRedactor = vaultRedactor;
    }

    public string Sanitize(string? rawContent, string toolName)
    {
        if (string.IsNullOrEmpty(rawContent))
        {
            return $"[tool:{toolName}] (no output)";
        }

        try
        {
            // 1. Redact any value produced by the vault before other transformations.
            var vaultRedacted = _vaultRedactor?.Redact(rawContent) ?? rawContent;

            // 2. Unicode normalization (NFC) — defense against homoglyph attacks
            var normalized = vaultRedacted.Normalize(NormalizationForm.FormC);

            // 2. Strip control characters (except CR/LF/TAB)
            var stripped = StripControlChars(normalized);

            // 3. Check for pathological line lengths
            var lineLengthChecked = EnforceMaxLineLength(stripped, _options.MaxLineLength);

            // 4. HTML escape to neutralize injection markers
            var escaped = HtmlMinimallyEscape(lineLengthChecked);

            // 5. Detect and wrap prompt-injection markers
            var markerWrapped = WrapInjectionMarkers(escaped);

            // 6. Truncate at configured MaxLength
            var truncated = Truncate(markerWrapped, _options.MaxLength);

            // 7. Wrap in a clearly delimited block so prompt-injection text inside the
            // tool output cannot impersonate a system/user instruction line.
            return $"<tool_output tool=\"{HtmlMinimallyEscape(toolName)}\">\n{truncated}\n</tool_output>";
        }
        catch
        {
            // Never throw — fall back to a neutered placeholder so the agent loop continues.
            return $"<tool_output tool=\"{toolName}\">[sanitizer failed]</tool_output>";
        }
    }

    private static string StripControlChars(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == '\n' || ch == '\r' || ch == '\t' || !char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string HtmlMinimallyEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + TruncationMarker;

    /// <summary>
    /// Enforce maximum line length to prevent pathological line-length attacks.
    /// Lines exceeding MaxLineLength are truncated with a marker.
    /// </summary>
    private static string EnforceMaxLineLength(string s, int maxLineLength)
    {
        var lines = s.Split('\n');
        var sb = new StringBuilder(s.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0)
                sb.Append('\n');

            var line = lines[i];
            if (line.Length > maxLineLength)
            {
                sb.Append(line.Substring(0, maxLineLength));
                sb.Append("…[line truncated]");
            }
            else
            {
                sb.Append(line);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Detect prompt-injection markers (e.g., "ignore previous", "system:", "assistant:")
    /// and wrap them with clear delimiters to prevent them from being interpreted as instructions.
    /// </summary>
    private static string WrapInjectionMarkers(string s)
    {
        foreach (var marker in InjectionMarkers)
        {
            // Case-insensitive detection
            var index = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // Simple wrapping: replace marker with [DETECTED:marker]
                // This prevents the LLM from interpreting it as a real instruction.
                s = s.Replace(marker, $"[DETECTED:{marker}]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return s;
    }
}
