namespace OpenClawNet.Integrations.Jev;

public sealed class JevRoutingOptions
{
    public const string SectionName = "Jev";

    public bool Enabled { get; set; }

    public bool Shadow { get; set; } = true;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);

    public string ApiKey { get; set; } = string.Empty;

    public Uri Endpoint { get; set; } = new("https://api.typesafe.ai");

    public string? DefaultModel { get; set; }
}
