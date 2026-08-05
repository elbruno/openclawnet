# Public Site — `https://elbruno.github.io/openclawnet/`

**Owner:** 🔄 **Ralph** (Work Monitor) — keeps the public site healthy and the published artifacts in sync with the plan repo. Escalates to 🔧 **Irving** for workflow/build changes and to ⚛️ **Helly** for landing page UX tweaks.

## What gets published

GitHub Pages is enabled on **`elbruno/openclawnet`** (the public repo). The workflow `.github/workflows/deploy-pages.yml` assembles a `_site/` directory and uploads it as the Pages artifact:

| URL path | Source folder (public repo) | Source of truth (plan repo) |
|----------|------------------------------|-----------------------------|
| `/` | `docs/landing/index.html` | `docs/landing/index.html` |
| `/test-dashboard/` | `docs/test-dashboard/` | `docs/test-dashboard/` |
| `/sessions/session-N/` | `sessions/session-N/` | `docs/sessions/session-N/` |

**Triggers:** push to `main` touching any of `docs/landing/**`, `docs/test-dashboard/**`, `sessions/**`, or the workflow itself. Also `workflow_dispatch`.

## How to add a new session to the site

1. Render the slides in the **plan repo**: `pwsh scripts\render-slides.ps1 -Sessions session-N`
   (Wraps Marp + injects the ☀️/🌙/💻 theme switcher widget. To render all sessions, run with no `-Sessions` arg.)
2. Mirror `docs/sessions/session-N/` → public repo `sessions/session-N/` (slides.md + slides.html + any assets).
3. Mirror `docs/sessions/_theme/openclaw.css` → public repo `sessions/_theme/openclaw.css` if the theme changed.
4. In `docs/landing/index.html` (both repos), enable the corresponding session card — change `class="card disabled"` to `class="card" href="./sessions/session-N/slides.html"` and remove the "Coming soon" arrow.
5. Commit + push both repos. The Pages workflow auto-deploys within ~1–2 min.

## Updating the test dashboard

The dashboard source of truth lives in `docs/test-dashboard/` of the plan repo. The publish process (`scripts/publish-test-dashboard.ps1` or equivalent) rebuilds `index.html`, `summary.json`, and the `.trx` files there; the repo sync flow mirrors that folder to the public repo. **Don't hand-edit `index.html`** — re-run the publisher.

## Updating the landing page

`docs/landing/index.html` is plain HTML/CSS, no build step. Edit it directly in the public repo and copy back to the plan repo (or vice versa). The two copies must stay byte-identical so neither repo drifts.

## Verifying a deploy

After a push, check **Actions → Deploy GitHub Pages** in `elbruno/openclawnet`. The job logs include a `Site contents` group listing every file uploaded — confirm the new slides/dashboard files are in the list before assuming it's live.

## Common breakage

- **404 on a session slide** → `sessions/session-N/slides.html` wasn't committed to the **public** repo (only the plan repo has it).
- **Old test results showing** → the publisher script wasn't re-run, or the push didn't include `docs/test-dashboard/**`.
- **Workflow didn't trigger** → the changed files don't match the `paths:` filter. Add the path to the filter or use `workflow_dispatch`.
