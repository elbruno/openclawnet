# 04 — SkillPicker (bonus)

## What it shows

Skill selection inside an agent is **not magic** — it's deterministic retrieval. This demo scans a folder of `*.skill.md` files, parses the YAML frontmatter by hand, and scores each skill against a user prompt by counting trigger-word matches. The output is the ordered shortlist an agent would consider before any LLM call. Same mechanic, ~200 lines of C#, fully debuggable with `Console.WriteLine`. No Ollama, no NuGets, no model in the loop.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

That's it. No Ollama, no models, no third-party packages.

## Run

From this folder:

```powershell
# Score all skills against a prompt
dotnet run -- "please review this code for bugs"

# List every skill discovered
dotnet run -- --list

# Explain why a specific skill scored what it did
dotnet run -- --explain "please review this code for bugs" code-reviewer
```

Override the skills folder:

```powershell
$env:SKILLS_DIR = "C:\path\to\my\skills"
dotnet run -- "translate this to spanish"
```

## Sample output

Default scoring command (example output):

```
Prompt: "please review this code for bugs"
Found 5 skills in ./skills/

Score  Skill              Matched triggers
-----  -----------------  --------------------------------
    2  code-reviewer      review, bug
    0  pirate-voice       —
    0  shakespeare        —
    0  spanish-translate  —
    0  summarize          —

→ Would load: code-reviewer
```

`--explain` (example output):

```
Skill: code-reviewer
Triggers (5): review, bug, refactor, code quality, lint
Prompt:   "please review this code for bugs"
Matched (2):     review, bug
Not matched (3): refactor, code quality, lint
Name match: no
Final score: 2
```

## Skill file format

Every file in `./skills/` is `*.skill.md` and starts with YAML frontmatter:

```markdown
---
name: code-reviewer
description: Reviews code for bugs and style issues
triggers: [review, bug, refactor, code quality, lint]
---

(skill body — markdown, free-form, ignored by the picker)
```

The picker only reads the frontmatter. The body is what a real agent would inject into the system prompt after the skill is selected.

## How it works

- Scans `./skills/*.skill.md` (override with `SKILLS_DIR`).
- Parses the YAML frontmatter manually — a tiny line-by-line reader, no library. Handles `key: value` and inline arrays `key: [a, b, c]`.
- Lowercases the prompt and normalizes punctuation to whitespace, then scans for trigger phrases as whole tokens.
- `score = (count of triggers found in prompt) + (1 if skill name appears in prompt)`.
- Sorted score DESC, then name ASC. Threshold to "would load" is `score >= 1`.
- Real agents do exactly this step before the LLM call, then load the top-N skill bodies into the system prompt. The retrieval part is not the AI — it's plain code you can step through.

## Try this

- Drop a new `whatever.skill.md` into `skills/`, give it a `name`, `description`, and a `triggers: [...]` array, and re-run. It shows up immediately.
- Change the scoring rule in `Program.cs` from substring-of-tokens to **whole-word equality only** and watch which prompts stop matching.
- Add per-trigger weights (e.g., the skill's `name` matching counts +3 instead of +1) and see how the ranking shifts.
- Wire the output of this picker into demo 01 (`SkillOnOff`) — pick the top skill automatically instead of passing it on the CLI.
