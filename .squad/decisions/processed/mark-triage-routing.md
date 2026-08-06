# Mark — Triage Routing Decisions (2026-05-02)

## Decision: Issue Routing Strategy

**Status:** APPROVED  
**Date:** 2026-05-02  
**By:** Mark (Lead 🏗️)

### Semantic/Skills Integration → Petey (Agent Platform 🧠)

- **Issues:** Semantic ranking, embeddings, skill re-ranking logic
- **Example:** #89 (SemanticSkillRanker into DefaultPromptComposer)
- **Note:** No `squad:petey` label exists yet; use `squad:mark` with "route to Petey" comment

### Parameter Validation / Backend Services → Irving (Backend 🔧)

- **Issues:** Service stub completion, guard clauses, input validation
- **Example:** #93 (DefaultHybridSearchService validation)

### Test Infrastructure → Dylan (Tester 🧪)

- **Issues:** Flaky tests, concurrent race conditions, assembly loading, transitive dependency issues
- **Examples:** #94 (file permissions), #95 (OllamaSharp load)

### Plan vs. Code Repo

**Guidance:** Keep multi-sprint architectural work in **plan** repo.
- Plan repo = squad worklog + architectural decisions + acceptance criteria
- Code repo = implementation branches + PRs
- Link plan issues to code PRs by URL comment

---

## Squad Label Availability

Current labels: `squad:mark`, `squad:irving`, `squad:helly`, `squad:dylan`  
Missing labels: `squad:petey`, `squad:drummond`, `squad:ricken`

When routing to missing squad members, use `squad:mark` + comment noting the intended recipient.
