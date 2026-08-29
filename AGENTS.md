# AGENTS.md

Single source of truth for all AI coding tools working in this repo. Read this before writing code.

> **Status:** Pre-implementation. Only `docs/FRS.md` and `docs/SDS.md` exist today. Everything below is the *target* structure/stack defined by those specs (starting at ticket AB-1001) — treat it as the contract to build toward, not a description of existing code.

## 1. Project Overview

A web-based Note Taking Application for authenticated users to create, organize, search, share, and version their notes. Core capabilities: JWT-based auth with OTP password reset, rich-text notes with tags, SQL Server full-text search, public read-only share links with atomic view counting, and full version history with restore. Deliberately excludes real-time collaboration, attachments, OAuth, folders, and any external email/search services.

## 2. Repository Structure

Target monorepo layout (pnpm workspaces):

```
/
├── apps/
│   ├── web/            # React 19 + TypeScript frontend (Vite)
│   └── api/             # ASP.NET Core backend (own .sln/.csproj structure)
├── packages/
│   └── shared/           # Shared TS types, DTOs, Zod schemas — see §12
├── openspec/             # Spec-driven dev: changes/ and archive/ per ticket
├── docs/
│   ├── FRS.md            # Functional Requirements Specification
│   └── SDS.md            # Software Design Specification (source for this file)
├── .claude/               # Claude Code config/commands
├── AGENTS.md              # This file
├── package.json
└── pnpm-workspace.yaml
```

Inside `apps/api`, follow the layered structure in §5. Inside `apps/web/src`: `components/`, `features/`, `pages/`, `hooks/`, `services/`, `stores/`, `lib/`.

## 3. Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite |
| Server state | TanStack Query |
| Client state | Zustand |
| Editor | TipTap |
| UI components | shadcn/ui |
| Backend | ASP.NET Core MVC/Web API, C# |
| ORM | Entity Framework Core (**not** Prisma) |
| Database | Microsoft SQL Server (+ SQL Server Full-Text Search) |
| Auth | JWT access token + DB-backed refresh token |
| Backend tests | MSTest |
| Frontend tests | Vitest + React Testing Library |
| E2E tests | Playwright |
| Monorepo | pnpm workspaces |
| API docs | OpenAPI / Swagger |
| Git hooks | Husky + commitlint |
| Spec workflow | OpenSpec |

Exact language/framework versions are pinned in AB-1001 and recorded in `package.json` / `.csproj` files — never install with `@latest`.

## 4. Key Commands

```bash
pnpm install            # install all workspace deps
pnpm build               # build all packages/apps
pnpm lint --max-warnings 0
pnpm test                 # run all test suites
pnpm test --coverage      # required before ticket completion (≥80% new-code coverage)
pnpm --filter web dev     # frontend dev server
dotnet run --project apps/api   # backend dev server
dotnet ef database update       # apply EF Core migrations
```

`pnpm build && pnpm lint --max-warnings 0 && pnpm test` must pass at every phase checkpoint.

## 5. Architecture Patterns

Backend is layered, thin-controller:

```
Controllers → Application Services → Domain → EF Core → SQL Server
```

