# 🎤 Session 3: Speaker Script — Skills + Memory

**Duration:** 50 minutes | **Presenters:** Bruno (lead) + Co-presenter (demos + audience)

---

## Pre-Session Checklist (10 min before)

- [ ] `aspire run` — verify Session 2 app is running end-to-end
- [ ] Prepare a test skill file for live demo (e.g., `security-auditor.md`)
- [ ] Navigate to Skills page — toggle one skill to confirm loading works
- [ ] Pre-run 20+ messages in a conversation to have summarization data ready
- [ ] Have sample skills ready to display: `skills/built-in/dotnet-expert.md`
- [ ] Open `MemoryEndpoints.cs` in VS Code for Copilot Moment #2
- [ ] Verify skills API: `curl http://localhost:5000/api/skills`

---

## Timeline

### 0:00–2:00 — Opening (2 min)

**Bruno:**
> "Session 2 gave our agent hands — it can read files, run commands, fetch web pages. But every user gets the same generic experience. Today we fix that. We give the agent **personality** through skills and **long-term memory** through summarization."

- Show Sessions 1–2 recap: Foundation ✅, Tools ✅, Security ✅
- Transition: *"Same agent, different behavior. No code changes — just Markdown files."*

**Co-presenter:**
- Confirm audience can see slides
- Quick check: "Everyone's Session 2 working?" (thumbs up/down)

---

### 2:00–14:00 — Stage 1: Skill System (12 min)

**Bruno (concepts + code, 12 min):**

**[0:02–0:04] What is a skill? (2 min)**
- *"A Markdown file with YAML frontmatter. No code changes needed."*
- Show `dotnet-expert.md` example — YAML frontmatter fields: name, description, tags, enabled
- Content after `---` is pure Markdown — the agent's behavior instructions
- *"Your product manager could write a skill. Your QA engineer could write one."*

**[0:04–0:06] SkillDefinition and SkillContent models (2 min)**
- `SkillDefinition` — immutable record for UI: name, description, tags, enabled, filePath
- `SkillContent` — what the prompt composer uses: name, content, description, tags
- *"Sealed records for immutability. Different types for different consumers."*

**[0:06–0:09] FileSkillLoader implementation (3 min)**
- Scans `skills/built-in/` and `skills/samples/` directories for `*.md` files
- Thread-safe with lock for `_disabledSkills` HashSet
- Graceful error handling — malformed files skipped with a warning
- `EnableSkill` / `DisableSkill` — just HashSet add/remove
- `ReloadAsync` — re-scan directory without restart

**[0:09–0:11] SkillParser — YAML extraction (2 min)**
- Regex pattern: `^---\s*\n(.*?)\n---\s*\n(.*)$`
- Group 1: YAML frontmatter → deserialized into SkillDefinition fields
- Group 2: Markdown content → the actual behavior instructions
- Static utility — no state, no DI needed

**[0:11–0:14] How skills weave into the system prompt (3 min)**
- Show `DefaultPromptComposer.ComposeAsync()` code
- Three lines: get active skills → format as Markdown sections → append to system prompt
- *"The LLM sees skill content as instructions. More skills = longer prompt = more tokens."*
- Tool definitions are separate (via model API), skills are in the system prompt

### 🤖 Copilot Moment #1 (~minute 10)

**Co-presenter leads, Bruno narrates:**

> "Let's create a brand-new skill file from scratch using Copilot."

1. Open Copilot Chat
2. Type the prompt (see [copilot-prompts.md](./copilot-prompts.md) → Prompt 1)
3. Accept the generated `security-auditor.md` file
4. Save to `skills/samples/security-auditor.md`
5. Reload skills via API: `curl -X POST http://localhost:5000/api/skills/reload`
6. Verify it appears: `curl http://localhost:5000/api/skills`

**[FALLBACK]** If Copilot doesn't generate valid YAML frontmatter:
- Show a pre-prepared `security-auditor.md` file
- Paste it manually, reload, verify
- *"The format is simple — anyone can create one by following the pattern."*

---

### 14:00–29:00 — Stage 2: Memory & Summarization (15 min)

**Bruno (concepts + code, 15 min):**

**[0:14–0:17] The context window problem (3 min)**
- *"LLMs have token limits — 8K to 128K. Long conversations fill up fast."*
- More tokens = higher cost (even local models get slower)
- Naive truncation = lost context — the agent forgets important details
- *"We need a smarter approach."*

**[0:17–0:20] Summarization strategy (3 min)**
- Show the strategy table:
  - Recent messages (last N): keep **verbatim**
  - Older messages: **summarize** into key points
  - Very old: available via **semantic search**
- Summary injected at top of system prompt
- Triggered automatically based on message count

**[0:20–0:23] DefaultMemoryService code (3 min)**
- Uses `IDbContextFactory<OpenClawDbContext>` — correct pattern for async services
- `GetSessionSummaryAsync` — returns most recent summary
- `StoreSummaryAsync` — persists new `SessionSummary` entity
- `GetStatsAsync` — returns TotalMessages, SummaryCount, CoveredMessages, LastSummaryAt
- *"MemoryStats gives the UI transparency into what the memory system is doing."*

