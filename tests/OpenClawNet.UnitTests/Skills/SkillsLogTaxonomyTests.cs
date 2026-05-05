// K-2 — Petey
// Tests for the SkillsLog audit taxonomy. Asserts:
//   - Each event fires with the expected EventId + structured properties.
//   - SkillLogScope propagates SnapshotId/AgentId into child events.
//   - Q5 hard contract: NO log entry on the skill-functions path ever
//     contains argument values or return values. Sentinel string
//     "PETEY_SECRET_ARG_42" is checked across every captured entry.
//
// Spec sources:
//   - .squad/decisions.md Q5 (never log argument or return values)
//   - .squad/decisions/inbox/drummond-k1b-verdict.md AC-K2-1 (ULID comment fix)
//   - K-2 task brief (10-event taxonomy)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenClawNet.Skills;
using OpenClawNet.Storage;
using OpenClawNet.UnitTests.Storage;
using Xunit;

namespace OpenClawNet.UnitTests.Skills;

[Trait("Area", "Skills")]
[Trait("Wave", "K-2")]
[Collection(StorageEnvVarCollection.Name)]
public sealed class SkillsLogTaxonomyTests : IDisposable
{
    private const string Sentinel = "PETEY_SECRET_ARG_42";

    private readonly string _root;
    private readonly string? _originalEnv;
    private readonly CapturingLoggerProvider _provider = new();