- **Api/** — controllers, middleware, filters; HTTP concerns only, no business logic.
- **Application/** — services, DTOs, validators, use-case orchestration, entity↔DTO mapping.
- **Domain/** — entities, enums, domain rules; no ASP.NET Core dependency.
- **Infrastructure/** — `ApplicationDbContext`, EF entity configurations, migrations, repositories.

Frontend: server state lives in TanStack Query (notes, tags, search, shares, versions, auth calls); Zustand is for UI-only state (modals, editor UI, preferences) — never duplicate server state into Zustand.

## 6. Coding Standards

- Controllers stay thin — delegate to Application services.
- All backend endpoints validate every external input (required fields, lengths, email/password format, pagination limits, sort allowlist, resource IDs).
- Sorting/filtering use explicit allowlists — never concatenate user input into SQL/LINQ.
- Timestamps are stored and compared in UTC.
- Entity configuration is separated from entity classes (`XConfiguration` classes in Infrastructure).
- Multi-write operations (note save + version snapshot, version restore, auth writes, share updates) run inside an EF Core transaction — all-or-nothing.
- Standard API response envelope for lists: `{ items, page, pageSize, totalCount, totalPages }`.
- Errors use a consistent contract (ASP.NET Problem Details): `{ type, title, status, detail }`.
- Test names describe behavior: `Method_Condition_ExpectedResult` (e.g. `DeleteNote_SetsDeletedAt`).

## 7. Auth Approach

JWT access token (15 min TTL) + cryptographically random refresh token persisted in SQL Server (7 day TTL). `sub` claim carries the user ID; signature, expiration, issuer, and audience are validated on every request. Refresh tokens (and password-reset OTPs) are stored as **hashes**, never raw. Logout revokes the refresh token. Forgot-password issues a time-limited, single-use OTP logged to the console (no real email provider). Authorization is always enforced server-side: `Note.UserId == currentUserId` for every private-resource access — the frontend is never an authorization boundary. Public share access is authenticated purely by an unguessable, hashed share token (valid, not revoked, not expired).

## 8. API Design Conventions

REST under `/api`. Status codes: `200` GET/PUT, `201` POST, `204` DELETE, `400` validation, `401` unauthenticated, `403` forbidden, `404` not found, `409` conflict, `500` unexpected.

Key routes:
```
POST /api/auth/{register,login,refresh,logout,forgot-password,reset-password}
GET|POST /api/notes            GET|PUT|DELETE /api/notes/{id}      POST /api/notes/{id}/restore
GET|POST|PUT|DELETE /api/tags[/{id}]
GET /api/search
POST|GET /api/notes/{id}/shares   DELETE /api/notes/{id}/shares/{shareId}   GET /api/shared/{token}  (public, no auth)
GET /api/notes/{id}/versions[/{versionId}]   POST /api/notes/{id}/versions/{versionId}/restore
```

Every API-changing ticket ships a `delta-openapi.yaml` before implementation; the OpenAPI contract is authoritative over ad-hoc behavior.

## 9. DB Schema Summary

```
Users (Id, Name, Email[unique], PasswordHash, CreatedAt, UpdatedAt)
RefreshTokens (Id, UserId→Users, TokenHash, ExpiresAt, RevokedAt, CreatedAt)
PasswordResetOtps (Id, UserId→Users, OtpHash, ExpiresAt, UsedAt, CreatedAt)
Notes (Id, UserId→Users, Title, Content, CreatedAt, UpdatedAt, DeletedAt)   -- soft delete
Tags (Id, UserId→Users, Name, Color, CreatedAt, UpdatedAt)                 -- unique per (UserId, Name)
NoteTags (NoteId→Notes, TagId→Tags)                                          -- composite PK, many-to-many
ShareLinks (Id, NoteId→Notes, TokenHash, ExpiresAt, RevokedAt, ViewCount, CreatedAt)
NoteVersions (Id, NoteId→Notes, VersionNumber, Title, Content, CreatedAt)   -- unique per (NoteId, VersionNumber), immutable
```

Soft delete: `DeletedAt = NULL` → active; normal queries exclude `DeletedAt IS NOT NULL` (EF global query filter where appropriate). ShareLinks.ViewCount increments atomically (no read-modify-write). Indexes at minimum: `Users.Email`, `Notes.UserId/DeletedAt/UpdatedAt`, `Tags.UserId(+Name)`, `NoteTags.NoteId/TagId`, `ShareLinks.TokenHash/NoteId/ExpiresAt`, `NoteVersions.NoteId(+VersionNumber)`.

## 10. Testing Approach

Four layers, bottom-up: Backend unit (MSTest) → Backend integration against real EF Core/SQL Server (MSTest) → Frontend unit/component (Vitest + React Testing Library, API calls mocked) → Playwright E2E across the full stack.

- Backend tests live under `apps/api/**/Tests` (or the project's `Tests/` layer); run via `dotnet test`.
- Frontend tests live beside source or under `apps/web/src/**/__tests__`; run via `pnpm test` (Vitest).
- E2E specs live under a top-level `e2e/` or `apps/web/e2e/`; run via `pnpm playwright test`.
- RTL tests target accessible roles/labels/text, not internal state or implementation details.
- Every approved spec scenario gets exactly one named test. New code requires ≥80% coverage — no trivial tests just to hit the number.

## 11. Do NOT Do

- Do not use Prisma, MongoDB, PostgreSQL, or any ORM/DB other than EF Core + SQL Server.
- Do not use Elasticsearch, Algolia, Azure Cognitive Search, or any external search — SQL Server Full-Text Search only.
- Do not implement real-time collaboration, file/image attachments, OAuth/social login, folders, or nested notes.
- Do not send real email — OTPs are logged, never delivered externally.
- Do not concatenate user input into SQL or LINQ strings; parameterize everything.
- Do not treat the frontend as an authorization boundary — always re-check ownership server-side.
- Do not overwrite or delete existing note versions on restore — restore always creates a new version.
- Do not physically delete a note before its 30-day recovery window elapses.
- Do not return passwords, password hashes, raw refresh tokens, or raw share tokens in any API response or log.
- Do not install packages with `@latest` — pin exact versions.
- Do not duplicate shared DTOs/types between frontend and `packages/shared`.
- Do not skip or reorder tickets (AB-1001 → AB-1016 is a strict dependency chain); do not begin implementation before its spec/plan is approved.

## 12. Shared Packages

`packages/shared` holds all shared **frontend** TypeScript contracts, imported by `apps/web` instead of being redefined:

- Auth, Note, Tag, Search, Share, and Version DTOs
- Zod validation schemas (mirrored, not replacing, backend validation)
- Pagination types and API error/response types

The backend (`apps/api`) owns the authoritative C# implementation of these same contracts — `packages/shared` is TypeScript-only and never a runtime dependency of the .NET backend. Frontend validation via these schemas is a UX convenience only; it never replaces server-side validation.
