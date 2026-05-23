using System.Text.Json;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Tools.Abstractions;
using OpenClawNet.Tools.Core;

namespace OpenClawNet.UnitTests.Tools;

public class ToolExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsFail_WhenToolNotFound()
    {
        var registry = new ToolRegistry();
        var executor = new ToolExecutor(registry, new AlwaysApprovePolicy(), NullLogger<ToolExecutor>.Instance, CreateLoggingState(false));
        
        var result = await executor.ExecuteAsync("nonexistent", "{}");
        
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
    
    [Fact]
    public async Task ExecuteAsync_CallsTool_WhenFound()
    {
        var registry = new ToolRegistry();
        registry.Register(new SuccessTool());
        var executor = new ToolExecutor(registry, new AlwaysApprovePolicy(), NullLogger<ToolExecutor>.Instance, CreateLoggingState(false));
        
        var result = await executor.ExecuteAsync("success_tool", "{}");
        
        result.Success.Should().BeTrue();
        result.Output.Should().Be("executed");
    }
    
    [Fact]
    public async Task ExecuteBatchAsync_ExecutesAllTools()
    {
        var registry = new ToolRegistry();
        registry.Register(new SuccessTool());
        var executor = new ToolExecutor(registry, new AlwaysApprovePolicy(), NullLogger<ToolExecutor>.Instance, CreateLoggingState(false));
        
        var calls = new List<(string, string)> { ("success_tool", "{}"), ("success_tool", "{}") };
        var results = await executor.ExecuteBatchAsync(calls);
        
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }

    [Fact]
    public async Task ExecuteAsync_LogsDetailedEntries_WhenExtensiveLoggingEnabled()
    {
        var registry = new ToolRegistry();
        registry.Register(new SuccessTool());
        var logger = new ListLogger<ToolExecutor>();

        var executor = new ToolExecutor(
            registry,
            new AlwaysApprovePolicy(),
            logger,
            CreateLoggingState(true));

        var result = await executor.ExecuteAsync("success_tool", """{"message":"hello"}""");

        result.Success.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Tool invocation start"));
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Tool invocation end"));
    }

    [Fact]
    public async Task ExecuteAsync_UsesBaselineLogs_WhenExtensiveLoggingDisabled()
    {
        var registry = new ToolRegistry();
        registry.Register(new SuccessTool());
        var logger = new ListLogger<ToolExecutor>();

        var executor = new ToolExecutor(
            registry,
            new AlwaysApprovePolicy(),
            logger,
            CreateLoggingState(false));

        var result = await executor.ExecuteAsync("success_tool", """{"message":"hello"}""");

        result.Success.Should().BeTrue();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("Executing tool success_tool"));
        logger.Entries.Should().NotContain(e => e.Message.Contains("Tool invocation start"));
    }

    private static IToolExecutionLoggingState CreateLoggingState(bool enabled)
    {
        var options = new ToolExecutionLoggingOptions
        {
            Enabled = enabled,
            ArgumentPreviewLength = 256,
            OutputPreviewLength = 256
        };

        return new ToolExecutionLoggingState(options);
    }
    
    private sealed class SuccessTool : ITool
    {
        public string Name => "success_tool";
        public string Description => "Always succeeds";
        public ToolMetadata Metadata => new()
        {
            Name = Name,
            Description = Description,
            ParameterSchema = JsonDocument.Parse("{}")
        };
        
        public Task<ToolResult> ExecuteAsync(ToolInput input, CancellationToken cancellationToken = default)
            => Task.FromResult(ToolResult.Ok(Name, "executed", TimeSpan.FromMilliseconds(1)));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
