# 🧪 Session 2 — Demo Code

Three small console apps that exercise the **`OpenClawNet.Tools`** stack from the Microsoft Reactor session.

| # | Demo | What it shows | LLM required? |
|---|------|---------------|---------------|
| [1](./demo1-tool/README.md) | **Implement an `ITool`** | Custom tool, `ToolMetadata`, `ParameterSchema`, `ToolResult` | ❌ |
| [2](./demo2-approval/README.md) | **The approval gate** | `IToolApprovalPolicy` swap → safe vs. dangerous arguments | ❌ |
| [3](./demo3-agent-loop/README.md) | **Real agent loop** | `IChatClient` + `AIFunction` + `UseFunctionInvocation()` | ✅ Ollama |

## Build all

```pwsh
$env:NUGET_PACKAGES = "$env:USERPROFILE\.nuget\packages2"
dotnet build docs\sessions\session-2\code\demo1-tool
dotnet build docs\sessions\session-2\code\demo2-approval
dotnet build docs\sessions\session-2\code\demo3-agent-loop
```

Each demo is a normal .NET 10 console app with `ProjectReference`s into `src\OpenClawNet.Tools.*`. Open them in VS Code or Visual Studio and run with F5.
