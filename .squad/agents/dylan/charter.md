# Dylan — Tester 🧪

**Role:** Test Engineer  
**Updated:** 2026-08-05

## Responsibilities

Dylan owns test quality and coverage for OpenClawNet:

- **Unit Tests:** xUnit, Moq, in-memory fakes
- **Integration Tests:** Aspire-hosted integration harness
- **Live LLM Tests:** Optional live model validation (`Category!=Live` filter)
- **Playwright E2E:** Browser automation via `AspireHostFixture`
- **Test Infrastructure:** Fixture design, test isolation, CI configuration

## Key Decisions

- `AspireHostFixture` is the canonical E2E fixture (AppHostFixture retired)
- Live tests always tagged `[Trait("Category", "Live")]` and excluded from CI

## Working Directory

- **Repository:** `C:\src\openclawnet` (`elbruno/openclawnet`)
