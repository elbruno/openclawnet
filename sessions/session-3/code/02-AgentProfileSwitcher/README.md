# 02 — AgentProfileSwitcher

## What it shows

A "profile" — what most agent frameworks call a persona, role, or character — is just **a row in a database**. This demo seeds two profiles (`code-reviewer`, `pirate`) into a local SQLite file, runs a REPL, and lets you switch the active profile mid-session with a single command. Switching personas is one `UPDATE`. On every chat call, the active profile's `instructions` field is prepended as the `system` prompt to Ollama. No DI container, no plugin host, no YAML — just a table.

## Prerequisites

- .NET 10 SDK
- [Ollama](https://ollama.com) running locally (`ollama serve`)
- `ollama pull llama3.2`

## Run

```pwsh
dotnet run
```

The SQLite file `profiles.db` is created on first run next to the executable and seeded with two profiles. Re-runs reuse it.

## Sample session (example output)

```text
AgentProfileSwitcher — SQLite at .../profiles.db
Type :help for commands, :quit to exit.

[code-reviewer] > Why might this loop hang? while (!done) { Process(); }
[code-reviewer] - `done` is never reassigned in the visible scope — likely missing a writer.
- If `done` is shared across threads, mark it `volatile` or use `Interlocked`.
- `Process()` may itself block; add a timeout or cancellation token.

[code-reviewer] > :use pirate
✓ active profile: pirate

[pirate] > Why might this loop hang? while (!done) { Process(); }
[pirate] Arr matey! Yer ship be stuck in the doldrums — the `done` flag never catches a fair wind. Hoist a writer somewhere to flip it true, or ye'll be circling the same reef 'til the tide turns. If two crews share the wheel, lash it down with a `volatile` or `Interlocked` cleat.

[pirate] > :quit
```

Same question, two completely different answers — only the row in `profiles` changed.

## Commands

| Command         | Description                                                     |
|-----------------|-----------------------------------------------------------------|
| `:list`         | List all profiles (name, model, first 60 chars of instructions) |
| `:use <name>`   | Switch the active profile                                       |
| `:show`         | Print the active profile's full instructions                    |
| `:add <name>`   | Interactively add a new profile (multiline, end with `.`)       |
| `:help`         | Show command help                                               |
| `:quit`/`:exit` | Exit (Ctrl-Z + Enter / EOF also works)                          |
| anything else   | Sent as a chat prompt to the active profile                     |

## How it works

- **Schema** — Two tables created idempotently with `CREATE TABLE IF NOT EXISTS`:
  - `profiles(name PK, instructions, model)`
  - `state(key PK, value)` — a tiny key/value table; the active profile lives at `active_profile`.
- **Seed-on-first-run** — `INSERT OR IGNORE` puts the two starter profiles in. After the first run it's a no-op, so you can edit profiles in place without losing them.
- **Active profile** — One row in `state`. `:use <name>` is literally `UPDATE state SET value = $n WHERE key = 'active_profile'`.
- **System-prompt prepending** — Each chat call loads the active profile's `instructions` and sends them as the `system` message to `POST /api/chat`. The user's typed line is the `user` message. `stream: false`.
- **Model override** — Per-profile `model` column, with `OLLAMA_MODEL` env var as a global override (matches demo #1).

## Try this

- `:add shakespeare` and write instructions like *"Reply in iambic pentameter, in the voice of the Bard."* Then ask it to review code.
- `:add explain-like-im-five` — *"Use only common words. Compare technical concepts to toys, snacks, or animals."*
- `:add security-auditor` — *"Look exclusively for security issues: injection, auth bypass, secret leakage, unsafe deserialization. Output as a numbered risk list with severity."*
- Edit a seeded profile by hand: open `profiles.db` with `sqlite3` and `UPDATE profiles SET instructions = '...' WHERE name = 'pirate';`. Next chat call picks it up — no restart needed.
