namespace OpenClawNet.Storage;

public interface IVaultErrorShield
{
    string GenericToolError { get; }
    bool IsVaultFailure(Exception exception);
}

public sealed class VaultErrorShield : IVaultErrorShield
{
    public const string GenericUnavailableMessage = "required configuration unavailable";

    public string GenericToolError => GenericUnavailableMessage;

    public bool IsVaultFailure(Exception exception) =>
        exception is VaultException || exception.InnerException is VaultException;
}
