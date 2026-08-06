# Skills Management & Extraction Guide

**Version:** 1.0  
**Date:** 2026-04-30  
**Owner:** Ricken (DevRel/Technical Writer)  
**Audience:** Squad agents, team developers  

---

## Table of Contents

1. [Introduction](#introduction)
2. [What Is a Skill?](#what-is-a-skill)
3. [Quick Start: Extract a Skill in 5 Minutes](#quick-start-extract-a-skill-in-5-minutes)
4. [Marker Syntax Guide](#marker-syntax-guide)
5. [Confidence Lifecycle](#confidence-lifecycle)
6. [Full Extraction Workflow](#full-extraction-workflow)
7. [Using Skills as an Agent](#using-skills-as-an-agent)
8. [FAQ](#faq)
9. [Examples](#examples)

---

## Introduction

### Why We Extract Skills

OpenClawNet's value lies in **reusability**. When an agent solves a problem well, we capture that solution as a **Skill** — a documented pattern that any agent (or human developer) can read, understand, and apply to similar problems.

Skills serve four purposes:

1. **Prevent duplicate work** — Next time a similar problem appears, agents read the skill instead of solving from scratch
2. **Raise the bar** — Documented patterns are scrutinized; good patterns spread; weak ones get improved
3. **Build institutional knowledge** — Over time, our skill catalog becomes the team's operating manual
4. **Onboard new contributors** — Public skills at `.squad/skills/` teach .NET/Agent/Blazor best practices to outsiders

### Skill Lifecycle Overview

```
New Skill (discovered)
    ↓ @extracted by discoverer
    ↓ Confidence: LOW
    ↓ Other agents read & use
    ↓ @validated-by each agent who uses it successfully
    ↓ Confidence bumps: LOW → MEDIUM → HIGH
    ↓ Team consensus: "This is a canonical pattern"
    ↓ Deprecated (if replaced): Status set to `retired`
    ↓ Archived (if unused 90 days): Move to `.squad/skills/_archive/`
```

---

## What Is a Skill?

### Definition

A **Skill** is a reusable solution to a specific problem, extracted during or after agent work, documented in a standardized format, and validated by repeated use.

### Examples of Skills

**Good skill candidates:**
- "Scaffold Aspire-Registered Blazor Server Project" — mechanical, repeatable multi-step procedure (aspire-blazor-scaffold/)
- "Live Test Coverage Analysis" — strategic guidance on test planning for LLM-driven systems (live-test-coverage/)
- "Tool Write Hardening Review" — security checklist for code review (tool-write-hardening-review/)
- "NDJSON Request Correlation" — pattern for tracing requests through async streams (ndjson-request-correlation/)

**Not skills:**
- One-off bug fixes or experimental code
- Personal notes or incomplete work-in-progress
- Architecture decisions (those go in `.squad/decisions.md`)
- Code samples that are part of a PR (those go in documentation)

### Skill Structure

Each skill lives in `.squad/skills/{name}/`:

```
.squad/skills/aspire-blazor-scaffold/
├── SKILL.md                 ← The canonical reference document
└── (optional) supporting files (images, code samples, templates)
```

Every `SKILL.md` contains:

- **Frontmatter:** Name, author, date, context (meta)
- **Purpose section:** What problem does this solve? When to use it.
- **Core pattern:** Step-by-step procedure, code examples, or strategic guidance
- **Common issues:** Pitfalls, workarounds, debugging tips
- **References:** Links to related decisions, PRs, upstream patterns
- **Markers:** `@extracted` and `@validated-by` tags (see below)
- **Confidence level:** low | medium | high

---

## Quick Start: Extract a Skill in 5 Minutes

### Step 1: Recognize the Moment (10 seconds)

You're in the middle of agent work. You solve a problem that took research or careful thinking. You realize: **"This pattern is worth reusing."**

Examples:
- You scaffold a Blazor project three times; the steps are now muscle memory → skill
- You plan test coverage for an LLM-driven feature and discover a strategic framework → skill
- You debug a subtle bug and find a root-cause checklist → skill

### Step 2: Create the Directory (20 seconds)

```powershell
mkdir .squad\skills\{kebab-case-name}
cd .squad\skills\{kebab-case-name}
```

Name rules (from agentskills.io spec):
- Lowercase alphanumeric + dashes only: `^[a-z0-9]([-a-z0-9]{0,62}[a-z0-9])?$`
- ≤64 characters
- Descriptive: `aspire-blazor-scaffold`, not `skill-1` or `blazor-stuff`

### Step 3: Write `SKILL.md` (4 minutes)

Copy the template below, fill in your content:

```markdown
# Skill: {Descriptive Title}

**Author:** {Your name}  
**Date:** {YYYY-MM-DD}  
**Context:** {Where this skill came from; what problem triggered extraction}

**Markers:**
- `@extracted` (2026-04-30 by Ricken)

**Confidence:** low

---

## Purpose

One paragraph: What problem does this solve? When should an agent use this skill?

---

## Pattern

### Step 1: {First step}
Explanation. Code example if helpful.

### Step 2: {Second step}
Explanation. Code example if helpful.

---

## Common Issues

### Problem X
**Symptom:** What does the user see when this goes wrong?

**Fix:** How to fix it.

---

## References

- Related decision: `.squad/decisions.md` § {title}
- Implementation: {PR #123, commit SHA, or file path}
- Upstream: {Link to external reference}
```

### Step 4: Add Markers (10 seconds)

Add to your SKILL.md frontmatter:

```markdown
**Markers:**
- `@extracted` (2026-04-30 by Ricken)

**Confidence:** low
```

✅ Done! Your skill is now discoverable and tracked.

---

## Marker Syntax Guide

### The `@extracted` Marker

**Usage:** Applied when a skill is first documented.

**Format:**
```markdown
@extracted: YYYY-MM-DD, agent-name, from context-description
```

**Example:**
```markdown
@extracted: 2026-04-30, ricken, from DevRel workflow documentation
```

**Meaning:** "This skill was discovered and documented on this date by this agent, from this context."

**Rules:**
- Always include: date (YYYY-MM-DD), agent name, brief context (what triggered extraction)
- Applied once, never removed
- Place immediately after the skill title, before other frontmatter
- Context should be 1-2 sentences max describing the originating work

---

### The `@validated-by` Marker

**Usage:** Applied each time another agent reads and successfully uses the skill.

**Format:**
```markdown
@validated-by: agent-name-1 (confidence), agent-name-2 (confidence), ...
```

**Example:**
```markdown
@validated-by: ricken (high), irving (medium), helly (medium)
```

**Meaning:** "These agents have confirmed this skill works, with these confidence levels."

**Rules:**
- Format: `agent-name (confidence-level)` separated by commas
- Confidence levels: `low`, `medium`, `high`
- List validators in chronological order (first = extractor)
- Add new validators when they independently confirm the skill works
- Update confidence as validation accumulates

---

### Marker Placement in `SKILL.md`

**Always place markers immediately after the title**, before other frontmatter:

```markdown
# Skill: Scaffold Aspire-Registered Blazor Server Project

@extracted: 2026-04-23, mark, from Job Output Dashboard Phase 1 implementation  
@validated-by: mark (high), irving (medium), helly (medium)

**Author:** Mark (Lead Architect)  
**Date:** 2026-04-23  
**Context:** Job Output Dashboard Phase 1 — OpenClawNet.Channels website

---

## Purpose
...
```

**Rationale:** Markers are metadata about the skill's lifecycle (extraction/validation), distinct from authorship (who wrote it). Placing them prominently helps scripts parse them easily.

---

## Confidence Lifecycle

### The Three Confidence Levels

| Level | Definition | How Reached | When to Use |
|-------|-----------|-----------|-----------|
| **low** | Skill is documented but unproven | `@extracted` by initial author; no validations yet | New skills; experimental patterns |
| **medium** | Skill used successfully by ≥1 other agent | ≥1 `@validated-by` markers from different agents | Ready for routine reference |
| **high** | Skill validated by team consensus; canonical pattern | ≥3 `@validated-by` markers OR explicit team approval | Seeds all agent spawn prompts |

### Confidence Bump Rules

**LOW → MEDIUM:**
- When: Any agent (besides the extractor) reads the skill, uses it successfully, and adds a `@validated-by` marker
- Who can bump: Any agent who used the skill
- Process: Add `@validated-by {your_name} (YYYY-MM-DD, context)` to SKILL.md markers, then update Confidence field to `medium`
- Record in: `.squad/agents/{your_name}/history.md` under Learnings section (optional but encouraged)

**MEDIUM → HIGH:**
- When: Either:
  - ≥3 independent `@validated-by` markers (three different agents have used it successfully)
  - OR explicit team consensus (mentioned in `.squad/decisions.md`)
- Who can bump: Any team member (typically the skill owner or lead)
- Process: Update Confidence field to `high`, add note in `.squad/agents/{bumper_name}/history.md`
- Record in: Add decision entry to `.squad/decisions.md` if using the consensus path

**HIGH → RETIRED:**
- When: Skill is replaced by a better pattern or no longer applies
- Process: Set `status: retired` in SKILL.md frontmatter (don't delete the file)
- Example: If you replace "Use SignalR for streaming" with "Use NDJSON for streaming", mark the old skill retired

**90-Day Archive Rule:**
- If a skill reaches 90 days with zero validations and zero recent agent references, move to `.squad/skills/_archive/{name}/`
- Create a note in `.squad/agents/ricken/history.md` explaining why it was archived
- Can be recovered from archive if needed again

### Confidence Tracking

Confidence is tracked in two places:

1. **In SKILL.md frontmatter:** `**Confidence:** low | medium | high`
2. **In `.squad/agents/ricken/history.md`:** Maintain a periodic index of skill confidence levels (quarterly snapshot)

---

## Full Extraction Workflow

### When to Extract

**Extract a skill AFTER validation, not during:**

| Scenario | Action |
|----------|--------|
| You're mid-experiment, unsure if pattern works | Don't extract yet. Wait for validation. |
| You've solved a problem and it worked well | Extract ASAP (while the solution is fresh). |
| Another agent asks "how did you do X?" | Extract the answer as a skill. |
| Same problem recurs for a third time | Extract the accumulated wisdom. |
| You see the pattern in MAF, MCP, or upstream | Extract as a reference skill (cite upstream). |

### Extraction Checklist

Before marking `@extracted`, verify:

- [ ] **Title is descriptive** (searchable by someone looking for this problem)
- [ ] **Purpose section answers:** When should I use this? What problem does it solve?
- [ ] **Pattern section is repeatable** (another agent could follow it without asking clarifying questions)
- [ ] **Code examples are tested** (copied from working implementation, not pseudocode)
- [ ] **Common Issues section covers gotchas** (the 2-3 mistakes you made or saw others make)
- [ ] **References cite PRs, commits, or upstream patterns** (so readers know where to find proof)
- [ ] **Frontmatter is complete** (author, date, context, markers, confidence)
- [ ] **Naming is kebab-case, ≤64 chars** (directory name matches skill-name rule)

### Extraction Template

Start with this and fill in each section:

```markdown
# Skill: {Descriptive Title}

**Author:** {Your Name}  
**Date:** {YYYY-MM-DD}  
**Context:** {Where did this skill come from? What problem triggered extraction?}

**Markers:**
- `@extracted` (YYYY-MM-DD by {Your Name})

**Confidence:** low

---

## Purpose

{One paragraph: What problem does this solve? When should an agent use this pattern?}

---

## Pattern

### Step 1: {Title}
{Explanation. Include code examples if helpful.}

### Step 2: {Title}
{Explanation. Include code examples if helpful.}

{Add more steps as needed.}

---

## Common Issues

### Problem: {What goes wrong?}

**Symptom:** {What does the user see/experience?}

**Fix:** {How to resolve or prevent it.}

{Add more issues as discovered.}

---

## References

- Related decision: `.squad/decisions.md` § {title}
- Implementation: {PR #123, commit hash, or file path}
- Upstream: {Link to external reference, e.g., https://openclaw.ai}
- Test evidence: {Test file path or test results}

```

---

## Using Skills as an Agent

### When Starting Work

Before you begin a new task, check for related skills:

1. **Browse `.squad/skills/`** for skills matching your domain
2. **Read skills in order of confidence:**
   - HIGH confidence first (most validated patterns)
   - MEDIUM confidence next (emerging patterns)
   - LOW confidence last (unproven experiments)
3. **Add to your history.md:** Note which skills you read and plan to apply
4. **Validate by using:** If the skill works, add `@validated-by` marker

### How Skills Appear in Spawn Prompts

When Coordinator or Scribe routes work to you, they may include skill references like:

```
Task: Implement a Blazor dashboard for job output

Relevant skills:
- .squad/skills/aspire-blazor-scaffold/SKILL.md (confidence: high)
  Read before starting. Covers project scaffolding, MudBlazor setup, Aspire registration.

- .squad/skills/live-test-coverage/SKILL.md (confidence: high)
  Reference for testing strategy. Plan your dashboard tests against this framework.
```

**Your job:** Read the linked skills before starting work.

### Recording Skill Usage

In your agent history (`.squad/agents/{your_name}/history.md`), add a line when you use a skill:

```markdown
## 2026-04-30 — Used skill: aspire-blazor-scaffold

Read `.squad/skills/aspire-blazor-scaffold/SKILL.md` for Project structure phase of Job Output Dashboard. 
Followed steps 1-7 verbatim; saved 2 hours of research. 
MudBlazor providers required `@rendermode="InteractiveServer"` (documented in Common Issues).
Validated the pattern works.

**Action:** Added `@validated-by Irving (2026-04-30, Job-Channels scaffolding)` to skill.
```

---

## FAQ

### Q: How do I distinguish between a Skill and a Decision?

**A:** 

- **Decision** (goes in `.squad/decisions.md`): Answers a binary or multi-option question ("Should we use SignalR or NDJSON?") with a **commitment**. Binding.
- **Skill** (goes in `.squad/skills/{name}/SKILL.md`): Documents a **repeatable procedure** or strategic framework. Guidance, not law.

Example:
- Decision: "We will use NDJSON for streaming (not SignalR)" → binding, all future work follows
- Skill: "How to integrate NDJSON streaming into a Blazor app" → repeatable guidance, references the decision

### Q: Can a LOW-confidence skill be wrong?

**A:** Yes. LOW-confidence skills are experimental. They may fail, be incomplete, or need revision. That's why they're marked LOW. Agents who use them are doing early-stage validation. If a LOW-confidence skill fails, post-mortem the failure and either fix the skill or retire it.

### Q: Who decides when confidence bumps happen?

**A:** Any agent can add `@validated-by` when they use a skill successfully. Confidence automatically bumps when thresholds are met:
- LOW → MEDIUM: First `@validated-by` from a different agent
- MEDIUM → HIGH: Third `@validated-by` (or team decision)

For the HIGH threshold, either:
- Ricken or the skill owner updates the field when 3+ validations exist
- OR any team member adds a decision entry in `.squad/decisions.md` for team consensus bumps

### Q: What if I find a bug in a skill?

**A:** Post a note in the skill's `.squad/agents/{your_name}/history.md` describing the bug, then:

1. If it's a **minor correction** (typo, clarification): Edit SKILL.md directly, add a note in your history
2. If it's a **major issue** (pattern doesn't work): Keep the skill as-is, extract a new corrected skill with a different name, and note the deprecation in both skills
3. If it's a **rare edge case**: Add to Common Issues section

Do not delete the old skill. Skills are immutable records of team learning over time.

### Q: Can I extract a skill from someone else's work?

**A:** Yes, with permission and credit. Format:

```markdown
# Skill: {Title}

**Author:** Ricken (extracted from Irving's work)  
**Date:** {extraction_date}  
**Context:** Extracted from Irving's implementation of {feature_name}

**Markers:**
- `@extracted` (2026-04-30 by Ricken, based on Irving's {PR #123})
```

Always credit the originator and link to the source.

### Q: How long should a SKILL.md be?

**A:** Aim for **500–2000 words**. 

- **Too short** (<300 words): Probably not a skill, just a note
- **Too long** (>3000 words): Break into multiple skills or move prose to upstream documentation
- **Sweet spot**: Enough detail that another agent could follow it without questions, but concise enough to read in 10 minutes

### Q: What if a skill needs to be updated?

**A:** Update SKILL.md directly (in-place revision). Add a note to markers:

```markdown
**Markers:**
- `@extracted` (2026-04-23 by Mark)
- `@validated-by` Irving (2026-04-25, Channels scaffolding)
- `@updated` (2026-05-10 by Ricken, added MudBlazor rendermode workaround to Common Issues)
```

This keeps skill history transparent.

---

## Examples

### Example 1: Well-Extracted Skill (aspire-blazor-scaffold)

**Location:** `.squad/skills/aspire-blazor-scaffold/SKILL.md`

**Markers:**
```
- `@extracted` (2026-04-23 by Mark)
- `@validated-by` Irving (2026-04-25, Channels scaffolding)
- `@validated-by` Helly (2026-04-26, Dashboard UI)
```

**Confidence:** medium

**Why this is good:**
- Clear, repeatable steps with full code examples
- Common Issues section covers the MudBlazor provider gotcha
- Multiple validations from different agents
- References PRs and upstream Aspire docs

---

### Example 2: Low-Confidence Skill (in progress)

**Location:** `.squad/skills/new-pattern/SKILL.md`

**Markers:**
```
- `@extracted` (2026-04-30 by Irving)
```

**Confidence:** low

**Why it's LOW:**
- Only the extractor has tested it
- Pattern is still experimental
- Awaiting validation from other agents

**Path to MEDIUM:** Irving documents it; another agent (Helly, Petey, etc.) reads, uses in real work, adds `@validated-by` marker.

---

### Example 3: Deprecated/Retired Skill

**Markers:**
```
- `@extracted` (2026-03-01 by Irving)
- `@validated-by` Mark (2026-03-15, authentication-v1)
- `@deprecated` (2026-04-20 by Mark, replaced by authentication-v2-oauth)
```

**Status:** retired

**Why:**
- Superseded by a better pattern (OAuth v2 auth)
- Old skill still exists (immutable record)
- New skill links to this as "deprecated predecessor"

---

### Example 4: Updating a Skill (MudBlazor rendermode workaround)

**Original:**
```
# Skill: Scaffold Aspire-Registered Blazor Server Project

**Author:** Mark  
**Date:** 2026-04-23  
...
```

**After discovery of MudBlazor gotcha:**
```
# Skill: Scaffold Aspire-Registered Blazor Server Project

**Author:** Mark  
**Date:** 2026-04-23  

**Markers:**
- `@extracted` (2026-04-23 by Mark)
- `@validated-by` Irving (2026-04-25, Channels scaffolding)
- `@validated-by` Helly (2026-04-26, Dashboard UI)
- `@updated` (2026-04-26 by Helly, added MudBlazor rendermode fix to Common Issues)

...

## Common Issues

### MudBlazor Providers Not Rendered

**Symptom:** `InvalidOperationException: Missing <MudPopoverProvider />`

**Fix:** Add `@rendermode="InteractiveServer"` to each MudBlazor provider in MainLayout.

[Discovered by Helly during Dashboard UI implementation, 2026-04-26]
```

---

## Glossary

| Term | Definition |
|------|-----------|
| **Skill** | A documented, reusable solution to a specific problem |
| **Extraction** | The act of documenting a skill after solving a problem |
| **Marker** | A tag (`@extracted`, `@validated-by`) that tracks skill provenance and validation |
| **Confidence** | A three-level rating (low/medium/high) of skill maturity and team agreement |
| **Validation** | When an agent reads a skill and successfully applies it to real work |
| **agentskills.io spec** | Open standard for skill folder structure (frontmatter + body + optional scripts/assets/references) |
| **MAF** | Microsoft.Agents.AI — the framework that serves skills to OpenClawNet agents |
| **Progressive disclosure** | Four-stage skill loading: Advertise → Load → Read resource → Run script |

---

## Next Steps (Phase 2 Recommendations)

1. **Skill Visualization Dashboard** — Web UI showing all skills, confidence levels, validation counts, search
2. **Marker Automation** — Git hooks or commit validation to enforce marker format
3. **Archive & Deprecation UI** — Visual flow for retiring and archiving skills
4. **Skill Scoring** — Metrics on skill quality (completeness, validation velocity, usage frequency)
5. **Integration with Agent Spawn Prompts** — Automatic skill recommendation based on task domain

---

**Questions?** Post in `.squad/agents/ricken/history.md` or raise an issue on the squad discussions.
