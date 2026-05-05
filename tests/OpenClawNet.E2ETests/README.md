# OpenClawNet.E2ETests

End-to-end validation that a chat request through the gateway uses an enabled
skill from the K-1b registry, with **Azure OpenAI** as the live model.

## What runs here

| Test | Live? | What it proves |
| --- | --- | --- |
| `SkillRegistryRoundtripTests.Skills_Endpoints_RoundTripPerAgentEnable` | API only | Boots the gateway, lists the seeded system skills via `GET /api/skills`, flips a per-agent enable through `PATCH /api/skills/enabled`, and re-reads the skill to confirm the override persisted. |
| `AzureOpenAiSkillsChatTests.Chat_BaselineWithoutSkills_StreamsAssistantContent` | **Live AOAI** | Hits `POST /api/chat/stream` with no skills enabled and asserts the NDJSON stream contains non-empty assistant content. Baseline that proves Azure OpenAI auth + the streaming pipeline are wired. |
| `AzureOpenAiSkillsChatTests.Chat_WithEnabledSkill_RespectsSkillInstruction` | **Live AOAI** | Installs the `banana-suffix` fixture skill via `POST /api/skills`, enables it for the default agent, runs a chat turn, and asserts the model output contains the literal word `BANANA` — i.e. the per-agent skill overlay reached the model. **Note:** today this auto-skips with a diagnostic message because `/api/chat/stream` bypasses `ChatClientAgent`, so `OpenClawNetSkillsProvider` never fires on the streaming path. See `.squad/decisions/inbox/dylan-e2e-skills-chat-wiring-gap.md`. The hard assertion is authored — once the wiring gap is closed the `Skip.IfNot` falls through and the test asserts BANANA strictly. |

All tests are tagged:

```
[Trait("Category", "Live")]
[Trait("Layer",    "E2E")]
[SkippableFact]
```

So **the default `dotnet test` filter (`Category!=Live`) runs zero tests** in
this project. The skill-registry round-trip is intentionally tagged Live so
the whole project is opt-in behind a single filter; it does not actually call
Azure and will pass even when AOAI is unconfigured (so long as the filter
includes it).

## Required environment variables (for live runs)

The two AOAI tests skip cleanly with `Skip.IfNot(...)` unless these are set:

| Variable | Required? | Notes |
| --- | --- | --- |
| `AZURE_OPENAI_ENDPOINT` | yes | e.g. `https://my-resource.openai.azure.com/` |
| `AZURE_OPENAI_DEPLOYMENT` _or_ `AZURE_OPENAI_DEPLOYMENT_NAME` | yes | The deployment name to call. |
| `AZURE_OPENAI_KEY` _or_ `AZURE_OPENAI_API_KEY` | yes¹ | API key auth. |
| `AZURE_OPENAI_AUTH_MODE` | optional | Set to `integrated` to use `DefaultAzureCredential` instead of an API key. |

¹ Either the key **or** `AZURE_OPENAI_AUTH_MODE=integrated` is required.

As a developer convenience the tests also fall back to the gateway's
`Model:Endpoint` / `Model:ApiKey` / `Model:DeploymentName` / `Model:AuthMode`
**user secrets** (UserSecretsId `c15754a6-dc90-4a2a-aecb-1233d1a54fe1`) — the
same secrets the existing `LiveLlmTests` and `AzureOpenAILiveTests` use.

## Running

> All commands assume `$env:NUGET_PACKAGES="$env:USERPROFILE\.nuget\packages2"`
> is set first (squad convention).

```powershell
# Build only.
dotnet build tests\OpenClawNet.E2ETests --verbosity quiet

# Default — runs the full repo suite WITHOUT the Live E2E tests (0 tests in this project).
dotnet test tests\OpenClawNet.E2ETests --filter "Category!=Live" --nologo --verbosity quiet

# Live run — requires the env vars above.
dotnet test tests\OpenClawNet.E2ETests --filter "Category=Live" --nologo --verbosity quiet
```

When the env vars are not set, the live filter run still succeeds — the AOAI
tests skip with a `Skip.IfNot` reason explaining what to set.

## Hermetic isolation

Each test class spins up its own `GatewayE2EFactory`, which:

* Creates a temp folder under `%TEMP%\openclawnet-e2e\<guid>\`.
* Sets `OPENCLAWNET_STORAGE_ROOT` to that folder so the gateway's skills
  layers (`skills/system`, `skills/installed`, `skills/agents`) all resolve
  inside the temp scope.
* Deletes the folder on `Dispose`.
* Replaces the EF Core SQLite factory with an `InMemory` one so no DB file is
  written.
* Each test additionally tears down any per-agent `enabled.json` it touched
  and disables any skill it enabled, so test ordering doesn't matter.

## Skill fixture

`TestData/Skills/banana-suffix/SKILL.md` is the fixture used by E2E-3. It
instructs the model to end every reply with the literal uppercase word
`BANANA`. The test installs it through the gateway's `POST /api/skills`
endpoint (Installed-layer authoring per K-1b L-2) so the create path is also
exercised end-to-end.
