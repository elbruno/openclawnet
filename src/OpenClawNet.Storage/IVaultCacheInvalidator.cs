namespace OpenClawNet.Storage;

public interface IVaultCacheInvalidator
{
    void Invalidate(string name);
}
