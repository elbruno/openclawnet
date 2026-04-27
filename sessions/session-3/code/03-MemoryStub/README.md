# 03 — MemoryStub

## What it shows

A REPL chat with **two layers of memory**, backed by a single SQLite table — no embeddings, no vector DB. Every user/assistant message is appended to `messages`. On each turn the program builds the prompt from (a) the **last N turns** of the current session (recency) and (b) **one earlier message** chosen by naive keyword overlap with the user's input (recall). It is the simplest possible thing that proves the idea: even token-level recall changes the model's behavior in a visible, demoable way. This stub is the scaffolding the real implementation eventually swaps for embeddings.

## Prerequisites

- .NET 10 SDK
- [Ollama](https://ollama.com) running locally (`ollama serve`)
- A model pulled: `ollama pull llama3.2`

## Run

```
dotnet run
```

Environment overrides:

| Variable        | Default        | Meaning                                 |
| --------------- | -------------- | --------------------------------------- |
| `MEMORY_WINDOW` | `6`            | Number of recent messages kept in-context |
| `OLLAMA_MODEL`  | `llama3.2`  | Model name passed to Ollama             |

The DB lives next to the binary at `bin/Debug/net10.0/memory.db`.

## Sample session (example session — fabricated)

**Run #1** — talking through a side project:

```
Session: a1b2c3d4  |  Memory window: last 6 turns + 1 recall  |  Model: llama3.2
> I'm building a tide-prediction CLI in Rust for sailors on the Pacific coast.
assistant> Nice — what data source are you pulling from? NOAA's CO-OPS API is the obvious starting point...

> I want offline support so it should cache stations and predictions locally.
assistant> SQLite is a good fit for that — small, file-based, no daemon...

> :quit
```

**Run #2** — new session, tangentially related question:

```
Session: 9f8e7d6c  |  Memory window: last 6 turns + 1 recall  |  Model: llama3.2
> how should I handle errors in a small Rust CLI?
   [recalled from session a1b2c3d4]
assistant> For a small CLI like your tide-prediction tool, the idiomatic path is `anyhow` at the
binary boundary and `thiserror` for any library crates you split out. For the offline cache layer
specifically, distinguish "station not in cache yet" from "cache corrupted" — the first is recoverable
by hitting NOAA, the second isn't...
```

The `[recalled from session a1b2c3d4]` line is the audience-visible signal that the memory layer fired and pulled the prior tide-prediction context into the prompt.

## Commands

| Command           | Effect                                                                   |
| ----------------- | ------------------------------------------------------------------------ |
| `:history`        | Last 20 messages of the current session (id, role, first 80 chars)        |
| `:recall <text>`  | Show what the recall layer would surface for `<text>` (no chat call)      |
| `:sessions`       | List distinct session ids in the DB with message counts                   |
| `:forget`         | `DELETE FROM messages` after a `YES` confirmation — demo reset            |
| `:help`           | Show command help                                                         |
| `:quit` / `:exit` | Exit                                                                     |

Anything that doesn't start with `:` is sent as a chat prompt.

## How it works

- One SQLite table (`messages`), append-only message log keyed by `session_id` and `id`.
- **Recency** = `SELECT role, content FROM messages WHERE session_id = @current ORDER BY id DESC LIMIT N`, then reversed into chronological order.
- **Recall** = lowercase the user input, split on whitespace/punct, keep distinct tokens of length ≥ 4, drop stopwords, then score every other message (different session, OR same session but older than the recency window) by **token-overlap count**. Threshold = `2`.
- The top-1 candidate (if any) is joined to the prompt as a `system` message: `Remembered from session {sid} at {ts}: "..."`.
- No embeddings, no vector DB, no FTS5 — this is the literal stub, on purpose.
- The real implementation will swap `FindRecall` for an embedding-similarity search; the surrounding plumbing (schema, persistence, prompt assembly) stays the same.

## Try this

- Lower the threshold from `2` to `1` and watch how often unrelated history bleeds into answers.
- Expand the stopword list and see which "obvious" matches stop firing.
- Swap the scoring loop for `WHERE content LIKE '%word%'` and compare what the model picks up.
- Set `MEMORY_WINDOW=2` and feel how much the assistant "forgets" within a single session.
