# Release Guidance

**Last Updated:** 2026-08-20

**Release Target:** v1.0.0 from current `main`

**Release Type:** First release; source-only GitHub Release

## Release Scope

OpenClawNet v1.0.0 is a source release for people who want to clone, build, learn from, or fork the repository.

### Included

- The commit tagged `v1.0.0` on `main`
- A GitHub Release created through the `.github/workflows/release.yml` tag gate
- GitHub's automatically generated source-code `.zip` and `.tar.gz` archives

### Not Included

- NuGet publishing to NuGet.org or a private feed
- `.nupkg` files, compiled binaries, installers, containers, or other uploaded release assets
- Deployment to Azure or another environment
- A prerelease channel

The test-result artifact uploaded by PR CI is a temporary CI diagnostic and is not a v1.0.0 release asset.

## Required Tag Gate

The release must be created by `.github/workflows/release.yml` from a pushed semantic-version tag. Because the workflow must exist in the tagged commit to receive the tag event, verify that this file is committed on the exact `main` commit before tagging. Do not create the release manually as a substitute for the gate.

The workflow accepts tags matching `v*.*.*`, then validates the exact `vMAJOR.MINOR.PATCH` form. On `windows-latest` it restores for `win-x64`, builds and runs the offline unit and mocked Azure unit test projects, and only then runs `gh release create <tag> --generate-notes`. It contains no package-publish, asset-upload, or deployment step.

This is the first release, so release notes must not claim a comparison with an earlier OpenClawNet release. GitHub may generate notes from merged work included in the tagged commit.

## Exact v1.0.0 Tag Process

Run these commands only after the release workflow and all intended release changes are merged:

```powershell
git switch main
git pull --ff-only origin main
git fetch origin --tags --prune
git branch --show-current
git status --short
Test-Path .github\workflows\release.yml
git tag --list v1.0.0
git tag -a v1.0.0 -m "OpenClawNet v1.0.0"
git show --no-patch --decorate v1.0.0
git push origin v1.0.0
```

Before the push:

1. `git branch --show-current` must print `main`.
2. `git status --short` must be empty.
3. `Test-Path` must return `True`, and the workflow must be committed rather than an untracked local file.
4. `git tag --list v1.0.0` must print nothing after fetching remote tags.
5. `git show` must identify the intended current `main` commit.

The tag push is the release action. Do not move or reuse `v1.0.0` after publication. Confirm that the workflow completed and that the GitHub Release contains no uploaded assets beyond GitHub's source archives.

## PR CI Validation Scope

`.github/workflows/pr-ci.yml` is the actual pull-request gate. It runs for opened, synchronized, or reopened pull requests targeting `main`, `dev`, `preview`, or `insider`.

The single `windows-latest` job:

1. Restores `OpenClawNet.slnx` for `win-x64`.
2. Builds `OpenClawNet.UnitTests` and `OpenClawNet.UnitTests.Azure` in Release mode.
3. Runs `OpenClawNet.UnitTests` with `Category!=Live`.
4. Runs `OpenClawNet.UnitTests.Azure`, whose Azure clients are mocked for offline execution.
5. Uploads the resulting TRX files for diagnostics.

PR CI does **not** run the Integration, E2E, Playwright, or Deployment projects. It also excludes live tests from the primary unit-test project. The workflow is Windows-only; it is not a Windows/macOS/Linux matrix.

Passing PR CI therefore means the supported offline Windows unit-test gate passed. It does not prove that every environment-dependent suite passed.

## Environment-Dependent Validation Blockers

The excluded suites require resources that are not consistently available on PR runners or developer machines:

| Requirement or blocker | Affected validation |
|---|---|
| Docker and a working Aspire AppHost | Integration and container-orchestration tests |
| A running application stack | E2E and Playwright scenarios |
| Installed Playwright browser/runtime prerequisites | Browser tests; Windows runs may also encounter the tracked `node.exe` access-denied startup blocker |
| Azure/OpenAI configuration, credentials, and reachable resources | Live Azure AI and cloud-infrastructure tests |
| GitHub Copilot authentication and an eligible subscription | Live Copilot provider tests |
| A running Ollama service and required local model | Live local-model tests |
| Cloud deployment infrastructure | Deployment tests |
| Teams, Slack, or other external-service credentials where a scenario uses them | External delivery E2E tests |
| Available local ports and sufficient startup time | Aspire and browser-driven tests |

Some tests self-skip when prerequisites are absent, but not every environment failure can be treated as a successful validation. Record passed, skipped, and failed results separately, and do not describe an excluded or skipped suite as passing.

No full environment-dependent test result is asserted by this document. For setup details, see `docs/architecture/TEST-ENVIRONMENT.md`, while treating `.github/workflows/pr-ci.yml` and the current test code as authoritative when older prose differs.

## Dependency and Licensing Notes

Package versions are declared in the individual `.csproj` files; `Directory.Build.props` contains shared project metadata rather than a centralized package-version catalog. Avoid copying a broad version table into release notes because it becomes stale.

The release-specific licensing decision is:

- `SixLabors.ImageSharp` stays at **3.1.12**, which is used under the MIT license.
- Do not upgrade to ImageSharp 4.x as part of v1.0.0. That major version requires a separate commercial-license/procurement decision and corresponding validation.

Other dependency versions should be read from the project files at the tagged commit.

## Release Checklist

- [ ] Release workflow exists at `.github/workflows/release.yml` on current `main`
- [ ] Intended release commit is current, reviewed `main`, not the stale `feat/harness-phase2` branch
- [ ] PR CI passed for the release changes
- [ ] Environment-dependent results are reported honestly as passed, skipped, failed, or not run
- [ ] ImageSharp remains at 3.1.12 under the MIT decision
- [ ] Annotated `v1.0.0` tag points to the intended commit
- [ ] Tag is pushed once to trigger the release workflow
- [ ] GitHub Release is the first release and is not marked as a prerelease
- [ ] No NuGet publication or uploaded release assets were produced
