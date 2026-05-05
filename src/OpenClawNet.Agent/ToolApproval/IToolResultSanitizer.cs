namespace OpenClawNet.Agent.ToolApproval;

/// <summary>
/// Sanitizes the textual output of a tool before it is injected into the LLM
/// context as a tool-role message. Concept-review §4a (Security) — defends
/// against prompt-injection payloads and trims pathological outputs.
/// </summary>
/// <remarks>
/// Sanitizers must be deterministic and side-effect-free. The runtime calls
/// <see cref="Sanitize(string?, string)"/> on every tool result, regardless of success.
/// </remarks>
public interface IToolResultSanitizer
{
    /// <summary>
    /// Returns a sanitized form of <paramref name="rawContent"/> safe to embed
    /// in an LLM tool message. Implementations should never throw — return
    /// a safe fallback if sanitization fails.
    /// </summary>
    string Sanitize(string? rawContent, string toolName);
}
