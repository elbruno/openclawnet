namespace OpenClawNet.Tools.Core;

public sealed class ToolExecutionLoggingState : IToolExecutionLoggingState
{
    private readonly object _gate = new();
    private ToolExecutionLoggingOptions _current;

    public ToolExecutionLoggingState(ToolExecutionLoggingOptions options)
    {
        _current = Clone(options);
    }

    public ToolExecutionLoggingOptions Current
    {
        get
        {
            lock (_gate)
            {
                return Clone(_current);
            }
        }
    }

    public void Update(ToolExecutionLoggingOptions options)
    {
        lock (_gate)
        {
            _current = Clone(options);
        }
    }

    public void UpdateEnabled(bool enabled)
    {
        lock (_gate)
        {
            _current.Enabled = enabled;
        }
    }

    private static ToolExecutionLoggingOptions Clone(ToolExecutionLoggingOptions options)
    {
        return new ToolExecutionLoggingOptions
        {
            Enabled = options.Enabled,
            ArgumentPreviewLength = options.ArgumentPreviewLength,
            OutputPreviewLength = options.OutputPreviewLength
        };
    }
}
