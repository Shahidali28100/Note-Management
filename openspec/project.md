# Project Context — Note Taking Application

Source: `docs/FRS.md` (business/functional requirements) and `docs/SDS.md` (technical design). This file is the OpenSpec project-context reference — consult it when proposing, planning, or reviewing any change.

> **Status:** Pre-implementation. No application code exists yet; the repo currently holds only `docs/FRS.md`, `docs/SDS.md`, and tooling config. Ticket AB-1001 (project setup) establishes the structure described below.

## What This Product Is

A full-stack web app where authenticated users create, organize, search, share, and version their notes: JWT auth with OTP-based password reset, rich-text notes with user-scoped tags, SQL Server full-text search with highlighting, public read-only share links with atomic view counting, and immutable version history with restore. Out of scope: real-time collaboration, attachments, OAuth/social login, folders/nested notes, and any real email delivery or external search service.

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite |
| Server state | TanStack Query |
| Client state | Zustand |
| Rich text editor | TipTap |
| UI components | shadcn/ui |
| Backend | ASP.NET Core MVC/Web API, C# |
| ORM | Entity Framework Core |
| Database | Microsoft SQL Server (+ SQL Server Full-Text Search) |
| Auth | JWT access token (15 min) + DB-backed refresh token (7 days) |
| Backend tests | MSTest (unit + integration) |
| Frontend tests | Vitest + React Testing Library |
| E2E tests | Playwright |
| Monorepo | pnpm workspaces |
| API docs | OpenAPI / Swagger |
| Git hooks | Husky + commitlint |
| Spec workflow | OpenSpec |

Versions are pinned in `package.json`/`.csproj` — never install with `@latest`.

## Architectural Constraints

- **Layered backend, one direction of dependency:** `Controller → Application Service → Domain → EF Core (Infrastructure) → SQL Server`. Controllers stay thin; the Domain layer has zero ASP.NET Core dependency.
- **EF Core + SQL Server only.** No Prisma, no Mongo, no Postgres, no other ORM/DB — this is explicit and non-negotiable per SDS ADR-003/ADR-004.
- **No external search.** Full-text search is SQL Server Full-Text Search only — no Elasticsearch, Algolia, Azure Cognitive Search, etc.
- **Soft delete, not hard delete.** Notes use `DeletedAt` timestamp with a 30-day recovery window; normal queries exclude `DeletedAt IS NOT NULL`.
- **Version history is immutable and append-only.** Restoring a version creates a *new* version — never overwrites or deletes prior versions.
- **Atomicity where it matters.** Note-save + version-snapshot, version-restore + new-version, and share-link `ViewCount` increments must be atomic/transactional — no read-modify-write races.
- **Server-side authorization is absolute.** The frontend is never an authorization boundary; every private-resource access re-checks `Note.UserId == currentUserId` (or equivalent ownership check) server-side.
- **Shared TypeScript contracts live in `packages/shared` only** — the frontend never redeclares a DTO/type/schema that already exists there; the backend C# implementation is the authoritative source of truth those types mirror.
- **Monorepo layout:** `apps/api` (backend), `apps/web` (frontend), `packages/shared` (shared TS), `openspec/` (specs), managed via pnpm workspaces.
- Out-of-scope technical features (see FRS §3/§16, SDS §96) must not be introduced without an approved spec change: real-time collab, attachments, OAuth, folders, nested notes, real email delivery, non-EF-Core persistence, non-SQL-Server-FTS search.

## Team Conventions

- **Spec-driven workflow:** every ticket goes `/spec → human review → delta-openapi.yaml approval → /plan → human review → /tasks → human review → /implement → validation → fresh-terminal /review → openspec archive → /pr`. Implementation never starts before the spec/plan is approved.
- **Ticket isolation:** one ticket per Claude Code session; `/clear` between tickets. Tickets are implemented strictly in order AB-1001 → AB-1016 — never skipped or reordered.
- **Commit format:** `type(scope): description AB#ticket` (e.g. `feat(auth): add jwt authentication AB#1002`), enforced by Husky + commitlint.
- **Branch naming:** `type/AB-xxxx-short-kebab-description` (e.g. `feat/AB-1004-notes-crud`), one branch per ticket.
- **Test naming:** `Method_Condition_ExpectedResult` (e.g. `DeleteNote_SetsDeletedAt`, `Search_ReturnsOnlyCurrentUsersNotes`).
- **Every API-changing ticket ships a `delta-openapi.yaml` before implementation begins** — the OpenAPI contract is authoritative over ad-hoc behavior.
- **Context7 MCP stays active** for verifying library APIs (ASP.NET Core, EF Core, React, TanStack Query, Zustand, TipTap, shadcn/ui, Vitest, RTL, Playwright, MSTest) against current docs — no inventing APIs from memory.
- **Every file write requires explicit `[y/n]` confirmation** — no silent creates/modifies.
- **Model selection matches task weight:** Haiku for boilerplate, Sonnet for standard implementation, Opus + ultrathink for architecture/schema/auth decisions.
- **Git worktrees** for any two tasks explicitly marked `[PARALLEL]` (e.g. simultaneous frontend/backend work); merge only after each side's validation gates pass.

## Quality Standards

- **Global acceptance criteria** (FRS §15) — a feature is complete only when: happy path works, validation errors are handled, authorization rules are enforced, user data isolation holds, every approved spec scenario has a named test, API status codes match the SDS contract, manual smoke testing is done, automated tests pass, `openspec validate` passes, and the change passes fresh-terminal review before being archived.
- **Coverage:** ≥80% on new code per ticket; trivial tests must not be used to inflate coverage.
- **Quality gates, run in order, all must pass:** `pnpm lint --max-warnings 0` → `pnpm build` → `pnpm test` (`--coverage` before ticket completion). A failing gate blocks progression to the next.
- **HTTP status codes** follow the fixed mapping: `200` GET/PUT, `201` POST, `204` DELETE, `400` validation, `401` unauthenticated, `403` forbidden, `404` not found, `409` conflict, `500` unexpected — ticket-specific OpenSpec contracts take precedence where they differ.
- **Error contract:** consistent shape via ASP.NET Core Problem Details (`type`, `title`, `status`, `detail`).
- **Security principles (SDS §95), all mandatory:** server-side authorization only; strict user-data isolation; passwords never stored/returned in plaintext; refresh tokens and OTPs stored as hashes; share tokens cryptographically unguessable; SQL injection prevented via parameterization everywhere (search, sorting, filtering, share-token lookup); search-highlight output must not enable XSS; secrets never committed to source control; logs never expose passwords, hashes, refresh tokens, or share tokens; soft-deleted notes never leak through APIs or search; resources protected against IDOR/horizontal privilege escalation.
- **Review gate before any PR:** a fresh-terminal, read-only `/review AB-xxxx` must show ✅ for every requirement — any ❌ Missing, ⚠️ Drifted, 🔴 Security, or 📋 FRS gap finding blocks the PR.
- **Package versions are pinned**, never installed via `@latest`.
