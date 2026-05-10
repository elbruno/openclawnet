using Microsoft.Extensions.Logging;

namespace OpenClawNet.Storage;

public sealed class VaultService : IVault
{
    private readonly ISecretsStore _store;
    private readonly ISecretAccessAuditor _auditor;
    private readonly IVaultSecretRedactor _redactor;
    private readonly ILogger<VaultService> _logger;

    public VaultService(
        ISecretsStore store,
        ISecretAccessAuditor auditor,
        IVaultSecretRedactor redactor,
        ILogger<VaultService> logger)
    {
        _store = store;
        _auditor = auditor;
        _redactor = redactor;
        _logger = logger;
    }

    public async Task<string?> ResolveAsync(string name, VaultCallerContext ctx, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            await _auditor.RecordAsync("<invalid>", ctx, success: false, ct).ConfigureAwait(false);
            throw new VaultException();
        }

        try
        {
            var value = await _store.GetAsync(name, ct).ConfigureAwait(false);
            var success = value is not null;
            await _auditor.RecordAsync(name, ctx, success, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Vault secret resolved: secretName={SecretName}, callerType={CallerType}, success={Success}",
                name,
                ctx.CallerType,
                success);

            if (value is not null)
                _redactor.TrackResolvedValue(value);

            if (!success)
                throw new VaultException();

            return value;
        }
        catch (VaultException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _auditor.RecordAsync(name, ctx, success: false, ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Vault secret resolution failed: secretName={SecretName}, callerType={CallerType}, success=False, errorClass={ErrorClass}",
                name,
                ctx.CallerType,
                ex.GetType().Name);
            throw new VaultException(ex);
        }
    }
}
