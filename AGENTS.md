# kajarlabs.Framework — working conventions

## Language
English for everything in the repository and on GitHub: code, comments, docs, commit messages, issues, PRs.

## Repository Layout
- `libs/` — Core framework packages (`IsAotCompatible`).
- `tests/` — xUnit v3 test suites (`InternalsVisibleTo` already configured for `kajarlabs.Framework.Tests`).
- `samples/` — Sample apps, including `samples/aot-smoke` used for NativeAOT verification.
- `scripts/` — Verification (`check.ps1` / `check.sh`) and shader compilation (`compile-shaders.ps1` / `compile-shaders.sh`).
- `Directory.Build.props` / `Directory.Packages.props` — Shared build settings (`net11.0`, analyzers) and single source of truth for NuGet versions.

## Workflow & Gates
- Every change starts from an issue.
- **Plan first:** When asked to work on an issue, inspect the affected files and output an implementation plan (files to touch, public API changes, test strategy). Wait for explicit approval before editing files.
- Branch name: `<type>/<issue-number>-<short-slug>`, e.g. `feat/12-headless-host`.
- Target branch for all PRs is `develop` (`gh pr create --base develop`). One issue per PR when possible.
- PR body must follow `.github/pull_request_template.md` and include `Closes #<issue-number>`.
- Never push to `develop`. Never force-push a pushed branch. Never merge a PR unless explicitly instructed.

## Verification (run before opening a PR)
- Full check (restore, format check, Release build/test, NativeAOT smoke, pack): `./scripts/check.ps1` (Windows) or `./scripts/check.sh` (Linux/macOS).
- Apply formatting fixes if needed: `dotnet format`.

## Commits & Labels
- Conventional Commits: `feat`, `fix`, `docs`, `ci`, `chore`, `test`, `refactor`. Append `!` for breaking changes (`feat!:`).
- Exactly one primary label per PR: `feature`, `fix`, `docs`, `ci`, `chore`, or `dependencies`.
- Add `breaking-change` when introducing a breaking public API change.
- Add relevant `area:*` labels (required for `feature`, `fix`, `ci`, or `chore` touching code/build).
- Labels exist only in `.github/labels.yml`. Never create labels via CLI or UI.

## Architecture & Build Rules
- `TreatWarningsAsErrors` and `CodeAnalysisTreatWarningsAsErrors` are on. Do not suppress warnings without a written justification.
- Libraries in `libs/` are `IsAotCompatible`: no reflection, `dynamic`, or runtime code generation on the main path.
- Package versions live only in `Directory.Packages.props` (no `Version` attributes in `.csproj`).
- SDL types (`SDL3-CS`) must remain `internal` and never appear in the public API. Never make an `internal` type `public` just to test it.
- Code copied from osu!framework keeps its original copyright header and must be registered in `THIRD-PARTY-NOTICES.md` at the repo root.

## Tests
- xUnit v3. Tests requiring a host must use the headless host.
- Tests must be isolated and share no mutable global state.
