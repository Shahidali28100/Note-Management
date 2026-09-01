## Why

Authenticated users currently have no way to create or manage notes — `authentication` (AB-1002) and `password reset` (AB-1003) only cover identity. FRS-NOTE-001 through FRS-NOTE-005 and the FRS §14 / SDS §93 ticket-traceability table assign core note create/read/update/soft-delete/recovery to **AB-1004**, next in the strict AB-1001 → AB-1016 dependency chain. This proposal implements that using the `Notes` table already scoped in SDS §13 / AGENTS.md §9, resolving the three "Open Technical Decisions" SDS §97 lists for AB-1004 (note content persistence format, note validation rules, recovery behavior) via user-approved answers below.

## What Changes

- **POST /api/notes** — creates a note owned by the authenticated caller. `title` (required, 1–200 chars after trimming) and `content` (required, non-empty after trimming) are the only accepted fields. `content` is persisted as an **opaque string** (`nvarchar(max)`) with no structural/format validation — the final rich-text representation is an AB-1012 decision (SDS §53/§97) and must not be front-loaded here. Responds `201 Created` with the new note (id, title, content, createdAt, updatedAt).
- **GET /api/notes** — lists the caller's active (non-deleted) notes using the standard list envelope (`{ items, page, pageSize, totalCount, totalPages }`, AGENTS.md §6). This ticket ships a **fixed default view only**: `page=1`, `pageSize=20`, sorted by `updatedAt desc`, no query-string parameters accepted yet. FRS-NOTE-006/007/008 (client-driven pagination, sorting, tag filtering) are explicitly **AB-1005**'s scope per the FRS traceability table — this ticket establishes the response shape AB-1005 extends without a breaking change.
- **GET /api/notes/{id}** — returns a single active note owned by the caller. `404 Not Found` when the note doesn't exist, is soft-deleted, or belongs to another user — the response never distinguishes "missing" from "not yours" (AGENTS.md §7: the frontend is never an authorization boundary, and non-owners must not learn whether an ID exists).
- **PUT /api/notes/{id}** — full replace of `title`/`content` on a note owned by the caller; same validation as create. Updates `updatedAt`. `404 Not Found` under the same missing-or-not-owned rule as GET. **Does not** create a `NoteVersions` snapshot — version history is AB-1009's table/feature and is out of scope here (FRS-NOTE-003's "creates a version-history snapshot" clause is deferred to that ticket, mirroring how AB-1006 owns Tags).
- **DELETE /api/notes/{id}** — soft-deletes a note owned by the caller: sets `DeletedAt = UTC now`. Responds `204 No Content`. `404 Not Found` if the note doesn't exist, doesn't belong to the caller, or is already soft-deleted (an already-deleted note is indistinguishable from a missing one through the normal query filter).
- **POST /api/notes/{id}/restore** — clears `DeletedAt` on a soft-deleted note owned by the caller (FRS-NOTE-005, 30-day recovery). Responds `200 OK` with the restored note. `404 Not Found` if the note doesn't exist or isn't owned by the caller (looked up including soft-deleted rows, but ownership/existence is still checked before revealing state); `409 Conflict` if the note exists, is owned by the caller, but is **not currently deleted** (nothing to restore). Automatic purge after the 30-day window is **out of scope** for this ticket — no purge job exists yet (that mechanism, like `NoteVersions` purge, belongs to a dedicated retention-process ticket), so restore succeeds for any soft-deleted note regardless of how long ago it was deleted.
- New `Note` EF Core entity + configuration + migration (SDS §13): `Id, UserId, Title, Content, CreatedAt, UpdatedAt, DeletedAt`. Global query filter excludes `DeletedAt IS NOT NULL` for all standard queries; restore explicitly bypasses the filter to locate soft-deleted rows. Indexes per AGENTS.md §9: `Notes.UserId`, `Notes.DeletedAt`, `Notes.UpdatedAt`.
- All five endpoints use the standard Problem Details error contract (SDS §39) and require a valid JWT (`bearerAuth`), same pattern as AB-1002/AB-1003.

Out of scope for this ticket: tags on notes / `NoteTags` (FRS-NOTE-001 lists tags as a minimum field, but the `Tags` table doesn't exist until **AB-1006** — note DTOs simply omit a tags field in AB-1004; AB-1006 adds it as its own delta); client-driven pagination/sorting/tag-filtering query params (**AB-1005**); version-history snapshots on save (**AB-1009**); search (**AB-1007**); sharing (**AB-1008**); any frontend notes UI (**AB-1011/AB-1012**); automatic 30-day purge of soft-deleted notes (no ticket currently owns this — flagged as a gap, not silently assumed).

## Capabilities

### New Capabilities
- `notes`: create, retrieve (single + list), update, soft-delete, and restore a user's own notes — `openspec/specs/notes/spec.md` (new).

### Modified Capabilities
_None._

## Impact

- **DB**: new `Notes` table + EF Core migration under `apps/api/src/NoteManagement.Infrastructure/Migrations`, matching the SDS §13 baseline exactly (no extra columns beyond `Id, UserId, Title, Content, CreatedAt, UpdatedAt, DeletedAt`).
- **Domain**: `Note` entity (`apps/api/src/NoteManagement.Domain`), no ASP.NET Core dependency.
- **Application**: `NoteService` (or equivalent) with create/get/list/update/delete/restore use-cases, `CreateNoteRequestDto`/`UpdateNoteRequestDto`/`NoteResponseDto`/`NoteListResponseDto`, validators, and a `NoteRepository` interface enforcing `Note.UserId == currentUserId` on every read/write.
- **Api**: new `NotesController` with the five actions above, all `[Authorize]`.
- **Shared TS contracts**: `Note`, `CreateNoteRequest`, `UpdateNoteRequest`, `NoteListResponse` DTOs + mirrored Zod schemas added to `packages/shared` (AGENTS.md §12), for later consumption by AB-1011/AB-1012.
- **Downstream dependencies**: AB-1005 (pagination/sorting/filtering), AB-1006 (tags), AB-1009 (version snapshots) all extend this capability's endpoints/DTOs without breaking them — the envelope shape and DTO fields chosen here are deliberately additive-only.
- **Process**: `delta-openapi.yaml` for these five endpoints is included in this change, required before `/plan` per AGENTS.md §8 / SDS §66.
