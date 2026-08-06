# Routing

How work gets assigned to squad members.

| Signal | Route to |
|--------|----------|
| Architecture, scope, multi-area decisions, code review | 🏗️ Mark (Lead) |
| Blazor pages, Razor components, UI/UX, JS interop, styling | ⚛️ Helly (Frontend) |
| Gateway endpoints, services, EF Core, SQLite, DI, runtime config | 🔧 Irving (Backend) |
| Microsoft Agent Framework, MCP SDK, system prompts, tool wiring, model providers (Ollama/Azure OpenAI/etc.), local model ecosystem | 🧠 Petey (Agent Platform Specialist) |
| Unit tests, integration tests, live LLM tests, test infrastructure | 🧪 Dylan (Tester) |
| Public site content, landing/READMEs, getting-started, sample skills, demo scripts, slide copy | 📝 Ricken (DevRel) |
| Sandboxing, secret/credential management, container & deploy hardening, CI/CD security, threat modeling, tool isolation | 🔒 Drummond (Platform Hardening) |
| Videos, educational walkthroughs, product showcase scripts, demo storyboards from E2E definitions | 🎬 Milchick (Educational Media Producer) |
| Documentation that's purely architectural/decisional | 🏗️ Mark |
| Build/CI failures, dependency issues, packaging | 🔧 Irving |
| CI/CD security review (secrets, supply chain, action pinning) | 🔒 Drummond |
| Multi-domain feature ("team, build X") | Fan out: Mark + Helly + Irving + Dylan in parallel |
| Decision logging, session memory, history merging | 📋 Scribe (silent) |
| GitHub backlog, PR review feedback, CI status, merge readiness | 🔄 Ralph |
| GitHub Project 2 dashboard hygiene, item status sync, release/manual-validation visibility | 🔄 Ralph |
| Public site (`elbruno.github.io/openclawnet`), Pages workflow, slide/dashboard sync between plan ↔ public repos | 🔄 Ralph (see [.squad/public-site.md](./public-site.md)) |
| GitHub Project 2 workflow/field changes, board-structure decisions | 🏗️ Mark |

## Notes

- Bruno is the human stakeholder. He approves PRs and makes product calls.
- Default branch: `main`. Feature branches use the pattern `feat/{slug}` or `squad/{issue-N}-{slug}`.
- **Active repository:** `C:\src\openclawnet` (`elbruno/openclawnet`). The `openclawnet-plan` repository is retired as of 2026-08-05.
- GitHub Project 2 (`https://github.com/users/elbruno/projects/2/views/1`) is a **secondary dashboard** only. Do not let it replace issues, PRs, or `.squad/decisions.md`.
- Build: `$env:NUGET_PACKAGES="$env:USERPROFILE\.nuget\packages2"; dotnet build src\OpenClawNet.AppHost\OpenClawNet.AppHost.csproj --verbosity quiet`
- Tests: `dotnet test tests\OpenClawNet.UnitTests --filter "Category!=Live" --no-build`