**[0:23–0:26] DefaultEmbeddingsService code (3 min)**
- Backed by `Elbruno.LocalEmbeddings` — ONNX models, runs locally
- `EmbedAsync` — text → embedding vector
- `CosineSimilarity` — dot product / (magnitude1 × magnitude2)
- *"Find past conversations about 'dependency injection' even if the user said 'IoC container'."*
- Key point: no API calls, no data leaves the machine

**[0:26–0:29] SessionSummary entity (3 min)**
- Show the entity: Id, SessionId, Summary, CoveredMessageCount, CreatedAt
- One session → many summaries (as conversation grows)
- Cascade-deletes with the parent session

### [DEMO] Summarization Trigger (~minute 25)

**Co-presenter demonstrates:**

1. Show a conversation with 20+ messages in the Blazor UI
2. Check memory stats: `curl http://localhost:5000/api/memory/{sessionId}/stats`
3. Point out: TotalMessages, SummaryCount, CoveredMessages
4. Send a few more messages to push past summarization threshold
5. Re-check stats — show SummaryCount increased

**[FALLBACK]** If summarization hasn't triggered:
- Show pre-captured stats output showing before/after
- Explain the threshold and trigger mechanism
- *"The summarization runs automatically — no user action needed."*

---

### 29:00–44:00 — Stage 3: Integration + UI (15 min)

**Bruno (endpoints + integration, 15 min):**

**[0:29–0:32] SkillEndpoints walkthrough (3 min)**
- Show `SkillEndpoints.cs` — four Minimal API endpoints
- `GET /api/skills` — list all skills with metadata
- `POST /api/skills/reload` — re-scan directory
- `POST /api/skills/{name}/enable` / `disable` — toggle at runtime
- *"No restart required. The UI calls these when you click the toggle."*

**[0:32–0:35] MemoryEndpoints walkthrough (3 min)**
- Show `MemoryEndpoints.cs` — three read-only endpoints
- `GET /api/memory/{sessionId}/summary` — latest summary
- `GET /api/memory/{sessionId}/summaries` — all summaries
- `GET /api/memory/{sessionId}/stats` — dashboard data
- *"Stats gives the UI everything it needs to render a memory dashboard."*

### [DEMO] Skill Toggle — Before/After (~minute 35)

**Co-presenter demonstrates:**

1. **Enable** `dotnet-expert` skill: `curl -X POST http://localhost:5000/api/skills/dotnet-expert/enable`
2. Chat: "What's the best way to handle DI in .NET?"
3. → Expert response with .NET-specific patterns, Microsoft docs references
4. **Disable** `dotnet-expert`: `curl -X POST http://localhost:5000/api/skills/dotnet-expert/disable`
5. Chat: same question
6. → Generic response about dependency injection
7. *"Same agent. Same model. Different behavior. Just a Markdown file."*

**[FALLBACK]** If skill toggle doesn't visibly change responses:
- Show pre-recorded side-by-side comparison
- Explain: "With smaller models the difference may be subtle, but with GPT-4o it's dramatic"

### [DEMO] Memory Stats Panel (~minute 38)

**Co-presenter:**
1. Show the Blazor UI memory stats component
2. Point out: total messages, summary count, last summary time
3. *"Full transparency — users see exactly what the memory system is doing."*

### 🤖 Copilot Moment #2 (~minute 40)

**Co-presenter leads, Bruno narrates:**

> "Let's add date filtering to the memory API — a real feature request."

1. Open `MemoryEndpoints.cs`
2. Open Copilot Chat, type the prompt (see [copilot-prompts.md](./copilot-prompts.md) → Prompt 2)
3. Accept the suggestion — new endpoint with `from` and `to` query parameters
4. Test: `curl "http://localhost:5000/api/memory/{sessionId}/summaries?from=2025-01-01T00:00:00Z&to=2025-12-31T23:59:59Z"`

**[FALLBACK]** If Copilot doesn't generate the expected endpoint:
- Manually add the date-filtered endpoint
- *"The pattern is the same Minimal API style — Copilot matches existing conventions."*

---

### 44:00–50:00 — Closing (6 min)

**Bruno:**

**[0:44–0:46] Key insight (2 min)**
> "Skills are just markdown. Memory is transparent."
- Anyone can create a skill — no C# required
- Users see what's summarized, not a black box
- Developers get clean abstractions: `ISkillLoader`, `IMemoryService`

**[0:46–0:48] What we built checklist (2 min)**
- ✅ Skill system: YAML + Markdown → agent behavior
- ✅ FileSkillLoader: scan, parse, enable/disable at runtime
- ✅ DefaultPromptComposer: skills woven into system prompt
- ✅ DefaultMemoryService: summarization with DB persistence
- ✅ DefaultEmbeddingsService: local semantic search
- ✅ Skills API + Memory API endpoints
- ✅ 2 Copilot moments: skill file + date filter

**[0:48–0:50] Session 4 preview (2 min)**
> "Our agent has personality and memory. Next session: we take it to the cloud."

