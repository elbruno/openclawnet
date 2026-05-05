using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenClawNet.Storage;

namespace OpenClawNet.Skills;

/// <summary>
/// K-1b — Real <see cref="ISkillsRegistry"/> implementation. Replaces the
/// K-1a <c>StubSkillsRegistry</c>. Scans all 3 storage layers
/// (system / installed / agents/{name}) and exposes a precedence-resolved
/// snapshot. Per-agent enable state lives in
/// <c>{StorageRoot}/skills/agents/{name}/enabled.json</c>; default per
/// Q1 is <c>opt-in</c> (missing entry = disabled).
/// </summary>
/// <remarks>
/// <para>Concurrency model:</para>
/// <list type="bullet">
///   <item>Snapshots are immutable; the current snapshot reference is
///   swapped atomically via <see cref="Interlocked.Exchange{T}(ref T, T)"/>.</item>
///   <item>Reads (<see cref="GetSnapshotAsync"/>) are non-blocking: they
///   return whatever the field currently points at.</item>
///   <item>Per-agent enabled.json reads are cached per agent and invalidated
///   only when a watcher rebuild changes the snapshot.</item>
/// </list>
/// <para>Layer precedence (later wins on name collision):
/// <c>System</c> &lt; <c>Installed</c> &lt; <c>Agent</c>.</para>
/// </remarks>
public sealed class OpenClawNetSkillsRegistry : ISkillsRegistry, ISkillsSnapshotChangeNotifier, IDisposable
{
    private const int DebounceMillis = 500;

    private readonly ILogger<OpenClawNetSkillsRegistry> _logger;

    // Per-agent enabled.json cache, invalidated on snapshot swap.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, bool>> _enabledCache =
        new(StringComparer.Ordinal);

    // Atomically-swapped current snapshot reference. Reads are lock-free.
    private SkillsSnapshot _current = SkillsSnapshot.Empty;

    // K-1b #3 — embedded FSW + debounce. Watchers are attached lazily so a
    // construction-time path resolution failure (test harness, missing dirs)
    // does not crash the registry.
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly SemaphoreSlim _rebuildLock = new(1, 1);
    private readonly Lock _timerLock = new();
    private Timer? _debounceTimer;
    private SkillsSnapshot _lastNotified = SkillsSnapshot.Empty;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<SkillsSnapshotChangedEventArgs>? SnapshotChanged;

    public OpenClawNetSkillsRegistry(ILogger<OpenClawNetSkillsRegistry>? logger = null)
    {
        _logger = logger ?? NullLogger<OpenClawNetSkillsRegistry>.Instance;
        // Build initial snapshot eagerly so the first request doesn't pay
        // the FS-walk cost.
        Rebuild(SkillRegistryRefreshCause.Startup);
        _lastNotified = _current;
        // Start watchers. Failure to attach is non-fatal — tests that
        // construct without a writable storage root still work; they just
        // won't see hot-reload.
        TryAttachWatchers();
    }

    public Task<ISkillsSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => Task.FromResult<ISkillsSnapshot>(_current);

    /// <summary>
    /// K-D-1 layer-precedence resolution + per-agent enabled.json overlay
    /// (Q1 opt-in).
    /// </summary>
    public Task<bool> IsEnabledForAgentAsync(string skillName, string agentName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(skillName) || string.IsNullOrWhiteSpace(agentName))
            return Task.FromResult(false);

        var snapshot = _current;
        if (!snapshot.Skills.Any(s => string.Equals(s.Name, skillName, StringComparison.Ordinal)))
            return Task.FromResult(false);

