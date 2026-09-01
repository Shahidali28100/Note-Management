# CLAUDE.md — packages/shared

Scoped to the shared TypeScript package. Inherits root `AGENTS.md`/`CLAUDE.md` — this file adds only rules local to this package.

## What Exists Here

TypeScript-only contracts consumed by `apps/web` (never a runtime dependency of `apps/api`):

- **DTOs / types** — Auth, Note, Tag, Search, Share, Version request/response shapes; pagination envelope (`{ items, page, pageSize, totalCount, totalPages }`); API error shape (Problem Details).
- **Zod schemas** — validation mirrors of the above DTOs, used for frontend form/input validation (UX convenience, not a replacement for backend validation).
- **Utils** — small, pure, cross-cutting helpers used by more than one frontend feature (e.g. shared formatting/pagination helpers). Not a dumping ground for one-off feature logic.

The backend (`apps/api`, C#) holds the authoritative implementation of these same contracts. Types here must mirror it, not invent independent shapes.

## Rule: Never Duplicate What's Already Here

Before defining any type, DTO, Zod schema, or cross-feature util in `apps/web`, check `packages/shared` first. If an equivalent already exists, import it — do not redeclare, rename, or fork a local copy "just for this component." A duplicate definition here is a bug waiting to drift from the API contract.

## How to Add a New Shared Item

1. Confirm it doesn't already exist (search `packages/shared/src`).
2. Confirm it's genuinely shared — used (or clearly about to be used) by more than one feature/page. Feature-local types stay in `apps/web/src/features/*`.
3. Add it under the matching subfolder (e.g. `src/types/`, `src/schemas/`, `src/utils/`), matching the backend's DTO shape exactly.
4. Export it from the package's entry point (`src/index.ts`).
5. If it's a Zod schema, add a corresponding type via `z.infer<>` rather than hand-writing both.
6. If it wraps logic (not just a type), add a Vitest unit test alongside it.
7. Update `apps/web` call sites to import from `@repo/shared` (or the configured package alias) instead of any local copy.
