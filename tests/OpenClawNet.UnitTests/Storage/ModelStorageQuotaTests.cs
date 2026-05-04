// W-3 — Dylan
// Tests for IModelStorageQuota / ModelStorageQuota introduced in Wave 3.
// Spec sources:
//   - This prompt (squad spawn) — contract block
//   - .squad/decisions/inbox/drummond-w2-gate-verdict.md (W-3 P0 #2)
//
// Contract under test:
//   Task<QuotaCheckResult> CheckAsync(string modelsRoot, long incomingBytes, CancellationToken ct = default);
//   record QuotaCheckResult(bool Allowed, long CurrentTotalBytes, long AvailableDiskBytes, string? DenyReason);
//
// Defaults: 50 GB total, 20 GB per-file. 30s directory-walk cache.
// DriveInfo.AvailableFreeSpace cross-check.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Storage;

[Trait("Area", "Storage")]
[Trait("Wave", "W-3")]
public sealed class ModelStorageQuotaTests : IDisposable
{
    private readonly string _modelsRoot;

    private const long Gb = 1024L * 1024L * 1024L;
    private const long DefaultMaxTotalBytes = 50L * Gb;
    private const long DefaultMaxPerFileBytes = 20L * Gb;

    public ModelStorageQuotaTests()
    {
        _modelsRoot = Path.Combine(Path.GetTempPath(), $"oc-w3-quota-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_modelsRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_modelsRoot)) Directory.Delete(_modelsRoot, recursive: true); }
        catch { /* best-effort */ }
    }

    private static IModelStorageQuota NewSut(StorageOptions? options = null)
    {
        options ??= new StorageOptions();
        return new ModelStorageQuota(
            Options.Create(options),
            NullLogger<ModelStorageQuota>.Instance);
    }

    private static StorageOptions OptionsWith(long? maxTotal = null, long? maxPerFile = null)
    {
        var o = new StorageOptions();
        if (maxTotal.HasValue) o.ModelMaxTotalBytes = maxTotal.Value;
        if (maxPerFile.HasValue) o.ModelMaxPerFileBytes = maxPerFile.Value;
        return o;
    }

    // ====================================================================
    // A. Happy path
    // ====================================================================

    [Fact]
    public async Task EmptyModelsRoot_With1GbIncoming_IsAllowed()
    {
        var sut = NewSut();

        var result = await sut.CheckAsync(_modelsRoot, 1L * Gb);

        result.Allowed.Should().BeTrue();
        result.CurrentTotalBytes.Should().Be(0);
        result.DenyReason.Should().BeNull();
        result.AvailableDiskBytes.Should().BeGreaterThanOrEqualTo(0);
    }

    // ====================================================================
    // B. Per-file ceiling
    // ====================================================================

    [Fact]
    public async Task PerFileOverDefault_Is21Gb_Denied_WithPerFileReason()
    {
        var sut = NewSut();

        var result = await sut.CheckAsync(_modelsRoot, 21L * Gb);

        result.Allowed.Should().BeFalse();
        result.DenyReason.Should().NotBeNull();
        result.DenyReason!.ToLowerInvariant().Should().Contain("per-file",
            "DenyReason must mention per-file ceiling");
    }

    [Fact]
    public async Task CustomPerFileLimit_OverridesDefault()
    {
        var sut = NewSut(OptionsWith(maxPerFile: 100L)); // 100 bytes ceiling

        var result = await sut.CheckAsync(_modelsRoot, 200L);

        result.Allowed.Should().BeFalse();
        result.DenyReason.Should().NotBeNull();
        result.DenyReason!.ToLowerInvariant().Should().Contain("per-file");
    }

    // ====================================================================
    // C. Total quota
    // ====================================================================

    [Fact]
    public async Task TotalOverQuota_ExistingPlusIncomingExceeds_Denied_WithTotalReason()
    {
        // Use a small synthetic quota so we don't have to write 49 GB.
        // Spec equivalent: existing 49 GB + 2 GB incoming → denied.
        // Equivalent: existing 1 KB + 5 KB incoming with total cap of 4 KB.
        WriteFile("existing.bin", 1024);
        var sut = NewSut(OptionsWith(maxTotal: 4096, maxPerFile: long.MaxValue));

        var result = await sut.CheckAsync(_modelsRoot, 5 * 1024);

        result.Allowed.Should().BeFalse();
        result.DenyReason.Should().NotBeNull();
        result.DenyReason!.ToLowerInvariant().Should().Contain("total",
            "DenyReason must mention total quota");
        result.CurrentTotalBytes.Should().Be(1024);
    }

    [Fact]
    public async Task BoundaryCase_ExistingPlusIncomingEqualsQuota_DocumentsBehavior()
    {
        // Spec asks: "Existing 49 GB + 1 GB incoming = exactly 50 GB → boundary
        // case (decide: allowed or denied; document decision)."
        //
        // DECISION (Dylan, recorded in test): boundary is INCLUSIVE (allowed).
        // Rationale: defaults are advertised in GB units which round; making the
        // exact-equal case allowed avoids surprising a user whose model lands
        // exactly at the configured ceiling. If Irving prefers exclusive, this
        // single assertion flips and the decision moves to the inbox.
        WriteFile("existing.bin", 2048);
        var sut = NewSut(OptionsWith(maxTotal: 4096, maxPerFile: long.MaxValue));

        var result = await sut.CheckAsync(_modelsRoot, 2048);

        result.Allowed.Should().BeTrue("inclusive boundary is the documented decision");
    }

    [Fact]
    public async Task CustomTotalLimit_OverridesDefault()
    {
        var sut = NewSut(OptionsWith(maxTotal: 1024, maxPerFile: long.MaxValue));

        var result = await sut.CheckAsync(_modelsRoot, 2048);

        result.Allowed.Should().BeFalse();
    }

    // ====================================================================
    // D. CurrentTotalBytes accuracy
    // ====================================================================

    [Fact]
    public async Task CurrentTotalBytes_ReflectsWalkedSize()
    {
        WriteFile("a.bin", 100);
        WriteFile("b.bin", 200);
        WriteFile("nested\\c.bin", 300);
        var sut = NewSut();

        var result = await sut.CheckAsync(_modelsRoot, 1);

        result.CurrentTotalBytes.Should().Be(600,
            "directory walk must include nested files");
    }

    // ====================================================================
    // E. AvailableDiskBytes
    // ====================================================================

    [Fact]
    public async Task AvailableDiskBytes_NonNegative()
    {
        var sut = NewSut();

        var result = await sut.CheckAsync(_modelsRoot, 1);

        result.AvailableDiskBytes.Should().BeGreaterThanOrEqualTo(0);
    }

    // ====================================================================
    // F. Cancellation
    // ====================================================================

    [Fact]
    public async Task Cancellation_Throws_OperationCanceledException()
    {
        var sut = NewSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.CheckAsync(_modelsRoot, 1, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ====================================================================
    // G. Cache behavior — ≤30s directory-walk cache
    // ====================================================================

    [Fact]
    public async Task DirectoryWalkCache_SecondCallSeesCachedTotal_WithinWindow()
    {
        WriteFile("seed.bin", 100);
        var sut = NewSut();

        var first = await sut.CheckAsync(_modelsRoot, 1);
        first.CurrentTotalBytes.Should().Be(100);

        // Add 500 more bytes BETWEEN calls.
        WriteFile("added.bin", 500);

        var second = await sut.CheckAsync(_modelsRoot, 1);

        // Within 30s window the walk is cached; CurrentTotalBytes should still
        // reflect the first walk (100), not 600.
        second.CurrentTotalBytes.Should().Be(100,
            "directory walk is cached for ≤30s — second call must reuse first walk");
    }

    [Fact(Skip = "needs virtual time injection — re-enable once IClock seam exists")]
    public async Task DirectoryWalkCache_InvalidatesAfter30Seconds()
    {
        await Task.CompletedTask;
        // When ModelStorageQuota accepts an IClock (or equivalent test seam),
        // advance virtual time past 30s and assert the second walk picks up
        // newly written files. Without a clock seam this would require a
        // 30+s sleep, which we refuse to do in unit tests.
    }

    // ====================================================================
    // H. Concurrency safety — no double-walk, lock works
    // ====================================================================

    [Fact]
    public async Task ConcurrentCalls_AreSafe()
    {
        WriteFile("seed.bin", 100);
        var sut = NewSut();

        var tasks = new Task<QuotaCheckResult>[16];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = sut.CheckAsync(_modelsRoot, 1);
        }

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
        {
            r.Should().NotBeNull();
            r.CurrentTotalBytes.Should().Be(100);
            r.Allowed.Should().BeTrue();
        }
    }

    // ====================================================================
    // I. Null / missing models root resilience
    // ====================================================================

    [Fact]
    public async Task NonExistentModelsRoot_TreatsCurrentTotalAs0_AndAllowsSmallIncoming()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"oc-quota-missing-{Guid.NewGuid():N}");
        var sut = NewSut();

        var result = await sut.CheckAsync(bogus, 1024);

        result.CurrentTotalBytes.Should().Be(0);
        result.Allowed.Should().BeTrue();
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private void WriteFile(string relPath, int sizeBytes)
    {
        var full = Path.Combine(_modelsRoot, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var data = new byte[sizeBytes];
        File.WriteAllBytes(full, data);
    }
}