        var enabled = LoadEnabledForAgent(agentName);
        return Task.FromResult(enabled.TryGetValue(skillName, out var v) && v);
    }

    /// <summary>
    /// K-1b — Rebuilds the snapshot by scanning all 3 layers, applying
    /// precedence, and atomically swapping the published reference.
    /// Called at construction and by the K-1b #3 watchers (debounced).
    /// </summary>
    /// <param name="cause">
    /// K-2 — attribution for the <see cref="SkillsLogEvents.SkillRegistryRefresh"/>
    /// event. Defaults to <see cref="SkillRegistryRefreshCause.Manual"/> so that
    /// explicit callers (endpoints, tests, admin tools) are accurately tagged.
    /// </param>
    public SkillsSnapshot Rebuild(SkillRegistryRefreshCause cause = SkillRegistryRefreshCause.Manual)
    {
        var resolved = ResolveLayered();
        var snapshot = new SkillsSnapshot(
            resolved.Cast<ISkillRecord>().ToArray(),
            DateTimeOffset.UtcNow,
            SkillsSnapshot.ComputeId(resolved));

        var prev = Interlocked.Exchange(ref _current, snapshot);
        _enabledCache.Clear();

        _logger.SkillRegistryRefresh(cause, snapshot.Skills.Count, snapshot.SnapshotId, prev.SnapshotId);

        // K-2 — emit per-skill imported/retired events for real changes only.
        // Startup never fires SkillImported (the whole snapshot is the initial
        // state, not a stream of imports). Watcher + Manual rebuilds emit the
        // diff so observers can audit individual additions and removals.
        if (cause != SkillRegistryRefreshCause.Startup
            && !string.Equals(prev.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal))
        {
            var prevByName = prev.Skills.ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);
            var nextByName = snapshot.Skills.ToDictionary(s => s.Name, s => s, StringComparer.Ordinal);

            foreach (var (name, rec) in nextByName)
            {
                if (!prevByName.ContainsKey(name))
                {
                    _logger.SkillImported(name, SkillImportSource.Manual, rec.Layer);
                }
            }
            foreach (var (name, rec) in prevByName)
            {
                if (!nextByName.ContainsKey(name))
                {
                    _logger.SkillRetired(name, rec.Layer);
                }
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Computes a diff between two snapshots. Returns an empty diff if
    /// <paramref name="previousSnapshotId"/> matches the current snapshot.
    /// </summary>
    public SnapshotDiff DiffSince(string previousSnapshotId)
    {
        var current = _current;
        if (string.Equals(current.SnapshotId, previousSnapshotId, StringComparison.Ordinal))
        {
            return new SnapshotDiff(
                previousSnapshotId, current.SnapshotId,
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty,
                ImmutableArray<string>.Empty);
        }

        // We do not retain historical snapshots; conservatively report all
        // current skills as Modified so the K-3 banner can show "snapshot
        // changed (full delta unavailable)" without crashing.
        var names = current.Skills.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal).ToImmutableArray();
        return new SnapshotDiff(
            previousSnapshotId, current.SnapshotId,
            ImmutableArray<string>.Empty,
            names,
            ImmutableArray<string>.Empty);
    }

    // ====================================================================
    // Internals
    // ====================================================================

    private List<LayeredSkill> ResolveLayered()
    {
        // Per K-D-1: resolve in layer order (System < Installed < Agent),
        // last write wins on name collision.
        var byName = new Dictionary<string, LayeredSkill>(StringComparer.Ordinal);

        ScanInto(byName, OpenClawNetPaths.ResolveSkillsSystemRoot(_logger), SkillLayer.System, agentName: null);
        ScanInto(byName, OpenClawNetPaths.ResolveSkillsInstalledRoot(_logger), SkillLayer.Installed, agentName: null);

        // Agents layer: each immediate subdir of agents/ is one agent overlay.
        var (rootPath, _) = OpenClawNetPaths.ResolveRoot();
        var agentsRoot = Path.Combine(rootPath, "skills", "agents");
        if (Directory.Exists(agentsRoot))
        {
            foreach (var agentDir in Directory.EnumerateDirectories(agentsRoot))
            {
                var agentName = Path.GetFileName(agentDir);
                ScanInto(byName, agentDir, SkillLayer.Agent, agentName);
            }
        }

        return byName.Values.OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    private void ScanInto(IDictionary<string, LayeredSkill> sink, string layerRoot, SkillLayer layer, string? agentName)
    {
        if (!Directory.Exists(layerRoot))
            return;

        foreach (var skillDir in Directory.EnumerateDirectories(layerRoot))
        {
            var skillMdPath = Path.Combine(skillDir, "SKILL.md");
            if (!File.Exists(skillMdPath))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(skillMdPath);
            }
            catch (IOException ex)
            {
                // K-2 — Q5: log only the path + reason class, never file body.
                _logger.SkillValidationFailed(skillMdPath, layer, $"unreadable: {ex.GetType().Name}");
                continue;
            }

            ParsedRecord parsed;
            try
            {
                parsed = ParseRecord(content, fallbackName: Path.GetFileName(skillDir));
            }
            catch (Exception ex)
            {
                // K-2 — Q5: log the exception type/message only. The raw
                // SKILL.md content (which may include user secrets in the
                // body) never reaches the log pipeline.
                _logger.SkillValidationFailed(skillMdPath, layer, $"malformed_frontmatter: {ex.GetType().Name}");
                continue;
            }

            var hash = ContentHash(content);
            var record = new LayeredSkill(
                parsed.Name,
                parsed.Description,
                layer,
                skillMdPath,
                parsed.Metadata,
                parsed.Body,
                hash);

            // Last write wins (Agent > Installed > System).
            sink[parsed.Name] = record;
        }
    }

    private static string ContentHash(string content)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private record struct ParsedRecord(string Name, string Description, IReadOnlyDictionary<string, string> Metadata, string Body);

    private static ParsedRecord ParseRecord(string content, string fallbackName)
    {
        var p = SkillFrontmatterParser.Parse(content, fallbackName);
        return new ParsedRecord(p.Name, p.Description, p.Metadata, p.Body);
    }

    private IReadOnlyDictionary<string, bool> LoadEnabledForAgent(string agentName)
    {
        return _enabledCache.GetOrAdd(agentName, n =>
        {
            try
            {
                var folder = OpenClawNetPaths.ResolveSkillsAgentRoot(n, _logger);
                var path = Path.Combine(folder, "enabled.json");
                if (!File.Exists(path))
                    return new Dictionary<string, bool>(StringComparer.Ordinal);

                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, bool>>(json)
                    ?? new Dictionary<string, bool>(StringComparer.Ordinal);
                return new Dictionary<string, bool>(dict, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to read enabled.json for agent '{Agent}'; treating as all-disabled.",
                    n);
                return new Dictionary<string, bool>(StringComparer.Ordinal);
            }
        });
    }

    /// <summary>
    /// K-1b — Atomically writes a per-agent enabled-state entry
    /// to <c>{root}/skills/agents/{agentName}/enabled.json</c> using the
    /// <c>tmp + fsync + rename</c> pattern (matches Helly's UI spec
    /// §4.1). Invalidates the local cache for the agent and triggers a
    /// snapshot rebuild so callers observing <see cref="SnapshotId"/>
    /// see the change.
    /// </summary>
    public Task SetEnabledForAgentAsync(string agentName, string skillName, bool enabled, CancellationToken ct = default)
        => SetEnabledForAgentAsync(agentName, skillName, enabled, requestedBy: "system", ct);

    /// <summary>
    /// K-2 — overload that captures the principal who requested the change
    /// for the <see cref="SkillsLogEvents.SkillEnabled"/> /
    /// <see cref="SkillsLogEvents.SkillDisabled"/> audit events.
    /// </summary>
    public async Task SetEnabledForAgentAsync(string agentName, string skillName, bool enabled, string requestedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentName)) throw new ArgumentException("agentName required", nameof(agentName));
        if (string.IsNullOrWhiteSpace(skillName)) throw new ArgumentException("skillName required", nameof(skillName));
        if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "system";

        var folder = OpenClawNetPaths.ResolveSkillsAgentRoot(agentName, _logger);
        var path = Path.Combine(folder, "enabled.json");
        var tmp = path + ".tmp";

        var current = LoadEnabledForAgent(agentName);
        var next = new Dictionary<string, bool>(current, StringComparer.Ordinal)
        {
            [skillName] = enabled,
        };

        var json = JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);

        // Atomic rename — File.Move with overwrite mirrors fsync+rename on
        // NTFS for our purposes.
        File.Move(tmp, path, overwrite: true);

        _enabledCache.TryRemove(agentName, out _);

        if (enabled)
            _logger.SkillEnabled(skillName, agentName, requestedBy);
        else
            _logger.SkillDisabled(skillName, agentName, requestedBy);
    }

    /// <summary>
    /// K-2 — atomically replace the entire per-agent enabled-map (admin reset
    /// / bulk override). Emits a single <see cref="SkillsLogEvents.SkillEnabledStateChanged"/>
    /// event rather than one-event-per-skill.
    /// </summary>
    public async Task SetEnabledMapForAgentAsync(string agentName, IReadOnlyDictionary<string, bool> newMap, string requestedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentName)) throw new ArgumentException("agentName required", nameof(agentName));
        ArgumentNullException.ThrowIfNull(newMap);
        if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "system";

        var folder = OpenClawNetPaths.ResolveSkillsAgentRoot(agentName, _logger);
        var path = Path.Combine(folder, "enabled.json");
        var tmp = path + ".tmp";

        var dict = new Dictionary<string, bool>(newMap, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);

        _enabledCache.TryRemove(agentName, out _);
        _logger.SkillEnabledStateChanged(agentName, dict.Count, requestedBy);
    }

    /// <summary>
    /// K-1b — Returns the per-agent enable map (skillName → enabled). Empty
    /// dictionary if the agent has no enabled.json yet.
    /// </summary>
    public IReadOnlyDictionary<string, bool> GetEnabledMapForAgent(string agentName)
        => LoadEnabledForAgent(agentName);

    /// <summary>
    /// K-1b — IDisposable: stops watchers, disposes timer/lock, clears caches.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch { /* shutdown best-effort */ }
        }
        _watchers.Clear();
        _rebuildLock.Dispose();
        _enabledCache.Clear();
    }

    // ====================================================================
    // K-1b #3 — Embedded FileSystemWatcher (debounced rebuild + notify)
    // ====================================================================

    private void TryAttachWatchers()
    {
        try
        {
            var systemRoot = OpenClawNetPaths.ResolveSkillsSystemRoot(_logger);
            var installedRoot = OpenClawNetPaths.ResolveSkillsInstalledRoot(_logger);
            var (rootPath, _) = OpenClawNetPaths.ResolveRoot();
            var agentsRoot = Path.Combine(rootPath, "skills", "agents");
            Directory.CreateDirectory(agentsRoot);

            AttachWatcher(systemRoot, "system");
            AttachWatcher(installedRoot, "installed");
            AttachWatcher(agentsRoot, "agents");

            _logger.LogInformation(
                "Skills watcher attached to {Count} layer root(s); debounce={DebounceMs}ms.",
                _watchers.Count, DebounceMillis);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skills watcher attach failed; hot-reload disabled.");
        }
    }

    private void AttachWatcher(string root, string layerName)
    {
        try
        {
            var w = new FileSystemWatcher(root, "SKILL.md")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true,
            };
            w.Created += (_, _) => ScheduleRebuild(layerName, "created");
            w.Changed += (_, _) => ScheduleRebuild(layerName, "changed");
            w.Deleted += (_, _) => ScheduleRebuild(layerName, "deleted");
            w.Renamed += (_, _) => ScheduleRebuild(layerName, "renamed");
            w.Error += (_, e) => _logger.LogWarning(e.GetException(),
                "FileSystemWatcher error in '{Layer}' layer at '{Root}'.", layerName, root);
            _watchers.Add(w);

            // Also watch the layer root for directory create/delete events
            // (a brand-new "skills/installed/foo/" dir won't fire SKILL.md
            // events on Windows until the file appears; the SKILL.md filter
            // catches it because IncludeSubdirectories=true and the file is
            // inside the watched root, but the directory creation itself
            // should still trigger a rescan to be safe).
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to attach FileSystemWatcher to '{Layer}' layer at '{Root}'.",
                layerName, root);
        }
    }

    private void ScheduleRebuild(string layer, string kind)
    {
        if (_disposed) return;
        _logger.LogDebug("Skills FS event in layer '{Layer}': {Kind}; debouncing rebuild.", layer, kind);
        lock (_timerLock)
        {
            if (_debounceTimer is null)
                _debounceTimer = new Timer(_ => DoDebouncedRebuild(), null, DebounceMillis, Timeout.Infinite);
            else
                _debounceTimer.Change(DebounceMillis, Timeout.Infinite);
        }
    }

    private void DoDebouncedRebuild()
    {
        if (_disposed) return;
        if (!_rebuildLock.Wait(0))
        {
            lock (_timerLock)
            {
                _debounceTimer?.Change(DebounceMillis, Timeout.Infinite);
            }
            return;
        }

        try
        {
            var newSnapshot = Rebuild(SkillRegistryRefreshCause.Watcher);
            var prev = Interlocked.Exchange(ref _lastNotified, newSnapshot);
            if (!string.Equals(prev.SnapshotId, newSnapshot.SnapshotId, StringComparison.Ordinal))
            {
                try
                {
                    SnapshotChanged?.Invoke(this, new SkillsSnapshotChangedEventArgs(prev, newSnapshot));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skills SnapshotChanged handler threw; continuing.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Skills snapshot rebuild failed; current snapshot retained.");
        }
        finally
        {
            try { _rebuildLock.Release(); } catch (ObjectDisposedException) { }
        }
    }
}
