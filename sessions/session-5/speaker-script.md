# Session 5 Speaker Script

**OpenClawNet — Channels, Browser & Events**  
Duration: ~50 minutes

---

## Pre-Session Setup (10 min before)

- Start `aspire run` and confirm all services healthy
- Open Aspire Dashboard (port 15100)
- Open Bot Framework Emulator — connect endpoint ready (but not connected yet)
- Have `curl` ready in a terminal
- Have the `src/OpenClawNet.Tools.Browser/BrowserTool.cs` and `src/OpenClawNet.Adapters.Teams/TeamsAdapter.cs` open in editor

---

## Opening (2 min)

> "We've built a complete AI agent platform. It has tools, skills, memory, cloud providers, and a scheduler. But there's a problem: users have to come to OpenClawNet. Today, OpenClawNet goes to them."

[Pause for effect]

> "By the end of this session, the same agent that answers your web questions also lives in Teams, browses the web like a human, and wakes up the moment something interesting happens in the world."

---

## Stage 1: Teams Integration (15 min)

### Intro (2 min)

> "Most enterprise users spend their day in Teams. If we want our agent to actually get used, it needs to be where people already are — not behind a URL they have to remember."

> "The challenge: Teams has a completely different wire protocol. But our agent runtime doesn't need to know about that. We use the same pattern we've used all series: abstraction."

### Show IBotAdapter (3 min)

Open `IBotAdapter.cs`.

> "Three lines. This is the entire abstraction over any chat platform. The Teams adapter implements it. A future Slack adapter would implement it. The agent orchestrator never knows which one is running."

> "Notice Platform is a string — not an enum, not a type hierarchy. This is intentional. Channels are a configuration concern, not a type concern."

### Walk through TeamsAdapter and OpenClawNetBot (5 min)

Open `TeamsAdapter.cs`.

> "The Teams adapter is a thin wrapper. It takes the incoming HTTP request, hands it to the Bot Framework CloudAdapter — which handles all the Teams-specific JWT validation and parsing — and then routes it to our bot logic."

Open `OpenClawNetBot.cs`.

> "This is the interesting part. When Teams sends us a message, we need to map the Teams conversation to an OpenClawNet session. We use a ConcurrentDictionary — conversation ID in, session Guid out."

> "Then we call `_orchestrator.ProcessAsync`. That's it. The agent runs with all its tools, all its skills, all its memory — exactly the same as if the user typed in the web UI."

### Live Demo — Bot Framework Emulator (5 min)

> "Let me show you this working. I'm using the Bot Framework Emulator — it simulates Teams locally, no Azure account needed."

[Connect Emulator to localhost:5000/api/messages]

> "Let me send a message... 'What .NET files are in the current directory?'"

[Show response, show FileSystem tool being used]

> "Same tools, same skills, different input channel. That's the power of the IBotAdapter abstraction."

---

## Stage 2: Browser Control (15 min)

### Problem setup (2 min)

> "Our web_fetch tool can download a URL. But try fetching a modern SPA, or a page that loads content with JavaScript — you get empty HTML."

> "Playwright solves this. It's a real browser running headlessly. No window. No display. Just Chromium, fully programmable from .NET."

### Walk through BrowserTool (5 min)

Open `BrowserTool.cs`.

> "Five actions, all following the same pattern: create Playwright, launch headless Chromium, navigate, do the thing, return a ToolResult. The agent picks which action to call based on the task."

> "extract-text is the most powerful for research tasks. The agent asks 'what's on this page?' and gets real content, not markup soup."

> "screenshot is great for reporting and debugging. The agent can describe what a page looks like, or save evidence of what it found."

### Setup reminder (1 min)

> "One setup step: after building, run `playwright install chromium`. This downloads the browser binaries once. After that, every browser tool call works offline — no external services."

### Live Demo (7 min)

[Demo 1 — Research]
> "Let me ask the agent: 'Extract the main content from the .NET blog and summarize the latest post.'"

[Show browser tool invocation in traces, then agent summarizing]

[Demo 2 — Screenshot]
> "Now: 'Take a screenshot of the Aspire docs homepage.'"

[Show screenshot path returned, open the PNG]

[Copilot Moment]
> "Let me ask Copilot to add a `wait-for-selector` action — super useful for SPAs that render asynchronously."

[Show Copilot generating the action following the existing pattern]

---

## Stage 3: Event-Driven Webhooks (10 min)

### Framing (2 min)

> "The scheduler from Session 4 checks for jobs every 30 seconds. That's polling. It works, but there's a fundamental limit: you can't react faster than your poll interval."

> "Events are different. Something happens → your agent runs. Immediately. GitHub merges a PR, a monitoring alert fires, a form gets submitted — any of these can become an agent trigger."

### Walk through WebhookEndpoints (3 min)

Open `WebhookEndpoints.cs`.

> "The endpoint is beautifully simple. It receives any event type, creates an audit session, builds a natural-language message the agent understands, and calls ProcessAsync. The agent gets full tool access."

> "We store every webhook trigger as a ChatSession with Provider = 'webhook'. That means every event is auditable, replayable, and visible in the web UI."

### Live Demo (5 min)

```bash
curl -X POST http://localhost:5000/api/webhooks/deployment-alert \
  -H "Content-Type: application/json" \
  -d '{"message": "CPU usage above 90% on production. Investigate and suggest fixes.", "data": {"service": "gateway", "cpu": 92, "region": "eastus"}}'
```

> "Watch the agent process this. It might call the web tool to check known issues, use the file system to look at recent changes, or just reason about the alert and suggest next steps."

[Show Aspire traces]
[Show the created session in GET /api/webhooks]

---

## Closing (8 min)

### Series Recap (3 min)

> "Five sessions. One platform. Let me walk through what we've built."

[Walk through the table in the slides]

> "Session 1 was the skeleton. Session 5 is the nervous system — connecting to the world."

### Architecture (2 min)

[Show the full architecture diagram]

> "Look at this. Five input channels: web, REST, Teams, webhooks, scheduler. Six tool types. Four model providers. One runtime. That's the power of interfaces and abstraction in .NET."

### Next Steps (2 min)

> "Where do you take this next? The obvious ones: add Slack, add Discord. More interesting: use Durable Functions for multi-step workflows that survive restarts. Add HMAC signature verification to webhooks so only trusted systems can trigger your agent."

> "And multi-agent: what if one agent could call another? Route tasks to specialized agents — a research agent, a coding agent, a monitoring agent — all coordinated by an orchestrator."

### Goodbye (1 min)

> "Thank you for joining us for all five sessions. The code is on GitHub, the guides are in the repo, and the Reactor community is waiting for your questions."

> "Build something wild. Ship it."

---

## Emergency Fallbacks

| If... | Do this |
|-------|---------|
| Emulator won't connect | Show the code only, explain the pattern |
| Playwright download fails | Pre-recorded demo video of browser extraction |
| Webhook demo times out | Use shorter prompt without tool calls |
| Build errors | Git checkout session-5-complete |
