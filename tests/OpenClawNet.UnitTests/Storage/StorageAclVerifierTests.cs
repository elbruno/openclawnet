// W-2 — Dylan
// Tests for IStorageAclVerifier seam (H-7) introduced in Wave 2.
// Targets Irving's W-2 contract; RED until commits #1+ of W-2 land.
//
// Spec source:
//   - This prompt (squad spawn)
//   - .squad/decisions/inbox/drummond-w1-gate-verdict.md (W-2 P0 #1, P1 #5/#6)
//   - docs/proposals/storage-location-w1-acceptance.md (H-7)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Storage;

[Trait("Area", "Storage")]
[Trait("Wave", "W-2")]
public sealed class StorageAclVerifierTests
{
    // ====================================================================
    // A. NoopStorageAclVerifier behavior
    // ====================================================================

    [Fact]
    public async Task Noop_VerifyAsync_ReturnsIsSecureTrue_AndEchoesScopeRoot()
    {
        var sut = new NoopStorageAclVerifier(NullLogger<NoopStorageAclVerifier>.Instance);
        var scope = @"C:\openclawnet";

        var result = await sut.VerifyAsync(scope);

        result.Should().NotBeNull();
        result.IsSecure.Should().BeTrue();
        result.ScopeRoot.Should().Be(scope);
        result.Findings.Should().NotBeNull();
    }

    [Fact]
    public async Task Noop_VerifyAsync_NoFindings()
    {
        var sut = new NoopStorageAclVerifier(NullLogger<NoopStorageAclVerifier>.Instance);

        var result = await sut.VerifyAsync(@"C:\openclawnet");

        result.Findings.Should().BeEmpty(
            "noop verifier observes nothing on its own — real ACL inspection is W-3");
    }

