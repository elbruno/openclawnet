# Phase 2 Feature 1: Story Cards (Quick Reference)

Prepared by Mark (Architect) — 2026-05-08

---

## 📌 Story 1: Adapter Infrastructure & Factory Setup
**Owner:** Irving | **Pts:** 5 | **Days:** 0.5  
**Depends on:** none  
**Status:** 🟢 Ready

### What to Build
- `IChannelDeliveryAdapterFactory` interface
- Hardcoded factory implementation (switch statement: "teams", "slack", "webhook")
- Register in Gateway DI container

### Success Criteria
- [ ] Factory resolves "teams", "slack", "webhook" by name
- [ ] Factory throws `InvalidOperationException` for unknown names
- [ ] Unit tests: happy path + unknown adapter
- [ ] DI registration complete in `Program.cs`

### Implementation Notes
```csharp
interface IChannelDeliveryAdapterFactory {
    Task<IChannelDeliveryAdapter> GetAdapterAsync(string name, CancellationToken ct);
}
```
Location: `OpenClawNet.Gateway/Services/ChannelDeliveryAdapterFactory.cs`

---

## 📌 Story 2: Generic Webhook Adapter (MVP)
**Owner:** Irving | **Pts:** 5 | **Days:** 0.5  
**Depends on:** Story 1  
**Status:** 🟢 Ready

### What to Build
HTTP POST adapter: accepts webhook URL, sends JSON payload with artifact data.

### Success Criteria
- [ ] HTTP POST to configurable URL
- [ ] Payload: `{ jobId, jobName, artifactId, artifactType, content }`
- [ ] Success: returns `DeliveryResult(Success: true, ExternalId: "200")`
- [ ] Failure: returns `DeliveryResult(Success: false, ErrorMessage: "...")`
- [ ] Timeout: 5 seconds → `DeliveryResult(Success: false, ErrorMessage: "timeout")`
- [ ] Unit tests: success, 4xx, 5xx, timeout, malformed URL

### Implementation Notes
- No authentication (MVP)
- Use HttpClient (injected)
- Catch: `HttpRequestException`, `TaskCanceledException`
- Location: `OpenClawNet.Channels/Adapters/GenericWebhookDeliveryAdapter.cs`

---

## 📌 Story 3: Job-to-Channel Routing Data Model
**Owner:** Irving | **Pts:** 4 | **Days:** 0.5  
**Depends on:** none  
**Status:** 🟢 Ready

### What to Build
- `ScheduledJob.DeliveryChannels` property (string: "teams,slack,webhook")
- New `AdapterDeliveryLog` entity (audit trail for all adapter calls)

### Success Criteria
- [ ] `ScheduledJob` has `DeliveryChannels` property
- [ ] `AdapterDeliveryLog` entity created with: Id, JobId, JobRunId, AdapterName, Success, ErrorMessage, ExternalId, DeliveredAt
- [ ] EF DbSet added
- [ ] Migration generated: `dotnet ef migrations add AddAdapterDeliveryLog`
- [ ] Default `DeliveryChannels` is empty (no delivery unless configured)

### Implementation Notes
Location: `OpenClawNet.Storage/Entities/ScheduledJob.cs`, `AdapterDeliveryLog.cs`

---

## 📌 Story 4: Adapter Delivery Service (Fire-and-Forget)
**Owner:** Irving | **Pts:** 6 | **Days:** 0.75  
**Depends on:** Story 1, 3  
**Status:** 🟢 Ready

### What to Build
Service that orchestrates delivery to multiple channels:
```csharp
interface IChannelDeliveryService {
    Task<Dictionary<string, DeliveryResult>> DeliverToChannelsAsync(
        Guid jobId, Guid jobRunId, IEnumerable<string> channels, CancellationToken ct);
}
```

### Success Criteria
- [ ] Parses job's `DeliveryChannels` property
- [ ] Calls factory to get adapters
- [ ] Fire-and-forget: executes in background, doesn't block caller
- [ ] Logs results to `AdapterDeliveryLog` (best-effort)
- [ ] Catches adapter exceptions, continues to next adapter
- [ ] Fetches webhook URL from `appsettings.json`
- [ ] Unit tests: success, adapter missing, timeout, partial failure

