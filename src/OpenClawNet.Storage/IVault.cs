namespace OpenClawNet.Storage;

/// <summary>Audited runtime secret resolution facade.</summary>
public interface IVault
{
    Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default);
}

public sealed record VaultCallerContext(
    VaultCallerType CallerType,
    string CallerId,
    string? SessionId = null);

public enum VaultCallerType
{
    Tool,
    Configuration,
    Cli,
    System
}

public sealed class VaultException : InvalidOperationException
{
    public VaultException() : base("Vault secret resolution failed.") { }

    public VaultException(Exception innerException) : base("Vault secret resolution failed.", innerException) { }
}
