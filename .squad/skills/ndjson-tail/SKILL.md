# NDJSON DB-Tail Streaming

@extracted: 2026-04-27, petey, from Scheduler live-console panel implementation  
@validated-by: petey (high), irving (high)

**Pattern:** Stream incremental updates from a database table to a Blazor browser
client over `application/x-ndjson` by polling the table on a short cadence and
emitting one JSON line per change. No SignalR, no DB triggers, no message bus.

**Use when:**

* You need a "live console" / "live status" view of a long-running operation.
* The producer of the data already writes to the database (e.g., an append-only
  event log, a status row, log lines).
* You don't want to introduce SignalR, RabbitMQ, or rebuild the producer to
  push events out-of-band.
* End-to-end latency of ~1s is acceptable.

**Don't use when:**

* You need <100 ms latency (use a real push channel).
* The producer doesn't actually save anything until done — there's nothing to
  tail. (Either fix the producer or fall back to status-only polling.)
* The "table" is enormous and unindexed by your tail key.

---

## Server skeleton

```csharp
public static class FooStreamEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const int PollDelayMs = 1000;
    private const int MaxStreamSeconds = 60 * 30;

    public static void MapFooStreamEndpoints(this WebApplication app)
    {
        app.MapGet("/api/foo/{id:guid}/stream", async (
            Guid id,
            HttpContext ctx,
            IDbContextFactory<MyDbContext> dbFactory,
            CancellationToken ct) =>
        {
            ctx.Response.ContentType = "application/x-ndjson";
            ctx.Response.Headers.CacheControl = "no-cache";

            var startedAt = DateTime.UtcNow;
            var lastSequence = -1;
            FooSnapshot? lastSnapshot = null;
            var sentInitial = false;

            while (!ct.IsCancellationRequested
                && (DateTime.UtcNow - startedAt).TotalSeconds < MaxStreamSeconds)
            {
                FooRow? row;
                List<FooEvent> newEvents;
                await using (var db = await dbFactory.CreateDbContextAsync(ct))
                {
                    row = await db.Foo.AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == id, ct);
                    if (row is null) { await Write(ctx, NotFound(id), ct); return; }

                    newEvents = await db.FooEvents.AsNoTracking()
                        .Where(e => e.FooId == id && e.Sequence > lastSequence)
                        .OrderBy(e => e.Sequence)
                        .ToListAsync(ct);
                }

                if (!sentInitial)
                {
                    await Write(ctx, Snapshot(row), ct);
                    sentInitial = true;
                    lastSnapshot = SnapshotOf(row);
                }

                foreach (var ev in newEvents)
                {
                    await Write(ctx, EventLine(ev), ct);
                    lastSequence = ev.Sequence;
                }

                if (HasChanged(row, lastSnapshot))
                {
                    await Write(ctx, StatusLine(row), ct);
                    lastSnapshot = SnapshotOf(row);
                }

                if (IsTerminal(row))
                {
                    await Write(ctx, Complete(row), ct);
                    return;
                }

                try { await Task.Delay(PollDelayMs, ct); }
                catch (OperationCanceledException) { return; }
            }
        });
    }

    private static async Task Write(HttpContext ctx, object payload, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(payload, JsonOpts);
        await ctx.Response.WriteAsync(line + "\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
}
```

## Client skeleton (Blazor Server)

```csharp
var client = HttpClientFactory.CreateClient("my-service");
using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/foo/{id}/stream");
req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
if (!resp.IsSuccessStatusCode) { /* show error */ return; }

await using var stream = await resp.Content.ReadAsStreamAsync(ct);
using var reader = new StreamReader(stream);

while (!ct.IsCancellationRequested)
{
    var line = await reader.ReadLineAsync(ct);
    if (line is null) break;
    if (string.IsNullOrWhiteSpace(line)) continue;

    var evt = JsonSerializer.Deserialize<MyDto>(line,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (evt is null) continue;
    await HandleAsync(evt);
}
```

## Wire-format conventions

* One JSON object per line, terminated by `\n`.
* Every object has a discriminator field (`type`, `kind`, …).
* All optional fields — single DTO type with nullable members keeps client
  parsing trivial; switch on the discriminator.
* Always emit a final terminal frame (e.g. `type: "complete"`) before closing
  — the client can stop spinners deterministically without inferring from EOF.
* `not_found` / error frames are normal lines, not HTTP errors. HTTP status is
  decided once at the very top, before the first byte is flushed.

## Checklist

1. [ ] Endpoint sets `Content-Type: application/x-ndjson` and `Cache-Control: no-cache` BEFORE the first write.
2. [ ] Each write does `WriteAsync(line + "\n")` then `Body.FlushAsync()`.
3. [ ] Use `AsNoTracking()` and a fresh `DbContext` per poll iteration (the loop is long-lived; trackers will leak).
4. [ ] Track `lastSequence` (or `lastTimestamp`) so you only project deltas.
5. [ ] Hard time cap (`MaxStreamSeconds`) — clients can reconnect; servers shouldn't leak forever.
6. [ ] Terminal frame before exiting the handler.
7. [ ] Client uses `HttpCompletionOption.ResponseHeadersRead` — without it, `StreamReader` blocks until the server closes.
8. [ ] Client deserializes with `PropertyNameCaseInsensitive = true` (server uses camelCase, DTO is PascalCase).

## Realised in this codebase

* `src/OpenClawNet.Services.Scheduler/Endpoints/JobRunStreamEndpoints.cs` —
  tails `JobRun` + `JobRunEvent` for the Scheduler live-console panel.
* `src/OpenClawNet.Services.Scheduler/Components/LiveConsole.razor` — client.
* Reference (push-style NDJSON, not poll-tail): `src/OpenClawNet.Gateway/Endpoints/ChatStreamEndpoints.cs`.

## Related skills

* `ndjson-request-correlation` — pairing NDJSON streams with side-channel
  POSTs (e.g. tool-approval mid-stream).
