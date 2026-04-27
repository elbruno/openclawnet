# 05 — ProviderCatalogCli (bonus)

A non-interactive SQLite CRUD CLI for an "LLM provider catalog" — the table an
agent reads to know which model endpoints it can talk to.

## What it shows

A provider catalog is just a SQLite table plus a thin CLI. Every other piece of
the agent (resolver, dispatcher, health probe) just *queries* this table, so
"agent configuration" really does collapse to CRUD over a handful of rows. This
demo is the skeleton of OpenClawNet's `ModelProviderDefinitionStore` with the
production complexity stripped away — same shape, same primary-key-on-name,
same single-default invariant, none of the orchestration. Contrast with demos
02 and 03: those were REPLs because the lesson was *interaction state*; this
one is one-shot subcommands because the lesson is *configuration storage*.

## Prerequisites

- .NET 10 SDK
- (No Ollama, no network — pure local SQLite.)

## Run

```powershell
dotnet run -- seed
dotnet run -- list
dotnet run -- show local-llama
dotnet run -- add my-claude --kind anthropic --endpoint https://api.anthropic.com --model claude-3-5-sonnet --key-env ANTHROPIC_API_KEY
dotnet run -- update local-llama --model llama3.2
dotnet run -- set-default openai-gpt4
dotnet run -- remove my-claude
```

The DB lives next to the built executable as `providers.db`. Override the
location with the `PROVIDERS_DB` environment variable:

```powershell
$env:PROVIDERS_DB = "C:\temp\my-providers.db"
dotnet run -- list
```

## Sample output (example)

After `seed`, then `list`:

```
NAME         KIND          MODEL         ENDPOINT                                DEFAULT
-----------  ------------  ------------  --------------------------------------  -------
azure-prod   azure-openai  gpt-4o        https://my-resource.openai.azure.com
local-llama  ollama        llama3.2   http://localhost:11434                  ✓
openai-gpt4  openai        gpt-4o-mini   https://api.openai.com/v1
```

`show local-llama`:

```
name:        local-llama
kind:        ollama
endpoint:    http://localhost:11434
model:       llama3.2
api_key_env: (none)
is_default:  yes
created_at:  2026-04-27T14:02:11.4421137Z
updated_at:  2026-04-27T14:02:11.4421137Z
```

Asking for something that isn't there:

```
> dotnet run -- show nope
❌ no such provider: nope
(exit code 1)
```

## Commands

| Command                            | Description                                              |
| ---------------------------------- | -------------------------------------------------------- |
| `list`                             | Print all providers as a table, sorted by name.          |
| `show <name>`                      | Print every column of one provider, one per line.        |
| `add <name> --kind … --endpoint … --model … [--key-env …]` | Insert a new provider; errors if name exists. |
| `update <name> [--kind …] [--endpoint …] [--model …] [--key-env …]` | Partial UPDATE; only supplied fields change. |
| `remove <name>`                    | DELETE a provider; warns if it was the default.          |
| `set-default <name>`               | Clear all defaults, set this one (in a transaction).     |
| `seed`                             | Insert 3 sample providers if the table is empty.         |
| `help` / `-h` / `--help`           | Print usage with one example per command.                |

Exit codes: `0` success, `1` user error (missing flag, name not found, duplicate), `2` unknown subcommand.

## How it works

- **One table, `providers`**, primary-keyed on `name` — so duplicates can't
  silently appear, and lookups are trivial.
- **Single-default invariant enforced in code**: `set-default` runs `UPDATE …
  SET is_default = 0` then `UPDATE … SET is_default = 1` for the chosen row,
  both inside a SQLite transaction so the table is never in a two-defaults
  state.
- **Partial updates**: `update` builds the `SET` clause dynamically from the
  flags that were actually supplied, so `--model llama3.2` won't clobber
  `endpoint` or `kind`.
- **Manual flag parser** (`ParseFlags`) — ~10 lines, no CLI library. Long-form
  flags only (`--kind`, `--endpoint`, `--model`, `--key-env`).
- **No orchestration logic anywhere** — just CRUD. A real agent reads the same
  table, picks the row where `is_default = 1` (or the one named in its config),
  and dials the endpoint. That's it.
- **Mirrors production**: this is the shape OpenClawNet's
  `ModelProviderDefinitionStore` reduces to once you remove validation,
  multi-tenancy, and the resolver layer. Definitions table + a thin manager
  CLI, nothing fancier.

## Try this

- Add a `priority INTEGER` column and let the agent fall back to the next
  provider if the default is unreachable.
- Add a `last_used_at TEXT` column, stamped every time the agent picks a
  provider — gives you free usage telemetry.
- Add a sibling `provider_health` table (one row per `name` + a `status`/`checked_at`
  pair) and write a `check <name>` subcommand that pings the endpoint and
  upserts the result. This is exactly how the real codebase grows: schema first,
  command second.
