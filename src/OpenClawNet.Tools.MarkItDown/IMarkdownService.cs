using ElBruno.MarkItDotNet;

namespace OpenClawNet.Tools.MarkItDown;

/// <summary>
/// Mockable wrapper around <see cref="MarkdownService"/> that exposes only the
/// two conversion paths used by <see cref="MarkItDownTool"/>.
/// Introduced as a testability seam (IMP-4): <c>MarkdownService</c> is a
/// concrete class whose methods are non-virtual, making them impossible to
/// intercept with standard mocking frameworks.
/// </summary>
public interface IMarkdownService
{
    /// <summary>Converts a stream (with a known file extension) to Markdown.</summary>
    Task<ConversionResult> ConvertAsync(
        Stream stream,
        string fileExtension,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches a URL and converts its content to Markdown.</summary>
    Task<ConversionResult> ConvertUrlAsync(
        string url,
        CancellationToken cancellationToken = default);
}
