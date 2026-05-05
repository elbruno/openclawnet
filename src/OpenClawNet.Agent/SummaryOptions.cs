namespace OpenClawNet.Agent;

/// <summary>
/// Configuration for <see cref="DefaultSummaryService"/>'s local-fallback
/// summarization path. Bind from the <c>Summary</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Issue #107: previously the local summarizer hard-coded the model name
/// <c>llama3.2</c>, which meant operators changing <c>OPENCLAW_OLLAMA_MODEL</c>
/// (or <c>Model:Model</c>) got the configured model for chat but a separate
/// pull for summaries. Operators can now override <c>Summary:Model</c>
/// independently.
/// </para>
/// <para>
/// The default value <c>llama3.2</c> is preserved as a last-resort fallback so
/// existing deployments that haven't set the section keep behaving the same.
/// Per repo policy, no tagged variants (e.g. <c>:3b</c>, <c>:1b</c>) are used.
/// </para>
/// </remarks>
public sealed class SummaryOptions
{
    public const string SectionName = "Summary";

    /// <summary>
    /// Model name forwarded to <see cref="OpenClawNet.Models.Abstractions.IModelClient"/>
    /// when summarizing locally. Defaults to <c>llama3.2</c>.
    /// </summary>
    public string Model { get; set; } = "llama3.2";
}
