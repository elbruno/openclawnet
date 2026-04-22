# Demo 3 — A real agent loop with Ollama + 2 tools

A small console that wires `OllamaAgentProvider` to `Microsoft.Extensions.AI`'s `IChatClient`, exposes two tools (`calculator`, `now`), and lets the model decide when to call them. Watch the loop step through prompt → model → tool call → result → final answer.

## Prerequisites

- [Ollama](https://ollama.com/) running locally
- A **tool-capable** model pulled, for example:
  ```pwsh
  ollama pull gemma3:4b
  # or: llama3.1, qwen2.5, phi4
  ```

## Run

```pwsh
cd sessions\session-2\code\demo3-agent-loop
dotnet run "What is sqrt(2024) rounded to 2 decimals?"
dotnet run "What time is it in Tokyo right now?"
$env:OLLAMA_MODEL = "llama3.1"; dotnet run "Compute 12 * (4 + 3) / 2"
```

Sample output:

```
📨 user: What is sqrt(2024) rounded to 2 decimals?

   🛠️  tool: calculator(SQRT(2024))
   ✅ result: 44.99...

💬 assistant:
The square root of 2024 is approximately 44.99.
```

## What's happening under the hood

1. `OllamaAgentProvider.CreateChatClient(...)` returns an `IChatClient` (the `Microsoft.Extensions.AI` standard interface).
2. We wrap it with `UseFunctionInvocation()` — that's the **agent loop**: it inspects every model response for tool calls, runs them, appends the results to the conversation, and re-calls the model.
3. The two `AIFunctionFactory.Create(...)` instances become the tool manifest the model sees.
4. `ChatToolMode.Auto` lets the model decide whether (and which) tools to call.

This is the **same machinery** the OpenClawNet `Gateway` and `DefaultAgentRuntime` use — just spread across more files, with security policies, persistence, and approval gates layered on top.

## Try it

- Add a third tool that returns weather for a city (mock data is fine).
- Set `ToolMode = ChatToolMode.None` and ask a math question — watch the model hallucinate without help.
- Switch to a **non**-tool-capable model and observe what happens (some models will ignore the tool manifest and answer directly).

> **Caveat:** Some smaller local models are inconsistent with tool calling. If the model never invokes the tool, try a larger one (`llama3.1:8b`, `qwen2.5:7b`, `gemma3:4b`).
