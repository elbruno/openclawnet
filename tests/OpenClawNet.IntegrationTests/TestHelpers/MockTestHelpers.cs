using System.Net;
using Microsoft.Extensions.Logging;

namespace OpenClawNet.IntegrationTests.TestHelpers;

/// <summary>
/// Mock HttpMessageHandler for testing HTTP calls
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private Exception? _exception;
    public string? RequestUri { get; private set; }
    public string? RequestContent { get; private set; }

    public void Setup(HttpStatusCode statusCode)
    {
        _statusCode = statusCode;
        _exception = null;
    }

    public void SetupThrowException(Exception exception)
    {
        _exception = exception;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_exception != null)
            throw _exception;

        RequestUri = request.RequestUri?.ToString();

        if (request.Content != null)
        {
            RequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(_statusCode);
    }
}

/// <summary>
/// Mock logger for testing logging calls
/// </summary>
internal class MockLogger<T> : ILogger<T>
{
    public List<LogCall> LogCalls { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        LogCalls.Add(new LogCall { Level = logLevel, Message = formatter(state, exception) });
    }

    public class LogCall
    {
        public LogLevel Level { get; set; }
        public string? Message { get; set; }
    }
}
