# Memory Seed Conversation

Paste these messages into a fresh chat **before the talk starts**. Run them one at a time and let each reply complete. Goal: build up ≥ 20 messages of real history so the Stage 2 summarization demo has something meaningful to compress.

The topic mix is intentional — three different frameworks, a couple of personal preferences, and one specific fact (the "second framework") so you can verify recall in the follow-up demo prompt.

---

## Messages to send (in order)

1. `I'm building a distributed app in Aspire. What does the AppHost project actually do at runtime?`
2. `Got it. Now compare that to Dapr — how do they overlap?`
3. `Which one would you pick for a new greenfield project where the team has no prior service-mesh experience?`
4. `Switching topics — what's the current state of Blazor United (server + WASM hybrid render modes) in net10.0?`
5. `When does interactive server rendering make more sense than interactive WebAssembly?`
6. `Show me the smallest possible component that switches render mode based on a query string.`
7. `Now the database side — I want EF Core with PostgreSQL via Npgsql. Any gotchas around connection pooling under Aspire?`
8. `What about migrations — should I use EF Core migrations or hand-rolled SQL scripts when the schema is owned by multiple services?`
9. `Personal preference: I dislike code-first migrations. What's a clean schema-first workflow that still gives me typed entities?`
10. `Recommend a logging approach for this stack. I want structured logs in development and shipped to OpenTelemetry in production.`
11. `Does Serilog have first-class OpenTelemetry exporters now, or do I still need a separate sink?`
12. `Tests next. I prefer xUnit. Walk me through testing a minimal API endpoint that depends on a Postgres-backed service.`
13. `For integration tests, should I use Testcontainers or the Aspire test host?`
14. `My team is split on dependency injection scopes. When should an HttpClient be registered as singleton vs typed-client vs IHttpClientFactory?`
15. `One more — I keep seeing "ahead-of-time compilation" mentioned for ASP.NET Core. Is AOT production-ready for minimal APIs in net10.0?`
16. `What breaks under AOT that I should watch for?`
17. `Auth: I want OIDC against Entra ID. Is there a clean Aspire integration or do I wire it up manually?`
18. `If I add a Blazor WebAssembly client, what's the recommended way to share the auth token with API calls?`
19. `Last setup question — what's the right place to put cross-cutting concerns like rate limiting in this stack?`
20. `Quick recap before we move on — what are the three things I told you I personally dislike or prefer in this conversation?`

---

## Verification before going live

After message 20, check that:

- The reply mentions all three preferences you stated (dislike code-first migrations, prefer xUnit, prefer structured logs / OTel).
- The chat shows ≥ 20 message bubbles — if not, the auto-summarizer may have already kicked in mid-seed (which is fine; the demo still works).

---

## Demo trigger (during the talk)

Use the prompt from `demo-prompts.md` → Stage 2 → "Summarize what we've discussed so far in 5 bullet points."

Then: "What was the second framework I asked about?" — correct answer is **Dapr** (message 2). This proves the summarized memory preserved the ordered fact.
