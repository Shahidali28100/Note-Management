## Why

AB-1004 shipped `GET /api/notes` as a **fixed default view only** (`page=1`, `pageSize=20`, sorted `updatedAt desc`, no query parameters accepted) and explicitly deferred client-driven pagination and sorting to AB-1005 (FRS-NOTE-006/FRS-NOTE-007, FRS §14 / SDS §93 ticket-traceability table), next in the strict AB-1001 → AB-1016 dependency chain. This proposal adds `page`, `pageSize`, `sortBy`, and `sortDirection` query parameters to that endpoint, extending the existing response envelope additively (no breaking change) per user-approved answers below.

FRS-NOTE-008 (tag filtering) is out of scope here: the `Tags`/`NoteTags` tables don't exist until AB-1006, and AB-1004 already established the precedent of deferring tag-dependent behavior to the ticket that owns Tags. AB-1006 will extend `GET /api/notes` again, additively, to add a `tagId` filter.

## What Changes

- **GET /api/notes** now accepts four optional query parameters instead of none:
  - `page` (integer, default `1`): must be a valid integer `>= 1`. Missing → default. Malformed (non-integer) or `< 1` → `400 Bad Request`.
  - `pageSize` (integer, default `20`, max `100`): must be a valid integer. Missing → default `20`. Malformed (non-integer) or `< 1` → `400 Bad Request`, consistent with how `page < 1` is handled — a non-positive value is invalid input, not a magnitude to clamp. A valid integer `> 100` is silently clamped down to `100` (this remains a clamp, not a rejection, since "too large" is a magnitude problem, not an invalid one). Values within `1–100` are used as-is.
  - `sortBy` (string enum, default `updatedAt`): one of `createdAt`, `updatedAt`, `title` (FRS-NOTE-007's minimum sortable set), enforced via an explicit backend allowlist (AGENTS.md §6). Any other value → `400 Bad Request`.
  - `sortDirection` (string enum, default `desc`): one of `asc`, `desc`. Any other value → `400 Bad Request`.
  - When no query parameters are supplied, behavior is unchanged from AB-1004 (`page=1`, `pageSize=20`, `updatedAt desc`) — this is a non-breaking, additive extension of the same endpoint.
  - A `page` beyond the last available page returns `200 OK` with an empty `items` array and accurate `totalCount`/`totalPages` — not an error — matching standard REST pagination semantics implied by the SDS §40 envelope.
  - User scoping (only the caller's non-deleted notes) and the response envelope shape (`items, page, pageSize, totalCount, totalPages`) are unchanged from AB-1004.
- Sorting is implemented via an explicit allowlist mapping `sortBy` values to columns — never by concatenating the query value into LINQ/SQL (AGENTS.md §6, SDS §41/§59).
- **Explicitly out of scope**: tag filtering (FRS-NOTE-008 / AB-1006), search (AB-1007), any new endpoint, and any change to `POST/GET/PUT/DELETE /api/notes/{id}` or `/restore`.

## Capabilities

### New Capabilities
_None._

### Modified Capabilities
- `notes`: the **Note Listing** requirement changes from a fixed `page=1`/`pageSize=20`/`updatedAt desc` view with no accepted query parameters, to a client-driven view accepting `page`, `pageSize`, `sortBy`, `sortDirection` with the validation/clamping/allowlist rules above. Defaults match AB-1004's prior fixed behavior exactly when no parameters are supplied.

## Impact

- **Application**: `NoteService`/`INoteRepository` list use-case extended to accept pagination + sort parameters; a query-parameter validator applies the `page`/`pageSize`/`sortBy`/`sortDirection` rules above and rejects invalid input with `400 Bad Request` via the existing Problem Details contract.
- **Api**: `NotesController.GetNotes` (or equivalent) binds the four query parameters (e.g. via a query DTO) instead of taking none; no new routes.
- **Shared TS contracts**: `packages/shared` gets a `NoteListQuery` (or equivalent) type + mirrored Zod schema for `page`/`pageSize`/`sortBy`/`sortDirection`, for later consumption by AB-1011's notes-list UI.
- **DB**: no schema change. `Notes.UpdatedAt` and (already-existing) title/id columns are queried with `ORDER BY`; no new index is required beyond the `Notes.UpdatedAt` index already listed in AGENTS.md §9 — `CreatedAt`/`Title` sorts are acceptable as unindexed `ORDER BY` at this data scale and are not blocking for this ticket.
- **Process**: `delta-openapi.yaml` for the modified `GET /api/notes` operation is included in this change, required before `/plan` per AGENTS.md §8 / SDS §66.
- **Downstream dependencies**: AB-1006 (tag filtering) extends this same endpoint additively with a `tagId` parameter; AB-1007 (search) is a separate endpoint (`GET /api/search`) and is not affected by this change.