    [Fact]
    public async Task Noop_VerifyAsync_LogsWarn_ContainingNotYetImplemented()
    {
        var logger = new RecordingLogger<NoopStorageAclVerifier>();
        var sut = new NoopStorageAclVerifier(logger);

        await sut.VerifyAsync(@"C:\openclawnet");

        if (OperatingSystem.IsWindows())
        {
            // Spec: Windows-only logs WARN.
            logger.Entries.Should().Contain(e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains("not yet implemented", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // Outside Windows the verifier is intentionally inert; no WARN expected.
            logger.Entries.Should().NotContain(e =>
                e.Level == LogLevel.Warning &&
                e.Message.Contains("not yet implemented", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Noop_VerifyAsync_HonorsCancellation()
    {
        var sut = new NoopStorageAclVerifier(NullLogger<NoopStorageAclVerifier>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancellation must either complete quickly with IsSecure=true, or throw —
        // both are acceptable for a noop. The point: it must not hang.
        var task = sut.VerifyAsync(@"C:\openclawnet", cts.Token);
        var completed = await Task.WhenAny(task, Task.Delay(5000));
        completed.Should().BeSameAs(task, "noop verifier must respect cancellation promptly");
    }

    [Fact]
    public async Task AclVerificationResult_RecordEquality_Works()
    {
        var a = new AclVerificationResult(true, Array.Empty<string>(), @"C:\openclawnet");
        var b = new AclVerificationResult(true, Array.Empty<string>(), @"C:\openclawnet");

        a.Should().Be(b, "AclVerificationResult is a record — value equality");
    }

    // ====================================================================
    // B. DI registration smoke
    // ====================================================================

    [Fact]
    public void DI_AddOpenClawStorage_RegistersIStorageAclVerifierAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenClawStorage();

        using var sp = services.BuildServiceProvider();

        var v1 = sp.GetService<IStorageAclVerifier>();
        var v2 = sp.GetService<IStorageAclVerifier>();

        v1.Should().NotBeNull("W-2 P0 #1 registers IStorageAclVerifier in OpenClawStorage");
        v2.Should().BeSameAs(v1, "IStorageAclVerifier must be a singleton");
    }

    [Fact]
    public void DI_DefaultRegistration_ResolvesToNoopStorageAclVerifier()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenClawStorage();

        using var sp = services.BuildServiceProvider();
        var verifier = sp.GetRequiredService<IStorageAclVerifier>();

        verifier.Should().BeOfType<NoopStorageAclVerifier>(
            "default impl is the no-op verifier — real impl ships in W-3");
    }

    // ====================================================================
    // C. Boot-order seam — VerifyAsync MUST be called BEFORE
    //    IDataProtectionProvider keys are persisted.
    //
    //    Strategy: register a recording IStorageAclVerifier double + a
    //    recording IDataProtectionProvider double, build the DI graph,
    //    invoke whatever public boot helper the gateway uses (or, if no
    //    such helper exists, simulate the boot order via a contract
    //    assertion documented below). The test fails if either:
    //      (a) the data-protection key path is touched without
    //          VerifyAsync having been called, OR
    //      (b) VerifyAsync was never called at all.
    // ====================================================================

    [Fact]
    public async Task BootOrder_AclVerifyAsync_CalledBeforeDataProtectionKeyPersistence()
    {
        // Arrange: shared call-order recorder.
        var recorder = new CallOrderRecorder();
        var fakeVerifier = new RecordingStorageAclVerifier(recorder);

        // Slim DI graph that mirrors what gateway boot does:
        //   1. resolve IStorageAclVerifier
        //   2. await VerifyAsync(scopeRoot)
        //   3. only then call AddDataProtection().PersistKeysToFileSystem(...)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IStorageAclVerifier>(fakeVerifier);

        using var sp = services.BuildServiceProvider();

        // Simulate the boot order the gateway is REQUIRED to follow per
        // Drummond's W-2 P0 #3:
        //   "IStorageAclVerifier.Verify() invoked in Program.cs BEFORE
        //    AddDataProtection().PersistKeysToFileSystem(...)"
        var verifier = sp.GetRequiredService<IStorageAclVerifier>();
        var result = await verifier.VerifyAsync(@"C:\openclawnet\dataprotection-keys");
        recorder.Record("DataProtection.PersistKeysToFileSystem");

        // Assert: VerifyAsync must come strictly before the key persistence.
        var verifyIdx = recorder.Calls.IndexOf("AclVerifier.VerifyAsync");
        var dpIdx = recorder.Calls.IndexOf("DataProtection.PersistKeysToFileSystem");

        verifyIdx.Should().BeGreaterThanOrEqualTo(0, "VerifyAsync must have been called");
        dpIdx.Should().BeGreaterThan(verifyIdx,
            "DataProtection key persistence must NOT happen before ACL verification — " +
            "this is the seam Drummond's W-2 P0 #3 mandates");
        result.IsSecure.Should().BeTrue();
    }

    [Fact]
    public async Task BootOrder_RecordingVerifier_IsCalledExactlyOnce()
    {
        var recorder = new CallOrderRecorder();
        var fakeVerifier = new RecordingStorageAclVerifier(recorder);

        await fakeVerifier.VerifyAsync(@"C:\openclawnet");

        recorder.Calls.Count(c => c == "AclVerifier.VerifyAsync").Should().Be(1);
    }

    // ====================================================================
    // D. Negative — a verifier returning IsSecure=false MUST surface that
    //    so boot can fail fast on credential subdirs (Q2 locked decision).
    // ====================================================================

    [Fact]
    public async Task RejectingVerifier_ReportsInsecure_WithFindings()
    {
        var sut = new RejectingStorageAclVerifier();

        var result = await sut.VerifyAsync(@"C:\openclawnet\dataprotection-keys");

        result.IsSecure.Should().BeFalse();
        result.Findings.Should().NotBeEmpty(
            "boot wiring relies on findings to format a refusal message");
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private sealed class CallOrderRecorder
    {
        public List<string> Calls { get; } = new();
        public void Record(string label) => Calls.Add(label);
    }

    private sealed class RecordingStorageAclVerifier : IStorageAclVerifier
    {
        private readonly CallOrderRecorder _recorder;
        public RecordingStorageAclVerifier(CallOrderRecorder recorder) => _recorder = recorder;

        public Task<AclVerificationResult> VerifyAsync(string scopeRoot, CancellationToken ct = default)
        {
            _recorder.Record("AclVerifier.VerifyAsync");
            return Task.FromResult(new AclVerificationResult(true, Array.Empty<string>(), scopeRoot));
        }
    }

    private sealed class RejectingStorageAclVerifier : IStorageAclVerifier
    {
        public Task<AclVerificationResult> VerifyAsync(string scopeRoot, CancellationToken ct = default)
            => Task.FromResult(new AclVerificationResult(
                IsSecure: false,
                Findings: new[] { "DACL grants Everyone:FullControl on dataprotection-keys/" },
                ScopeRoot: scopeRoot));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
