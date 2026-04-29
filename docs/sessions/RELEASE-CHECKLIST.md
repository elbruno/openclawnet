# Session Release Checklist

Use this checklist whenever a session is ready to be **closed / released** to the public site. Each session ends with a release sign-off doc at `docs/sessions/session-N/RELEASE.md` that captures the final, confirmed state.

This is the project's lightweight "end-of-sprint" gate for an OpenClawNet session.

---

## When to release

A session is ready to release when:
- The live presentation has been delivered (or the recording is published).
- All deliverables for that session are committed, deployed, and verified live on the public site.
- No open follow-ups blocking publication remain.

A subsequent edit (typo fix, link correction, slide tweak) does NOT require a new release — just a new commit. Releases are coarse-grained.

---

## Release checklist

Tick every item before signing off. Anything not applicable → mark `n/a` with a short reason.

### 1. Slides
- [ ] **English source:** `docs/sessions/session-N/slides.md` matches the version actually presented
- [ ] **English HTML:** `slides.html` is up to date — re-rendered after last `slides.md` edit
- [ ] **Spanish source (if applicable):** `slides-es.md` translation matches English source
- [ ] **Spanish HTML (if applicable):** `slides-es.html` re-rendered
- [ ] **Speaker info correct** on title + closing slides — co-speaker matches `metadata.json` for that language

### 2. Demos / code
- [ ] All demo projects under `docs/sessions/session-N/code/` build cleanly
- [ ] README in each demo project explains how to run it
- [ ] Target framework is `.NET 10` (per project rule)
- [ ] Ollama model references use bare `llama3.2` — no tagged variants (`:3b`, `:1b`, etc.)

### 3. Docs
- [ ] Session README (`docs/sessions/session-N/README.md`) is accurate
- [ ] Spanish README (if applicable): `README-es.md`
- [ ] Architecture / developer guides (if applicable) are in sync

### 4. Metadata
- [ ] `docs/sessions/metadata.json` has both `en` and `es` blocks (where applicable) for the session
- [ ] Speakers in metadata match the slide credits
- [ ] `status` set to `"published"`
- [ ] **`releasedAt`** field added/updated with ISO 8601 date (e.g. `"2026-04-29"`)

### 5. Public site deployment
- [ ] Materials copied from `docs/sessions/session-N/` to `sessions/session-N/` in the **main product repo** (`elbruno/openclawnet`) — required for GitHub Pages
- [ ] Landing page (`docs/landing/index.html`) links are active for both languages where applicable
- [ ] Pushed to `elbruno/openclawnet@main`
- [ ] Verified live URL responds 200 and renders correctly:
  - `https://elbruno.github.io/openclawnet/sessions/session-N/slides.html`
  - `https://elbruno.github.io/openclawnet/sessions/session-N/slides-es.html` (if applicable)

### 6. Sign-off doc
- [ ] Write `docs/sessions/session-N/RELEASE.md` using the template below
- [ ] Commit + push (plan repo and/or public repo as appropriate)

---

## RELEASE.md template

```markdown
# Session N — Release

**Released:** YYYY-MM-DD
**Released by:** <name>

## Title
- EN: <title>
- ES: <title> (if applicable)

## Speakers
- EN: Bruno Capuano + <co-speaker>
- ES: Bruno Capuano + <co-speaker> (if applicable)

## Live URLs
- EN slides: <url>
- ES slides: <url>
- EN README: <url>
- ES README: <url>

## Deliverables snapshot
- Slides: EN ✅ / ES ✅
- Demos: <count> projects, target net10.0
- Docs: README EN/ES, architecture (if any)
- Metadata: published, releasedAt set

## Known follow-ups (non-blocking)
- <item or "none">

## Notes
<anything noteworthy — known errata, recording link, attendee count, etc.>
```

---

## Process

1. Walk the checklist top-to-bottom. Fix anything that fails before signing off.
2. Write `RELEASE.md` for the session.
3. Update `metadata.json` with `releasedAt`.
4. Single commit (or commit-per-fix if multiple things needed correcting): `release(session-N): publish + sign-off`.
5. After the release, only edit-in-place is needed for small fixes — no new release required for typos or link patches.

---

## Why this is lightweight

There is no separate release branch, no version number, no tagged GitHub release for sessions. The repo's `main` branch IS the release. The checklist exists to catch deployment-path mistakes (the recurring failure mode: forgetting to copy from `docs/sessions/` to `sessions/`) and to guarantee the public site reflects the actual presented version.
