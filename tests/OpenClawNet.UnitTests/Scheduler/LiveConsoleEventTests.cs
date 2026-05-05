using System.Text.Json;
using FluentAssertions;
using OpenClawNet.Services.Scheduler.Endpoints;
using OpenClawNet.Storage.Entities;

namespace OpenClawNet.UnitTests.Scheduler;

/// <summary>
/// DTO tests for <see cref="LiveConsoleEvent"/> — the wire format used by the
/// NDJSON live-console stream (<c>/api/scheduler/jobs/{jobId}/runs/{runId}/stream</c>).
/// Failures here mean the JobDetail UI's <c>LiveConsole.razor</c> can no longer
/// parse server output.
/// </summary>
public sealed class LiveConsoleEventTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Snapshot_FromRunningJobRun_ProjectsCoreFields()
    {
        var started = new DateTime(2026, 4, 25, 9, 0, 0, DateTimeKind.Utc);
        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            Status = "running",
            StartedAt = started
        };

        var evt = LiveConsoleEvent.Snapshot(run);

        evt.Type.Should().Be("snapshot");
        evt.RunId.Should().Be(run.Id);
        evt.Status.Should().Be("running");
        evt.StartedAt.Should().Be(started);
        evt.CompletedAt.Should().BeNull();
        evt.ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Complete_FromTerminalJobRun_PopulatesElapsed()
    {
        var started = DateTime.UtcNow.AddSeconds(-12);
        var completed = DateTime.UtcNow;
        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobId = Guid.NewGuid(),
            Status = "completed",
            StartedAt = started,
            CompletedAt = completed,
            Result = "ok"
        };

        var evt = LiveConsoleEvent.Complete(run);

        evt.Type.Should().Be("complete");
        evt.Status.Should().Be("completed");
        evt.CompletedAt.Should().Be(completed);
        evt.Result.Should().Be("ok");
        evt.ElapsedMs.Should().BeGreaterThanOrEqualTo(11_000);
    }

    [Fact]
    public void FromEvent_PreservesKindAndTimestamp()
    {
        var ev = new JobRunEvent
        {
            JobRunId = Guid.NewGuid(),
            Sequence = 4,
            Timestamp = new DateTime(2026, 4, 25, 9, 0, 5, DateTimeKind.Utc),
            Kind = JobRunEventKind.ToolCall,
            ToolName = "shell",
            Message = "running echo",
            DurationMs = 23
        };

        var evt = LiveConsoleEvent.FromEvent(ev);

        evt.Type.Should().Be("event");
        evt.Sequence.Should().Be(4);
        evt.Kind.Should().Be("tool_call");
        evt.ToolName.Should().Be("shell");
        evt.Message.Should().Be("running echo");
        evt.DurationMs.Should().Be(23);
    }

    [Fact]
    public void Roundtrip_SerializesWithCamelCaseAndDeserializesIdentically()
    {
        var ev = new JobRunEvent
        {
            JobRunId = Guid.NewGuid(),
            Sequence = 1,
            Timestamp = DateTime.UtcNow,
            Kind = JobRunEventKind.AgentCompleted,
            Message = "done",
            TokensUsed = 42
        };
        var original = LiveConsoleEvent.FromEvent(ev);

        var json = JsonSerializer.Serialize(original, Opts);

        // Camel-case wire format — the Razor component deserializes case-insensitively.
        json.Should().Contain("\"type\":\"event\"");
        json.Should().Contain("\"kind\":\"agent_completed\"");
        json.Should().Contain("\"tokensUsed\":42");

        var roundtripped = JsonSerializer.Deserialize<LiveConsoleEvent>(json, Opts);
        roundtripped.Should().NotBeNull();
        roundtripped!.Type.Should().Be("event");
        roundtripped.Kind.Should().Be("agent_completed");
        roundtripped.TokensUsed.Should().Be(42);
        roundtripped.Sequence.Should().Be(1);
    }

    [Fact]
    public void NotFound_BuildsExplicitErrorLine()
    {
        var id = Guid.NewGuid();
        var evt = LiveConsoleEvent.NotFound(id);

        evt.Type.Should().Be("not_found");
        evt.RunId.Should().Be(id);
        evt.Message.Should().Contain(id.ToString());
    }
}
