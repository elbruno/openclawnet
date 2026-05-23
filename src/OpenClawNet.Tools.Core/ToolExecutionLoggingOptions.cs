namespace OpenClawNet.Tools.Core;

public sealed class ToolExecutionLoggingOptions
{
    public bool Enabled { get; set; } = false;
    public int ArgumentPreviewLength { get; set; } = 512;
    public int OutputPreviewLength { get; set; } = 1024;
}
