namespace OpenClawNet.Agent.ToolApproval;

/// <summary>
/// Configuration for the approval prompt timeout. Concept-review §4a / UX —
/// adopted so a stuck approval doesn't block the agent loop indefinitely.
/// Wire via <c>builder.Services.Configure&lt;ToolApprovalOptions&gt;(...)</c>.
/// </summary>
public sealed class ToolApprovalOptions
{
    /// <summary>Section name used by <c>IConfiguration</c> binding.</summary>
    public const string SectionName = "ToolApproval";

    /// <summary>
    /// Seconds the runtime will wait for a user decision before auto-denying.
    /// <c>0</c> or negative = wait indefinitely (legacy behavior).
    /// Default raised to 600s (10 min) — the original 60s was too aggressive
    /// for real human review; expired requests caused the Approve button to
    /// appear unresponsive (POST returned "unknown request {GUID}").
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;
}