- Cloud providers (Azure OpenAI, Foundry)
- Job scheduling with cron expressions
- Health checks and testing
- **Series finale** — the full platform demo

**Co-presenter:**
- Share repo link: `github.com/elbruno/openclawnet`
- Remind: check out `session-3-complete` tag
- Thank audience, invite questions

---

## Key Talking Points (Quick Reference)

- **Skills = Markdown files** — no code, no deploy, no restart
- **Prompt composition** — base instructions + active skills + session summary + history
- **Context window management** — summarize old messages, keep recent ones verbatim
- **Local embeddings** — semantic search with no API calls, data stays on machine

---

## 🎬 Live Demo Commands — Headed E2E (for voice-over recording)

These PowerShell blocks launch real Playwright tests with **Chromium visible** and a **configurable slow-mo** between every step, so you can narrate the flow live (or capture clean screen-recordings for the deck).

> **Session-fit only:** This script lists ONLY the demos that **attach to an already-running Aspire instance**. Cold-boot variants (which spin Aspire up in-process and take ~60s) live in the test suite for CI/feature-check use, but they're too slow for a live talk and have been removed from this script. The attached variants start in 2-3s and keep the Aspire dashboard visible to the audience throughout.

> **Tuning for your pitch:**
> - `$env:PLAYWRIGHT_SLOWMO = "800"` — fast, energetic ~5min run
> - `$env:PLAYWRIGHT_SLOWMO = "1500"` — default, comfortable narration pace
> - `$env:PLAYWRIGHT_SLOWMO = "2500"` — slow, room for deep voice-over commentary
> - `$env:PLAYWRIGHT_SLOWMO = "0"` — disables slow-mo even in headed mode (chrome visible, full speed)

> **Pre-flight (one-time per session):** Start Aspire in a dedicated terminal and leave it running for the whole talk:
> ```powershell
> aspire start src\OpenClawNet.AppHost
> # Wait for green health checks + dashboard (http://localhost:15178)
> ```
> All demo commands below assume this is already up.

### Demo 1 — Add a Skill, Use It (Pirate persona)

**What it shows:** Toggle the `pirate` skill ON in Skills page → open chat → send message → agent replies in pirate voice. Proves "Markdown file → behavior change, no restart."

```powershell
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
$env:PLAYWRIGHT_HEADED = "true"
$env:PLAYWRIGHT_SLOWMO = "1500"   # tune to your pitch: 800=fast, 1500=default, 2500=slow

# Optional: override URLs if your ports differ (use `aspire show-links` to find actual URLs)
# $env:OPENCLAW_WEB_URL = "https://localhost:7294"
# $env:OPENCLAW_GATEWAY_URL = "https://localhost:7067"

dotnet test tests\OpenClawNet.PlaywrightTests `
  --filter "Category=DemoLive&FullyQualifiedName~PirateJourneyAttachedTests" `
  --logger "console;verbosity=normal"
```

**Voice-over beats** (synced to slow-mo pacing):
1. *"Watch the skill toggle flip — that's a single API call, no rebuild."*
2. *"Now we open a fresh chat — the prompt composer just inlined the pirate skill into the system prompt."*
3. *"The reply comes back in character. Same model, same code — different Markdown file."*
4. *"And notice: the Aspire dashboard stayed visible the whole time — no UI surprise for the audience."*

---

### Demo 2 — Awesome-Copilot Skill Import (manual walkthrough)

> **No headed E2E exists yet** for the awesome-copilot import flow (covered by integration tests only). Use this manual path live, or pre-record:

1. **Open** Skills page → click **"Import from awesome-copilot"** button.
2. **Pick** a skill from the GitHub catalog (e.g., `security-auditor.md`).
3. **Preview** — show the manifest: repo, commit SHA, SHA-256 hash. *"Pinned to a commit — no surprise updates."*
4. **Confirm** — file lands in `{StorageRoot}\skills\installed\security-auditor\SKILL.md`.
5. **Toggle ON** for an agent → ask the agent to review some code → see the security-auditor persona in action.

**Voice-over hook:** *"Two clicks to install a community skill, with cryptographic provenance. Your team can curate skills the same way you curate NuGet packages."*

---

### Cleanup (after the talk)

```powershell
Remove-Item Env:\PLAYWRIGHT_HEADED   # back to headless for normal CI runs
Remove-Item Env:\PLAYWRIGHT_SLOWMO -ErrorAction SilentlyContinue
# Stop Aspire in the other terminal (Ctrl+C) or:
# aspire stop
```

> **Note on other demos:** The cold-boot variants (`SkillsPirateJourneyE2ETests`, `ToolApprovalFlowTests`, `SkillsEmojiTeacherJourneyE2ETests`, etc.) still live in `tests/OpenClawNet.PlaywrightTests/` and are great for **feature checks during development** — they own the Aspire lifecycle and verify the full happy path from a clean slate. They're NOT in this script because the ~60s startup hurts a live talk. Run them with the in-process pattern (`PLAYWRIGHT_HEADED=true` + `--filter "FullyQualifiedName~..."`) when you're alone at your desk, not on stage.
- **Transparency** — users see memory stats, not a black box
