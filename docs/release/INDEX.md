# OpenClaw .NET Documentation Index

**Last Updated:** 2026-08-06  
**Current Release Base:** 674dbbd (feat: replacement for PR #205)

---

## 📖 Quick Links

### Getting Started
1. **[README.md](../../README.md)** — Overview, quick start, four-session journey
2. **[SETUP.md](../../SETUP.md)** — Setup checklist, release info, testing notes
3. **[Prerequisites](../manuals/00-prerequisites.md)** — Hardware, software, environment setup
4. **[Local Installation](../manuals/01-local-installation.md)** — Clone, build, run, first chat

### Release & Versioning
- **[Release Guidance](./RELEASE-GUIDANCE.md)** — ⭐ START HERE — NuGet scope, GitHub Releases (tag-gated), package versions, Harness status, test blockers
- **[Migration Notes](../migration/MIGRATION-NOTES.md)** — Harness adoption timeline (Q3 2026+), AspireHostFixture stability, API guarantees

### Architecture & Implementation
- **[Agent Runtime](../architecture/agent-runtime.md)** — IAgentOrchestrator (public), DefaultAgentRuntime (impl), streaming pipeline, context compaction
- **[Test Environment & Blockers](../architecture/TEST-ENVIRONMENT.md)** — Environment-dependent skips, Ollama/Docker/Playwright/Azure setup, xUnit configuration

### Session Materials & Demos
- **[Session 1–4](../../sessions/)** — Live Reactor materials, code, slides, demos
- **[Demo Scripts](../demos/)** — aspire-stack, gateway-only, tools, real-world scenarios
- **[Manuals](../manuals/)** — Settings, tools, jobs, E2E walkthrough

### Architecture Deep Dives
- **[Overview](../architecture/overview.md)** — System components, information flow
- **[Components](../architecture/components.md)** — Detailed breakdown of each module
- **[Runtime Flow](../architecture/runtime-flow.md)** — Execution lifecycle with diagrams
- **[Provider Model](../architecture/provider-model.md)** — Model provider abstraction and routing
- **[Jobs & Scheduling](../architecture/jobs.md)** — Job framework, state machine, persistence
- **[Memory & Skills](../architecture/memory-service-proposal.md)** — Semantic search, skill loading, context management
- **[Storage](../architecture/storage.md)** — SQLite/Azure SQL, schema, entities

---

## 🎯 Documentation by Role

### For First-Time Users
1. Read [README.md](../../README.md)
2. Follow [Prerequisites](../manuals/00-prerequisites.md) checklist
3. Run [Local Installation](../manuals/01-local-installation.md)
4. Try [Hello World](../manuals/02-hello-world.md)
5. Check [Release Guidance](./RELEASE-GUIDANCE.md) for NuGet clarification

### For Contributors
1. Read [Agent Runtime](../architecture/agent-runtime.md) (public API boundaries)
2. Review [Components](../architecture/components.md) (module organization)
3. Check [Test Environment & Blockers](../architecture/TEST-ENVIRONMENT.md) (running tests)
4. See [Migration Notes](../migration/MIGRATION-NOTES.md) (future changes)

### For Operators/Deployers
1. [Prerequisites](../manuals/00-prerequisites.md) — Hardware sizing
2. [Local Installation](../manuals/01-local-installation.md) — Build & run
3. [Release Guidance](./RELEASE-GUIDANCE.md) — Versioning, GitHub Releases
4. [Test Environment & Blockers](../architecture/TEST-ENVIRONMENT.md) — CI/CD setup

### For Educators/Reactor Facilitators
1. [Session Materials](../../sessions/) — Per-session README, code, scripts
2. [Demo Scripts](../demos/) — Pre-built demos
3. [Architecture Overview](../architecture/overview.md) — Concepts to explain
4. [Release Guidance](./RELEASE-GUIDANCE.md) — What's stable, what's planned

---

## 📋 Documentation Status

| Document | Type | Status | Notes |
|----------|------|--------|-------|
| README.md | Public | ✅ Updated | Versioning, NuGet scope, release link |
| SETUP.md | Public | ✅ Updated | Test blockers, architecture links |
| Prerequisites | Public | ✅ Current | No changes needed |
| Local Installation | Public | ✅ Current | No changes needed |
| Agent Runtime | Technical | ✅ Updated | Harness status, AspireHostFixture terminology |
| Test Environment & Blockers | Technical | 🆕 Created | Comprehensive guide to environment deps |
| Release Guidance | Technical | 🆕 Created | NuGet scope, versioning, tag-gated releases |
| Migration Notes | Technical | 🆕 Created | Harness adoption (planned, not started) |
| Components | Technical | ✅ Current | No changes needed |
| Runtime Flow | Technical | ✅ Current | No changes needed |
| Provider Model | Technical | ✅ Current | No changes needed |

---

## 🔄 Key Clarifications (2026-08-06)

### NuGet Publishing ❌ Out of Scope
OpenClaw **is NOT** published to nuget.org. It's a reference platform for learning.
- Clone or fork locally for your own projects
- No private feed integration
- See [Release Guidance](./RELEASE-GUIDANCE.md#scope)

### GitHub Releases 🏷️ Tag-Gated
Releases are created automatically when you push a tag.
```bash
git tag -a v1.0.0 -m "Release: v1.0.0"
git push origin v1.0.0
# GitHub Release is created automatically
```
See [Release Guidance](./RELEASE-GUIDANCE.md#github-release-process-tag-gated)

### AspireHostFixture ✅ Stable
Test fixture is stable and well-integrated. No migration planned.
- Used in all integration tests
- Aspire.Hosting.Testing 13.4.6
- See [Test Environment & Blockers](../architecture/TEST-ENVIRONMENT.md)

### Harness (Microsoft.Agents.AI 1.17.0) 🟡 Available, Not Migrated
Harness API is available but adoption is **NOT yet complete**.
- Current impl: `DefaultAgentRuntime` + `ModelClientChatClientAdapter` (stable)
- Harness adoption planned for future phase
- No breaking changes to public `IAgentOrchestrator` API
- See [Release Guidance](./RELEASE-GUIDANCE.md#harness--aspirehostrfixture-terminology) and [Migration Notes](../migration/MIGRATION-NOTES.md)

### Preserved Behaviors ✅ Stable
- HTTP approval flow (tool pauses, client approves, resumes)
- NDJSON streaming (real-time tokens)
- SQLite/Azure SQL persistence (sessions, history, preferences)
See [Release Guidance](./RELEASE-GUIDANCE.md#preserved-behaviors)

### Environment-Dependent Test Blockers 🚧 Documented
Tests skip gracefully if environment requirements missing:
- Ollama not running
- Docker not available
- Playwright browsers missing
- Azure/GitHub credentials missing
See [Test Environment & Blockers](../architecture/TEST-ENVIRONMENT.md)

---

## 🗂️ File Structure

```
docs/
├── release/
│   ├── RELEASE-GUIDANCE.md          ← START: NuGet, versions, Harness, test blockers
│   └── INDEX.md                     ← This file
├── migration/
│   └── MIGRATION-NOTES.md           ← Harness adoption timeline
├── architecture/
│   ├── agent-runtime.md             ← Updated: Harness notes
│   ├── TEST-ENVIRONMENT.md          ← NEW: Comprehensive blocker guide
│   ├── overview.md
│   ├── components.md
│   ├── runtime-flow.md
│   ├── provider-model.md
│   ├── jobs.md
│   ├── memory-service-proposal.md
│   ├── storage.md
│   └── ...
├── manuals/
│   ├── 00-prerequisites.md
│   ├── 01-local-installation.md
│   ├── 02-hello-world.md
│   └── ...
└── demos/
    ├── aspire-stack/
    ├── gateway-only/
    ├── tools/
    └── real-world/

README.md                             ← Updated: Versioning, release link
SETUP.md                              ← Updated: Release info, test blockers, links
```

---

## 🔗 Navigation Tips

- **New to the project?** Start with [README.md](../../README.md) → [Prerequisites](../manuals/00-prerequisites.md) → [Local Installation](../manuals/01-local-installation.md)
- **Questions about releases?** See [Release Guidance](./RELEASE-GUIDANCE.md)
- **Tests failing?** See [Test Environment & Blockers](../architecture/TEST-ENVIRONMENT.md)
- **Curious about Harness?** See [Release Guidance](./RELEASE-GUIDANCE.md#harness--aspirehostrfixture-terminology) and [Migration Notes](../migration/MIGRATION-NOTES.md)
- **Deep technical dive?** Start with [Architecture Overview](../architecture/overview.md)

---

## 📞 Support

- **Issues:** https://github.com/elbruno/openclawnet/issues
- **Discussions:** https://github.com/elbruno/openclawnet/discussions
- **Discord:** [Microsoft Foundry Community](https://aka.ms/ai-discord/dotnet) (.NET channel)

---

## Version History

| Date | Change |
|------|--------|
| 2026-08-06 | Created INDEX.md, RELEASE-GUIDANCE.md, MIGRATION-NOTES.md, TEST-ENVIRONMENT.md; updated agent-runtime.md, README.md, SETUP.md |
