# CLAUDE.md — apps/web

Scoped to the React frontend. Inherits root `AGENTS.md`/`CLAUDE.md` — this file adds only frontend-local specifics.

## Commands

```bash
pnpm --filter web dev              # Vite dev server
pnpm --filter web build            # production build
pnpm --filter web test             # Vitest unit/component tests
pnpm --filter web test -- --coverage
pnpm --filter web lint --max-warnings 0
pnpm dlx shadcn@<pinned-version> add <component>   # add a shadcn/ui component
```

## Component + State Patterns

- Structure: `components/` (dumb/shared UI), `features/` (feature-scoped components+logic), `pages/`, `hooks/`, `services/` (API calls), `stores/` (Zustand), `lib/` (utilities).
- All API calls go through `services/` using TanStack Query — components never call `fetch`/`axios` directly.
- TanStack Query owns server state: notes, tags, search results, share links, versions, auth requests. Mutations invalidate the queries they affect.
- Zustand owns UI-only state: modal open/closed, editor UI flags, transient preferences. Never mirror server data into a Zustand store.
- TipTap editor changes flow through a debounced autosave hook — one save call per debounce window, not per keystroke.
- Import DTOs and Zod schemas from `packages/shared` — never redeclare a type that already exists there (see `packages/shared/CLAUDE.md`).
- Search-result highlighting renders through a safe mechanism (e.g. React nodes or a sanitizer) — never `dangerouslySetInnerHTML` on raw API text.

## Anti-Patterns

- No direct `fetch`/`axios` calls inside components — always via `services/` + TanStack Query.
- No duplicating server state into Zustand or component state "for convenience."
- No injecting raw HTML from search highlights or note content without sanitization.
- No treating client-side Zod validation as sufficient — it's UX only, backend is authoritative.
- No per-keystroke save requests — autosave must be debounced.
- No local redefinition of DTOs/types that already exist in `packages/shared`.
