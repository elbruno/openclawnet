---
marp: true
title: "OpenClawNet — Session 3: Automated Memory Extraction for Agents"
description: "Context windows, summarization, semantic search, and long-term memory"
theme: openclaw
paginate: true
size: 16:9
footer: "OpenClawNet · Session 3 · Automated Memory Extraction"
---

<!-- _class: lead -->

# OpenClawNet
## Session 3 — Automated Memory Extraction

**Microsoft Reactor Series · ~75 min · Intermediate .NET**

> *From chatbot to coworker: agents that remember what happened last time.*

<br>

<div class="speakers">

**Bruno Capuano** — Principal Cloud Advocate, Microsoft
[github.com/elbruno](https://github.com/elbruno) · [@elbruno](https://twitter.com/elbruno)

**Pablo Nunes Lopes** — Cloud Advocate, Microsoft
[linkedin.com/in/pablonuneslopes](https://www.linkedin.com/in/pablonuneslopes/)

</div>

<!--
SPEAKER NOTES — title slide.
Welcome back to Session 3. In Session 1 we built a chat app on Aspire. In Session 2 we gave the agent hands—tools and the ability to take action. Today we tackle one of the hardest problems in agent design: memory. How do we let an agent learn from past conversations without storing gigabytes of tokens? How do we find relevant context from months of history in milliseconds? Today we build the memory system that OpenClawNet agents use to remember your preferences, your domain knowledge, and what happened last time—without breaking the bank on tokens or latency.
-->

---

## Where Sessions 1–2 left off

- **Session 1** — Aspire foundation, `IAgentProvider`, NDJSON streaming, SQLite
- **Session 2** — `ITool` + MCP, agent loop, approval gates, security model
- 5 in-process tools, 5 bundled MCP servers
- 3 attacks blocked: path traversal, command injection, SSRF
- `aspire start` → tool-using agent in 30 seconds

> Today: **long-term memory without breaking context windows.**

<!--
SPEAKER NOTES — recap.
Quick recap. Session 1 gave us a working Aspire stack with five model providers behind one interface. Session 2 gave us the agent loop: tool calls, approval policies, and a security story. Both are on GitHub. This session assumes you have Session 2 running. If not, the recording and code are at github.com/elbruno/openclawnet.
-->

---

## The memory problem

| Without Memory | **With Memory System** |
|---|---|
| Token limit hit → conversation truncated | Summarize older messages → keep talking |
| "Tell me again about X" → no idea | Semantic search → find it in seconds |
| Same context sent every request | Send only relevant summaries |
| Expensive for long conversations | 10x cheaper. 100x faster context retrieval |
| Agent learns nothing | Agent learns personality and domain knowledge |

> **Key insight:** Every LLM has a context window. The moment a conversation exceeds it, you have two choices: truncate or compress.

<!--
SPEAKER NOTES — problem.
Here's the gap. Without memory, after 50–100 messages you run out of tokens. You either lose context or pay exponentially more to keep it. With a memory system, you compress old messages into summaries and store them. When you need context, you search by meaning, not keywords. So a conversation that would cost 50x on Azure OpenAI costs 1x with smart compression. And the agent doesn't just remember facts—it learns domain knowledge and personality.
-->

---

## Phase 3 Goals

1. **Summarization** — Keep recent messages, compress older ones
2. **Persistent storage** — SessionSummary entities in SQLite
3. **Semantic search** — Find past conversations by meaning, not keywords
4. **Local embeddings** — ONNX models, no API calls, no data leaves your machine
5. **Transparent UI** — Users see memory stats (messages, summary count, last update)
6. **Zero-restart architecture** — Hot-reload skills; memory persists across sessions

**Scope:** ~75 minutes | **Level:** Intermediate .NET | **Builds on:** Session 2

<!--
SPEAKER NOTES — goals.
Six goals for today. First, we set up the summarization trigger—after N messages, compress. Second, we persist those summaries to the database so they survive restarts. Third, we convert conversations to vectors so we can find meaning, not just keywords. Fourth, we do it locally with ONNX models—no API calls to Azure OpenAI. Fifth, we make it visible to the user. Sixth, we set up the architecture so you can hot-reload skills and the memory system keeps working.
-->

---

## Memory Extraction Architecture

```
┌─────────────────────┐
│  Chat Conversation  │  (messages flow in)
└──────────┬──────────┘
           │
      [Trigger: N messages]
           │
           ▼
┌─────────────────────────────────────┐
│  DefaultMemoryService               │
│  ├─ Compress messages → summary     │  (LLM-based summarization)
│  ├─ Persist to SessionSummary       │  (database)
│  └─ Extract key facts               │  (NLP)
└──────────┬──────────────────────────┘
           │
           ▼
┌─────────────────────────────────────┐
│  DefaultEmbeddingsService           │
│  ├─ Convert to vectors (ONNX)       │  (local inference)
│  ├─ Cosine similarity search        │  (fast matching)
│  └─ Store embeddings                │
└──────────┬──────────────────────────┘
           │
           ▼
┌─────────────────────────────────────┐
│  Prompt Composer                    │
│  ├─ Fetch relevant summaries        │  (semantic search)
│  ├─ Inject into system prompt       │  (context augmentation)
│  └─ Send to model                   │
└─────────────────────────────────────┘
```

<!--
SPEAKER NOTES — architecture.
Here's the flow. Conversation comes in. After N messages, the memory service kicks in: it takes the older messages, sends them to the LLM with a "summarize this" prompt, gets back a compressed summary, and stores it. Meanwhile, the embeddings service converts both the original messages and the summary into numeric vectors using an ONNX model running on your CPU—no API call, no latency spike. When you need context for the next request, the prompt composer queries the embeddings service for similar conversations, retrieves the summaries, and injects them at the top of the system prompt. The agent gets the context it needs without the token tax.
-->

---

## Implementation: DefaultMemoryService

```csharp
public class DefaultMemoryService : IMemoryService
{
    private readonly IDbContextFactory<OpenClawDbContext> _dbContextFactory;
    private readonly IEmbeddingsService _embeddings;
    private readonly IAgentProvider _agentProvider;

    // Keep recent N messages verbatim; summarize older ones
    private const int VERBATIM_THRESHOLD = 10;

    public async Task<string?> GetSessionSummaryAsync(Guid sessionId)
    {
        using var db = _dbContextFactory.CreateDbContext();
        return await db.SessionSummaries
            .Where(s => s.SessionId == sessionId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Summary)
            .FirstOrDefaultAsync();
    }

    public async Task StoreSummaryAsync(
        Guid sessionId, 
        string summary, 
        int messageCount)
    {
        using var db = _dbContextFactory.CreateDbContext();
        db.SessionSummaries.Add(new SessionSummary
        {
            SessionId = sessionId,
            Summary = summary,
            CoveredMessageCount = messageCount
        });
        await db.SaveChangesAsync();
    }

    public async Task<MemoryStats> GetStatsAsync(Guid sessionId)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var messages = await db.Messages
            .Where(m => m.SessionId == sessionId)
            .CountAsync();
            
        var summaries = await db.SessionSummaries
            .Where(s => s.SessionId == sessionId)
            .ToListAsync();

        return new MemoryStats(
            TotalMessages: messages,
            SummaryCount: summaries.Count,
            CoveredMessages: summaries.Sum(s => s.CoveredMessageCount),
            LastSummaryAt: summaries.MaxBy(s => s.CreatedAt)?.CreatedAt
        );
    }

    // Trigger summarization after VERBATIM_THRESHOLD messages
    public async Task SummarizeIfNeededAsync(Guid sessionId)
    {
        var stats = await GetStatsAsync(sessionId);
        if ((stats.TotalMessages - stats.CoveredMessages) > VERBATIM_THRESHOLD)
        {
            var recentMessages = await GetRecentMessagesAsync(
                sessionId, 
                VERBATIM_THRESHOLD);
            
            var summary = await SummarizeAsync(recentMessages);
            await StoreSummaryAsync(sessionId, summary, VERBATIM_THRESHOLD);
        }
    }
}
```

<!--
SPEAKER NOTES — memory service code.
Here's the pattern. We use `IDbContextFactory`—never a singleton DbContext in async code. VERBATIM_THRESHOLD is our trigger: keep the last 10 messages as-is, compress everything older. `GetSessionSummaryAsync` is a simple query for the most recent summary. `StoreSummaryAsync` persists a new SessionSummary entity. `GetStatsAsync` gives the UI complete transparency: how many total messages, how many summaries exist, how many messages are covered by summaries, and when we last summarized. The critical method is `SummarizeIfNeededAsync`: it checks if we've exceeded the threshold, grabs the older messages, calls the summarization prompt, and stores the result. That's the trigger that keeps the agent from drowning in tokens.
-->

---

## Implementation: DefaultEmbeddingsService

```csharp
public class DefaultEmbeddingsService : IEmbeddingsService
{
    // Backed by Elbruno.LocalEmbeddings (ONNX model, e.g., MiniLM-L6-v2)
    private readonly IEmbeddingProvider _embeddingProvider;

    public async Task<float[]> EmbedAsync(string text)
    {
        // Returns a 384-dimensional vector (MiniLM) or similar
        return await _embeddingProvider.EmbedAsync(text);
    }

    public float CosineSimilarity(float[] v1, float[] v2)
    {
        // similarity = (v1 · v2) / (|v1| * |v2|)
        float dotProduct = 0;
        for (int i = 0; i < v1.Length; i++) dotProduct += v1[i] * v2[i];

        float mag1 = (float)Math.Sqrt(v1.Sum(x => x * x));
        float mag2 = (float)Math.Sqrt(v2.Sum(x => x * x));

        return mag1 == 0 || mag2 == 0 ? 0 : dotProduct / (mag1 * mag2);
    }

    public async Task<List<(string Text, float Similarity)>> SearchAsync(
        string query, 
        IEnumerable<string> corpus,
        int topK = 3)
    {
        var queryEmbedding = await EmbedAsync(query);
        var results = new List<(string, float)>();

        foreach (var text in corpus)
        {
            var textEmbedding = await EmbedAsync(text);
            var similarity = CosineSimilarity(queryEmbedding, textEmbedding);
            results.Add((text, similarity));
        }

        return results
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();
    }
}
```

**Why local embeddings?**
- No API calls = no latency spikes
- No data leaves your machine = privacy
- ONNX models run on CPU in milliseconds
- Free to scale: embed millions of messages locally

<!--
SPEAKER NOTES — embeddings code.
Embeddings are numeric vectors that capture meaning. We use an ONNX model like MiniLM—tiny (22MB), fast (milliseconds per text), and open-source. The key method is `CosineSimilarity`: it measures how "close" two vectors are (0 = unrelated, 1 = identical). `SearchAsync` takes a query, embeds it, compares it to a corpus, and returns the top-K most similar texts. This is how we find relevant conversations. And it all runs locally. No API call to Azure, no latency, no cost per query. You can embed millions of messages for the price of downloading a small ONNX model once.
-->

---

## Integration: SessionSummary Entity

```csharp
public sealed class SessionSummary
{
    // Unique ID for this summary
    public Guid Id { get; set; } = Guid.NewGuid();

    // Foreign key to ChatSession
    public Guid SessionId { get; set; }

    // The compressed summary text
    public string Summary { get; set; } = string.Empty;

    // How many messages were covered by this summary
    public int CoveredMessageCount { get; set; }

    // When the summary was created
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // The embedding vector for semantic search
    public float[]? EmbeddingVector { get; set; }

    // Navigation property
    public ChatSession Session { get; set; } = null!;
}
```

**Database schema:**
```sql
CREATE TABLE session_summaries (
    id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES chat_sessions(id) ON DELETE CASCADE,
    summary TEXT NOT NULL,
    covered_message_count INT NOT NULL,
    created_at TIMESTAMP NOT NULL,
    embedding_vector BYTEA,
    UNIQUE(session_id, created_at)
);

CREATE INDEX idx_session_summaries_session_id 
    ON session_summaries(session_id, created_at DESC);
```

<!--
SPEAKER NOTES — entity and schema.
One session can have many summaries. As the conversation grows, we create new summaries every N messages. `CoveredMessageCount` tells us how many messages this summary compressed—useful for stats. `EmbeddingVector` is stored as binary—the ONNX embedding serialized to bytes. We cascade-delete on session deletion so orphaned summaries don't accumulate. The index on `(session_id, created_at DESC)` makes retrieval fast—we can quickly get the most recent summary or a range of summaries by date.
-->

---

## API Endpoints: Memory Retrieval

```csharp
public static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/memory").WithTags("Memory");

        // Get the most recent summary
        group.MapGet("/{sessionId:guid}/summary", 
            async (Guid sessionId, IMemoryService memory) =>
                Results.Ok(new { 
                    sessionId, 
                    summary = await memory.GetSessionSummaryAsync(sessionId) 
                }));

        // Get all summaries for a session
        group.MapGet("/{sessionId:guid}/summaries", 
            async (Guid sessionId, IMemoryService memory) =>
                Results.Ok(await memory.GetAllSummariesAsync(sessionId)));

        // Get memory stats (transparency for UI)
        group.MapGet("/{sessionId:guid}/stats", 
            async (Guid sessionId, IMemoryService memory) =>
                Results.Ok(await memory.GetStatsAsync(sessionId)));

        // Semantic search across summaries
        group.MapPost("/{sessionId:guid}/search", 
            async (Guid sessionId, [FromBody] SearchRequest req, 
                   IMemoryService memory) =>
                Results.Ok(await memory.SearchSummariesAsync(
                    sessionId, 
                    req.Query, 
                    req.TopK ?? 3)));
    }
}

public record SearchRequest(string Query, int? TopK);
```

**Example calls:**
```bash
# Get stats (shows message count, summary count, coverage)
curl http://localhost:5000/api/memory/{sessionId}/stats

# Search by meaning (not keyword)
curl -X POST http://localhost:5000/api/memory/{sessionId}/search \
  -H "Content-Type: application/json" \
  -d '{"query": "dependency injection patterns", "topK": 3}'
```

<!--
SPEAKER NOTES — endpoints.
These endpoints are the API surface. `GET /summary` gets the most recent compressed summary. `GET /summaries` gets all of them for a session. `GET /stats` is the transparency layer—it tells the UI exactly how many messages exist, how many summaries, how many messages are compressed, and when the last summary happened. `POST /search` is the payoff: semantic search. You send a question, and it returns the top-3 most relevant summaries from the entire conversation history. This is how the agent finds context without sending 100KB of messages every request.
-->

---

## Demo Preview: Memory Extraction in Action

### 1. Baseline: Before Memory System
```bash
# Send 50 messages in rapid succession
# Model receives all 50 messages in context
# Cost: ~50 tokens just for context
# As conversation grows, token cost explodes
```

### 2. With Memory System Active
```bash
# Send same 50 messages
# After message 10 (threshold), system triggers summarization
# Messages 1–9 → compressed to 1–2 sentences
# Messages 10–50 → sent verbatim (recent context)
# Model receives: summary + recent messages
# Cost: ~5 tokens for context (90% reduction!)
```

### 3. Semantic Search Demo
```bash
# User: "We talked about caching last week. What did we decide?"
# System searches ALL past summaries by meaning
# Returns: Summary from 3 days ago about Redis caching
# Agent: "You decided Redis was better than in-memory for this use case..."
# Result: Agent remembers a fact from a different conversation entirely!
```

### 4. Memory Stats UI
- **Total Messages:** 147
- **Summaries:** 4
- **Messages Covered:** 140
- **Last Summary:** 2 min ago
- **Compression Ratio:** 28:1

<!--
SPEAKER NOTES — demo.
Four demos. First, we show the cost problem: 50 messages = a lot of tokens. Second, we show the fix: same 50 messages but the first 40 get compressed into one summary, so the model sees a fraction of the tokens. Third, we show semantic search: the agent finds relevant information from weeks ago by meaning, not keywords. Fourth, we show the UI—completely transparent memory stats so the user knows what's happening. That's the payoff: context management that's fast, cheap, and invisible to the user.
-->

---

## 🤖 Copilot Moment: Add Date-Range Filtering

**Goal:** Extend `MemoryEndpoints` with date-range filtering.

```csharp
// Add to MemoryEndpoints:
group.MapGet("/{sessionId:guid}/summaries/range", 
    async (Guid sessionId, DateTime from, DateTime to, IMemoryService memory) =>
        Results.Ok(await memory.GetSummariesByDateAsync(sessionId, from, to)));

// Implement in DefaultMemoryService:
public async Task<IEnumerable<SessionSummary>> GetSummariesByDateAsync(
    Guid sessionId, DateTime from, DateTime to)
{
    using var db = _dbContextFactory.CreateDbContext();
    return await db.SessionSummaries
        .Where(s => s.SessionId == sessionId 
            && s.CreatedAt >= from 
            && s.CreatedAt <= to)
        .OrderByDescending(s => s.CreatedAt)
        .ToListAsync();
}
```

**Test it:**
```bash
curl "http://localhost:5000/api/memory/{sessionId}/summaries/range?from=2025-01-01&to=2025-01-31"
```

<!--
SPEAKER NOTES — copilot moment.
Your turn. Add a date-range filter to the memory endpoints. This is a real feature users want: "Show me everything we summarized about this topic last month." The implementation is straightforward: add an endpoint that accepts `from` and `to` DateTime parameters, and filter the summaries query. Test it with curl. By the end of this moment, you'll have extended the API yourself—it's a small change but it's yours.
-->

---

## Key Insights

> **"Memory is not magic. It's compression + retrieval."**

1. **Compression via LLM** — Let the model summarize. It's what it's good at.
2. **Local search** — Don't send everything to Azure. Use local embeddings.
3. **Transparent stats** — Users should see memory stats, not a black box.
4. **Fail gracefully** — If summarization takes too long, skip it. The agent still works.
5. **Privacy by default** — All embeddings computed locally. No data to third parties.

<!--
SPEAKER NOTES — key insights.
Five principles. First, memory is compression and retrieval—not magic. Second, use the LLM for what it's good at: summarization. Third, keep search local and fast. Fourth, show stats so users trust the system. Fifth, everything stays on your machine by default. This is why local embeddings matter—you can scale memory without scaling your Azure bill.
-->

---

## What We Built Today

✓ **Conversation summarization** — Keep recent messages, compress old ones  
✓ **Persistent storage** — SessionSummary entity + database schema  
✓ **Local embeddings** — ONNX models for semantic search  
✓ **Memory service** — Core logic for extraction and retrieval  
✓ **API endpoints** — List summaries, search, stats, date ranges  
✓ **Copilot moment** — Added date-range filtering yourself  
✓ **UI transparency** — Memory stats visible to users  

**Builds on:** Session 2 (foundation + tools)  
**Enables:** Session 4 (cloud deployment + production)

<!--
SPEAKER NOTES — checklist.
Seven things shipped today. A memory system that keeps conversations cheap and fast. Persistent summaries that survive restarts. Local semantic search. An API that makes memory queryable. You extended it yourself. And full transparency in the UI. This is the foundation for agent personalization: the agent learns your preferences and domain knowledge over weeks and months, but doesn't blow up your token budget.
-->

---

## Q&A / Next Steps

### Today's Repository
- **Code:** `github.com/elbruno/openclawnet` — Session 3 tag
- **Slides:** `docs/sessions/session-3/`
- **Demos:** `docs/sessions/session-3/demos-resources/`

### Next: Session 4 — Cloud Deployment & Production
- Azure Foundry Agent Host
- CI/CD pipelines with GitHub Actions
- Production configuration and monitoring
- From localhost to production in one session

### Resources
- [OpenClawNet GitHub](https://github.com/elbruno/openclawnet)
- [Microsoft Learn: RAG + Embeddings](https://learn.microsoft.com/en-us/azure/search/)
- [ONNX Runtime](https://onnxruntime.ai/)

**Let's build agents that remember. Together.**

<!--
SPEAKER NOTES — closing.
That's Session 3. We took the agent from forgetful to long-term memory. Session 4 we take it to production. The code is on GitHub, recorded, and ready to learn from. Questions? Email bruno@microsoft.com or open an issue. Thanks for joining us.
-->

---

<!-- _class: lead -->

# Thank You

**Next session:** Cloud Deployment & Production

**Follow-up:** office hours on Discord

**Code:** [github.com/elbruno/openclawnet](https://github.com/elbruno/openclawnet)
