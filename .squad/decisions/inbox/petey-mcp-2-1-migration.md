### 2026-08-19: ModelContextProtocol 2.1.0 migration policy for OpenClawNet
**By:** Petey
**What:** Upgrade all direct `ModelContextProtocol` references from `1.3.0` to `2.1.0` and keep current MCP host/wrapper implementation unchanged unless compile/runtime tests prove breakage.
**Why:** Official SDK `v2.0.0` introduced major protocol/transport changes, but OpenClawNet currently uses stable APIs (`McpClient`, `McpServer`, stdio/in-memory transports, tool attributes) that remain compatible in `v2.1.0`. Full MCP-focused and full acceptance tests passed on `2.1.0`, so preserving current abstractions avoids unnecessary churn while closing the version gap safely.