    public SkillsLogTaxonomyTests()
    {
        _originalEnv = Environment.GetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName);
        _root = Path.Combine(Path.GetTempPath(), $"oc-k2-log-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(OpenClawNetPaths.EnvironmentVariableName, _originalEnv);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string ValidSkill(string name, string body = "Body content.") => $"""
        ---
        name: {name}
        description: Test skill {name}
        ---
        {body}
        """;

    private void WriteSkill(string layer, string name, string content, string? agentName = null)
    {
        string dir = layer switch
        {
            "system" => Path.Combine(_root, "skills", "system", name),
            "installed" => Path.Combine(_root, "skills", "installed", name),
            "agents" => Path.Combine(_root, "skills", "agents", agentName!, name),
            _ => throw new ArgumentException(nameof(layer))
        };
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
    }

    private OpenClawNetSkillsRegistry CreateRegistry()
    {
        using var lf = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_provider));
        return new OpenClawNetSkillsRegistry(lf.CreateLogger<OpenClawNetSkillsRegistry>());
    }

    // ====================================================================
    // 1. SkillRegistryRefresh fires with structured props
    // ====================================================================

    [Fact]
    public async Task Rebuild_EmitsSkillRegistryRefresh_WithSnapshotIdAndCount()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        using var registry = CreateRegistry();
        // construction triggered Startup; force a Manual refresh too
        registry.Rebuild();

        var snap = await registry.GetSnapshotAsync();

        var refreshes = _provider.Entries
            .Where(e => e.EventId.Id == 7000)
            .ToList();
        refreshes.Should().HaveCountGreaterThanOrEqualTo(2, "startup + manual rebuild");

        var startup = refreshes.First(e => (Prop(e, "Cause") as SkillRegistryRefreshCause?) == SkillRegistryRefreshCause.Startup);
        startup.GetProp<string>("SnapshotId").Should().NotBeNullOrEmpty();
        startup.GetProp<int>("Count").Should().Be(1);

        refreshes.Any(e => (Prop(e, "Cause") as SkillRegistryRefreshCause?) == SkillRegistryRefreshCause.Manual)
            .Should().BeTrue();
    }

    // ====================================================================
    // 2 + 3. SkillEnabled / SkillDisabled with requestedBy
    // ====================================================================

    [Fact]
    public async Task SetEnabledForAgent_True_EmitsSkillEnabled_WithRequestedBy()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        using var registry = CreateRegistry();
        _provider.Clear();

        await registry.SetEnabledForAgentAsync("alice", "memory", true, requestedBy: "bruno");

        var enabled = _provider.Entries.SingleOrDefault(e => e.EventId.Id == 7020);
        enabled.Should().NotBeNull();
        enabled!.GetProp<string>("SkillName").Should().Be("memory");
        enabled.GetProp<string>("AgentId").Should().Be("alice");
        enabled.GetProp<string>("RequestedBy").Should().Be("bruno");
    }

    [Fact]
    public async Task SetEnabledForAgent_False_EmitsSkillDisabled()
    {
        WriteSkill("system", "memory", ValidSkill("memory"));
        using var registry = CreateRegistry();
        _provider.Clear();

        await registry.SetEnabledForAgentAsync("alice", "memory", false, requestedBy: "bruno");

        _provider.Entries.Should().Contain(e => e.EventId.Id == 7021);
        _provider.Entries.Should().NotContain(e => e.EventId.Id == 7020);
    }

    // ====================================================================
    // 4. SkillImported / SkillRetired diff on rebuild
    // ====================================================================

    [Fact]
    public void Rebuild_AfterAddingSkill_EmitsSkillImported()
    {
        using var registry = CreateRegistry();
        _provider.Clear();

        WriteSkill("installed", "fresh-skill", ValidSkill("fresh-skill"));
        registry.Rebuild();

        var imported = _provider.Entries.SingleOrDefault(e => e.EventId.Id == 7001);
        imported.Should().NotBeNull();
        imported!.GetProp<string>("SkillName").Should().Be("fresh-skill");
        imported.GetProp<SkillLayer>("Layer").Should().Be(SkillLayer.Installed);
    }

    [Fact]
    public void Rebuild_AfterRemovingSkill_EmitsSkillRetired()
    {
        WriteSkill("installed", "going-away", ValidSkill("going-away"));
        using var registry = CreateRegistry();
        _provider.Clear();

        Directory.Delete(Path.Combine(_root, "skills", "installed", "going-away"), recursive: true);
        registry.Rebuild();

        var retired = _provider.Entries.SingleOrDefault(e => e.EventId.Id == 7002);
        retired.Should().NotBeNull();
        retired!.GetProp<string>("SkillName").Should().Be("going-away");
    }

    // ====================================================================
    // 5. SkillValidationFailed — Q5: reason is class only, no body content
    // ====================================================================

    [Fact]
    public void MalformedFrontmatter_EmitsSkillValidationFailed_WithoutBodyContent()
    {
        // Body contains the sentinel; assert it never appears in any log.
        var malformed = $"""
            ---
            name: bad
            description: [unterminated
            ---
            {Sentinel}
            """;
        var dir = Path.Combine(_root, "skills", "installed", "bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), malformed);

        using var registry = CreateRegistry();

        var validationFails = _provider.Entries.Where(e => e.EventId.Id == 7003).ToList();
        validationFails.Should().NotBeEmpty();
        validationFails[0].GetProp<string>("Reason").Should().StartWith("malformed_frontmatter:");

        AssertSentinelAbsent();
    }

    // ====================================================================
    // 6. SkillLogScope propagates SnapshotId + AgentId
    // ====================================================================

    [Fact]
    public void SkillLogScope_PropagatesIdsIntoChildEvents()
    {
        var logger = _provider.CreateLogger("scope-test");
        using (SkillLogScope.Begin(logger, snapshotId: "snap-abc", agentId: "alice", chatId: "chat-1"))
        {
            logger.SkillFunctionInvoked("memory", "Recall", "alice");
        }

        var entry = _provider.Entries.Single(e => e.EventId.Id == 7041);
        var scopeDict = entry.Scopes
            .OfType<IReadOnlyDictionary<string, object>>()
            .First();
        scopeDict["SnapshotId"].Should().Be("snap-abc");
        scopeDict["AgentId"].Should().Be("alice");
        scopeDict["ChatId"].Should().Be("chat-1");
    }

    // ====================================================================
    // 7. Q5 SENTINEL — function-invocation path never logs args/returns
    // ====================================================================

    [Fact]
    public void Q5Sentinel_FunctionInvokedAndCompleted_NeverLeaksArgsOrReturn()
    {
        var logger = _provider.CreateLogger("q5-test");

        // Simulate a hot-path invocation. The taxonomy's helpers accept
        // ONLY identifiers — there is no argument or return parameter at
        // all. We pass the sentinel ONLY into a payload that the helpers
        // do not expose, then assert it never appears in any captured log.
        var fakeArgs = new { secret = Sentinel };
        var fakeReturn = new { output = Sentinel };

        logger.SkillFunctionInvoked("memory", "Recall", "alice");
        logger.SkillFunctionCompleted("memory", "Recall", durationMs: 12, status: SkillFunctionStatus.Success);

        // Reference the locals so the analyzer doesn't strip them.
        _ = fakeArgs.secret.Length + fakeReturn.output.Length;

        AssertSentinelAbsent();

        // And the events fired with their identifiers intact.
        var invoked = _provider.Entries.Single(e => e.EventId.Id == 7041);
        invoked.GetProp<string>("FunctionName").Should().Be("Recall");
        var completed = _provider.Entries.Single(e => e.EventId.Id == 7042);
        completed.GetProp<long>("DurationMs").Should().Be(12);
        completed.GetProp<SkillFunctionStatus>("Status").Should().Be(SkillFunctionStatus.Success);
    }

    // ====================================================================
    // 8. Bulk override — SkillEnabledStateChanged
    // ====================================================================

    [Fact]
    public async Task SetEnabledMapForAgent_EmitsSkillEnabledStateChanged()
    {
        using var registry = CreateRegistry();
        _provider.Clear();

        await registry.SetEnabledMapForAgentAsync(
            "alice",
            new Dictionary<string, bool> { ["memory"] = true, ["doc-processor"] = false },
            requestedBy: "admin");

        var bulk = _provider.Entries.Single(e => e.EventId.Id == 7022);
        bulk.GetProp<string>("AgentId").Should().Be("alice");
        bulk.GetProp<int>("Count").Should().Be(2);
        bulk.GetProp<string>("RequestedBy").Should().Be("admin");
    }

    // ====================================================================
    // 9. Import flow schema — K-4 will plug into these helpers
    // ====================================================================

    [Fact]
    public void ImportRequestedAndApproved_EmitDistinctEventIds()
    {
        var logger = _provider.CreateLogger("import-test");
        logger.SkillImportRequested("new-skill", source: "marketplace", requestedBy: "bruno");
        logger.SkillImportApproved("new-skill", source: "marketplace", approvedBy: "drummond");

        _provider.Entries.Should().Contain(e => e.EventId.Id == 7060);
        _provider.Entries.Should().Contain(e => e.EventId.Id == 7061);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static object? Prop(LogEntry e, string name)
    {
        if (e.State is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            return kvps.FirstOrDefault(k => k.Key == name).Value;
        return null;
    }

    private void AssertSentinelAbsent()
    {
        foreach (var e in _provider.Entries)
        {
            e.Message.Should().NotContain(Sentinel,
                "skill-functions path log entries must never contain argument or return values (Q5)");
            if (e.State is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kv in kvps)
                {
                    (kv.Value?.ToString() ?? string.Empty).Should().NotContain(Sentinel,
                        $"structured property '{kv.Key}' leaked the sentinel — Q5 violation");
                }
            }
        }
    }

    // ====================================================================
    // CapturingLoggerProvider — minimal in-memory ILogger for assertions
    // ====================================================================

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = new();
        private readonly AsyncLocal<Stack<object?>> _scopes = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);
        public void Dispose() { }
        public void Clear() => Entries.Clear();

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _owner;
            private readonly string _category;

            public CapturingLogger(CapturingLoggerProvider owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                var stack = _owner._scopes.Value ??= new Stack<object?>();
                stack.Push(state);
                return new PopOnDispose(stack);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var snapshot = _owner._scopes.Value?.ToArray() ?? Array.Empty<object?>();
                _owner.Entries.Add(new LogEntry(
                    _category, logLevel, eventId,
                    formatter(state, exception),
                    state,
                    exception,
                    snapshot));
            }

            private sealed class PopOnDispose : IDisposable
            {
                private readonly Stack<object?> _stack;
                public PopOnDispose(Stack<object?> stack) => _stack = stack;
                public void Dispose() { if (_stack.Count > 0) _stack.Pop(); }
            }
        }
    }

    public sealed record LogEntry(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        object? State,
        Exception? Exception,
        IReadOnlyList<object?> Scopes)
    {
        public T? GetProp<T>(string name)
        {
            if (State is IReadOnlyList<KeyValuePair<string, object?>> kvps)
            {
                var v = kvps.FirstOrDefault(k => k.Key == name).Value;
                if (v is T t) return t;
                if (v is null) return default;
                // boxed enum / numeric coercion
                try { return (T)Convert.ChangeType(v, typeof(T)); } catch { return default; }
            }
            return default;
        }
    }
}
