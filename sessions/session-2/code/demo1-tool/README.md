# Demo 1 — Implement an `ITool`

A 60-line console app that builds a custom tool from scratch and calls it through the same `IToolExecutor` the gateway uses in production.

## What it shows

1. **`ITool`** — the contract every tool implements.
2. **`ToolMetadata` + `ParameterSchema`** — what the LLM sees.
3. **`ToolResult.Ok` / `ToolResult.Fail`** — the success/failure contract.
4. **DI wiring** — `AddToolFramework()` + `AddTool<T>()` is all you need.

## Run

```pwsh
cd sessions\session-2\code\demo1-tool
dotnet run
dotnet run -- "Bruno"
```

Expected output:

```
📚 Tools in registry:
   • greeter — Greets a person by name...

⚙️  Executing 'greeter' with: {"name":"Bruno","shout":true}

✅ HELLO, BRUNO!  (took 0.3 ms)
```

## Try it

Open `Program.cs` and:

- Set `shout = false` to see the lower-case path.
- Add a new property to `ParameterSchema` (e.g. `language`) and use `input.GetStringArgument("language")` to switch greetings.
- Change `RequiresApproval = true` and re-run — it will still pass because the default policy is `AlwaysApprovePolicy`. (Demo 2 changes that.)
