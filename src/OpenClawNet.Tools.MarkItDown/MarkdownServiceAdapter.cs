using ElBruno.MarkItDotNet;

namespace OpenClawNet.Tools.MarkItDown;

/// <summary>
/// Thin adapter that delegates to the real <see cref="MarkdownService"/> while
/// implementing <see cref="IMarkdownService"/>, enabling constructor injection
/// and unit-test mocking without changing library internals.
/// </summary>
public sealed class MarkdownServiceAdapter : IMarkdownService
{
    private readonly MarkdownService _inner;

    public MarkdownServiceAdapter(MarkdownService inner)
    {
        _inner = inner;
    }

    public Task<ConversionResult> ConvertAsync(
        Stream stream,
        string fileExtension,
        CancellationToken cancellationToken = default)
        => _inner.ConvertAsync(stream, fileExtension, cancellationToken);

    public Task<ConversionResult> ConvertUrlAsync(
        string url,
        CancellationToken cancellationToken = default)
        => _inner.ConvertUrlAsync(url, cancellationToken);
}
