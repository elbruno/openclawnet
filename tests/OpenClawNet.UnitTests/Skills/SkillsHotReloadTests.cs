// K-1b — Dylan
// Tests for FileSystemWatcher + 500ms debounce + per-request snapshot pinning
// inside OpenClawNetSkillsRegistry.
//
// Spec contract:
//   - Watcher fires on add/modify/delete under {Root}\skills\
//   - Multiple events within 500ms collapse to ONE rebuild (debounce)
//   - Per-request snapshot pinning: a captured snapshot does NOT mutate even if
//     the underlying disk state changes; only NEW GetSnapshotAsync calls see new state
//   - Watcher cancels cleanly on Dispose / shutdown
//   - Watcher is resilient to filesystem errors (file in use, etc.)
//
// Spec sources:
//   - This prompt (squad spawn)
//   - .squad/decisions.md K-D-1 (next-turn hot reload semantics)
//
// ────────────────────────────────────────────────────────────────────────────
// ⚠ DORMANT until Petey's K-1b OpenClawNetSkillsRegistry lands.
// ────────────────────────────────────────────────────────────────────────────
#if K1B_LANDED
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Skills;
using OpenClawNet.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Skills;

[Trait("Area", "Skills")]
[Trait("Wave", "K-1b")]
[Collection("StorageEnvVar")]
public sealed class SkillsHotReloadTests : IDisposable
{
    private readonly string _root;
    private readonly string? _originalEnv;

    // Generous wait window — debounce is 500ms, leave headroom for FS notification latency.
    private static readonly TimeSpan PostDebounceWait = TimeSpan.FromMilliseconds(1500);

