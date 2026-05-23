namespace OpenClawNet.Tools.Core;

public interface IToolExecutionLoggingState
{
    ToolExecutionLoggingOptions Current { get; }
    void Update(ToolExecutionLoggingOptions options);
    void UpdateEnabled(bool enabled);
}
