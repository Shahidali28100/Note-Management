@AGENTS.md

# CLAUDE.md

Claude Code operating rules for this repo. Engineering/product context lives in AGENTS.md above — this file covers only how Claude Code should behave here, and does not repeat anything from AGENTS.md.

## Permission Model: Ask vs Proceed

**Ask first (`[y/n]`):**
- Every file write or edit — no silent create/modify (SDS §86).
- `git commit`, `git push`, opening a PR.
- `openspec archive`, deleting/renaming files, migrations, schema changes.
- Any destructive or irreversible command (`rm`, `git reset --hard`, DB drops, force-push).

**Proceed without asking:**
- Read-only exploration: `git status/diff/log`, `grep`/search, listing files.
- Running tests or builds that don't mutate tracked files.
- Repeating a command already approved earlier in the same ticket/session.

## Context Management

- Watch context usage. At **~60k tokens** used in a session, stop and run `/clear`.
- If mid-ticket state still needs to be preserved at that point, run `/compact` instead, then continue.
- Always `/clear` between tickets — one ticket per session (SDS §83). Never carry AB-xxxx context into the next ticket's session.

## Thinking Depth

Match reasoning depth to the work (SDS §88):

| Work | Model / Depth |
|---|---|
| Boilerplate, scaffolding, repetitive edits | Haiku, no extended thinking |
| Standard feature implementation | Sonnet, normal thinking |
| Architecture, schema, auth, or cross-cutting decisions | Opus + ultrathink |

Default to normal thinking. Escalate to ultrathink only for decisions that are expensive to reverse later (schema design, auth/token flow, transaction boundaries).

## Commit Message Format

```
type(scope): description AB#ticket
```
Examples: `feat(auth): add jwt authentication AB#1002`, `fix(notes): correct soft delete filter AB#1004`.
Enforced by Husky + commitlint — never bypass with `--no-verify`.

## Branch Naming

```
type/AB-xxxx-short-kebab-description
```
Example: `feat/AB-1004-notes-crud`. One branch per ticket, cut from the latest validated main.

## Quality Gates — Run In Order

1. `pnpm lint --max-warnings 0`
2. `pnpm build`
3. `pnpm test` (`pnpm test --coverage` before marking a ticket complete)

Fix and re-run on the first failure before proceeding to the next gate — never run a later gate against code that failed an earlier one. All three must pass before requesting review or opening a PR.

## Commands Requiring `[y/n]`

- Any `Write`/`Edit` tool call touching a file.
- `git commit`, `git push`, `gh pr create`.
- `openspec archive AB-xxxx`.
- `dotnet ef migrations add`, `dotnet ef database update`.
- `pnpm add` / `pnpm remove` (dependency changes).
- Any command that deletes, overwrites, or force-pushes.

Read-only commands and commands already approved earlier in the same session do not require re-confirmation.