    public SkillsHotReloadTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName);
        _root = Path.Combine(Path.GetTempPath(), $"oc-k1b-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "skills", "system"));
        Directory.CreateDirectory(Path.Combine(_root, "skills", "installed"));
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _originalEnv);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private static string ValidSkill(string name, string body = "Body.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    private void WriteInstalledSkill(string name, string body = "Body.")
    {
        var dir = Path.Combine(_root, "skills", "installed", name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), ValidSkill(name, body));
    }

    private static OpenClawNetSkillsRegistry CreateRegistry()
        => new(NullLogger<OpenClawNetSkillsRegistry>.Instance);

    private static async Task<bool> WaitForAsync(Func<Task<bool>> predicate, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await predicate()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    // ====================================================================
    // A. Add / modify / delete propagation
    // ====================================================================

    [Fact]
    public async Task AddFile_SnapshotEventuallyContainsNewSkill()
    {
        using var registry = CreateRegistry();
        var s0 = await registry.GetSnapshotAsync();
        s0.Skills.Should().BeEmpty();

        WriteInstalledSkill("design-rules");

        var ok = await WaitForAsync(async () =>
        {
            var s = await registry.GetSnapshotAsync();
            return s.Skills.Any(x => x.Name == "design-rules");
        }, PostDebounceWait);

        ok.Should().BeTrue("watcher must pick up the new SKILL.md within debounce + slack");
    }

    [Fact]
    public async Task ModifyFileBody_SnapshotIdChanges()
    {
        WriteInstalledSkill("memory", "v1");
        using var registry = CreateRegistry();
        var s1 = await registry.GetSnapshotAsync();

        var path = Path.Combine(_root, "skills", "installed", "memory", "SKILL.md");
        File.WriteAllText(path, ValidSkill("memory", "v2"));

        var ok = await WaitForAsync(async () =>
        {
            var s = await registry.GetSnapshotAsync();
            return s.SnapshotId != s1.SnapshotId;
        }, PostDebounceWait);

        ok.Should().BeTrue("body change must alter SnapshotId after debounce window");
    }

    [Fact]
    public async Task DeleteFile_RemovedFromSnapshot()
    {
        WriteInstalledSkill("temp");
        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        Directory.Delete(Path.Combine(_root, "skills", "installed", "temp"), recursive: true);

        var ok = await WaitForAsync(async () =>
        {
            var s = await registry.GetSnapshotAsync();
            return !s.Skills.Any(x => x.Name == "temp");
        }, PostDebounceWait);

        ok.Should().BeTrue();
    }

    // ====================================================================
    // B. Debounce behavior — burst of events collapses to one rebuild
    // ====================================================================

    [Fact]
    public async Task RapidChangesWithin500ms_CoalesceToOneRebuild()
    {
        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        // Burst: 10 writes back-to-back within ~100ms.
        var dir = Path.Combine(_root, "skills", "installed", "burst");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SKILL.md");
        for (int i = 0; i < 10; i++)
        {
            File.WriteAllText(path, ValidSkill("burst", $"v{i}"));
            await Task.Delay(10);
        }

        // After debounce, snapshot reflects the FINAL state.
        await Task.Delay(PostDebounceWait);
        var snap = await registry.GetSnapshotAsync();

        var burst = snap.Skills.SingleOrDefault(s => s.Name == "burst");
        burst.Should().NotBeNull("debounced rebuild must surface the final write");
        burst!.Body.Should().Contain("v9");

        // Note: We can't directly assert "exactly one rebuild" from the public API.
        // Petey may expose a counter or a SnapshotResolved event; this test is the
        // observable-behavior gate (final state correct, no torn snapshots).
    }

    // ====================================================================
    // C. Per-request snapshot pinning
    // ====================================================================

    [Fact]
    public async Task PinnedSnapshot_DoesNotMutateAfterDiskChange()
    {
        WriteInstalledSkill("memory");
        using var registry = CreateRegistry();
        var pinned = await registry.GetSnapshotAsync();
        var pinnedNames = pinned.Skills.Select(s => s.Name).ToList();
        var pinnedId = pinned.SnapshotId;

        // Change disk state
        WriteInstalledSkill("design-rules");
        await Task.Delay(PostDebounceWait);

        // Pinned snapshot reference is immutable — even though watcher fired and
        // a new snapshot is now available, the captured value is unchanged.
        pinned.SnapshotId.Should().Be(pinnedId);
        pinned.Skills.Select(s => s.Name).Should().BeEquivalentTo(pinnedNames);
    }

    [Fact]
    public async Task NewRequestAfterChange_GetsNewSnapshot()
    {
        WriteInstalledSkill("memory");
        using var registry = CreateRegistry();
        var s1 = await registry.GetSnapshotAsync();

        WriteInstalledSkill("design-rules");
        await Task.Delay(PostDebounceWait);

        var s2 = await registry.GetSnapshotAsync();
        s2.SnapshotId.Should().NotBe(s1.SnapshotId);
        s2.Skills.Select(x => x.Name).Should().Contain("design-rules");
    }

    // ====================================================================
    // D. Lifecycle — Dispose / shutdown
    // ====================================================================

    [Fact]
    public async Task DisposingRegistry_CancelsWatcher_NoExceptionsAfterDispose()
    {
        var registry = CreateRegistry();
        await registry.GetSnapshotAsync();
        registry.Dispose();

        // Subsequent disk changes must not throw / log AggregateException etc.
        WriteInstalledSkill("post-dispose");
        await Task.Delay(PostDebounceWait);

        // Test passes if no background exception bubbles up to the test runner.
    }

    // ====================================================================
    // E. Resilience to FS errors
    // ====================================================================

    [Fact]
    public async Task FileLockedDuringScan_DoesNotCrashRegistry()
    {
        WriteInstalledSkill("locked");
        var path = Path.Combine(_root, "skills", "installed", "locked", "SKILL.md");

        using var registry = CreateRegistry();
        await registry.GetSnapshotAsync();

        // Open the file with exclusive lock to simulate Windows file-lock conflict.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // While locked, trigger a sibling change to force a rebuild.
            WriteInstalledSkill("trigger");
            await Task.Delay(PostDebounceWait);

            // Should not throw — Petey's spec calls for retry/backoff (250ms x 4) then soft error.
            var snap = await registry.GetSnapshotAsync();
            snap.Should().NotBeNull();
        }
    }
}
#endif
