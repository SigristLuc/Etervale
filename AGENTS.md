# Agent instructions

Applies throughout this repository. Follow the user's explicit instructions first. Read the affected code and relevant
documentation before editing.

## Working rules

- Start at [docs/README.md](docs/README.md): it routes each task to the doc that maps the code involved. Read that doc
  before searching the source.
- Keep documentation current when changing behavior, contracts, formats, settings, or commands: update the matching
  doc in `docs/` in the same change. Docs are in English and describe the current state only, never a changelog.
  Use [README.md](README.md) only for setup and run instructions; keep this file limited to agent guidance.
- Treat restore, build, and analyzer warnings as errors. Build in Release with `-warnaserror`; keep
  `TreatWarningsAsErrors=true`. Fix causes instead of suppressing diagnostics.
- Write readable C#: lines at most 240 characters, one statement per line, clear names, and braces on separate lines for
  control flow. Never use the C# `dynamic` type or implicitly dynamic Dapper queries.
- Keep gameplay `Update`/`Draw` and the authoritative tick free of blocking network, database, disk, task, and long lock
  waits. Prepare work outside the loop; apply GPU work on the correct thread.
- Add comments only for non-obvious invariants, external contracts, legacy formats, or formulas. Avoid XML comments,
  section banners, implementation history, and commented-out code.
- Verify claims against the current implementation. Preserve the user's changes and stay within the task.
  Inspect `git status` and `git diff`; do not run `git add`, `git commit`, or `git push`.
- Use the user's language when reporting work. State the changes, checks performed, and remaining limitations.
- Hard rule: keep the agent's temporary folder clean, `claude` under the system temporary directory
  (`%LOCALAPPDATA%\Temp\claude` on Windows, `$TMPDIR/claude` on macOS, `/tmp/claude` on Linux). Captures, probe output,
  scratch scripts and file copies live there only while a step needs them; delete them as soon as it is done, and
  leave nothing behind at the end of a task.

## Architecture and data

- Keep shared gameplay rules in `Shared/PristonTale.Simulation` and online authority in
  `Shared/PristonTale.Application`. Keep MonoGame in the client, transport mapping at the network edges, and SQL in
  persistence.
- Use `AssetFiles` (`Shared/PristonTale.Assets`) for original asset paths. Check version and encoding before extracting behavior from
  local reference material.
- Never cite the original source in this repository: no file names, paths, line numbers, function or struct names
  from it in code, comments, identifiers, messages, tests or documentation, and no mention of where local reference
  material is kept. Describe behavior, rules and binary formats in Etervale's own terms.
- Keep database migrations structural. Import authored world data through `tools/PristonTale.WorldImport`, preserving
  duplicate definitions and unresolved references. Check importer, repository, runtime catalog, and both providers when
  data contracts change.

## Verification

Use the Release build and regression commands in [README.md](README.md). Build changed standalone tools separately. The
regression runner is a console app, not a `dotnet test` suite. Use disposable databases for import and migration tests.
For documentation-only changes, check facts, links, line length, and the diff; avoid unrelated tests.
