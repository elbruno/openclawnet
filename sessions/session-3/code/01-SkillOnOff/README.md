# 01 — SkillOnOff

## What it shows

The smallest possible "skill" demo: send the **same prompt** to a local Ollama model **twice**, once with a vanilla system prompt and once with a short skill file prepended to the system prompt, then print both answers side-by-side. The lift from a 5-line skill is usually obvious at a glance — that's the entire point. No agent framework, no orchestration, just `HttpClient` and a Markdown file.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) installed and running (`ollama serve`)
- A model pulled locally:
  ```powershell
  ollama pull llama3.2
  ```
- Optional: override the model with `$env:OLLAMA_MODEL = "qwen2.5:3b"`.

## Run

From this folder:

```powershell
dotnet run -- "Write a short bio for a senior developer." concise-tone
```

Both arguments are optional. Defaults: prompt = `"Write a short bio for a senior developer."`, skill = `concise-tone`.

Try the second built-in skill:

```powershell
dotnet run -- "Explain garbage collection in one paragraph." pirate-voice
```

## Sample output

```
Model:  llama3.2
Skill:  concise-tone
Prompt: Write a short bio for a senior developer.

Calling Ollama twice (raw + skill)...

─── ─ RAW ─────────────────────────────────────  ─── ─ WITH skill: concise-tone ─────────────────
Jane Doe is a passionate and highly experienced  - 12+ years building distributed systems
senior software developer with over twelve       - Specializes in C#/.NET, Go, Kubernetes
years of professional experience building        - Led platform team of 8 engineers
distributed systems at scale. She specializes    - Open-source maintainer, frequent speaker
in C#, .NET, Go, and Kubernetes, and has led     - Mentors junior devs across 3 timezones
platform teams of up to eight engineers across   - Coffee, vinyl, mountain biking
multiple time zones. In her spare time she
enjoys mountain biking and curating her vinyl
collection.
```

*(example output — your model will phrase things differently)*

## How it works

- Loads `skills/{name}.skill.md`, strips the YAML frontmatter, keeps the body as a system-prompt fragment.
- Fires **two** `POST http://localhost:11434/api/chat` calls in parallel via `Task.WhenAll`:
  - **Raw:** `system = "You are a helpful assistant."`
  - **Skill:** `system = "You are a helpful assistant.\n\n" + skillBody`
- Both calls use `stream: false` so we get one tidy JSON blob each.
- A tiny word-wrap helper prints both responses in two ~50-column columns so the lift is obvious live.

## Try this

Built-in skills:

| File | Effect |
| ---- | ------ |
| `skills/concise-tone.skill.md` | Terse bullets, no adjectives, hard cap on length. |
| `skills/pirate-voice.skill.md` | Rewrites every reply as an 18th-century pirate. |

Drop your own `*.skill.md` into the `skills/` folder — frontmatter (`name`, `description`) plus a body — then pass the filename (without the extension) as the second argument:

```powershell
dotnet run -- "Summarize the CAP theorem." my-custom-skill
```

That's the whole "skills" pillar in ~150 lines of C#.