### Implementation Notes
- Use `Task.Run()` or background task queue for fire-and-forget
- Write `AdapterDeliveryLog` rows after each adapter completes
- Location: `OpenClawNet.Gateway/Services/ChannelDeliveryService.cs`

---

## 📌 Story 5: Job Definition UI — Channel Selection
**Owner:** Helly | **Pts:** 6 | **Days:** 0.75  
**Depends on:** Story 3  
**Status:** 🟢 Ready

### What to Build
Blazor UI form: checkboxes for Teams/Slack/Webhook, conditionally show webhook URL field.

### Success Criteria
- [ ] Job definition edit page has "Delivery Channels" section
- [ ] Checkboxes for Teams, Slack, Generic Webhook
- [ ] When "Generic Webhook" checked, show URL input field
- [ ] Form validation: URL required if webhook checked
- [ ] Save channels to `ScheduledJob.DeliveryChannels`
- [ ] Save webhook URL (to `appsettings.json` or ScheduledJob column)
- [ ] Both create and edit jobs support channels

### Implementation Notes
- Use MudBlazor Checkbox for channel selection
- Location: `OpenClawNet.Channels/Pages/Jobs/EditJob.razor`
- Webhook URL: consider storing in ScheduledJob column (separate from appsettings)

---

## 📌 Story 6: Integrate Adapter Delivery into Job Executor
**Owner:** Irving | **Pts:** 5 | **Days:** 0.5  
**Depends on:** Story 4, 3  
**Status:** 🟢 Ready

### What to Build
Hook `IChannelDeliveryService` into `JobExecutor` so jobs trigger adapter delivery on success.

### Success Criteria
- [ ] After `JobRun` saved successfully, call `DeliverToChannelsAsync()`
- [ ] Delivery is non-blocking (fire-and-forget)
- [ ] Job success NOT dependent on adapter delivery
- [ ] Integration test: execute job with channels → verify delivery attempted
- [ ] Audit trail entries created

### Implementation Notes
- Modify `JobExecutor.ExecuteJobAsync()` (around line 150+)
- Call delivery service AFTER job completion
- Do NOT await delivery
- Location: `OpenClawNet.Gateway/Services/JobExecutor.cs`

---

## 📌 Story 7: Teams Proactive Message Adapter
**Owner:** Irving | **Pts:** 7 | **Days:** 1  
**Depends on:** Story 1, 2  
**Status:** 🟢 Ready

### What to Build
Outbound bot framework adapter using `ConnectorClient` to send proactive Teams messages.

### Success Criteria
- [ ] Implements `IChannelDeliveryAdapter`
- [ ] Uses Bot Framework `ConnectorClient`
- [ ] Sends proactive message: "Job '${jobName}' artifact: ${artifactType}"
- [ ] Returns `DeliveryResult(Success: true, ExternalId: <message-id>)` on success
- [ ] Returns `DeliveryResult(Success: false, ErrorMessage: <error>)` on failure
- [ ] Unit tests: success, auth failure, invalid conversation
- [ ] Manual integration test: post to Teams bot → run job → verify message in Teams

### Implementation Notes
- Requires stored conversation reference (from inbound bot message)
- For MVP: store during first inbound message OR hard-code single conversation ref
- Location: `OpenClawNet.Adapters.Teams/TeamsDeliveryAdapter.cs`
- Uses: `Microsoft.Bot.Connector.ConnectorClient` (already in dependencies)

---

## 📌 Story 8: Slack Webhook Adapter
**Owner:** Irving | **Pts:** 6 | **Days:** 0.75  
**Depends on:** Story 1, 2  
**Status:** 🟢 Ready

### What to Build
Webhook adapter using Slack Block Kit to post job artifacts to Slack.

### Success Criteria
- [ ] Implements `IChannelDeliveryAdapter`
- [ ] HTTP POST to Slack webhook URL (from `appsettings.json`)
- [ ] Uses Slack Block Kit format: title + content
- [ ] Timeout: 5 seconds
- [ ] Returns `DeliveryResult(Success: true, ExternalId: <timestamp>)` on success
- [ ] Returns `DeliveryResult(Success: false, ErrorMessage: <error>)` on failure
- [ ] Unit tests: success, invalid webhook, timeout, malformed JSON
- [ ] Manual integration test: run job → verify message in Slack channel

