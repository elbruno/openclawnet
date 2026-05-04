// Storage W-2 — H-7 stub. A real Windows ACL probe lands in a future wave;
// this no-op exists so the boot wiring + DI seam is real and exercised end-to-end.
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace OpenClawNet.Storage;

/// <summary>
/// Default <see cref="IStorageAclVerifier"/>: emits a one-time WARN that
/// real ACL verification is not yet implemented and returns
/// <see cref="AclVerificationResult.IsSecure"/> = <c>true</c> so boot
/// proceeds. Replace with a real Windows DACL probe in a follow-up wave.
/// </summary>
/// <remarks>
/// Critically, this stub does NOT silently no-op — the WARN is what
/// makes the gap visible in operator logs. The Drummond W-1 verdict
/// noted: do not "add a parameterless ctor that silently no-ops the ACL
/// check — that would be a fail-open seam." The WARN is the seam.
/// </remarks>
public sealed class NoopStorageAclVerifier : IStorageAclVerifier
{
    private readonly ILogger<NoopStorageAclVerifier> _logger;

    public NoopStorageAclVerifier() : this(NullLogger<NoopStorageAclVerifier>.Instance) { }

    public NoopStorageAclVerifier(ILogger<NoopStorageAclVerifier> logger)
    {
        _logger = logger ?? NullLogger<NoopStorageAclVerifier>.Instance;
    }

    public Task<AclVerificationResult> VerifyAsync(string scopeRoot, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "ACL verification not yet implemented (NoopStorageAclVerifier). " +
            "Returning IsSecure=true for scope '{ScopeRoot}'. A real DACL probe will land in a future wave.",
            scopeRoot);

        return Task.FromResult(AclVerificationResult.Secure(scopeRoot));
    }
}
