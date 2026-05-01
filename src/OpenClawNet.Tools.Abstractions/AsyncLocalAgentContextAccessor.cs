namespace OpenClawNet.Tools.Abstractions;

/// <summary>
/// Default <see cref="IAgentContextAccessor"/> backed by <see cref="AsyncLocal{T}"/>,
/// so the agent identity flows through async/await boundaries without explicit plumbing.
/// </summary>
public sealed class AsyncLocalAgentContextAccessor : IAgentContextAccessor
{
    private static readonly AsyncLocal<AgentExecutionContext?> _current = new();

    public AgentExecutionContext? Current => _current.Value;

    public IDisposable Push(AgentExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = _current.Value;
        _current.Value = context;
        return new Restorer(previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly AgentExecutionContext? _previous;
        private bool _disposed;

        public Restorer(AgentExecutionContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
