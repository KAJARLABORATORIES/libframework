# kajarlabs.Framework — working conventions

## Language
English for everything in the repository and on GitHub: code, comments, docs, commit messages, issues, PRs.

## Flow
- Every change starts from an issue.
- Branch name: `<type>/<issue-number>-<short-slug>`, e.g. `feat/12-headless-host`.
- One issue per PR when possible. The PR body fills in the template and includes `Closes #<issue-number>`.
- Never push to `develop`. Never force-push a branch that has been pushed.

## Commits
Conventional Commits: `feat`, `fix`, `docs`, `ci`, `chore`, `test`, `refactor`. Append `!` to the type for breaking changes (`feat!:`).

## Labels
- Exactly one primary type label per PR: `feature`, `fix`, `docs`, `ci`, `chore` or `dependencies`.
- Add `breaking-change` as an additional modifier label when the PR introduces a breaking public API change.
- Add the relevant `area:*` labels (at least one for `feature`, `fix`, `ci` or `chore` changes touching code or build).
- Labels exist only in `.github/labels.yml`. Never create labels by hand or with `gh label create`.

## Build rules
- `TreatWarningsAsErrors` is on. Do not suppress warnings without a written justification.
- Libraries in `libs/` are `IsAotCompatible`. No reflection or runtime code generation on the main path.
- Package versions live only in `Directory.Packages.props`.
- SDL types never appear in the public API.
- Code copied from osu!framework keeps its copyright header and is listed in `THIRD-PARTY-NOTICES.md` (create it at the repository root when adding the first copied file).

## Tests
xUnit v3. Tests that need a host use the headless host. Tests must not share global state.