### Implementation Notes
- Slack webhook format: `{"text": "...", "blocks": [...]}`
- HttpClient injected
- Location: `OpenClawNet.Adapters.Slack/SlackDeliveryAdapter.cs` (new project OR add to Channels)
- Register in factory (Story 1)

---

## 📌 Story 9: Testing & Demo Preparation
**Owner:** Dylan | **Pts:** 8 | **Days:** 1  
**Depends on:** Stories 2–8  
**Status:** 🟢 Ready

### What to Build
- Live integration tests for all 3 adapters
- Unit test suite covering adapters + services
- Manual demo script

### Success Criteria
- [ ] Live test: webhook → create job → execute → verify POST received
- [ ] Live test: teams → create job → execute → verify message delivered
- [ ] Live test: slack → create job → execute → verify message delivered
- [ ] Unit test coverage ≥ 80% (adapters + services)
- [ ] `AdapterDeliveryLog` entries verified for each delivery
- [ ] Demo script: 3 jobs across Teams/Slack/Webhook with pass/fail criteria

### Implementation Notes
- Use test webhook service (webhook.site) or local mock server
- Manual validation for Teams/Slack (live credentials needed)
- Location: `tests/OpenClawNet.UnitTests/` + demo script
- Dylan creates demo checklist document

---

## 📊 Effort Summary

| Story | Owner | Pts | Days | Start | End |
|-------|-------|-----|------|-------|-----|
| 1 | Irving | 5 | 0.5 | D1-AM | D1-AM |
| 2 | Irving | 5 | 0.5 | D1-AM | D1-PM |
| 3 | Irving | 4 | 0.5 | D1-AM | D1-AM |
| 4 | Irving | 6 | 0.75 | D1-PM | D1-PM |
| 5 | Helly | 6 | 0.75 | D1-PM | D1-PM |
| 6 | Irving | 5 | 0.5 | D2-AM | D2-AM |
| 7 | Irving | 7 | 1 | D2-AM | D2-PM |
| 8 | Irving | 6 | 0.75 | D2-PM | D2-PM |
| 9 | Dylan | 8 | 1 | D2-PM | D3-AM |
| **Total** | **Team** | **52** | **6.5** | **D1-AM** | **D3-AM** |

---

## 🚀 Quick Start (Irving's First 48 Hours)

### Tomorrow (Day 1) Morning: 9 AM
1. **S1 (Factory)** — Create factory interface + implementation, register DI
   - 2–3 hours
   - Files: `OpenClawNet.Gateway/Services/ChannelDeliveryAdapterFactory.cs`

2. **S3 (Data Model)** — Add `DeliveryChannels` to ScheduledJob, create AdapterDeliveryLog
   - 2–3 hours
   - Files: `OpenClawNet.Storage/Entities/ScheduledJob.cs`, `AdapterDeliveryLog.cs`
   - Run: `dotnet ef migrations add AddAdapterDeliveryLog`

### Tomorrow (Day 1) Afternoon: 1 PM
1. **S2 (Webhook Adapter)** — Implement GenericWebhookDeliveryAdapter
   - 3 hours
   - File: `OpenClawNet.Channels/Adapters/GenericWebhookDeliveryAdapter.cs`

2. **S4 (Delivery Service)** — Orchestrate adapter calls, fire-and-forget
   - 2–3 hours
   - File: `OpenClawNet.Gateway/Services/ChannelDeliveryService.cs`

### Tomorrow Evening: Before 5 PM
- [ ] Push all commits
- [ ] Mark stories 1, 2, 3 as "in review"
- [ ] Ping Helly: S3 ready for UI work

---

## 🎯 Success Criteria (Feature-Complete)

- ✅ 52 story points delivered
- ✅ All 9 stories completed
- ✅ Generic Webhook adapter demo'd
- ✅ Teams adapter sends proactive messages
- ✅ Slack adapter posts to channel
- ✅ UI allows per-job channel selection
- ✅ Audit trail (`AdapterDeliveryLog`) capturing all deliveries
- ✅ Unit tests ≥ 80% coverage
- ✅ Session 5 demo achievable

---

**Last Updated:** 2026-05-08  
**Next Review:** Daily standup (9 AM)
